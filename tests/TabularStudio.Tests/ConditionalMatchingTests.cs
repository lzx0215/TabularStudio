using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.Tests;

public sealed class ConditionalMatchingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TabularStudioTests", Guid.NewGuid().ToString("N"));

    private DataMatchingRequest Create(Action<IXLWorksheet>? configure = null)
    {
        Directory.CreateDirectory(_directory);
        using var book = new XLWorkbook();
        var master = book.AddWorksheet("Master");
        var reference = book.AddWorksheet("Reference");
        string[][] rows = [
            ["Flag", "Key1", "Key2", "Key3"],
            ["是", "a", "b", "c"],
            ["否", "a", "b", "c"],
            ["", "a", "b", "c"],
            ["是", "a", "b", "different"],
            ["是", "dup", "b", "c"],
            ["是", "", "b", "c"],
            ["否", "", "b", "c"]];
        for (int row = 0; row < rows.Length; row++)
            for (int col = 0; col < rows[row].Length; col++) master.Cell(row + 1, col + 1).Value = rows[row][col];
        string[][] lookup = [["K1", "K2", "K3", "Flag"], ["a", "b", "c", "answer"],
            ["dup", "b", "c", "first"], ["dup", "b", "c", "last"]];
        for (int row = 0; row < lookup.Length; row++)
            for (int col = 0; col < lookup[row].Length; col++) reference.Cell(row + 1, col + 1).Value = lookup[row][col];
        configure?.Invoke(master);
        string input = Path.Combine(_directory, "input.xlsx");
        book.SaveAs(input);
        return new(new(input, "Master", 1), new(input, "Reference", 1),
            [new(new(2, null), new(1, null)), new(new(3, null), new(2, null)), new(new(4, null), new(3, null))], [new(4, null)], true,
            new(), Path.Combine(_directory, "result.xlsx"), false)
        { MasterFilter = new(new(1, null), "是") };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FiltersBeforeMatchingAndPreservesRowsAndInput(bool status)
    {
        var request = Create() with { StatusColumn = new() { Enabled = status } };
        var original = File.ReadAllBytes(request.Master.FilePath);
        var result = await new DataMatchingService().ExecuteAsync(request);
        Assert.True(result.Success, result.Error?.Message);
        var summary = result.Summary!;
        Assert.Equal(7, summary.TotalMasterDataRowCount);
        Assert.Equal(1, summary.MatchedCount);
        Assert.Equal(1, summary.UnmatchedCount);
        Assert.Equal(1, summary.DuplicateCount);
        Assert.Equal(1, summary.EmptyKeyCount);
        Assert.Equal(3, summary.SkippedCount);
        Assert.Equal(summary.TotalMasterDataRowCount, summary.MatchedCount + summary.UnmatchedCount + summary.DuplicateCount + summary.EmptyKeyCount + summary.SkippedCount);
        Assert.Equal(original, File.ReadAllBytes(request.Master.FilePath));
        using var output = new XLWorkbook(result.OutputFilePath!);
        using var input = new XLWorkbook(request.Master.FilePath);
        var sheet = output.Worksheet("Master");
        Assert.Equal("Flag_匹配", sheet.Cell(1, 5).GetString());
        Assert.Equal("answer", sheet.Cell(2, 5).GetString());
        for (int row = 1; row <= 8; row++)
            for (int col = 1; col <= 4; col++) Assert.Equal(input.Worksheet("Master").Cell(row, col).Value, sheet.Cell(row, col).Value);
        for (int row = 3; row <= 8; row++) Assert.True(sheet.Cell(row, 5).IsEmpty());
        if (status)
        {
            Assert.Equal(new[] { "匹配成功", "未参与匹配", "未参与匹配", "未匹配", "重复", "匹配键为空", "未参与匹配" },
                Enumerable.Range(2, 7).Select(row => sheet.Cell(row, 6).GetString()));
        }
        else Assert.Null(result.ActualStatusColumnName);
    }

    [Fact]
    public async Task DisabledFilterKeepsExistingBehavior()
    {
        var result = await new DataMatchingService().ExecuteAsync(Create() with { MasterFilter = null });
        Assert.True(result.Success);
        Assert.Equal(0, result.Summary!.SkippedCount);
        Assert.Equal(3, result.Summary.MatchedCount);
        Assert.Equal(2, result.Summary.EmptyKeyCount);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task FilterUsesComparisonNormalizationSwitch(bool normalize, int matched)
    {
        var request = Create(s => s.Cell(2, 1).Value = " \t是\u200B ") with { NormalizeComparisonKeys = normalize };
        var result = await new DataMatchingService().ExecuteAsync(request);
        Assert.True(result.Success);
        Assert.Equal(matched, result.Summary!.MatchedCount);
    }

    [Theory]
    [InlineData(0, "是", OperationErrorCode.ColumnNotFound)]
    [InlineData(99, "是", OperationErrorCode.ColumnNotFound)]
    [InlineData(1, "", OperationErrorCode.InvalidConfiguration)]
    [InlineData(1, "\u200B", OperationErrorCode.InvalidConfiguration)]
    public async Task InvalidFilterFailsWithoutOutput(int column, string value, OperationErrorCode code)
    {
        var request = Create() with { MasterFilter = new(new(column, null), value) };
        var result = await new DataMatchingService().ExecuteAsync(request);
        Assert.False(result.Success);
        Assert.Equal(code, result.Error!.Code);
        Assert.False(File.Exists(request.OutputFilePath));
    }

    [Fact]
    public async Task FormulaFilterDoesNotUseCachedResult()
    {
        var request = Create(s => s.Cell(2, 1).FormulaA1 = "\"是\"");
        var result = await new DataMatchingService().ExecuteAsync(request);
        Assert.Equal(OperationErrorCode.FormulaCellNotAllowedForMatching, result.Error!.Code);
        Assert.False(File.Exists(request.OutputFilePath));
    }

    [Fact]
    public async Task AllSkippedStillProducesAnOutput()
    {
        var request = Create() with { MasterFilter = new(new(1, null), "不满足") };
        var result = await new DataMatchingService().ExecuteAsync(request);
        Assert.True(result.Success);
        Assert.Equal(7, result.Summary!.SkippedCount);
        Assert.Equal(0, result.Summary.MatchedCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
