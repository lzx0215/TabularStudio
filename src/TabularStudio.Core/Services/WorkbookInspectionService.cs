using System.Globalization;
using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using static TabularStudio.Core.Services.WorkbookFileOperations;

namespace TabularStudio.Core.Services;

public sealed class WorkbookInspectionService : IWorkbookInspectionService
{
    private const int PreviewRowLimit = 20;
    private const int SharingViolation = 32;
    private const int LockViolation = 33;

    public Task<WorkbookInspectionResult> InspectAsync(
        WorkbookInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() => Inspect(request, cancellationToken), cancellationToken);
    }

    public Task<WorksheetPreviewResult> GetPreviewAsync(
        WorksheetPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() => GetPreview(request, cancellationToken), cancellationToken);
    }

    private static WorkbookInspectionResult Inspect(
        WorkbookInspectionRequest? request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateFile(request?.FilePath);
        if (validationError is not null)
        {
            return InspectionFailure(validationError);
        }

        var filePath = request!.FilePath;
        cancellationToken.ThrowIfCancellationRequested();

        var openResult = TryOpenWorkbook(filePath, cancellationToken);
        if (openResult.Error is not null)
        {
            return InspectionFailure(openResult.Error);
        }

        using var stream = openResult.Stream!;
        using var workbook = openResult.Workbook!;

        try
        {
            var worksheets = new List<WorksheetInfo>();
            foreach (var worksheet in workbook.Worksheets.Where(_ => !workbook.IsCsv))
            {
                cancellationToken.ThrowIfCancellationRequested();
                worksheets.Add(new WorksheetInfo(worksheet.Name));
            }

            return new WorkbookInspectionResult(true, worksheets, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return InspectionFailure(CreateProcessingFailedError(filePath, exception));
        }
    }

    private static WorksheetPreviewResult GetPreview(
        WorksheetPreviewRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Source is null)
        {
            return PreviewFailure(new OperationError(
                OperationErrorCode.InvalidConfiguration,
                "工作表预览配置不能为空。"));
        }

        var source = request.Source;
        var validationError = ValidateFile(source.FilePath);
        if (validationError is not null)
        {
            return PreviewFailure(validationError);
        }

        if (!IsCsvPath(source.FilePath) && string.IsNullOrWhiteSpace(source.WorksheetName))
        {
            return PreviewFailure(new OperationError(
                OperationErrorCode.InvalidConfiguration,
                "工作表名称不能为空。"));
        }

        if (source.HeaderRowNumber < 1)
        {
            return PreviewFailure(new OperationError(
                OperationErrorCode.InvalidHeaderRow,
                "表头行号必须大于或等于 1。",
                source.HeaderRowNumber.ToString(CultureInfo.InvariantCulture)));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var openResult = TryOpenWorkbook(source.FilePath, cancellationToken);
        if (openResult.Error is not null)
        {
            return PreviewFailure(openResult.Error);
        }

        using var stream = openResult.Stream!;
        using var workbook = openResult.Workbook!;

        try
        {
            var worksheet = workbook.FindSheet(source.WorksheetName);

            if (worksheet is null)
            {
                return PreviewFailure(new OperationError(
                    OperationErrorCode.WorksheetNotFound,
                    "指定的工作表不存在。",
                    source.WorksheetName));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var contentCells = workbook.ContentCells(worksheet)
                .Where(cell => cell.Address.RowNumber >= source.HeaderRowNumber)
                .ToArray();

            if (contentCells.Length == 0)
            {
                return PreviewFailure(new OperationError(
                    OperationErrorCode.InvalidHeaderRow,
                    "指定表头行及其下方没有可预览的数据区域。",
                    source.HeaderRowNumber.ToString(CultureInfo.InvariantCulture)));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var firstColumnNumber = contentCells.Min(cell => cell.Address.ColumnNumber);
            var lastColumnNumber = contentCells.Max(cell => cell.Address.ColumnNumber);
            var lastContentRowNumber = contentCells.Max(cell => cell.Address.RowNumber);

            var columns = new List<ColumnReference>(lastColumnNumber - firstColumnNumber + 1);
            for (var columnNumber = firstColumnNumber; columnNumber <= lastColumnNumber; columnNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var headerText = workbook.Display(worksheet.Cell(source.HeaderRowNumber, columnNumber));
                columns.Add(new ColumnReference(columnNumber, headerText));
            }

            var rows = new List<PreviewRow>();
            var firstPreviewRowNumber = source.HeaderRowNumber + 1;
            var lastPreviewRowNumber = Math.Min(
                lastContentRowNumber,
                source.HeaderRowNumber + PreviewRowLimit);

            for (var rowNumber = firstPreviewRowNumber; rowNumber <= lastPreviewRowNumber; rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cells = new List<PreviewCell>(columns.Count);
                foreach (var column in columns)
                {
                    var cell = worksheet.Cell(rowNumber, column.ColumnNumber);
                    cells.Add(new PreviewCell(column.ColumnNumber, workbook.Display(cell)));
                }

                rows.Add(new PreviewRow(rowNumber, cells));
            }

            var preview = new PreviewTable(
                workbook.IsCsv ? null : worksheet.Name,
                source.HeaderRowNumber,
                columns,
                rows);

            return new WorksheetPreviewResult(true, preview, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return PreviewFailure(CreateProcessingFailedError(source.FilePath, exception));
        }
    }

    private static OperationError? ValidateFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new OperationError(
                OperationErrorCode.InvalidConfiguration,
                "Excel 文件路径不能为空。");
        }

        if (!File.Exists(filePath))
        {
            return new OperationError(
                OperationErrorCode.FileNotFound,
                "Excel 文件不存在。",
                filePath);
        }

        if (!IsSupportedPath(filePath))
        {
            return new OperationError(
                OperationErrorCode.UnsupportedFileType,
                "仅支持 .xlsx/.xls/.csv 文件。",
                filePath);
        }

        return null;
    }

    private static string? GetHeaderText(IXLCell cell)
    {
        if (cell.IsEmpty(XLCellsUsedOptions.Contents))
        {
            return null;
        }

        var text = GetDisplayValue(cell);
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? GetDisplayValue(IXLCell cell)
    {
        if (cell.IsEmpty(XLCellsUsedOptions.Contents))
        {
            return null;
        }

        return cell.HasFormula
            ? cell.CachedValue.ToString(CultureInfo.CurrentCulture)
            : cell.GetFormattedString(CultureInfo.CurrentCulture);
    }

    private static WorkbookInspectionResult InspectionFailure(OperationError error) =>
        new(false, [], error);

    private static WorksheetPreviewResult PreviewFailure(OperationError error) =>
        new(false, null, error);

    private static OperationError CreateProcessingFailedError(string filePath, Exception exception) =>
        new(
            OperationErrorCode.ProcessingFailed,
            "处理 Excel 工作簿时发生意外错误。",
            $"{filePath} ({exception.GetType().Name})");

}
