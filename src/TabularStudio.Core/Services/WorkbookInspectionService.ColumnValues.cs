using System.Globalization;
using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using static TabularStudio.Core.Services.WorkbookFileOperations;

namespace TabularStudio.Core.Services;

public sealed partial class WorkbookInspectionService
{
    public Task<ColumnValuesResult> GetColumnValuesAsync(ColumnValuesRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => ReadColumnValues(request, cancellationToken), cancellationToken);
    }

    private static ColumnValuesResult ReadColumnValues(ColumnValuesRequest? request, CancellationToken token)
    {
        static ColumnValuesResult Fail(OperationError error) => new(false, [], error);
        if (request?.Source is null || request.Column is null)
            return Fail(new(OperationErrorCode.InvalidConfiguration, "请选择主表及筛选列。"));
        var source = request.Source;
        var validation = ValidateFile(source.FilePath);
        if (validation is not null) return Fail(validation);
        if (!IsCsvPath(source.FilePath) && string.IsNullOrWhiteSpace(source.WorksheetName))
            return Fail(new(OperationErrorCode.InvalidConfiguration, "工作表名称不能为空。"));
        if (source.HeaderRowNumber < 1 || source.HeaderRowNumber > XLHelper.MaxRowNumber)
            return Fail(new(OperationErrorCode.InvalidHeaderRow, "表头行号无效。"));
        var opened = TryOpenWorkbook(source.FilePath, token);
        if (opened.Error is not null) return Fail(opened.Error);
        using var stream = opened.Stream!;
        using var workbook = opened.Workbook!;
        try
        {
            var sheet = workbook.FindSheet(source.WorksheetName);
            if (sheet is null) return Fail(new(OperationErrorCode.WorksheetNotFound, "指定的工作表不存在。", source.WorksheetName));
            var cells = new List<IXLCell>();
            foreach (var cell in workbook.ContentCells(sheet))
            {
                token.ThrowIfCancellationRequested();
                if (cell.Address.RowNumber >= source.HeaderRowNumber) cells.Add(cell);
            }
            if (cells.Count == 0) return Fail(new(OperationErrorCode.InvalidHeaderRow, "表头及其下方没有数据。"));
            var column = request.Column.ColumnNumber;
            if (column < cells.Min(c => c.Address.ColumnNumber) || column > cells.Max(c => c.Address.ColumnNumber))
                return Fail(new(OperationErrorCode.ColumnNotFound, "筛选列不在主表数据区域内。"));
            var values = new List<ColumnValueOption>();
            var seen = new HashSet<ColumnFilterValue>();
            foreach (var cell in cells.Where(c => c.Address.ColumnNumber == column && c.Address.RowNumber > source.HeaderRowNumber)
                         .OrderBy(c => c.Address.RowNumber))
            {
                token.ThrowIfCancellationRequested();
                if (cell.HasFormula)
                    return Fail(new(OperationErrorCode.FormulaCellNotAllowedForMatching,
                        "主表筛选列包含公式，无法读取候选值。",
                        $"File={source.FilePath}; Sheet={source.WorksheetName}; Header={source.HeaderRowNumber}; Cell={cell.Address}"));
                if (cell.DataType == XLDataType.Blank || (cell.DataType == XLDataType.Text && string.IsNullOrWhiteSpace(cell.GetString()))) continue;
                var value = cell.DataType switch
                {
                    XLDataType.Text => new ColumnFilterValue(ColumnValueKind.Text, cell.GetString()),
                    XLDataType.Number => new(ColumnValueKind.Number, cell.GetDouble().ToString("R", CultureInfo.InvariantCulture)),
                    XLDataType.Boolean => new(ColumnValueKind.Boolean, cell.GetBoolean().ToString()),
                    XLDataType.DateTime => new(ColumnValueKind.DateTime, cell.GetDateTime().ToString("O", CultureInfo.InvariantCulture),
                        DataMatchingService.HasTimeComponent(cell, cell.GetDateTime())),
                    XLDataType.TimeSpan => new(ColumnValueKind.TimeSpan, cell.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture)),
                    XLDataType.Error => new(ColumnValueKind.Error, cell.Value.GetError().ToString()),
                    _ => throw new InvalidOperationException("Unsupported cell value type.")
                };
                if (seen.Add(value)) values.Add(new(workbook.Display(cell) ?? value.RawValue, value));
            }
            // Only ambiguous display labels need extra context; never deduplicate formatted text.
            var collisions = values.GroupBy(v => v.DisplayText, StringComparer.Ordinal)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            return new(true, values.Select(v => collisions.Contains(v.DisplayText)
                ? v with { DisplayText = $"{v.DisplayText} [{KindLabel(v.Value.Kind)}: {v.Value.RawValue}{(v.Value.HasTime ? "; 含时间" : "")}]" }
                : v).ToArray(), null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Fail(CreateProcessingFailedError(source.FilePath, ex)); }
    }

    private static string KindLabel(ColumnValueKind kind) => kind switch
    {
        ColumnValueKind.Text => "文本", ColumnValueKind.Number => "数字", ColumnValueKind.Boolean => "布尔",
        ColumnValueKind.DateTime => "日期", ColumnValueKind.TimeSpan => "时长", _ => "错误"
    };
}
