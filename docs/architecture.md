# 架构

- Owner: Codex
- Current implementation: Issue #34 Core 多格式支持；WPF 集成不在本 Issue。

## 项目与依赖方向

Windows / .NET 10 / WPF，完全本地离线。TabularStudio.App 与 TabularStudio.Tests 引用 TabularStudio.Core，Core 不引用 WPF。

Core Services：WorkbookInspectionService、FormatStandardizationService、DataMatchingService。ApprovedValueNormalization 复用已确认清理/比较规则；WorkbookFileOperations 负责只读输入、临时输出和提交/清理。

TabularWorkbook 是内部格式适配器：

- XLSX：持有原 ClosedXML XLWorkbook，沿用既有读写。
- XLS：持有原 HSSF 工作簿和内存值模型；既有算法作用于内存模型，只把变化写回原 HSSF，不生成中间 XLSX，不复制重建整个 XLS。
- CSV：CsvHelper 本地解析/写出，内存表格只供 Core 使用，不暴露虚拟 Sheet。具体编码、记录、错误与大小边界见 processing-rules §11。

Contract 只暴露普通 .NET 数据结构，CSV nullable Sheet 增量见 contracts.md；没有引入网络 API、Office、数据库或在线授权。

## 依赖与验证

现有 ClosedXML 0.105.1；新增 NPOI 2.7.4、CsvHelper 33.1.0；为修复旧依赖显式约束 BouncyCastle.Cryptography 2.6.2、SixLabors.ImageSharp 2.1.11、System.Security.Cryptography.Xml 10.0.11。最终选择和许可见 decisions/ADR-20260907-offline-xls-csv.md。

自动化回归在 tests/TabularStudio.Tests；独立 Core FT harness 在 tools/Issue34.FunctionalQA，运行时只生成 synthetic 文件，逐例验证并检查输出 reopen 与输入 SHA256，不使用 WPF 自动化。

断网整机、无 SDK 干净机器、任意复杂宏/嵌入对象保真不由 build/test 推断为通过；发布仍遵守 Gate 3。本 Issue 不创建发布包。
