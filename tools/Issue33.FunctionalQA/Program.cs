using System.IO;
using System.Security.Cryptography;
using ClosedXML.Excel;
using NPOI.HSSF.UserModel;
using TabularStudio.App.Dialogs;
using TabularStudio.App.Services;
using TabularStudio.App.ViewModels;
using TabularStudio.Core.Services;

// Calls real ViewModels and Core with synthetic files. No desktop GUI automation.
var root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine("artifacts", "ui-integration-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
if (Directory.Exists(root)) throw new InvalidOperationException("Use a new evidence directory.");
Directory.CreateDirectory(root);
var paths = new[] { ".xlsx", ".xls", ".csv" }.Select(e => Path.Combine(root, "input" + e)).ToArray();
using (var book = new XLWorkbook())
{ var s = book.AddWorksheet("Data"); s.Cell(1,1).Value = "Key"; s.Cell(1,2).Value = "Value"; s.Cell(2,1).Value = "001"; s.Cell(2,2).Value = "  yes  "; book.SaveAs(paths[0]); }
using (var book = new HSSFWorkbook())
{ var s = book.CreateSheet("Data"); s.CreateRow(0).CreateCell(0).SetCellValue("Key"); s.GetRow(0).CreateCell(1).SetCellValue("Value"); s.CreateRow(1).CreateCell(0).SetCellValue("001"); s.GetRow(1).CreateCell(1).SetCellValue("  yes  "); using var file = File.Create(paths[1]); book.Write(file,true); }
File.WriteAllText(paths[2], "Key,Value\r\n001,  yes  \r\n");
var hashes = paths.ToDictionary(p => p, Hash);
var preferences = new OutputDirectoryPreferenceService(Path.Combine(root, "preferences.json"));
var inspection = new WorkbookInspectionService();
string? sent = null;
var batch = new BatchFormatViewModel(inspection, new FormatStandardizationService(), preferences,
    confirm: _ => ExistingOutputChoice.Overwrite, sendToMatching: p => sent = p);
await batch.LoadFilesAsync([paths[0], Path.Combine(root, "missing.csv"), paths[1], paths[2]]);
Check(batch.Files.Count == 4 && batch.CanStart, "selection");
Check(!batch.Rules.TrimOuterWhitespace && !batch.Rules.NormalizeUnambiguousDates, "defaults");
batch.Rules.TrimOuterWhitespace = true;
await batch.StartAsync();
Check(batch.Summary.Contains("成功 3，失败 1"), batch.Summary);
foreach (var item in batch.Files.Where(f => f.ResultPath is not null))
{
    var preview = await inspection.GetPreviewAsync(new(new(item.ResultPath!, item.Editor.SelectedWorksheet?.Name, 1)));
    Check(preview.Success && preview.Preview!.Rows[0].Cells[1].DisplayValue == "yes", "output reopen");
    Check(Hash(item.FilePath) == hashes[item.FilePath], "input changed");
}
batch.SelectedFile = batch.Files[3]; batch.SendSelectedToMatching();
Check(sent is not null && sent.EndsWith(".csv"), "selected output handoff");
var matching = new DataMatchingViewModel(inspection, new DataMatchingService(), outputDirectoryPreferenceService: preferences);
await matching.LoadMasterFileAsync(sent!); await matching.LoadReferenceFileAsync(paths[1]);
matching.Conditions[0].SelectedMasterColumn = matching.MasterAvailableColumns[0];
matching.Conditions[0].SelectedReferenceColumn = matching.ReferenceAvailableColumns[0]; matching.ReturnFields[1].IsSelected = true;
matching.IsMasterFilterEnabled = true; matching.SelectedMasterFilterColumn = matching.MasterAvailableColumns[0]; matching.MasterFilterValue = "001";
await matching.StartAsync();
Check(matching.HasSuccess && matching.ResultMatchedCount == 1, matching.ErrorMessage ?? "match failed");
Check(matching.ResultOutputFilePath!.EndsWith(".csv"), "master output format");
var result = await inspection.GetPreviewAsync(new(new(matching.ResultOutputFilePath!, null, 1)));
Check(result.Preview!.Rows[0].Cells[2].DisplayValue == "  yes  ", "returned source value");
await batch.StartAsync(); Check(batch.CanStart && batch.Summary.Contains("成功 3，失败 1"), "retry");
Console.WriteLine("PASS: mixed batch -> preview/output reopen -> selected CSV result -> conditional matching against XLS -> CSV output; retry and input hashes verified.");
Console.WriteLine("WPF/WPS manual UI QA NOT RUN. No GUI automation.");
Console.WriteLine(root);
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
