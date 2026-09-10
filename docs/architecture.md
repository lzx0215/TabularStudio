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

断网整机、无 SDK 干净机器、任意复杂宏/嵌入对象保真不由 build/test 推断为通过；发布仍遵守 Gate 3。

## 条件匹配增量（Issue #45，2026-09-08）

当前数据匹配由 App 收集条件，Core DataMatchingService 统一处理。

- DataMatchingRequest 新增可选 MasterFilter，DataMatchingSummary 新增 SkippedCount；均使用 init 属性保持既有构造参数和调用兼容。
- UI 只收集筛选列与文本、校验完整性和展示统计；筛选算法、公式约束、安全标准化、行保留及输出保护均由 Core 实现。
- 复用现有 KeyPart/CompositeKey 比较，不新增库或表达式引擎。筛选在内存执行，不改写输入。
- 条件筛选复用同一内存算法覆盖三种格式；多格式与条件匹配在 #34 同步主线后共同验证。
- App.Tests 引用 App 验证实际 ViewModel 和 Core 调用；Core 测试保持独立。此类自动化不等同于桌面人工 QA。

## 批量编排（Issue #32）

BatchFormatStandardizationService 在 Core 内顺序调用 IFormatStandardizationService，复用多格式与原子写入逻辑；只增加批次输入保护、逐项隔离、结果/进度汇总。单文件契约与匹配服务不变。UI 调用 IBatchFormatStandardizationService；无需新依赖或持久存储。

## 本机处理配置（Issue #56）

`JsonProcessingProfileStore` 与 `ProcessingProfileValidator` 位于 Core，无新增生产依赖。App 通过接口调用存储与校验；已有表格服务保持原行为。配置目录默认是 LocalApplicationData/TabularStudio/processing-profiles，构造函数允许测试注入独立目录。

每个命名配置一个版本 1 JSON，按功能分目录，文件名由规范化名称的 SHA256 生成，名称不能控制文件路径。读取拒绝未知成员、缺失必需构造参数、错误结构、未知版本和超过 1 MiB 的文件；错误单独展示，不使其他配置不可用。写入先完整序列化并写临时文件、刷盘，再以同目录原子替换提交；失败保留原文件。每功能目录短期独占锁串行化协作进程的保存/删除，不长期锁住配置或输入文件。

配置不是数据文件快照：只有显式选定的筛选字面值属于配置。应用时检查新预览和新候选，列号与原表头均相同但业务含义变化无法自动识别。DEBUG GUI 验收可在组合根中注入测试目录；发布版本不启用测试环境覆盖。
