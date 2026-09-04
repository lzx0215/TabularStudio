using System.Diagnostics;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using TabularStudio.Core.Contracts;

namespace TabularStudio.Core.Services;

public sealed class FormatStandardizationService : IFormatStandardizationService
{
    private const int ExcelMaximumSafeSignificantDigits = 15;
    private const int SharingViolation = 32;
    private const int LockViolation = 33;
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly char[] ApprovedOuterWhitespace = [' ', '\u00A0', '\u3000'];

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

            var workbookOpen = TryOpenWorkbook(validated.InputFilePath);
            if (workbookOpen.Error is not null)
            {
                return FailureAfterCleanup(workbookOpen.Error, stagingPath);
            }

            using var inputStream = workbookOpen.Stream!;
            using var workbook = workbookOpen.Workbook!;

            cancellationToken.ThrowIfCancellationRequested();

            var worksheet = workbook.Worksheets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, validated.WorksheetName, StringComparison.Ordinal));

            if (worksheet is null)
            {
                return FailureAfterCleanup(new OperationError(
                    OperationErrorCode.WorksheetNotFound,
                    "指定的工作表不存在。",
                    validated.WorksheetName), stagingPath);
            }

            var contentAtOrAfterHeader = worksheet
                .CellsUsed(XLCellsUsedOptions.Contents)
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
                workbook.SaveAs(stagingStream);
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
                worksheet.Name,
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

        if (string.IsNullOrWhiteSpace(source.WorksheetName))
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

        if (!IsXlsxPath(inputFilePath) || !IsXlsxPath(outputFilePath))
        {
            return new ValidationResult(null, new OperationError(
                OperationErrorCode.UnsupportedFileType,
                "输入和输出文件都必须使用 .xlsx 扩展名。"));
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

    private static string NormalizeText(string value, FormatStandardizationOptions options)
    {
        var normalized = value;

        if (options.NormalizeUnicode)
        {
            normalized = normalized.Normalize(NormalizationForm.FormKC);
        }

        if (options.NormalizeFullWidthHalfWidth)
        {
            normalized = ConvertApprovedFullWidthCharacters(normalized);
        }

        if (options.RemoveTabsNewLinesAndHiddenCharacters)
        {
            normalized = RemoveApprovedHiddenCharacters(normalized);
        }

        if (options.TrimOuterWhitespace)
        {
            normalized = normalized.Trim(ApprovedOuterWhitespace);
        }

        return normalized;
    }

    private static string ConvertApprovedFullWidthCharacters(string value)
    {
        StringBuilder? builder = null;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var converted = character switch
            {
                >= '\uFF01' and <= '\uFF5E' => (char)(character - 0xFEE0),
                '\u3000' => ' ',
                _ => character
            };

            if (converted == character && builder is null)
            {
                continue;
            }

            builder ??= new StringBuilder(value.Length).Append(value, 0, index);
            builder.Append(converted);
        }

        return builder?.ToString() ?? value;
    }

    private static string RemoveApprovedHiddenCharacters(string value)
    {
        StringBuilder? builder = null;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var shouldRemove = character <= '\u001F'
                || character == '\u007F'
                || character == '\u200B'
                || character == '\uFEFF';

            if (!shouldRemove && builder is null)
            {
                continue;
            }

            builder ??= new StringBuilder(value.Length).Append(value, 0, index);
            if (!shouldRemove)
            {
                builder.Append(character);
            }
        }

        return builder?.ToString() ?? value;
    }

    private static bool TryGetSafeExcelNumber(string value, out double number)
    {
        number = default;
        if (value.Length == 0)
        {
            return false;
        }

        var startIndex = value[0] == '-' ? 1 : 0;
        if (startIndex == value.Length || value[0] == '+')
        {
            return false;
        }

        var decimalPointIndex = -1;
        for (var index = startIndex; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '.')
            {
                if (decimalPointIndex >= 0)
                {
                    return false;
                }

                decimalPointIndex = index;
                continue;
            }

            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        var integerEndIndex = decimalPointIndex >= 0 ? decimalPointIndex : value.Length;
        var integerDigitCount = integerEndIndex - startIndex;
        if (integerDigitCount == 0
            || (decimalPointIndex >= 0 && decimalPointIndex == value.Length - 1))
        {
            return false;
        }

        if (integerDigitCount > 1 && value[startIndex] == '0')
        {
            return false;
        }

        var significantDigitCount = CountSignificantDigits(value, startIndex);
        if (significantDigitCount > ExcelMaximumSafeSignificantDigits)
        {
            return false;
        }

        if (!decimal.TryParse(
                value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var decimalValue))
        {
            return false;
        }

        if (decimalValue == decimal.Zero && value[0] == '-')
        {
            return false;
        }

        var doubleValue = (double)decimalValue;
        if (!double.IsFinite(doubleValue))
        {
            return false;
        }

        var roundTripText = doubleValue.ToString("G15", CultureInfo.InvariantCulture);
        if (!decimal.TryParse(
                roundTripText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var roundTripValue)
            || roundTripValue != decimalValue)
        {
            return false;
        }

        number = doubleValue;
        return true;
    }

    private static int CountSignificantDigits(string value, int startIndex)
    {
        var firstNonZeroFound = false;
        var count = 0;

        for (var index = startIndex; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '.')
            {
                continue;
            }

            if (!firstNonZeroFound && character == '0')
            {
                continue;
            }

            firstNonZeroFound = true;
            count++;
        }

        return count == 0 ? 1 : count;
    }

    private static bool TryGetApprovedDate(
        string value,
        out DateTime date,
        out bool hasTime)
    {
        if (DateTime.TryParseExact(
                value,
                [DateTimeFormat.Replace('-', '/'), DateTimeFormat],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            hasTime = true;
            return true;
        }

        if (DateTime.TryParseExact(
                value,
                [DateFormat.Replace('-', '/'), DateFormat],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            hasTime = false;
            return true;
        }

        hasTime = false;
        return false;
    }

    private static OperationError? ProbeExistingOutput(string outputFilePath)
    {
        try
        {
            using var stream = new FileStream(
                outputFilePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return null;
        }
        catch (IOException exception) when (IsFileLocked(exception))
        {
            return new OperationError(
                OperationErrorCode.FileLocked,
                "输出文件正在被其它程序占用。",
                outputFilePath);
        }
        catch (UnauthorizedAccessException)
        {
            return new OperationError(
                OperationErrorCode.OutputDirectoryNotWritable,
                "无法覆盖输出文件。",
                outputFilePath);
        }
        catch (IOException exception)
        {
            return new OperationError(
                OperationErrorCode.ProcessingFailed,
                "检查已有输出文件时失败。",
                exception.GetType().Name);
        }
    }

    private static StagingReservation ReserveStagingFile(string outputFilePath)
    {
        var outputDirectory = Path.GetDirectoryName(outputFilePath)!;
        var outputName = Path.GetFileNameWithoutExtension(outputFilePath);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var stagingPath = Path.Combine(
                outputDirectory,
                $".{outputName}.{Guid.NewGuid():N}.staging.xlsx");

            try
            {
                using var stream = new FileStream(
                    stagingPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                return new StagingReservation(stagingPath, null);
            }
            catch (UnauthorizedAccessException)
            {
                return new StagingReservation(null, new OperationError(
                    OperationErrorCode.OutputDirectoryNotWritable,
                    "无法写入输出目录。",
                    outputDirectory));
            }
            catch (IOException) when (attempt < 2)
            {
            }
            catch (IOException exception)
            {
                return new StagingReservation(null, new OperationError(
                    OperationErrorCode.OutputDirectoryNotWritable,
                    "无法在输出目录创建临时结果文件。",
                    $"{outputDirectory} ({exception.GetType().Name})"));
            }
        }

        return new StagingReservation(null, new OperationError(
            OperationErrorCode.OutputDirectoryNotWritable,
            "无法在输出目录创建临时结果文件。",
            outputDirectory));
    }

    private static WorkbookOpenResult TryOpenWorkbook(string filePath)
    {
        FileStream? stream = null;

        try
        {
            stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var workbook = new XLWorkbook(stream);
            return new WorkbookOpenResult(stream, workbook, null);
        }
        catch (FileNotFoundException)
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.FileNotFound, "输入 Excel 文件不存在。", filePath);
        }
        catch (DirectoryNotFoundException)
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.FileNotFound, "输入 Excel 文件不存在。", filePath);
        }
        catch (IOException exception) when (IsFileLocked(exception))
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.FileLocked, "输入文件正在被其它程序独占占用。", filePath);
        }
        catch (UnauthorizedAccessException)
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.WorkbookUnreadable, "输入工作簿无法读取。", filePath);
        }
        catch (IOException)
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.WorkbookUnreadable, "输入工作簿无法读取或已经损坏。", filePath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stream?.Dispose();
            return WorkbookOpenFailure(OperationErrorCode.WorkbookUnreadable, "输入工作簿无法读取或已经损坏。", filePath);
        }
    }

    private static OperationError? CommitStagingFile(
        string stagingPath,
        string outputFilePath,
        bool overwriteExistingOutput)
    {
        try
        {
            File.Move(stagingPath, outputFilePath, overwriteExistingOutput);
            return null;
        }
        catch (IOException exception) when (IsFileLocked(exception))
        {
            return new OperationError(
                OperationErrorCode.FileLocked,
                "输出文件正在被其它程序占用。",
                outputFilePath);
        }
        catch (IOException) when (!overwriteExistingOutput && File.Exists(outputFilePath))
        {
            return new OperationError(
                OperationErrorCode.OutputAlreadyExists,
                "输出文件已存在，且本次未确认覆盖。",
                outputFilePath);
        }
        catch (UnauthorizedAccessException)
        {
            return new OperationError(
                OperationErrorCode.OutputDirectoryNotWritable,
                "无法提交输出文件。",
                outputFilePath);
        }
        catch (IOException exception)
        {
            return new OperationError(
                OperationErrorCode.ProcessingFailed,
                "提交输出文件失败。",
                exception.GetType().Name);
        }
    }

    private static FormatStandardizationResult FailureAfterCleanup(
        OperationError error,
        string? stagingPath)
    {
        var cleanupError = TryCleanupStagingFile(stagingPath);
        return Failure(cleanupError ?? error);
    }

    private static OperationError? TryCleanupStagingFile(string? stagingPath)
    {
        if (string.IsNullOrEmpty(stagingPath) || !File.Exists(stagingPath))
        {
            return null;
        }

        try
        {
            File.Delete(stagingPath);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new OperationError(
                OperationErrorCode.IncompleteOutputCleanupFailed,
                "处理失败，且临时结果文件无法清理。",
                stagingPath);
        }
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

    private static bool IsXlsxPath(string path) =>
        string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase);

    private static bool IsFileLocked(IOException exception)
    {
        var nativeErrorCode = exception.HResult & 0xFFFF;
        return nativeErrorCode is SharingViolation or LockViolation;
    }

    private static ValidationResult ValidationFailure(string message, string? detail = null) =>
        new(null, new OperationError(OperationErrorCode.InvalidConfiguration, message, detail));

    private static FormatStandardizationResult Failure(OperationError error) =>
        new(false, null, null, error);

    private static WorkbookOpenResult WorkbookOpenFailure(
        OperationErrorCode code,
        string message,
        string detail) =>
        new(null, null, new OperationError(code, message, detail));

    private sealed record ValidatedRequest(
        string InputFilePath,
        string WorksheetName,
        int HeaderRowNumber,
        string OutputFilePath,
        string OutputDirectory,
        FormatStandardizationOptions Options,
        bool OverwriteExistingOutput);

    private sealed record ValidationResult(
        ValidatedRequest? Request,
        OperationError? Error);

    private sealed record StagingReservation(
        string? Path,
        OperationError? Error);

    private sealed record WorkbookOpenResult(
        FileStream? Stream,
        XLWorkbook? Workbook,
        OperationError? Error);
}
