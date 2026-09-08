using System.Globalization;
using System.Security.Cryptography;
using ClosedXML.Excel;
using CsvHelper;
using NPOI.HSSF.UserModel;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

// Explicit Core functional scenarios, separate from the regression test runner.
// No WPF automation, real data, network client, or Office automation.
var root = Path.Combine(Path.GetTempPath(), "TabularStudio-FT34-" + Guid.NewGuid());
Directory.CreateDirectory(root);
var inspection = new WorkbookInspectionService();
var standardization = new FormatStandardizationService();
var matching = new DataMatchingService();
string[] extensions = [".xlsx", ".xls", ".csv"];
var sources = extensions.ToDictionary(e => e, e => Path.Combine(root, "input" + e));
var references = extensions.ToDictionary(e => e, e => Path.Combine(root, "reference" + e));
var hashes = new Dictionary<string, string>();
string Hash(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
void Check(bool condition, string evidence) { if (!condition) throw new InvalidOperationException(evidence); }
WorksheetSource Source(string p, string sheet = "Data") => new(p, Path.GetExtension(p) == ".csv" ? null : sheet, 1);
string Output(string e) => Path.Combine(root, Guid.NewGuid() + e);
FormatStandardizationRequest F(string e, string? output = null) => new(Source(sources[e]), output ?? Output(e), new(), false);
DataMatchingRequest M(string a, string b) => new(Source(sources[a]), Source(references[b]), [new(new(1, "Key"), new(1, "Key"))], [new(2, "Value")], true, new(), Output(a), false);
async Task Run(string id, Func<Task> action)
{
    await action(); Console.WriteLine($"{id}: PASS");
}
try
{
    foreach (var e in extensions)
    {
        Write(sources[e], [["Key", "Value"], ["001", "  synthetic  "]]);
        Write(references[e], [["Key", "Value"], ["001", "returned"]]);
        hashes[sources[e]] = Hash(sources[e]); hashes[references[e]] = Hash(references[e]);
    }
    for (var i = 0; i < 3; i++)
    {
        var e = extensions[i];
        await Run($"FT34-{i + 1:00}", async () =>
        {
            var result = await inspection.InspectAsync(new(sources[e]));
            Check(result.Success && result.Worksheets.Count == (e == ".csv" ? 0 : 1), "Inspection sheets");
            var preview = await inspection.GetPreviewAsync(new(Source(sources[e])));
            Check(preview.Success && preview.Preview!.Rows[0].Cells[0].DisplayValue == "001", "Preview content");
        });
    }
    await Run("FT34-04", async () =>
    {
        foreach (var a in extensions) foreach (var b in extensions)
        {
            var request = M(a, b); var result = await matching.ExecuteAsync(request);
            Check(result.Success && result.Summary!.MatchedCount == 1, $"Matching {a}/{b}: {result.Error}");
            Check(Read(request.OutputFilePath)[1][2] == "returned", "Actual output reopen");
            Console.WriteLine($"  {a}/{b} -> {a}: reopened, value verified");
        }
    });
    for (var i = 0; i < 3; i++)
    {
        var e = extensions[i];
        await Run($"FT34-{i + 5:00}", async () =>
        {
            var request = F(e, Path.Combine(root, "input_格式统一" + e)); var result = await standardization.ExecuteAsync(request);
            Check(result.Success, result.Error?.ToString() ?? "Standardization failed");
            Check(Read(request.OutputFilePath)[1][1] == "synthetic", "Standardized output");
        });
    }
    await Run("FT34-08", async () =>
    {
        foreach (var pair in new[] { (".xls", ".csv"), (".csv", ".xlsx") })
        {
            var r = M(pair.Item1, pair.Item2) with { OutputFilePath = Path.Combine(root, "input_匹配结果" + pair.Item1) };
            var result = await matching.ExecuteAsync(r); Check(result.Success && Path.GetExtension(result.OutputFilePath) == pair.Item1, "Master output extension"); Read(r.OutputFilePath);
        }
    });
    await Run("FT34-09", async () => { var result = await standardization.ExecuteAsync(F(".csv")); Check(result.Success && result.Summary!.ProcessedWorksheetName is null, "CSV no sheet"); });
    await Run("FT34-10", async () => { foreach (var pair in new[] { (".csv", ".xls"), (".xlsx", ".csv") }) Check((await matching.ExecuteAsync(M(pair.Item1, pair.Item2))).Success, "CSV side no sheet"); });
    await Run("FT34-11", async () =>
    {
        Check((await inspection.InspectAsync(new(Path.Combine(root, "missing.xls")))).Error?.Code == OperationErrorCode.FileNotFound, "Missing file");
        var unsupported = Path.Combine(root, "input.ods"); File.WriteAllText(unsupported, "synthetic");
        Check((await inspection.InspectAsync(new(unsupported))).Error?.Code == OperationErrorCode.UnsupportedFileType, "Unsupported format");
        foreach (var e in extensions)
        {
            var corrupt = Output(e); File.WriteAllBytes(corrupt, [0xff, 0xff, 0xff]);
            Check((await inspection.InspectAsync(new(corrupt))).Error?.Code == OperationErrorCode.WorkbookUnreadable, "Corrupt " + e);
            Check((await inspection.GetPreviewAsync(new(Source(sources[e]) with { HeaderRowNumber = 0 }))).Error?.Code == OperationErrorCode.InvalidHeaderRow, "Header row");
            if (e != ".csv") Check((await inspection.GetPreviewAsync(new(Source(sources[e], "missing")))).Error?.Code == OperationErrorCode.WorksheetNotFound, "Missing sheet");
        }
    });
    await Run("FT34-12", () => { foreach (var p in sources.Values) Check(Hash(p) == hashes[p], "Standardization input SHA256"); return Task.CompletedTask; });
    await Run("FT34-13", () => { foreach (var p in hashes.Keys) Check(Hash(p) == hashes[p], "Matching input SHA256"); return Task.CompletedTask; });
    await Run("FT34-14", async () =>
    {
        foreach (var e in new[] { ".xlsx", ".xls" })
        {
            var path = Output(e); Write(path, [["Key", "Value"], ["001", "returned"]], true);
            var request = M(e, e) with { Master = Source(path), Reference = Source(path, "Ref") };
            Check((await matching.ExecuteAsync(request)).Success, "Same file distinct sheets");
        }
        var csv = M(".csv", ".csv") with { Reference = Source(sources[".csv"]) };
        Check((await matching.ExecuteAsync(csv)).Error?.Code == OperationErrorCode.InvalidConfiguration, "CSV no same-file sheet semantics");
    });
    await Run("FT34-15", async () =>
    {
        Check(!(await standardization.ExecuteAsync(F(".xls", Output(".xlsx")))).Success, "No format conversion");
        Check(!(await matching.ExecuteAsync(M(".csv", ".xlsx") with { OutputFilePath = Output(".xlsx") })).Success, "No matching conversion");
    });
    await Run("FT34-16", async () =>
    {
        foreach (var e in extensions)
        {
            Check((await standardization.ExecuteAsync(F(e, sources[e]) with { OverwriteExistingOutput = true })).Error?.Code == OperationErrorCode.OutputConflictsWithInput, "Protect input");
            foreach (var p in new[] { sources[e], references[e] }) Check((await matching.ExecuteAsync(M(e, e) with { OutputFilePath = p, OverwriteExistingOutput = true })).Error?.Code == OperationErrorCode.OutputConflictsWithInput, "Protect matching input");
            var existing = Output(e); File.WriteAllText(existing, "sentinel");
            Check((await standardization.ExecuteAsync(F(e, existing))).Error?.Code == OperationErrorCode.OutputAlreadyExists && File.ReadAllText(existing) == "sentinel", "Existing output");
        }
        foreach (var e in new[] { ".xlsx", ".xls" })
        {
            var path = Output(e); WriteFormula(path); var output = Output(e);
            var result = await standardization.ExecuteAsync(new(Source(path), output, new(), false)); Check(result.Success, "Formula standardization");
            CheckFormula(output);
        }
        foreach (var p in hashes.Keys) Check(Hash(p) == hashes[p], "Final input hashes");
    });
    Console.WriteLine("FUNCTIONAL QA: PASS (16 cases; Core only; synthetic; actual reopen and SHA256)");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine("FUNCTIONAL QA: FAIL " + error); return 1; }
finally { Directory.Delete(root, true); }

static void Write(string path, string[][] rows, bool twoSheets = false)
{
    if (path.EndsWith(".csv")) { File.WriteAllText(path, string.Join("\r\n", rows.Select(r => string.Join(',', r))) + "\r\n"); return; }
    if (path.EndsWith(".xls"))
    {
        using var b = new HSSFWorkbook();
        foreach (var name in twoSheets ? new[] { "Data", "Ref" } : new[] { "Data" }) { var s = b.CreateSheet(name); for (var r = 0; r < rows.Length; r++) { var row = s.CreateRow(r); for (var c = 0; c < rows[r].Length; c++) row.CreateCell(c).SetCellValue(rows[r][c]); } }
        using var output = File.Create(path); b.Write(output, true); return;
    }
    using var book = new XLWorkbook(); foreach (var name in twoSheets ? new[] { "Data", "Ref" } : new[] { "Data" }) { var s = book.AddWorksheet(name); for (var r = 0; r < rows.Length; r++) for (var c = 0; c < rows[r].Length; c++) s.Cell(r + 1, c + 1).Value = rows[r][c]; } book.SaveAs(path);
}
static string[][] Read(string path)
{
    if (path.EndsWith(".csv")) { using var r = new StreamReader(path); using var csv = new CsvParser(r, CultureInfo.InvariantCulture); var rows = new List<string[]>(); while (csv.Read()) rows.Add(csv.Record!); return rows.ToArray(); }
    if (path.EndsWith(".xls")) { using var f = File.OpenRead(path); using var b = new HSSFWorkbook(f); var s = b.GetSheetAt(0); return Enumerable.Range(0, s.LastRowNum + 1).Select(r => Enumerable.Range(0, s.GetRow(r).LastCellNum).Select(c => s.GetRow(r).GetCell(c)?.ToString() ?? "").ToArray()).ToArray(); }
    using var book = new XLWorkbook(path); var sheet = book.Worksheet(1); return Enumerable.Range(1, sheet.LastRowUsed()!.RowNumber()).Select(r => Enumerable.Range(1, sheet.LastColumnUsed()!.ColumnNumber()).Select(c => sheet.Cell(r, c).GetString()).ToArray()).ToArray();
}
static void WriteFormula(string path)
{
    if (path.EndsWith(".xls")) { using var b = new HSSFWorkbook(); var s = b.CreateSheet("Data"); s.CreateRow(0).CreateCell(0).SetCellValue("Formula"); s.CreateRow(1).CreateCell(0).SetCellFormula("1+2"); s.SetColumnWidth(0, 5000); using var f = File.Create(path); b.Write(f, true); }
    else { using var b = new XLWorkbook(); var s = b.AddWorksheet("Data"); s.Cell(1, 1).Value = "Formula"; s.Cell(2, 1).FormulaA1 = "1+2"; s.Column(1).Width = 25; b.SaveAs(path); }
}
static void CheckFormula(string path)
{
    if (path.EndsWith(".xls")) { using var f = File.OpenRead(path); using var b = new HSSFWorkbook(f); if (b.GetSheetAt(0).GetRow(1).GetCell(0).CellFormula != "1+2" || b.GetSheetAt(0).GetColumnWidth(0) != 5000) throw new Exception("XLS formula/layout changed"); }
    else { using var b = new XLWorkbook(path); if (b.Worksheet(1).Cell(2, 1).FormulaA1 != "1+2" || b.Worksheet(1).Column(1).Width != 25) throw new Exception("XLSX formula/layout changed"); }
}
