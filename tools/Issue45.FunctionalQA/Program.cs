using ClosedXML.Excel;
using TabularStudio.Core.Contracts;
using TabularStudio.Core.Services;

// Generates persistent synthetic fixtures and independently checks the real Core output.
// This is a Core functional run, not desktop GUI automation or UI acceptance.
var directory = Path.GetFullPath(args.Length == 1 ? args[0] : Path.Combine("artifacts", "issue45-qa-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
if (Directory.Exists(directory)) throw new InvalidOperationException("Choose a new directory to preserve earlier evidence.");
Directory.CreateDirectory(directory);
var masterPath = Path.Combine(directory, "表A.xlsx");
var referencePath = Path.Combine(directory, "表B.xlsx");
var outputPath = Path.Combine(directory, "Core验证结果.xlsx");
Write(masterPath, [
    ["是否参与", "员工编号", "部门", "地区"],
    ["是", "001", "财务", "上海"],
    ["否", "001", "财务", "上海"],
    ["", "001", "财务", "上海"],
    ["是", "001", "财务", "北京"],
    ["是", "002", "研发", "上海"],
    ["是", "", "财务", "上海"],
    ["否", "", "财务", "上海"]]);
Write(referencePath, [
    ["员工编号", "部门", "地区", "岗位", "备注"],
    ["001", "财务", "上海", "会计", "唯一记录"],
    ["002", "研发", "上海", "工程师", "重复记录1"],
    ["002", "研发", "上海", "测试员", "重复记录2"]]);
var originalMaster = File.ReadAllBytes(masterPath);
var originalReference = File.ReadAllBytes(referencePath);
var request = new DataMatchingRequest(new(masterPath, "数据", 1), new(referencePath, "数据", 1),
    [new(new(2, null), new(1, null)), new(new(3, null), new(2, null)), new(new(4, null), new(3, null))],
    [new(4, null), new(5, null)], true, new(), outputPath, false)
{ MasterFilter = new(new(1, null), "是") };
var result = await new DataMatchingService().ExecuteAsync(request);
Require(result.Success, result.Error?.Message ?? "Core failed");
var summary = result.Summary!;
Require(summary.TotalMasterDataRowCount == 7 && summary.MatchedCount == 1 && summary.UnmatchedCount == 1
    && summary.DuplicateCount == 1 && summary.EmptyKeyCount == 1 && summary.SkippedCount == 3, "Unexpected summary");
using (var output = new XLWorkbook(outputPath))
{
    var sheet = output.Worksheet("数据");
    string[] statuses = ["匹配成功", "未参与匹配", "未参与匹配", "未匹配", "重复", "匹配键为空", "未参与匹配"];
    for (int i = 0; i < statuses.Length; i++) Require(sheet.Cell(i + 2, 7).GetString() == statuses[i], "Unexpected row status");
    Require(sheet.Cell(2, 5).GetString() == "会计" && sheet.Cell(2, 6).GetString() == "唯一记录", "Returned columns differ");
    for (int row = 3; row <= 8; row++) Require(sheet.Cell(row, 5).IsEmpty() && sheet.Cell(row, 6).IsEmpty(), "Nonunique/ineligible row returned values");
}
Require(originalMaster.SequenceEqual(File.ReadAllBytes(masterPath)), "Master changed");
Require(originalReference.SequenceEqual(File.ReadAllBytes(referencePath)), "Reference changed");
File.WriteAllText(Path.Combine(directory, "验证说明.txt"), """
本目录仅包含合成样例，不含真实业务数据。
Core 功能运行已通过：总计7，成功1，未匹配1，重复1，空键1，未参与3；两个输入文件字节不变。
这不等于 WPF 界面或 WPS 人工验收通过。

界面验收步骤：
1. 使用本次候选程序，主表选择表A.xlsx、对照表选择表B.xlsx；工作表数据，表头行1。
2. 匹配条件设为 员工编号=员工编号 AND 部门=部门 AND 地区=地区。
3. 返回字段勾选岗位、备注；开启仅匹配满足条件的主表行，选择是否参与，等于“是”。
4. 输出另选 UI验证结果.xlsx，开始。应得到上述统计；文件内第2行带回会计/唯一记录，其余返回留空。
5. 关闭筛选后再次匹配，确认覆盖：成功3、未匹配1、重复1、空键2、未参与0。
6. 用 WPS 打开输出文件，再次开始并确认覆盖（若提示），应显示文件被占用且恢复操作。
7. 关闭 WPS 中的输出，直接再次开始，无需改配置或重启，应成功。
8. 再测试已有输出时的取消与另存、重新选择主表后筛选列清空、缺少筛选列/值时不能开始。
人工检查完成前，UI Functional QA 状态为 NOT RUN。此文件不代表正式发布包验收。
""");
Console.WriteLine("PASS Core functional scenario; UI QA NOT RUN");
Console.WriteLine(directory);

static void Write(string path, string[][] rows)
{
    using var workbook = new XLWorkbook();
    var sheet = workbook.AddWorksheet("数据");
    for (int row = 0; row < rows.Length; row++)
        for (int col = 0; col < rows[row].Length; col++) sheet.Cell(row + 1, col + 1).Value = rows[row][col];
    sheet.Columns().AdjustToContents();
    workbook.SaveAs(path);
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
