using System.Globalization;
using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using static TabularStudio.Core.Services.WorkbookFileOperations;

namespace TabularStudio.Core.Services;

public sealed class TableComparisonService : ITableComparisonService
{
    private static readonly ComparisonValue Empty = new("空", "");

    public Task<TableComparisonResult> CompareAsync(TableComparisonRequest request,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => Compare(request, progress, cancellationToken), cancellationToken);

    private static TableComparisonResult Compare(TableComparisonRequest request,
        IProgress<OperationProgress>? progress, CancellationToken token)
    {
        TableComparisonResult Fail(OperationError error) => new(false, request ?? new(new("", null), new("", null)), null, null, 0, [], error);
        try
        {
            token.ThrowIfCancellationRequested();
            if (request?.Left is null || request.Right is null ||
                string.IsNullOrWhiteSpace(request.Left.FilePath) || string.IsNullOrWhiteSpace(request.Right.FilePath))
                return Fail(new(OperationErrorCode.InvalidConfiguration, "请选择两张表格。"));
            request = new(new(Path.GetFullPath(request.Left.FilePath), request.Left.WorksheetName),
                new(Path.GetFullPath(request.Right.FilePath), request.Right.WorksheetName));
            foreach (var source in new[] { request.Left, request.Right })
            {
                if (!IsSupportedPath(source.FilePath))
                    return Fail(new(OperationErrorCode.UnsupportedFileType, "仅支持 .xlsx、.xls 和 UTF-8 .csv。"));
                if (!IsCsvPath(source.FilePath) && string.IsNullOrWhiteSpace(source.WorksheetName))
                    return Fail(new(OperationErrorCode.InvalidConfiguration, "请选择工作表。"));
            }
            progress?.Report(new(OperationStage.Reading, null, null, null));
            // Keep read locks through comparison so another writer cannot change either input mid-read.
            using var leftStream = new FileStream(request.Left.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var rightStream = new FileStream(request.Right.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var leftBook = new TabularWorkbook(leftStream, request.Left.FilePath, token);
            using var rightBook = new TabularWorkbook(rightStream, request.Right.FilePath, token);
            var leftSheet = leftBook.FindSheet(request.Left.WorksheetName);
            var rightSheet = rightBook.FindSheet(request.Right.WorksheetName);
            if (leftSheet is null || rightSheet is null)
                return Fail(new(OperationErrorCode.WorksheetNotFound, "所选工作表不存在，请重新选择。"));
            var left = Snapshot(leftBook, leftSheet, token);
            var right = Snapshot(rightBook, rightSheet, token);
            var leftExtent = Extent(left);
            var rightExtent = Extent(right);
            var differences = new List<CellDifference>();
            var addresses = left.Keys.Union(right.Keys).Order().ToArray();
            for (var i = 0; i < addresses.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var position = addresses[i];
                var a = left.GetValueOrDefault(position, Empty);
                var b = right.GetValueOrDefault(position, Empty);
                if (a != b) differences.Add(new(Address(position), a, b));
                if (i % 1000 == 0)
                    progress?.Report(new(OperationStage.Processing, (int)((long)i * 100 / addresses.Length), null, null));
            }
            token.ThrowIfCancellationRequested();
            progress?.Report(new(OperationStage.Completed, 100, null, null));
            return new(true, request, leftExtent, rightExtent,
                (long)Math.Max(leftExtent.Rows, rightExtent.Rows) * Math.Max(leftExtent.Columns, rightExtent.Columns),
                differences.AsReadOnly(), null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Fail(ReadError(ex)); }
    }

    private static Dictionary<long, ComparisonValue> Snapshot(TabularWorkbook book, IXLWorksheet sheet, CancellationToken token)
    {
        var result = new Dictionary<long, ComparisonValue>();
        // Sparse traversal avoids materializing an entire rectangular grid for distant cells.
        foreach (var cell in sheet.CellsUsed(XLCellsUsedOptions.Contents))
        {
            token.ThrowIfCancellationRequested();
            var value = Canonical(book.ComparisonValue(cell));
            if (value == Empty) continue;
            result.Add((long)(cell.Address.RowNumber - 1) * XLHelper.MaxColumnNumber + cell.Address.ColumnNumber - 1, value);
        }
        return result;
    }

    private static ComparisonValue Canonical(XLCellValue value) => value.Type switch
    {
        XLDataType.Blank => Empty,
        XLDataType.Text => value.GetText().Length == 0 ? Empty : new("文本", value.GetText()),
        XLDataType.Number => Number(value.GetNumber()),
        XLDataType.DateTime => Number(value.GetUnifiedNumber()),
        XLDataType.TimeSpan => Number(value.GetUnifiedNumber()),
        XLDataType.Boolean => new("布尔", value.GetBoolean() ? "TRUE" : "FALSE"),
        XLDataType.Error => new("错误", value.GetError().ToString()),
        _ => throw new InvalidDataException("无法比较的单元格类型。")
    };

    private static ComparisonValue Number(double value) => new("数字", (value == 0 ? 0 : value).ToString("R", CultureInfo.InvariantCulture));
    private static string Address(long position) => XLHelper.GetColumnLetterFromNumber((int)(position % XLHelper.MaxColumnNumber) + 1)
        + (position / XLHelper.MaxColumnNumber + 1).ToString(CultureInfo.InvariantCulture);
    private static ComparisonExtent Extent(Dictionary<long, ComparisonValue> cells) => cells.Count == 0 ? new(0, 0) :
        new((int)(cells.Keys.Max() / XLHelper.MaxColumnNumber) + 1, (int)cells.Keys.Max(p => p % XLHelper.MaxColumnNumber) + 1);

    private static OperationError ReadError(Exception ex) => ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => new(OperationErrorCode.FileNotFound, "输入文件不存在，请重新选择。"),
        IOException io when IsFileLocked(io) => new(OperationErrorCode.FileLocked, "输入文件被占用，请关闭编辑程序后重试。"),
        InvalidDataException => new(OperationErrorCode.WorkbookUnreadable, ex.Message),
        ArgumentException or NotSupportedException => new(OperationErrorCode.InvalidConfiguration, "文件路径或配置无效。"),
        _ => new(OperationErrorCode.WorkbookUnreadable, "无法读取或比较表格，请检查文件格式、内容及访问权限。", ex.GetType().Name)
    };

    public Task<OperationError?> ExportAsync(TableComparisonResult result, string outputFilePath,
        CancellationToken cancellationToken = default) => Task.Run(() => Export(result, outputFilePath, cancellationToken), cancellationToken);

    private static OperationError? Export(TableComparisonResult result, string path, CancellationToken token)
    {
        string? staging = null;
        OperationError? error = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!result.Success) return new(OperationErrorCode.InvalidConfiguration, "没有可导出的完整比较结果。");
            path = Path.GetFullPath(path);
            if (new[] { result.Request.Left.FilePath, result.Request.Right.FilePath }
                .Any(p => string.Equals(Path.GetFullPath(p), path, StringComparison.OrdinalIgnoreCase)))
                return new(OperationErrorCode.OutputConflictsWithInput, "报告不能覆盖输入文件。请使用新文件名。" );
            if (!IsXlsxPath(path)) return new(OperationErrorCode.UnsupportedFileType, "比较报告须保存为 .xlsx。" );
            if (File.Exists(path)) return new(OperationErrorCode.OutputAlreadyExists, "文件已存在，请选择新的报告文件名。" );
            var reservation = ReserveStagingFile(path);
            if (reservation.Error is not null) return reservation.Error;
            staging = reservation.Path;
            using var report = new XLWorkbook();
            var summary = report.AddWorksheet("比较结果");
            string[][] rows =
            [
                ["结论", result.AreEqual ? "数据完全一致" : "数据不一致"],
                ["差异单元格数", result.Differences.Count.ToString(CultureInfo.InvariantCulture)],
                ["比较位置数（含空白）", result.ComparedCellCount.ToString(CultureInfo.InvariantCulture)],
                ["表一文件", result.Request.Left.FilePath], ["表一工作表", result.Request.Left.WorksheetName ?? "CSV"],
                ["表一数据范围", $"{result.LeftExtent!.Rows} 行 × {result.LeftExtent.Columns} 列"],
                ["表二文件", result.Request.Right.FilePath], ["表二工作表", result.Request.Right.WorksheetName ?? "CSV"],
                ["表二数据范围", $"{result.RightExtent!.Rows} 行 × {result.RightExtent.Columns} 列"],
                ["口径", "从 A1 按位置比较，含表头，保留类型/空格/大小写差异，忽略样式和末尾纯空行列。"],
                ["公式", "只比较文件中已保存的计算结果，不执行公式；请先重新计算并保存。"],
                ["提示", "报告记录本次比较时的数据快照；输入文件后续修改不会更新此报告。"]
            ];
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++) summary.Cell(r + 1, c + 1).Value = rows[r][c];
            summary.Column(1).Width = 28; summary.Column(2).Width = 95;
            summary.Column(2).Style.Alignment.WrapText = true;
            summary.Column(1).Style.Font.Bold = true;
            IXLWorksheet? detail = null;
            for (var i = 0; i < Math.Max(1, result.Differences.Count); i++)
            {
                token.ThrowIfCancellationRequested();
                var row = i % (XLHelper.MaxRowNumber - 1) + 2;
                if (row == 2)
                {
                    detail = report.AddWorksheet($"差异明细{i / (XLHelper.MaxRowNumber - 1) + 1}");
                    string[] headers = ["位置", "表一类型", "表一值", "表二类型", "表二值"];
                    for (var c = 0; c < headers.Length; c++) detail.Cell(1, c + 1).Value = headers[c];
                    detail.Row(1).Style.Font.Bold = true;
                    detail.SheetView.FreezeRows(1);
                    detail.Columns(1, 5).Width = 16;
                    detail.Column(3).Width = 45; detail.Column(5).Width = 45;
                }
                if (i >= result.Differences.Count) break;
                var diff = result.Differences[i];
                string[] values = [diff.Address, diff.Left.Type, diff.Left.Value, diff.Right.Type, diff.Right.Value];
                for (var c = 0; c < values.Length; c++)
                    detail!.Cell(row, c + 1).Value = values[c]; // Explicit text values; never interpreted as formulas.
            }
            report.SaveAs(staging);
            token.ThrowIfCancellationRequested();
            error = CommitStagingFile(staging!, path, false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { error = new(OperationErrorCode.ProcessingFailed, "导出失败，请检查路径、文件占用和可用空间。", ex.GetType().Name); }
        finally
        {
            var cleanupError = TryCleanupStagingFile(staging);
            if (cleanupError is not null) error = cleanupError;
        }
        return error;
    }
}
