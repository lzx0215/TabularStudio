using System.Security.Cryptography;
using ClosedXML.Excel;
using NPOI.HSSF.UserModel;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.Tests;

public sealed class TableComparisonTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TabularComparison-" + Guid.NewGuid().ToString("N"));
    private readonly TableComparisonService service = new();
    public TableComparisonTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string PathFor(string name) => Path.Combine(directory, name);
    private static ComparisonSource Source(string path, string sheet = "Data") => new(path, path.EndsWith(".csv") ? null : sheet);
    private async Task<TableComparisonResult> Compare(string a, string b) => await service.CompareAsync(new(Source(a), Source(b)));
    private string Xlsx(string name, Action<IXLWorksheet>? fill = null, bool calculate = false)
    {
        var path = PathFor(name + ".xlsx");
        using var book = new XLWorkbook();
        fill?.Invoke(book.AddWorksheet("Data"));
        if (!book.Worksheets.Any()) book.AddWorksheet("Data");
        book.SaveAs(path, new SaveOptions { EvaluateFormulasBeforeSaving = calculate });
        return path;
    }

    public static IEnumerable<object[]> Pairs() => from a in new[] { ".xlsx", ".xls", ".csv" }
        from b in new[] { ".xlsx", ".xls", ".csv" } select new object[] { a, b };

    private string TextFile(string name, string extension)
    {
        var path = PathFor(name + extension);
        if (extension == ".csv") File.WriteAllText(path, "姓名,编号\r\n张三,001\r\n");
        else if (extension == ".xlsx") return Xlsx(name, s =>
        { s.Cell("A1").Value = "姓名"; s.Cell("B1").Value = "编号"; s.Cell("A2").Value = "张三"; s.Cell("B2").Value = "001"; });
        else
        {
            using var book = new HSSFWorkbook(); var sheet = book.CreateSheet("Data");
            var row = sheet.CreateRow(0); row.CreateCell(0).SetCellValue("姓名"); row.CreateCell(1).SetCellValue("编号");
            row = sheet.CreateRow(1); row.CreateCell(0).SetCellValue("张三"); row.CreateCell(1).SetCellValue("001");
            using var output = File.Create(path); book.Write(output, true);
        }
        return path;
    }

    [Theory, MemberData(nameof(Pairs))]
    public async Task AllFormatPairsCompareTextAndPreserveInputBytes(string a, string b)
    {
        var left = TextFile("left", a); var right = TextFile("right", b);
        var leftHash = SHA256.HashData(File.ReadAllBytes(left)); var rightHash = SHA256.HashData(File.ReadAllBytes(right));
        var result = await Compare(left, right);
        Assert.True(result.Success, result.Error?.ToString()); Assert.True(result.AreEqual);
        Assert.Equal(4, result.ComparedCellCount);
        Assert.Equal(leftHash, SHA256.HashData(File.ReadAllBytes(left)));
        Assert.Equal(rightHash, SHA256.HashData(File.ReadAllBytes(right)));
    }

    [Fact]
    public async Task StylesNumberFormatsAndTrailingBlankCellsDoNotChangeEquality()
    {
        var left = Xlsx("plain", s => { s.Cell("A1").Value = 45000d; s.Cell("B2").Value = "same"; });
        var right = Xlsx("styled", s =>
        {
            s.Cell("A1").Value = 45000d; s.Cell("A1").Style.DateFormat.Format = "yyyy-mm-dd";
            s.Cell("B2").Value = "same"; s.Cell("B2").Style.Font.Bold = true;
            s.Cell("B2").Style.Fill.BackgroundColor = XLColor.Red;
            s.Cell("Z100").Style.Border.BottomBorder = XLBorderStyleValues.Thick;
            s.Row(2).Hide(); s.Column(2).Width = 50;
        });
        var result = await Compare(left, right);
        Assert.True(result.AreEqual, result.Error?.ToString()); Assert.Equal(new(2, 2), result.RightExtent);
    }

    [Fact]
    public async Task StrictTypesWhitespaceCaseHeadersAndMissingCellsHaveExactCoordinates()
    {
        var left = Xlsx("left", s =>
        {
            s.Cell("A1").Value = "标题"; s.Cell("A2").Value = 1; s.Cell("B2").Value = "abc";
            s.Cell("C2").Value = "x "; s.Cell("D2").Value = true; s.Cell("A3").Value = "only left";
        });
        var right = Xlsx("right", s =>
        {
            s.Cell("A1").Value = "Title"; s.Cell("A2").Value = "1"; s.Cell("B2").Value = "ABC";
            s.Cell("C2").Value = "x"; s.Cell("D2").Value = 1; s.Cell("B4").Value = "only right";
        });
        var result = await Compare(left, right);
        Assert.True(result.Success); Assert.False(result.AreEqual);
        Assert.Equal(new[] { "A1", "A2", "B2", "C2", "D2", "A3", "B4" }, result.Differences.Select(d => d.Address));
        Assert.Equal("数字", result.Differences[1].Left.Type); Assert.Equal("文本", result.Differences[1].Right.Type);
        Assert.Equal("空", result.Differences[^1].Left.Type); Assert.Equal(16, result.ComparedCellCount);
    }

    [Fact]
    public async Task SwappedRowsAndColumnsAreNotMatchedByValue()
    {
        var left = Xlsx("left", s => { s.Cell("A1").Value = "a"; s.Cell("B2").Value = "b"; });
        var right = Xlsx("right", s => { s.Cell("B1").Value = "a"; s.Cell("A2").Value = "b"; });
        var result = await Compare(left, right);
        Assert.Equal(4, result.Differences.Count);
    }

    [Fact]
    public async Task EmptyTablesAndTrailingEmptyCsvRecordsAreEqual()
    {
        var left = Xlsx("empty"); var right = PathFor("empty.csv"); File.WriteAllText(right, ",\r\n\r\n");
        var result = await Compare(left, right);
        Assert.True(result.AreEqual); Assert.Equal(0, result.ComparedCellCount);
    }

    [Fact]
    public async Task SparseExtremeCoordinateDoesNotEnumerateTheFullRectangle()
    {
        var left = Xlsx("sparse", s => s.Cell("XFD1048576").Value = "far");
        var right = Xlsx("empty");
        var result = await Compare(left, right);
        Assert.True(result.Success); Assert.Equal("XFD1048576", Assert.Single(result.Differences).Address);
        Assert.Equal(17179869184L, result.ComparedCellCount);
    }

    [Fact]
    public async Task FormulaUsesSavedResultNotExpression()
    {
        var left = Xlsx("formula", s => { s.Cell("A1").FormulaA1 = "1+1"; s.Cell("B1").FormulaA1 = "\"\""; }, true);
        var right = Xlsx("value", s => s.Cell("A1").Value = 2);
        var result = await Compare(left, right);
        Assert.True(result.AreEqual, result.Error?.ToString());
    }

    [Fact]
    public async Task MissingFormulaCacheFailsWithoutClaimingEquality()
    {
        var left = Xlsx("uncached", s => s.Cell("C3").FormulaA1 = "1+1");
        var result = await Compare(left, left);
        Assert.False(result.Success); Assert.False(result.AreEqual);
        Assert.Contains("C3", result.Error!.Message); Assert.Contains("重新计算", result.Error.Message);
    }

    [Fact]
    public async Task BinaryFormulaCachedResultIsComparedWithoutEvaluation()
    {
        var left = PathFor("formula.xls");
        using (var book = new HSSFWorkbook())
        {
            var cell = book.CreateSheet("Data").CreateRow(0).CreateCell(0); cell.SetCellFormula("1+1");
            new HSSFFormulaEvaluator(book).EvaluateFormulaCell(cell);
            using var output = File.Create(left); book.Write(output, true);
        }
        var right = Xlsx("value", s => s.Cell("A1").Value = 2);
        Assert.True((await Compare(left, right)).AreEqual);
    }

    [Fact]
    public async Task SameWorkbookDifferentSheetsAndSameSheetAreSupported()
    {
        var path = Xlsx("sheets", s => s.Cell("A1").Value = "a");
        using (var book = new XLWorkbook(path)) { book.AddWorksheet("Other").Cell("A1").Value = "b"; book.Save(); }
        Assert.True((await Compare(path, path)).AreEqual);
        var result = await service.CompareAsync(new(Source(path), Source(path, "Other")));
        Assert.Equal("A1", Assert.Single(result.Differences).Address);
    }

    [Fact]
    public async Task ReportContainsEveryDifferenceAsTextAndPreservesSources()
    {
        var left = Xlsx("left", s => { for (var r = 1; r <= 1005; r++) s.Cell(r, 1).Value = "=1+1"; });
        var right = Xlsx("right"); var hash = SHA256.HashData(File.ReadAllBytes(left));
        var result = await Compare(left, right); var path = PathFor("report.xlsx");
        Assert.Null(await service.ExportAsync(result, path));
        using var report = new XLWorkbook(path);
        var details = report.Worksheet("差异明细1");
        Assert.Equal(1006, details.LastRowUsed()!.RowNumber());
        Assert.Equal("A1005", details.Cell(1006, 1).GetString());
        Assert.Equal("=1+1", details.Cell(2, 3).GetString()); Assert.False(details.Cell(2, 3).HasFormula);
        Assert.Equal("数据不一致", report.Worksheet("比较结果").Cell(1, 2).GetString());
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(left)));
        Assert.Empty(Directory.GetFiles(directory, "*.staging.*"));
    }

    [Fact]
    public async Task ReportRefusesBothSourcesAndExistingFiles()
    {
        var left = TextFile("left", ".xlsx"); var right = TextFile("right", ".xlsx");
        var result = await Compare(left, right);
        foreach (var source in new[] { left, right })
            Assert.Equal(OperationErrorCode.OutputConflictsWithInput, (await service.ExportAsync(result, source))!.Code);
        var path = PathFor("existing.xlsx"); File.WriteAllText(path, "keep");
        Assert.Equal(OperationErrorCode.OutputAlreadyExists, (await service.ExportAsync(result, path))!.Code);
        Assert.Equal("keep", File.ReadAllText(path));
        Assert.Equal(OperationErrorCode.UnsupportedFileType, (await service.ExportAsync(result, PathFor("report.csv")))!.Code);
    }

    [Fact]
    public async Task FailuresAreExplicitAndCancelableWorkCannotReturnEquality()
    {
        var path = Xlsx("valid");
        var missing = await Compare(path, PathFor("missing.xlsx"));
        Assert.False(missing.AreEqual); Assert.Equal(OperationErrorCode.FileNotFound, missing.Error!.Code);
        Assert.Equal(OperationErrorCode.WorksheetNotFound, (await service.CompareAsync(new(Source(path), Source(path, "missing")))).Error!.Code);
        var corrupt = PathFor("broken.xlsx"); File.WriteAllText(corrupt, "not a workbook");
        Assert.False((await Compare(path, corrupt)).Success);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(OperationErrorCode.FileLocked, (await Compare(path, path)).Error!.Code);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompareAsync(new(Source(path), Source(path)), cancellationToken: cts.Token));
        var valid = await Compare(path, path);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(valid, PathFor("canceled.xlsx"), cts.Token));
        Assert.False(File.Exists(PathFor("canceled.xlsx")));
        Assert.Null(await service.ExportAsync(valid, PathFor("equal.xlsx")));
        using var report = new XLWorkbook(PathFor("equal.xlsx"));
        Assert.Equal("数据完全一致", report.Worksheet("比较结果").Cell(1, 2).GetString());
    }

    [Fact]
    public async Task CancellationDuringComparisonLeavesInputsIntact()
    {
        var path = Xlsx("data", s => { for (var r = 1; r <= 2000; r++) s.Cell(r, 1).Value = r; });
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompareAsync(new(Source(path), Source(path)),
            new CancelProgress(cts), cts.Token));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(path)));
    }

    private sealed class CancelProgress(CancellationTokenSource cts) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) { if (value.Stage == OperationStage.Processing) cts.Cancel(); }
    }
}
