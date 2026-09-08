using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.Tests;

public sealed class BatchFormatTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TabularStudio-32-" + Guid.NewGuid());
    public BatchFormatTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private BatchFormatItem Item(string name, string extension = ".xlsx")
    {
        var input = Path.Combine(directory, name + extension);
        MultiFormatTests.Write(input, [["Key", "Value"], ["001", "  text  "]]);
        return new(new(input, extension == ".csv" ? null : "Data", 1), Path.Combine(directory, name + "_格式统一" + extension));
    }

    [Fact]
    public async Task MixedFormatsContinuePastFailureWithSharedOptions()
    {
        var a = Item("a"); var b = Item("b", ".xls"); var c = Item("c", ".csv");
        var before = new[] { a, b, c }.Select(i => File.ReadAllBytes(i.Source.FilePath)).ToArray();
        var invalid = new BatchFormatItem(new(Path.Combine(directory, "missing.csv"), null, 1), Path.Combine(directory, "bad.csv"));
        var result = await new BatchFormatStandardizationService().ExecuteAsync(new([a, invalid, b, c], new() { TrimOuterWhitespace = true }));
        Assert.Equal(3, result.SucceededCount); Assert.Equal(1, result.FailedCount);
        Assert.Equal(OperationErrorCode.FileNotFound, result.Items[1].Result.Error!.Code);
        foreach (var item in new[] { a, b, c }) Assert.Equal("text", MultiFormatTests.Read(item.OutputFilePath)[1][1]);
        Assert.Equal(before, new[] { a, b, c }.Select(i => File.ReadAllBytes(i.Source.FilePath)).ToArray());
    }

    [Fact]
    public async Task IndependentSheetsAndHeaders()
    {
        var a = Item("a"); var b = Item("b");
        using (var book = new XLWorkbook(b.Source.FilePath))
        { var sheet = book.Worksheet(1); sheet.Name = "Other"; sheet.Row(1).InsertRowsAbove(1); sheet.Cell(1, 1).Value = "preamble"; book.Save(); }
        b = b with { Source = b.Source with { WorksheetName = "Other", HeaderRowNumber = 2 } };
        var result = await new BatchFormatStandardizationService().ExecuteAsync(new([a, b], new()));
        Assert.Equal(2, result.SucceededCount);
        Assert.Equal("Other", result.Items[1].Result.Summary!.ProcessedWorksheetName);
        using var output = new XLWorkbook(b.OutputFilePath);
        Assert.Equal("preamble", output.Worksheet(1).Cell(1, 1).GetString());
        Assert.Equal("text", output.Worksheet(1).Cell(3, 2).GetString());
    }

    [Fact]
    public async Task CrossInputAndDuplicateOutputProtection()
    {
        var a = Item("a"); var b = Item("b"); var original = File.ReadAllBytes(b.Source.FilePath);
        var service = new BatchFormatStandardizationService();
        var result = await service.ExecuteAsync(new([a with { OutputFilePath = b.Source.FilePath, OverwriteExistingOutput = true }, b], new()));
        Assert.Equal(OperationErrorCode.OutputConflictsWithInput, result.Items[0].Result.Error!.Code);
        Assert.True(result.Items[1].Result.Success); Assert.Equal(original, File.ReadAllBytes(b.Source.FilePath));
        result = await service.ExecuteAsync(new([a, b with { OutputFilePath = a.OutputFilePath, OverwriteExistingOutput = true }], new()));
        Assert.Equal(2, result.FailedCount); Assert.False(File.Exists(a.OutputFilePath));
    }

    [Fact]
    public async Task AllFailedAndExistingOutputAreReportedIndividually()
    {
        var a = Item("a"); var b = Item("b"); File.WriteAllText(a.OutputFilePath, "existing");
        var result = await new BatchFormatStandardizationService().ExecuteAsync(new([a, b with { Source = b.Source with { HeaderRowNumber = 0 } }], new()));
        Assert.Equal(2, result.FailedCount); Assert.Equal("existing", File.ReadAllText(a.OutputFilePath));
        Assert.Equal(OperationErrorCode.OutputAlreadyExists, result.Items[0].Result.Error!.Code);
        Assert.Equal(OperationErrorCode.InvalidHeaderRow, result.Items[1].Result.Error!.Code);
    }

    [Fact]
    public async Task CancellationRetainsCommittedOutput()
    {
        var a = Item("a"); var b = Item("b"); using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BatchFormatStandardizationService().ExecuteAsync(new([a, b], new()), new CancelAfterFirst(cts), cts.Token));
        Assert.True(File.Exists(a.OutputFilePath)); Assert.False(File.Exists(b.OutputFilePath));
    }
    private sealed class CancelAfterFirst(CancellationTokenSource cts) : IProgress<BatchFormatProgress>
    { public void Report(BatchFormatProgress value) { if (value.CompletedCount == 1) cts.Cancel(); } }
}
