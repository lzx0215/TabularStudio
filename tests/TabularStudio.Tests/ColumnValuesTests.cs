using System.Globalization;
using System.Security.Cryptography;
using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.Tests;

public sealed class ColumnValuesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TabularStudio-values-" + Guid.NewGuid());
    private readonly WorkbookInspectionService inspection = new();
    public ColumnValuesTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string PathFor(string name) => Path.Combine(directory, name);
    private static WorksheetSource Source(string path, int header = 1) => new(path, path.EndsWith(".csv") ? null : "Data", header);
    private static DataMatchingRequest Match(WorksheetSource master, WorksheetSource reference, string output, ColumnFilterValue value, bool normalize = false) =>
        new(master, reference, [new(new(1, "Key"), new(1, "Key"))], [new(2, "Value")], normalize, new(), output, false)
        { MasterFilter = new(new(2, "Value"), "legacy fallback must not win") { SelectedValue = value } };

    [Theory]
    [InlineData(".xlsx")]
    [InlineData(".xls")]
    [InlineData(".csv")]
    public async Task FullColumnAfterHeaderDeduplicatesAndSelectsLateRowWithoutChangingInputs(string extension)
    {
        var master = PathFor("master" + extension); var reference = PathFor("reference" + extension);
        var rows = new List<string[]> { new[] { "ignore", "before header" }, new[] { "Key", "Value" } };
        rows.AddRange(Enumerable.Range(1, 25).Select(i => new[] { "k" + i, i == 25 ? "late" : "first" }));
        rows.Add(["blank", "  "]);
        MultiFormatTests.Write(master, rows.ToArray());
        MultiFormatTests.Write(reference, [["Key", "Value"], ["k25", "returned"]]);
        var hash = SHA256.HashData(File.ReadAllBytes(master));
        var preview = await inspection.GetPreviewAsync(new(Source(master, 2)));
        Assert.Equal(20, preview.Preview!.Rows.Count);
        var values = await inspection.GetColumnValuesAsync(new(Source(master, 2), new(2, "Value")));
        Assert.True(values.Success, values.Error?.ToString());
        Assert.Equal(new[] { "first", "late" }, values.Values.Select(v => v.Value.RawValue));
        var output = PathFor("result" + extension);
        var result = await new DataMatchingService().ExecuteAsync(Match(Source(master, 2), Source(reference), output, values.Values[1].Value));
        Assert.True(result.Success, result.Error?.ToString());
        Assert.Equal(1, result.Summary!.MatchedCount); Assert.Equal(25, result.Summary.SkippedCount);
        var actual = MultiFormatTests.Read(output);
        Assert.Equal("returned", actual[26][2]);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(master)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedCandidatesPreserveIdentityAndUseExistingComparisonRules(bool normalize)
    {
        var path = PathFor("typed.xlsx");
        using (var book = new XLWorkbook())
        {
            var s = book.AddWorksheet("Data"); s.Cell(1, 1).Value = "Key"; s.Cell(1, 2).Value = "Value";
            XLCellValue[] values = ["00123", "123", 123d, true, new DateTime(2026, 9, 8), new TimeSpan(1, 2, 3), XLError.NoValueAvailable,
                "12345678901234567890", 0.123456789012345d];
            for (var i = 0; i < values.Length; i++) { s.Cell(i + 2, 1).Value = "k" + i; s.Cell(i + 2, 2).Value = values[i]; }
            s.Cell(6, 2).Style.DateFormat.Format = "yyyy-MM-dd";
            book.SaveAs(path);
        }
        var reference = PathFor("reference.xlsx");
        MultiFormatTests.Write(reference, new[] { new[] { "Key", "Value" } }.Concat(Enumerable.Range(0, 9).Select(i => new[] { "k" + i, "v" + i })).ToArray());
        var candidates = await inspection.GetColumnValuesAsync(new(Source(path), new(2, "Value")));
        Assert.True(candidates.Success, candidates.Error?.ToString()); Assert.Equal(9, candidates.Values.Count);
        Assert.Equal(9, candidates.Values.Select(v => v.DisplayText).Distinct().Count());
        Assert.Equal(ColumnValueKind.Text, candidates.Values[1].Value.Kind);
        Assert.Equal(ColumnValueKind.Number, candidates.Values[2].Value.Kind);
        for (var i = 0; i < candidates.Values.Count; i++)
        {
            var output = PathFor($"typed-{i}.xlsx");
            var result = await new DataMatchingService().ExecuteAsync(Match(Source(path), Source(reference), output, candidates.Values[i].Value, normalize));
            Assert.True(result.Success, result.Error?.ToString());
            Assert.Equal(1, result.Summary!.MatchedCount); Assert.Equal(8, result.Summary.SkippedCount);
            using var actual = new XLWorkbook(output);
            Assert.Equal("v" + i, actual.Worksheet("Data").Cell(i + 2, 3).GetString());
        }
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task TypedNumericValueRespectsSafeNumericNormalization(bool normalize, int matches)
    {
        var master = PathFor("numbers.xlsx"); var reference = PathFor("ref.xlsx");
        using (var b = new XLWorkbook())
        {
            var s = b.AddWorksheet("Data"); s.Cell(1, 1).Value = "Key"; s.Cell(1, 2).Value = "Value";
            s.Cell(2, 1).Value = "a"; s.Cell(2, 2).Value = 123d;
            s.Cell(3, 1).Value = "b"; s.Cell(3, 2).Value = "123";
            b.SaveAs(master);
        }
        MultiFormatTests.Write(reference, [["Key", "Value"], ["a", "A"], ["b", "B"]]);
        var values = await inspection.GetColumnValuesAsync(new(Source(master), new(2, null)));
        var result = await new DataMatchingService().ExecuteAsync(Match(Source(master), Source(reference), PathFor("output.xlsx"), values.Values[0].Value, normalize));
        Assert.True(result.Success, result.Error?.ToString()); Assert.Equal(matches, result.Summary!.MatchedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task XlsDatesBooleansAndFormulaFailureKeepNativeSemantics(bool formula)
    {
        var path = PathFor("native.xls");
        using (var b = new NPOI.HSSF.UserModel.HSSFWorkbook())
        {
            var sheet = b.CreateSheet("Data");
            var h = sheet.CreateRow(0); h.CreateCell(0).SetCellValue("Key"); h.CreateCell(1).SetCellValue("Value");
            var r = sheet.CreateRow(1); r.CreateCell(0).SetCellValue("a"); r.CreateCell(1).SetCellValue(true);
            r = sheet.CreateRow(2); r.CreateCell(0).SetCellValue("b");
            var date = r.CreateCell(1); date.SetCellValue(new DateTime(2026, 9, 8, 12, 30, 0));
            var style = b.CreateCellStyle(); style.DataFormat = b.CreateDataFormat().GetFormat("yyyy-mm-dd hh:mm:ss"); date.CellStyle = style;
            if (formula) sheet.CreateRow(30).CreateCell(1).SetCellFormula("1+1");
            using var file = File.Create(path); b.Write(file);
        }
        var result = await inspection.GetColumnValuesAsync(new(Source(path), new(2, null)));
        if (formula)
        {
            Assert.False(result.Success); Assert.Empty(result.Values);
            Assert.Equal(OperationErrorCode.FormulaCellNotAllowedForMatching, result.Error!.Code); Assert.Contains("B31", result.Error.Detail);
            return;
        }
        Assert.True(result.Success, result.Error?.ToString());
        Assert.Equal(ColumnValueKind.Boolean, result.Values[0].Value.Kind);
        Assert.Equal(ColumnValueKind.DateTime, result.Values[1].Value.Kind); Assert.True(result.Values[1].Value.HasTime);
        var reference = PathFor("lookup.csv"); MultiFormatTests.Write(reference, [["Key", "Value"], ["a", "A"], ["b", "B"]]);
        foreach (var normalize in new[] { false, true })
        for (var i = 0; i < 2; i++)
        {
            var output = PathFor($"xls-{normalize}-{i}.xls");
            var match = await new DataMatchingService().ExecuteAsync(Match(Source(path), Source(reference), output, result.Values[i].Value, normalize));
            Assert.True(match.Success, match.Error?.ToString()); Assert.Equal(1, match.Summary!.MatchedCount); Assert.Equal(1, match.Summary.SkippedCount);
            Assert.Equal(i == 0 ? "A" : "B", MultiFormatTests.Read(output)[i + 1][2]);
        }
    }

    [Fact]
    public async Task FormulaAnywhereRejectsAllCandidatesAndIncludesRealLocation()
    {
        var path = PathFor("formula.xlsx");
        using (var b = new XLWorkbook())
        {
            var s = b.AddWorksheet("Data"); s.Cell(1, 1).Value = "Header"; s.Cell(2, 1).Value = "valid";
            s.Cell(50, 1).FormulaA1 = "1+1"; b.SaveAs(path);
        }
        var hash = File.ReadAllBytes(path);
        var result = await inspection.GetColumnValuesAsync(new(Source(path), new(1, null)));
        Assert.False(result.Success); Assert.Empty(result.Values);
        Assert.Equal(OperationErrorCode.FormulaCellNotAllowedForMatching, result.Error!.Code);
        Assert.Contains("A50", result.Error.Detail); Assert.Equal(hash, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task ReadErrorsEmptyColumnAndCancellationNeverReturnStaleOrPartialValues()
    {
        var path = PathFor("input.csv"); MultiFormatTests.Write(path, [["Key", "Value"], ["a", " "]]);
        Assert.Empty((await inspection.GetColumnValuesAsync(new(Source(path), new(2, null)))).Values);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(OperationErrorCode.FileLocked, (await inspection.GetColumnValuesAsync(new(Source(path), new(1, null)))).Error!.Code);
        Assert.Equal(OperationErrorCode.FileNotFound, (await inspection.GetColumnValuesAsync(new(Source(PathFor("missing.csv")), new(1, null)))).Error!.Code);
        Assert.Equal(OperationErrorCode.InvalidHeaderRow, (await inspection.GetColumnValuesAsync(new(Source(path, 0), new(1, null)))).Error!.Code);
        Assert.False((await inspection.GetColumnValuesAsync(new(Source(path), new(99, null)))).Success);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspection.GetColumnValuesAsync(new(Source(path), new(1, null)), cancellation.Token));
    }

    [Theory]
    [InlineData(ColumnValueKind.Number, "NaN")]
    [InlineData(ColumnValueKind.Number, "Infinity")]
    [InlineData(ColumnValueKind.DateTime, "not a date")]
    [InlineData(ColumnValueKind.Boolean, "yes")]
    [InlineData(ColumnValueKind.Text, " ")]
    [InlineData(ColumnValueKind.Error, "999")]
    public async Task InvalidTypedValuesAreRejectedBeforeWriting(ColumnValueKind kind, string raw)
    {
        var request = Match(Source(PathFor("missing.xlsx")), Source(PathFor("other.xlsx")), PathFor("result.xlsx"), new(kind, raw));
        var result = await new DataMatchingService().ExecuteAsync(request);
        Assert.Equal(OperationErrorCode.InvalidConfiguration, result.Error!.Code); Assert.False(File.Exists(request.OutputFilePath));
    }
}
