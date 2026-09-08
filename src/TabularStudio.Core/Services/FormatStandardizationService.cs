using System.Diagnostics;
using System.Globalization;
using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using static TabularStudio.Core.Services.ApprovedValueNormalization;
using static TabularStudio.Core.Services.WorkbookFileOperations;

namespace TabularStudio.Core.Services;

public sealed class FormatStandardizationService : IFormatStandardizationService
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    public Task<FormatStandardizationResult> ExecuteAsync(
        FormatStandardizationRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => Execute(request, progress, cancellationToken),
            cancellationToken);
    }

    private static FormatStandardizationResult Execute(
        FormatStandardizationRequest? request,
        IProgress<OperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        string? stagingPath = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var validation = ValidateRequest(request);
            if (validation.Error is not null)
            {
                return Failure(validation.Error);
            }

            var validated = validation.Request!;

            if (File.Exists(validated.OutputFilePath) && !validated.OverwriteExistingOutput)
            {
                return Failure(new OperationError(
                    OperationErrorCode.OutputAlreadyExists,
                    "输出文件已存在，且本次未确认覆盖。",
                    validated.OutputFilePath));
            }

            if (File.Exists(validated.OutputFilePath) && validated.OverwriteExistingOutput)
            {
                var outputProbeError = ProbeExistingOutput(validated.OutputFilePath);
                if (outputProbeError is not null)
                {
                    return Failure(outputProbeError);
                }
            }

            var stagingReservation = ReserveStagingFile(validated.OutputFilePath);
            if (stagingReservation.Error is not null)
            {
                return Failure(stagingReservation.Error);
            }

            stagingPath = stagingReservation.Path!;

            Report(progress, OperationStage.Reading, null, null, null);
            cancellationToken.ThrowIfCancellationRequested();

            var workbookOpen = TryOpenWorkbook(validated.InputFilePath, cancellationToken);
            if (workbookOpen.Error is not null)
            {
                return FailureAfterCleanup(workbookOpen.Error, stagingPath);
            }

            using var inputStream = workbookOpen.Stream!;
            using var workbook = workbookOpen.Workbook!;

            cancellationToken.ThrowIfCancellationRequested();

            var worksheet = workbook.FindSheet(validated.WorksheetName);

            if (worksheet is null)
            {
                return FailureAfterCleanup(new OperationError(
                    OperationErrorCode.WorksheetNotFound,
                    "指定的工作表不存在。",
                    validated.WorksheetName), stagingPath);
            }

            var contentAtOrAfterHeader = workbook.ContentCells(worksheet)
                .Where(cell => cell.Address.RowNumber >= validated.HeaderRowNumber)
                .ToArray();

            if (contentAtOrAfterHeader.Length == 0)
            {
                return FailureAfterCleanup(new OperationError(
                    OperationErrorCode.InvalidHeaderRow,
                    "指定表头行及其下方没有有效数据范围。",
                    validated.HeaderRowNumber.ToString(CultureInfo.InvariantCulture)), stagingPath);
            }

            var dataCells = contentAtOrAfterHeader
                .Where(cell => cell.Address.RowNumber > validated.HeaderRowNumber)
                .ToArray();
            var cellsByRow = dataCells
                .GroupBy(cell => cell.Address.RowNumber)
                .OrderBy(group => group.Key)
                .ToArray();

            Report(progress, OperationStage.Preparing, null, null, cellsByRow.Length);
            cancellationToken.ThrowIfCancellationRequested();

            var numericColumns = AnalyzeSafeNumericColumns(
                dataCells,
                validated.Options,
                cancellationToken);

            Report(
                progress,
                OperationStage.Processing,
                cellsByRow.Length == 0 ? 100 : 0,
                0,
                cellsByRow.Length);

            for (var rowIndex = 0; rowIndex < cellsByRow.Length; rowIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var cell in cellsByRow[rowIndex])
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    StandardizeCell(cell, validated.Options, numericColumns);
                }

                var processedRows = rowIndex + 1;
                Report(
                    progress,
                    OperationStage.Processing,
                    CalculatePercent(processedRows, cellsByRow.Length),
                    processedRows,
                    cellsByRow.Length);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, OperationStage.Writing, null, cellsByRow.Length, cellsByRow.Length);

            try
            {
                using var stagingStream = new FileStream(
                    stagingPath,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None);
                workbook.SaveAs(stagingStream, cancellationToken);
            }
            catch (IOException exception) when (IsFileLocked(exception))
            {
                return FailureAfterCleanup(new OperationError(
                    OperationErrorCode.FileLocked,
                    "输出文件正在被其它程序占用。",
                    validated.OutputFilePath), stagingPath);
            }
            catch (UnauthorizedAccessException)
            {
                return FailureAfterCleanup(new OperationError(
                    OperationErrorCode.OutputDirectoryNotWritable,
                    "无法写入输出目录。",
                    validated.OutputDirectory), stagingPath);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var commitError = CommitStagingFile(
                stagingPath,
                validated.OutputFilePath,
                validated.OverwriteExistingOutput);
            if (commitError is not null)
            {
                return FailureAfterCleanup(commitError, stagingPath);
            }

            stagingPath = null;
            stopwatch.Stop();

            var summary = new FormatStandardizationSummary(
                workbook.IsCsv ? null : worksheet.Name,
                cellsByRow.Length,
                stopwatch.Elapsed);

            Report(
                progress,
                OperationStage.Completed,
                100,
                cellsByRow.Length,
                cellsByRow.Length);

            return new FormatStandardizationResult(
                true,
                validated.OutputFilePath,
                summary,
                null);
        }
        catch (OperationCanceledException)
        {
            TryDeleteWithoutResult(stagingPath);
            throw;
        }
        catch (Exception exception)
        {
            var error = new OperationError(
                OperationErrorCode.ProcessingFailed,
                "格式统一处理失败。",
                exception.GetType().Name);

            return FailureAfterCleanup(error, stagingPath);
        }
    }

    private static ValidationResult ValidateRequest(FormatStandardizationRequest? request)
    {
        if (request?.Source is null || request.Options is null)
        {
            return ValidationFailure("格式统一请求、数据源和处理选项不能为空。");
        }

        var source = request.Source;
        if (string.IsNullOrWhiteSpace(source.FilePath))
        {
            return ValidationFailure("输入文件路径不能为空。");
        }

        if (!IsCsvPath(source.FilePath) && string.IsNullOrWhiteSpace(source.WorksheetName))
        {
            return ValidationFailure("工作表名称不能为空。");
        }

        if (source.HeaderRowNumber < 1)
        {
            return new ValidationResult(null, new OperationError(
                OperationErrorCode.InvalidHeaderRow,
                "表头行号必须大于或等于 1。",
                source.HeaderRowNumber.ToString(CultureInfo.InvariantCulture)));
        }

        if (string.IsNullOrWhiteSpace(request.OutputFilePath))
        {
            return ValidationFailure("输出文件路径不能为空。");
        }

        string inputFilePath;
        string outputFilePath;

        try
        {
            inputFilePath = Path.GetFullPath(source.FilePath);
            outputFilePath = Path.GetFullPath(request.OutputFilePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return ValidationFailure("输入或输出文件路径无效。", exception.GetType().Name);
        }

        if (!File.Exists(inputFilePath))
        {
            return new ValidationResult(null, new OperationError(
                OperationErrorCode.FileNotFound,
                "输入 Excel 文件不存在。",
                inputFilePath));
        }

        if (!IsSupportedPath(inputFilePath) || !SameFormat(inputFilePath, outputFilePath))
        {
            return new ValidationResult(null, new OperationError(
                OperationErrorCode.UnsupportedFileType,
                "输入必须为 .xlsx/.xls/.csv，输出格式必须与输入一致。"));
        }

        if (string.Equals(inputFilePath, outputFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return new ValidationResult(null, new OperationError(
                OperationErrorCode.OutputConflictsWithInput,
                "输出路径不能与输入文件相同。",
                outputFilePath));
        }

        var outputDirectory = Path.GetDirectoryName(outputFilePath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return new ValidationResult(null, new OperationError(
                OperationErrorCode.OutputDirectoryNotWritable,
                "输出目录不存在或不可用。",
                outputDirectory));
        }

        return new ValidationResult(
            new ValidatedRequest(
                inputFilePath,
                source.WorksheetName,
                source.HeaderRowNumber,
                outputFilePath,
                outputDirectory,
                request.Options,
                request.OverwriteExistingOutput),
            null);
    }

    private static HashSet<int> AnalyzeSafeNumericColumns(
        IReadOnlyList<IXLCell> dataCells,
        FormatStandardizationOptions options,
        CancellationToken cancellationToken)
    {
        var safeColumns = new HashSet<int>();
        if (!options.NormalizeSafeNumbers)
        {
            return safeColumns;
        }

        foreach (var columnGroup in dataCells.GroupBy(cell => cell.Address.ColumnNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hasTextNumberCandidate = false;
            var hasCounterEvidence = false;

            foreach (var cell in columnGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (cell.HasFormula)
                {
                    continue;
                }

                if (cell.DataType == XLDataType.Number)
                {
                    continue;
                }

                if (cell.DataType != XLDataType.Text)
                {
                    hasCounterEvidence = true;
                    break;
                }

                var candidate = NormalizeText(cell.GetString(), options);
                if (candidate.Length == 0)
                {
                    continue;
                }

                if (!TryGetSafeExcelNumber(candidate, out _))
                {
                    hasCounterEvidence = true;
                    break;
                }

                hasTextNumberCandidate = true;
            }

            if (hasTextNumberCandidate && !hasCounterEvidence)
            {
                safeColumns.Add(columnGroup.Key);
            }
        }

        return safeColumns;
    }

    private static void StandardizeCell(
        IXLCell cell,
        FormatStandardizationOptions options,
        IReadOnlySet<int> numericColumns)
    {
        if (cell.HasFormula || cell.DataType != XLDataType.Text)
        {
            return;
        }

        var normalizedText = NormalizeText(cell.GetString(), options);

        if (options.NormalizeSafeNumbers
            && numericColumns.Contains(cell.Address.ColumnNumber)
            && TryGetSafeExcelNumber(normalizedText, out var number))
        {
            cell.Value = number;
            return;
        }

        if (options.NormalizeUnambiguousDates
            && TryGetApprovedDate(normalizedText, out var date, out var hasTime))
        {
            cell.Value = date;
            cell.Style.NumberFormat.Format = hasTime ? DateTimeFormat : DateFormat;
            return;
        }

        if (!string.Equals(cell.GetString(), normalizedText, StringComparison.Ordinal))
        {
            cell.Value = normalizedText;
        }
    }

    private static FormatStandardizationResult FailureAfterCleanup(
        OperationError error,
        string? stagingPath)
    {
        var cleanupError = TryCleanupStagingFile(stagingPath);
        return Failure(cleanupError ?? error);
    }

    private static void TryDeleteWithoutResult(string? stagingPath)
    {
        if (string.IsNullOrEmpty(stagingPath) || !File.Exists(stagingPath))
        {
            return;
        }

        try
        {
            File.Delete(stagingPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Report(
        IProgress<OperationProgress>? progress,
        OperationStage stage,
        int? percent,
        int? processedRows,
        int? totalRows) =>
        progress?.Report(new OperationProgress(stage, percent, processedRows, totalRows));

    private static int CalculatePercent(int processedRows, int totalRows) =>
        totalRows == 0 ? 100 : (int)((long)processedRows * 100 / totalRows);

    private static ValidationResult ValidationFailure(string message, string? detail = null) =>
        new(null, new OperationError(OperationErrorCode.InvalidConfiguration, message, detail));

    private static FormatStandardizationResult Failure(OperationError error) =>
        new(false, null, null, error);

    private sealed record ValidatedRequest(
        string InputFilePath,
        string? WorksheetName,
        int HeaderRowNumber,
        string OutputFilePath,
        string OutputDirectory,
        FormatStandardizationOptions Options,
        bool OverwriteExistingOutput);

    private sealed record ValidationResult(
        ValidatedRequest? Request,
        OperationError? Error);

}
