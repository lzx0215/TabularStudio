# TabularStudio

TabularStudio 是 Windows 本地桌面工具，完全离线处理 `.xlsx`、`.xls` 和 UTF-8 `.csv` 表格。

## 核心功能

已确认功能：

1. **格式统一**  
   把来源表格整理成统一格式后导出。
2. **数据匹配**  
   在表格之间按已确认规则匹配数据并导出结果。
3. **表格对比**
   从 A1 开始按相同行列位置严格比较两张表的数据，包含表头、忽略样式，显示差异并导出 XLSX 报告。公式使用已保存的计算结果。详见 [比较口径与验证](docs/positional-comparison.md)。

## 当前项目状态

- 便携版本：[v0.3.1 Windows x64](https://github.com/lzx0215/TabularStudio/releases/tag/v0.3.1)。下载 ZIP 后解压运行 `TabularStudio.exe`，自包含 .NET 运行时。
- Windows 本地桌面、C#、.NET 10、WPF；格式读写使用 ClosedXML、NPOI 和 CsvHelper。
- 已有 Core、WPF 界面及自动化测试；表格对比为 2026-09-10 新增实现，Owner 已反馈“测试可以”并授权发布。
- 在仓库根目录执行 `dotnet build TabularStudio.sln` 构建，`dotnet test TabularStudio.sln` 验证。
- 执行 `dotnet run --project src/TabularStudio.App` 启动，选择顶部“表格对比”，选择两个文件及工作表后点击“开始对比”。

协作入口见 `AGENTS.md`。产品范围见 `docs/requirements.md`。
