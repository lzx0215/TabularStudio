using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

namespace TabularStudio.Tests;

public sealed class MultiFormatTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "TabularStudio-34-" + Guid.NewGuid());
    public MultiFormatTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private string FilePath(string name, string extension) => Path.Combine(directory, name + extension);
    private static WorksheetSource Source(string path, string sheet = "Data", int header = 1) => new(path, Path.GetExtension(path) == ".csv" ? null : sheet, header);
    private static byte[] Hash(string path) => SHA256.HashData(File.ReadAllBytes(path));
    private static DataMatchingRequest Match(string master, string reference, string output) => new(
        Source(master), Source(reference), [new(new(1, "Key"), new(1, "Key"))], [new(2, "Value")], true, new(), output, false);

    public static IEnumerable<object[]> Formats() => new[] { ".xlsx", ".xls", ".csv" }.Select(x => new object[] { x });
    public static IEnumerable<object[]> Pairs() => from a in Formats() from b in Formats() select new[] { a[0], b[0] };

    [Theory, MemberData(nameof(Formats))]
    public async Task InspectionPreviewAndStandardizationPreserveFormatAndInput(string extension)
    {
        var input = FilePath("source", extension); var output = FilePath("source_格式统一", extension);
        Write(input, [["Key", "Value"], ["00123", "  alpha  "], ["12345678901234567", "beta"]]);
        var hash = Hash(input);
        var service = new WorkbookInspectionService();
        var inspection = await service.InspectAsync(new(input));
        Assert.True(inspection.Success, inspection.Error?.ToString());
        Assert.Equal(extension == ".csv" ? 0 : 1, inspection.Worksheets.Count);
        var preview = await service.GetPreviewAsync(new(Source(input)));
        Assert.True(preview.Success, preview.Error?.ToString());
        Assert.Equal(2, preview.Preview!.Rows.Count);
        Assert.Equal("00123", preview.Preview.Rows[0].Cells[0].DisplayValue);
        Assert.Equal(extension == ".csv" ? null : "Data", preview.Preview.WorksheetName);
        var result = await new FormatStandardizationService().ExecuteAsync(new(Source(input), output, new(), false));
        Assert.True(result.Success, result.Error?.ToString());
        var rows = Read(output);
        Assert.Equal("00123", rows[1][0]); Assert.Equal("alpha", rows[1][1]);
        Assert.Equal("12345678901234567", rows[2][0]); Assert.Equal(hash, Hash(input));
        Assert.Equal(extension == ".csv" ? null : "Data", result.Summary!.ProcessedWorksheetName);
    }

    [Theory, MemberData(nameof(Pairs))]
    public async Task MatchingAllNinePairsReopensInMasterFormat(string masterExtension, string referenceExtension)
    {
        var master = FilePath("master", masterExtension); var reference = FilePath("reference", referenceExtension);
        var output = FilePath("master_匹配结果", masterExtension);
        Write(master, [["Key", "Original"], ["001", "keep"], ["missing", "keep2"], ["dup", "keep3"], ["", "keep4"]]);
        Write(reference, [["Key", "Value"], ["001", "found"], ["dup", "a"], ["dup", "b"]]);
        var mh = Hash(master); var rh = Hash(reference);
        var result = await new DataMatchingService().ExecuteAsync(Match(master, reference, output));
        Assert.True(result.Success, result.Error?.ToString());
        Assert.Equal(1, result.Summary!.MatchedCount); Assert.Equal(1, result.Summary.UnmatchedCount);
        Assert.Equal(1, result.Summary.DuplicateCount); Assert.Equal(1, result.Summary.EmptyKeyCount);
        var rows = Read(output); Assert.Equal("found", rows[1][2]); Assert.Equal("keep", rows[1][1]);
        Assert.Equal("匹配成功", rows[1][3]); Assert.Equal("未匹配", rows[2][3]);
        Assert.Equal("重复", rows[3][3]); Assert.Equal("匹配键为空", rows[4][3]);
        Assert.Equal(mh, Hash(master)); Assert.Equal(rh, Hash(reference));
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task FailuresNeverOverwriteInputOrExistingOutput(string extension)
    {
        var path = FilePath("input", extension); Write(path, [["Key", "Value"], ["a", "b"]]);
        var hash = Hash(path); var output = FilePath("output", extension);
        var service = new FormatStandardizationService();
        var request = new FormatStandardizationRequest(Source(path), output, new(), false);
        Assert.Equal(OperationErrorCode.OutputConflictsWithInput, (await service.ExecuteAsync(request with { OutputFilePath = path, OverwriteExistingOutput = true })).Error!.Code);
        File.WriteAllText(output, "existing");
        Assert.Equal(OperationErrorCode.OutputAlreadyExists, (await service.ExecuteAsync(request)).Error!.Code);
        Assert.Equal("existing", File.ReadAllText(output));
        File.Delete(output);
        Assert.Equal(OperationErrorCode.InvalidHeaderRow, (await service.ExecuteAsync(request with { Source = Source(path, header: 0) })).Error!.Code);
        Assert.Equal(OperationErrorCode.UnsupportedFileType, (await service.ExecuteAsync(request with { OutputFilePath = FilePath("other", extension == ".csv" ? ".xls" : ".csv") })).Error!.Code);
        if (extension != ".csv") Assert.Equal(OperationErrorCode.WorksheetNotFound, (await service.ExecuteAsync(request with { Source = Source(path, "missing") })).Error!.Code);
        Assert.Equal(hash, Hash(path)); Assert.Empty(Directory.GetFiles(directory, "*.staging.*"));
    }

    [Fact]
    public async Task CsvQuotesMultilineBlankRecordsBomAndHeaderRowRoundTrip()
    {
        var input = FilePath("quoted", ".csv");
        File.WriteAllText(input, "preamble\r\nKey,Value\r\na,\"comma, quote\"\" and\nnewline\"\r\n\r\nb,tail,\r\n", new UTF8Encoding(true));
        var preview = await new WorkbookInspectionService().GetPreviewAsync(new(new(input, "ignored", 2)));
        Assert.True(preview.Success); Assert.Null(preview.Preview!.WorksheetName);
        Assert.Equal(3, preview.Preview.Rows.Count); Assert.Equal(3, preview.Preview.Rows[0].WorksheetRowNumber);
        Assert.Equal("comma, quote\" and\nnewline", preview.Preview.Rows[0].Cells[1].DisplayValue);
        var output = FilePath("out", ".csv");
        var off = new FormatStandardizationOptions { TrimOuterWhitespace = false, RemoveTabsNewLinesAndHiddenCharacters = false, NormalizeFullWidthHalfWidth = false, NormalizeUnicode = false, NormalizeSafeNumbers = false, NormalizeUnambiguousDates = false };
        var result = await new FormatStandardizationService().ExecuteAsync(new(new(input, null, 2), output, off, false));
        Assert.True(result.Success, result.Error?.ToString()); Assert.Equal(Read(input), Read(output));
        Assert.Equal(new byte[] { 239, 187, 191 }, File.ReadAllBytes(output).Take(3));
    }

    [Fact]
    public async Task XlsPreservesFormulaStyleAndOtherSheetAndMatchesSameFile()
    {
        var input = FilePath("book", ".xls");
        using (var book = new HSSFWorkbook())
        {
            var sheet = book.CreateSheet("Data"); sheet.CreateRow(0).CreateCell(0).SetCellValue("Key"); sheet.GetRow(0).CreateCell(1).SetCellValue("Value");
            sheet.CreateRow(1).CreateCell(0).SetCellValue("a"); var value = sheet.GetRow(1).CreateCell(1); value.SetCellValue(" text ");
            var style = book.CreateCellStyle(); style.FillForegroundColor = 13; style.FillPattern = FillPattern.SolidForeground; value.CellStyle = style;
            sheet.GetRow(1).CreateCell(2).SetCellFormula("1+2"); sheet.SetColumnWidth(1, 5000);
            var other = book.CreateSheet("Ref"); other.CreateRow(0).CreateCell(0).SetCellValue("Key"); other.GetRow(0).CreateCell(1).SetCellValue("Value");
            other.CreateRow(1).CreateCell(0).SetCellValue("a"); other.GetRow(1).CreateCell(1).SetCellValue("returned");
            using var stream = File.Create(input); book.Write(stream, true);
        }
        var before = Hash(input); var output = FilePath("standard", ".xls");
        Assert.True((await new FormatStandardizationService().ExecuteAsync(new(Source(input), output, new(), false))).Success);
        using (var stream = File.OpenRead(output)) using (var book = new HSSFWorkbook(stream))
        {
            Assert.Equal("1+2", book.GetSheetAt(0).GetRow(1).GetCell(2).CellFormula);
            Assert.Equal(13, book.GetSheetAt(0).GetRow(1).GetCell(1).CellStyle.FillForegroundColor);
            Assert.Equal(5000, book.GetSheetAt(0).GetColumnWidth(1)); Assert.Equal("returned", book.GetSheetAt(1).GetRow(1).GetCell(1).StringCellValue);
        }
        var match = Match(input, input, FilePath("match", ".xls")) with { Reference = Source(input, "Ref") };
        Assert.True((await new DataMatchingService().ExecuteAsync(match)).Success);
        Assert.Equal("returned", Read(match.OutputFilePath)[1][3]); Assert.Equal(before, Hash(input));
        var formulaKey = match with { OutputFilePath = FilePath("bad", ".xls"), Conditions = [new(new(3, null), new(1, "Key"))] };
        // Blank header is rejected before data formula checks.
        Assert.False((await new DataMatchingService().ExecuteAsync(formulaKey)).Success);
    }

    [Fact]
    public async Task CsvInvalidBytesAndMalformedQuotesFailWithoutOutput()
    {
        foreach (var bytes in new[] { new byte[] { 0xff, 0xff }, Encoding.UTF8.GetBytes("Key,Value\r\na,\"unterminated") })
        {
            var input = FilePath(Guid.NewGuid().ToString(), ".csv"); File.WriteAllBytes(input, bytes);
            var output = FilePath(Guid.NewGuid().ToString(), ".csv");
            var result = await new FormatStandardizationService().ExecuteAsync(new(Source(input), output, new(), false));
            Assert.Equal(OperationErrorCode.WorkbookUnreadable, result.Error?.Code);
            Assert.False(File.Exists(output)); Assert.Equal(bytes, File.ReadAllBytes(input));
        }
    }

    [Theory, MemberData(nameof(Formats))]
    public async Task PreviewLimitAndCancellationRetainInput(string extension)
    {
        var input = FilePath("many", extension); Write(input, Enumerable.Range(0, 30).Select(i => new[] { i.ToString(), "value" }).ToArray());
        var preview = await new WorkbookInspectionService().GetPreviewAsync(new(Source(input)));
        Assert.Equal(20, preview.Preview!.Rows.Count);
        var hash = Hash(input); var output = FilePath("cancelled", extension);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FormatStandardizationService().ExecuteAsync(new(Source(input), output, new(), false), cancellationToken: cancellation.Token));
        Assert.Equal(hash, Hash(input)); Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task XlsFormulaFieldsRejectAndErrorValuesRoundTrip()
    {
        var input = FilePath("types", ".xls");
        using (var book = new HSSFWorkbook())
        {
            var s = book.CreateSheet("Data"); var h = s.CreateRow(0); h.CreateCell(0).SetCellValue("Key"); h.CreateCell(1).SetCellValue("Value"); h.CreateCell(2).SetCellValue("Formula");
            var row = s.CreateRow(1); row.CreateCell(0).SetCellValue("a"); row.CreateCell(1).SetCellErrorValue(7); row.CreateCell(2).SetCellFormula("1+2");
            using var stream = File.Create(input); book.Write(stream, true);
        }
        var reference = FilePath("ref", ".xls"); Write(reference, [["Key", "Value"], ["a", "returned"]]);
        var match = Match(reference, input, FilePath("matched", ".xls"));
        Assert.True((await new DataMatchingService().ExecuteAsync(match)).Success);
        using (var stream = File.OpenRead(match.OutputFilePath)) using (var book = new HSSFWorkbook(stream))
            Assert.Equal(7, book.GetSheetAt(0).GetRow(1).GetCell(2).ErrorCellValue);
        var bad = match with { ReturnFields = [new(3, "Formula")], OutputFilePath = FilePath("bad", ".xls") };
        Assert.Equal(OperationErrorCode.FormulaCellNotAllowedForMatching, (await new DataMatchingService().ExecuteAsync(bad)).Error?.Code);
    }

    internal static void Write(string path, string[][] rows)
    {
        if (Path.GetExtension(path) == ".csv")
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(false)); using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            foreach (var row in rows) { foreach (var value in row) csv.WriteField(value); csv.NextRecord(); } return;
        }
        if (Path.GetExtension(path) == ".xls")
        {
            using var book = new HSSFWorkbook(); var sheet = book.CreateSheet("Data");
            for (var r = 0; r < rows.Length; r++) { var row = sheet.CreateRow(r); for (var c = 0; c < rows[r].Length; c++) row.CreateCell(c).SetCellValue(rows[r][c]); }
            using var stream = File.Create(path); book.Write(stream, true); return;
        }
        using var xlsx = new XLWorkbook(); var target = xlsx.AddWorksheet("Data");
        for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++) target.Cell(r + 1, c + 1).Value = rows[r][c];
        xlsx.SaveAs(path);
    }

    internal static string[][] Read(string path)
    {
        if (Path.GetExtension(path) == ".csv")
        {
            using var reader = new StreamReader(path); using var parser = new CsvParser(reader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture) { IgnoreBlankLines = false });
            var rows = new List<string[]>(); while (parser.Read()) rows.Add(parser.Record!); return rows.ToArray();
        }
        if (Path.GetExtension(path) == ".xls")
        {
            Assert.Equal(new byte[] { 208, 207, 17, 224 }, File.ReadAllBytes(path).Take(4));
            using var stream = File.OpenRead(path); using var book = new HSSFWorkbook(stream); var sheet = book.GetSheetAt(0);
            return Enumerable.Range(0, sheet.LastRowNum + 1).Select(r => Enumerable.Range(0, Math.Max(0, (int)(sheet.GetRow(r)?.LastCellNum ?? 0))).Select(c => sheet.GetRow(r)?.GetCell(c)?.ToString() ?? "").ToArray()).ToArray();
        }
        using var xlsx = new XLWorkbook(path); var target = xlsx.Worksheet(1);
        return Enumerable.Range(1, target.LastRowUsed()!.RowNumber()).Select(r => Enumerable.Range(1, target.LastColumnUsed()!.ColumnNumber()).Select(c => target.Cell(r, c).GetString()).ToArray()).ToArray();
    }
}
