using System.Security.Cryptography;
using ClosedXML.Excel;
using NPOI.HSSF.UserModel;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

var root = Path.GetFullPath(args.FirstOrDefault() ?? Path.Combine("artifacts", "batch-qa-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
if (Directory.Exists(root)) throw new InvalidOperationException("Use a new evidence directory.");
Directory.CreateDirectory(root);
var items = new List<BatchFormatItem>();
var hashes = new Dictionary<string, string>();
foreach (var extension in new[] { ".xlsx", ".xls", ".csv" })
{
    var path = Path.Combine(root, "input" + extension);
    if (extension == ".csv") File.WriteAllText(path, "Key,Value\r\n001,  value  \r\n");
    else if (extension == ".xls")
    {
        using var book = new HSSFWorkbook(); var sheet = book.CreateSheet("Binary");
        sheet.CreateRow(0).CreateCell(0).SetCellValue("Key"); sheet.GetRow(0).CreateCell(1).SetCellValue("Value");
        sheet.CreateRow(1).CreateCell(0).SetCellValue("001"); sheet.GetRow(1).CreateCell(1).SetCellValue("  value  ");
        using var file = File.Create(path); book.Write(file, true);
    }
    else
    {
        using var book = new XLWorkbook(); var sheet = book.AddWorksheet("Excel");
        sheet.Cell(1, 1).Value = "preamble"; sheet.Cell(2, 1).Value = "Key"; sheet.Cell(2, 2).Value = "Value";
        sheet.Cell(3, 1).Value = "001"; sheet.Cell(3, 2).Value = "  value  "; book.SaveAs(path);
    }
    hashes[path] = Hash(path);
    items.Add(new(new(path, extension == ".csv" ? null : extension == ".xls" ? "Binary" : "Excel", extension == ".xlsx" ? 2 : 1), Path.Combine(root, "input_格式统一" + extension)));
}
var service = new BatchFormatStandardizationService();
var missing = new BatchFormatItem(new(Path.Combine(root, "missing.csv"), null, 1), Path.Combine(root, "missing-out.csv"));
var options = new FormatStandardizationOptions { TrimOuterWhitespace = true };
var mixed = await service.ExecuteAsync(new([items[0], missing, items[1], items[2]], options));
Check(mixed.SucceededCount == 3 && mixed.FailedCount == 1, "mixed counts");
foreach (var item in items)
{
    Check(File.Exists(item.OutputFilePath), "missing output");
    if (item.OutputFilePath.EndsWith(".csv")) Check(File.ReadAllText(item.OutputFilePath).Contains("001,value"), "CSV value");
    else if (item.OutputFilePath.EndsWith(".xls"))
    { using var file = File.OpenRead(item.OutputFilePath); using var book = new HSSFWorkbook(file); Check(book.GetSheet("Binary").GetRow(1).GetCell(1).StringCellValue == "value", "XLS value"); }
    else { using var book = new XLWorkbook(item.OutputFilePath); Check(book.Worksheet("Excel").Cell(3, 2).GetString() == "value", "XLSX header/value"); }
    Check(Hash(item.Source.FilePath) == hashes[item.Source.FilePath], "source changed");
}
Console.WriteLine("FT32-01/02/03 PASS: three formats, independent sheets/headers, reopen values, continue after failure.");
var failed = await service.ExecuteAsync(new([missing, missing with { OutputFilePath = Path.Combine(root, "missing2.csv") }], options));
Check(failed.FailedCount == 2 && failed.Items.All(i => i.Result.Error?.Code == OperationErrorCode.FileNotFound), "all failed errors");
Console.WriteLine("FT32-04 PASS: all failed, individual reasons.");
var protectedResult = await service.ExecuteAsync(new([items[0] with { OutputFilePath = items[1].Source.FilePath, OverwriteExistingOutput = true }, items[1]], options));
Check(protectedResult.Items[0].Result.Error?.Code == OperationErrorCode.OutputConflictsWithInput && protectedResult.Items[1].Result.Error?.Code == OperationErrorCode.OutputAlreadyExists, "output protection");
Check(Hash(items[1].Source.FilePath) == hashes[items[1].Source.FilePath], "cross input changed");
Console.WriteLine("FT32-05 PASS: cross-input and existing-output protection.");
var duplicate = Path.Combine(root, "duplicate.xlsx");
var collision = await service.ExecuteAsync(new([items[0] with { OutputFilePath = duplicate }, items[0] with { OutputFilePath = duplicate, OverwriteExistingOutput = true }], options));
Check(collision.FailedCount == 2 && !File.Exists(duplicate), "duplicate outputs");
Console.WriteLine("FT32-06 PASS: colliding outputs rejected.");
using var cts = new CancellationTokenSource();
var committed = Path.Combine(root, "committed.csv"); var pending = Path.Combine(root, "pending.csv");
try { await service.ExecuteAsync(new([items[2] with { OutputFilePath = committed }, items[2] with { OutputFilePath = pending }], options), new CancelAfterFirst(cts), cts.Token); throw new Exception("Expected cancellation"); }
catch (OperationCanceledException) { Check(File.Exists(committed) && !File.Exists(pending), "cancellation rollback"); }
Console.WriteLine("FT32-07 PASS: cancellation preserves committed result.");
Console.WriteLine(root);
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
sealed class CancelAfterFirst(CancellationTokenSource cts) : IProgress<BatchFormatProgress>
{ public void Report(BatchFormatProgress value) { if (value.CompletedCount == 1) cts.Cancel(); } }
