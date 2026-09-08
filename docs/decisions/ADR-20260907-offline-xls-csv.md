# ADR-20260907 Offline XLS / CSV processing

- Status: Accepted under Owner delegated implementation authority (2026-09-08)
- Decision: NPOI 2.7.4 HSSF + CsvHelper 33.1.0, with explicit security dependency constraints below
- Date: 2026-09-07
- Owner: Codex (Core Developer)
- Authority: [initial exact dependency approval](https://github.com/lzx0215/TabularStudio/issues/34#issuecomment-5571428555), followed by Owner 2026-09-08 instruction authorizing Codex to change library/version, remediate vulnerabilities/licenses and complete #34 without changing requirements.
- Related Issue: [#34](https://github.com/lzx0215/TabularStudio/issues/34)
- Gate 1: [Pre-Development Approval](https://github.com/lzx0215/TabularStudio/issues/34#issuecomment-5571217002)，2026-09-07T13:17:03Z
- Main baseline: `c0e91cb3f2f79b82d2c8ae80ce22374efdc7b801`
- Branch: `issue-34-core-multiformat`

## Context

## 2026-09-08 final decision (supersedes the historical proposal below)

本节为当前决定；下方 2026-09-07 Proposed / STOP / NOT VERIFIED 原文保留历史，不能覆盖本节的实际证据。

- NPOI 2.7.6 restore 实际引入 NSax 1.0.2 (LGPL-3.0-only) 和有 High 漏洞的 Cryptography.Xml 8.0.2，原实验已清理。
- 选 NPOI **2.7.4**：这是 NSax 引入前的官方 Apache-2.0 包，提供 HSSF 真实 XLS 读写。2.7.5/2.7.6 含 NSax；2.8.0 二进制许可与 native 依赖变化，不是本次最小风险路径。没有修改第三方源码、隐藏依赖或抑制 NuGet audit。
- 保留 CsvHelper **33.1.0**，采用 Apache-2.0；ClosedXML **0.105.1** 不变。
- 显式安全约束：**System.Security.Cryptography.Xml 10.0.11**（带入 Pkcs 10.0.11）、**SixLabors.ImageSharp 2.1.11**、**BouncyCastle.Cryptography 2.6.2**。这些直接引用是本轮授权的 remediation，不冒称最初精确两包审批已涵盖它们。
- 实际 NuGet audit：最终 Core graph 未报告已知漏洞；没有 NSax。该结果是当前源数据库的扫描结果，不保证未来无漏洞。依赖版本和扫描须持续维护。
- 最终直接包：ClosedXML 0.105.1、NPOI 2.7.4、CsvHelper 33.1.0、BouncyCastle.Cryptography 2.6.2、SixLabors.ImageSharp 2.1.11、System.Security.Cryptography.Xml 10.0.11。
- 实际传递包：ClosedXML.Parser 2.0.0、DocumentFormat.OpenXml 3.1.1、DocumentFormat.OpenXml.Framework 3.1.1、Enums.NET 5.0.0、ExcelNumberFormat 1.1.0、ExtendedNumerics.BigDecimal 2025.1001.2.129、MathNet.Numerics.Signed 5.0.0、Microsoft.IO.RecyclableMemoryStream 3.0.1、RBush.Signed 4.0.0、SharpZipLib 1.4.2、SixLabors.Fonts 1.0.1、System.IO.Packaging 8.0.1、System.Security.Cryptography.Pkcs 10.0.11、ZString 2.6.0。
- 使用官方 nuspec 和实际 project.assets.json 核对，不以候选最低依赖充当 resolved graph。许可清单与原文在 [issue34-licenses](issue34-licenses/README.md)。旧 NPOI 版本维护风险仍存在；不用 Office、不在线授权、不运行时 restore。
- 最终实现使用原 HSSF 工作簿 + 内存值投影，局部写回变化，不转换成中间 XLSX 文件。未变公式/样式/Sheet 留在原工作簿；复杂对象/旧 BIFF 完全保真 **NOT VERIFIED**。
- CSV 技术选择由本轮授权落地于 processing-rules §11：严格 UTF-8、逗号、RFC4180 quoting、保留空记录、不齐行、按解析记录编号、保持 BOM 有无。不声称这些是最初 Gate 1 已批准 Expected Result；没有增加产品功能。
- 已执行 .NET SDK 10.0.400 restore/build/regression 和独立 FT harness；具体 exact SHA 及最终结果归档于 PR。整机断网/无 SDK clean machine **NOT VERIFIED**，不由 no-restore 或代码检查推断。

精确源码/包依据：[NPOI 2.7.4 nuspec](https://api.nuget.org/v3-flatcontainer/npoi/2.7.4/npoi.nuspec)、[NPOI 2.7.5 nuspec](https://api.nuget.org/v3-flatcontainer/npoi/2.7.5/npoi.nuspec)、[Cryptography.Xml 10.0.11](https://www.nuget.org/packages/System.Security.Cryptography.Xml/10.0.11)。

## Historical proposal — 2026-09-07

Issue #34 的 Gate 1 已批准 Requirements / Scope / AC / FT / VC；该批准没有批准具体第三方库、版本或 Open Questions。本 ADR 只提出可评审的离线依赖选择，不修改批准的测试包或产品规则。

开始前工作树为空，已执行 `git fetch origin`；本地 main、origin/main 与 GitHub 远端 main 均为上述基线。已重读 Issue body/comments、AGENTS.md、development-process、requirements、processing-rules、contracts、architecture。Issue 的更新时间与 Gate 1 批准时间一致，未发现批准后的 packet 修改；当前 GitHub packet 的 FT34-01～16、VC34-01～04 是追溯依据，不使用先前聊天草案替代它。

已确认约束：

- Windows 本地桌面，C# / .NET 10；Core 当前目标 `net10.0`，SDK 基线 `10.0.100`。
- `.xlsx` 继续使用仓库现有 `ClosedXML 0.105.1`，本 ADR 不替换它。
- `.xls` / `.csv` 必须完全离线、本地执行；不引入 Web / API / 数据库 / 在线服务或在线授权。
- 格式统一输出跟输入格式；数据匹配输出跟主表格式，不自动跨格式转换，不能只改扩展名伪装格式。
- `.xlsx/.xls` 有 Sheet；CSV 不要求工作表名，不进入同文件不同 Sheet 语义。
- 输入文件不变、禁止覆盖输入、已有输出须确认；公式/样式在对应格式能力范围内尽量保留。
- 测试仅用 runtime-generated synthetic / non-sensitive 文件，不使用真实业务数据。

本轮只新增本 ADR。未修改 production code、csproj、packages、tests、contracts、processing-rules、architecture、ui-spec、requirements；未安装或 restore 新依赖，未开始 WPF 或其他 Issue。

## Evidence and interpretation

资料核对日期为 2026-09-07。下文的“支持”表示官方文档或包元数据描述的能力，不表示本项目已实测。所有候选的 .NET 10 项目兼容性、运行行为与输出保真均为 **NOT VERIFIED**。

本轮只读取官方网页及 NuGet `.nuspec` 文本元数据，没有下载/安装 NuGet 二进制或执行 restore。依赖列表是包声明的最低版本约束，不是已经解析并锁定的依赖图。最终 TFM/RID 资产选择、版本统一和发布文件集只能在获批后验证。

“直接依赖”分两层：Core 拟直接引用的顶层包；顶层包声明的一级依赖（对 Core 而言是传递依赖）。表格同时说明这两层，不能把“只调用 HSSF”视为“其他包不进入依赖图”。

## .xls Candidates

以下三个候选均具备官方描述的真实二进制 XLS 读取和写出能力；不是把 CSV/HTML 改名为 XLS。具体候选版本的项目往返测试均未执行。

| 项目 | X1: NPOI 2.7.6 / HSSF | X2: NPOI 2.8.0 / HSSF | X3: GemBox.Spreadsheet 2026.1.100 / Professional |
| --- | --- | --- | --- |
| 真实 `.xls` 读取 / 写出 | 支持 / 支持；HSSF 工作簿路径 | 支持 / 支持；HSSF 工作簿路径 | 支持 / 支持；官方列出 Excel 97–2003 XLS 读写 |
| Microsoft Office | 不需要 | 不需要 | 不需要 |
| 在线运行依赖 | 本地库方案，不需要在线处理服务；断网与无在线授权实测 NOT VERIFIED | 本地处理；二进制许可有维护费条款，不能据此推断有或没有联网校验，精确版本断网行为 NOT VERIFIED | 厂商说明可无互联网运行；本地 license key 模式，精确版本及所购许可的离线部署 NOT VERIFIED |
| License | 包声明 Apache-2.0 | NuGet 包声明 `OSMFEULA.txt`；源码 Apache-2.0 不能替代二进制 EULA 判断 | 专有 GemBox EULA；不限数据量的正常使用应评估 Professional 许可 |
| Core 顶层直接包 | `NPOI 2.7.6` | `NPOI 2.8.0` | `GemBox.Spreadsheet 2026.1.100` |
| 一级 / 主要传递依赖 | 见 D1；图像、字体、压缩、数值、加密依赖 | 见 D2；包含 SkiaSharp 及 native assets | 见 D3；包含 SkiaSharp、HarfBuzzSharp 及 native assets |
| .NET 10 兼容性 | **NOT VERIFIED**；包提供 net8.0 / netstandard 资产，net10.0 为兼容推断 | **NOT VERIFIED**；nuspec 有 net10.0 组，也不是项目 build/test 证据 | **NOT VERIFIED**；现有 Core 为 net10.0，预计使用 netstandard2.0 资产，不能直接当作 net6.0-windows 组 |
| 公式 / 样式 / Sheet | 有公式、样式和工作表模型，适合打开原工作簿后局部修改；不保证任意记录无损保留 | 同类模型；升级不能自动视为保真改善 | 有工作表/格式/公式模型；厂商明确各格式支持程度不同，不能据此承诺任意 XLS 元素保留 |
| 主要风险 | 固定旧版本的维护风险；与 ClosedXML 共用依赖的版本统一；尚无项目往返证据 | 二进制许可适用性与维护费；native 资产；版本升级带来的行为差异 | 商业许可成本；Free 模式行/Sheet 限制，Trial 模式会替换部分数据；不可用于正式完整数据处理 |
| 离线发布风险 | 必须随包交付解析出的全部必要依赖和许可证；不得运行时恢复包 | 必须核实 EULA、离线授权和 win-x64 native 文件；不能只复制 NPOI.dll | 必须核实许可分发条件、本地许可配置和 win-x64 native 文件；不得依赖在线激活或 Trial 降级 |
| 本次意见 | 优先推荐，仍须 Owner 决策与后续验证 | 不推荐自动升级或直接采用 | 商业备选，当前不推荐引入 |

能力依据：[NPOI 官方说明](https://github.com/nissl-lab/npoi)、[GemBox 格式支持](https://www.gemboxsoftware.com/spreadsheet/docs/supported-file-formats.html)、[GemBox 无 Office 读取说明](https://www.gemboxsoftware.com/spreadsheet/examples/open-read-excel-files-c-sharp/6009)。上述通用文档不是三个指定版本的项目测试报告。

许可与离线依据：[NPOI 2.7.6](https://www.nuget.org/packages/NPOI/2.7.6)、[NPOI 2.8.0](https://www.nuget.org/packages/NPOI/2.8.0)、[NPOI 二进制 EULA](https://github.com/nissl-lab/npoi/blob/master/OSMFEULA.txt)、[GemBox 许可模式](https://www.gemboxsoftware.com/spreadsheet/examples/free-trial-professional/1001)、[厂商离线运行答复](https://forum.gemboxsoftware.com/t/can-this-be-run-offline/613)。NPOI 2.8 的二进制维护费适用条件需按其 EULA 判断，不能笼统说成 Apache-2.0 免费包；本 ADR 不替 Owner 判定自身适用性。GemBox 精确版本 EULA 全文审核为 NOT VERIFIED。

公式/样式保留只提出验证方向：检查普通公式表达式、单元格格式、行列布局、工作表顺序及未选 Sheet。库提供计算 API 不代表 #34 获准启用重算。BIFF 代际、加密、宏和嵌入对象的支持/拒绝政策仍为 Open Questions；任何候选的限制都不能自动变成产品规则。

### D1 — NPOI 2.7.6 dependencies

[精确版本 nuspec](https://api.nuget.org/v3-flatcontainer/npoi/2.7.6/npoi.nuspec) 的 net8.0 组声明如下。版本为最低约束，未 restore，实际解析版本 **NOT VERIFIED**。

| NPOI 的一级依赖（均为 Core 的传递依赖） | 最低版本 |
| --- | --- |
| Enums.NET | 5.0.0 |
| ExtendedNumerics.BigDecimal | 2025.1001.2.129 |
| MathNet.Numerics.Signed | 5.0.0 |
| Microsoft.IO.RecyclableMemoryStream | 3.0.1 |
| BouncyCastle.Cryptography | 2.6.2 |
| SharpZipLib | 1.4.2 |
| SixLabors.Fonts | 1.0.1 |
| SixLabors.ImageSharp | 2.1.11 |
| ZString | 2.6.0 |
| NSax | 1.0.2 |
| System.Security.Cryptography.Xml | 8.0.2 |

重点的进一步依赖：ImageSharp 2.1.11 的 netcoreapp3.1 组声明 `System.Runtime.CompilerServices.Unsafe >= 5.0.0`、`System.Text.Encoding.CodePages >= 5.0.0`；Cryptography.Xml 8.0.2 的 net8.0 组声明 `System.Security.Cryptography.Pkcs >= 8.0.1`。这是候选依赖链，不能当作完整最终闭包。

已读元数据中，SixLabors.Fonts 1.0.1 / ImageSharp 2.1.11 为 Apache-2.0，SharpZipLib 1.4.2 / ExtendedNumerics.BigDecimal 2025.1001.2.129 / Cryptography.Xml 8.0.2 为 MIT。其他依赖及最终解析版本的完整许可/NOTICE 审核 **NOT VERIFIED**；不得沿用父包许可替代逐包审查。

来源：[ImageSharp nuspec](https://api.nuget.org/v3-flatcontainer/sixlabors.imagesharp/2.1.11/sixlabors.imagesharp.nuspec)、[Fonts nuspec](https://api.nuget.org/v3-flatcontainer/sixlabors.fonts/1.0.1/sixlabors.fonts.nuspec)、[Cryptography.Xml nuspec](https://api.nuget.org/v3-flatcontainer/system.security.cryptography.xml/8.0.2/system.security.cryptography.xml.nuspec)、[SharpZipLib nuspec](https://api.nuget.org/v3-flatcontainer/sharpziplib/1.4.2/sharpziplib.nuspec)、[BigDecimal nuspec](https://api.nuget.org/v3-flatcontainer/extendednumerics.bigdecimal/2025.1001.2.129/extendednumerics.bigdecimal.nuspec)。

### D2 — NPOI 2.8.0 dependencies

[精确版本 nuspec](https://api.nuget.org/v3-flatcontainer/npoi/2.8.0/npoi.nuspec) 的 net10.0 组声明：

- BouncyCastle.Cryptography >= 2.6.2、Enums.NET >= 5.0.0、ExtendedNumerics.BigDecimal >= 2025.1001.2.129、MathNet.Numerics.Signed >= 5.0.0。
- Microsoft.IO.RecyclableMemoryStream >= 3.0.1、Microsoft.SourceLink.GitHub >= 8.0.0、SharpZipLib >= 1.4.2。
- SkiaSharp >= 3.119.2、SkiaSharp.NativeAssets.Linux.NoDependencies >= 3.119.2、System.Security.Cryptography.Xml >= 8.0.2、ZString >= 2.6.0。

SkiaSharp 3.119.2 的通用 net8.0 组进一步声明 NativeAssets.Win32 / NativeAssets.macOS >= 3.119.2；最终 Windows 发布要验证 RID 资产选择，不能因为列表有 Linux 包就断言 Windows 不兼容，也不能假定 native DLL 无需发布。SourceLink 的 build 资产与运行时文件应分开核验。完整闭包、二进制 EULA 审核及项目兼容性 **NOT VERIFIED**。

来源：[SkiaSharp 3.119.2 nuspec](https://api.nuget.org/v3-flatcontainer/skiasharp/3.119.2/skiasharp.nuspec)。

### D3 — GemBox.Spreadsheet 2026.1.100 dependencies

Core 当前 `net10.0`，没有 Windows TFM；不为候选库自行修改 TFM。以下按候选 netstandard2.0 组列出，实际 restore 选择 **NOT VERIFIED**。

[精确版本 nuspec](https://api.nuget.org/v3-flatcontainer/gembox.spreadsheet/2026.1.100/gembox.spreadsheet.nuspec) 声明：

- HarfBuzzSharp / HarfBuzzSharp.NativeAssets.Linux >= 7.3.0.3。
- Microsoft.Bcl.Numerics >= 9.0.0、Portable.BouncyCastle >= 1.9.0。
- SkiaSharp / SkiaSharp.NativeAssets.Linux >= 2.88.9。
- System.Buffers >= 4.6.0、System.Reflection.Emit.ILGeneration / Lightweight >= 4.7.0。
- System.Runtime.CompilerServices.Unsafe >= 6.1.0、System.Text.Encoding.CodePages >= 6.0.0。

其 net6.0-windows7.0 组与此不同，不能混用：BouncyCastle.Cryptography >= 2.4.0、HarfBuzzSharp >= 7.3.0.3、SkiaSharp >= 2.88.9、System.Buffers >= 4.6.0、System.Runtime.CompilerServices.Unsafe >= 6.1.0、System.Text.Encoding.CodePages >= 6.0.0。

进一步依赖包括 SkiaSharp.NativeAssets.Win32 / macOS >= 2.88.9、HarfBuzzSharp.NativeAssets.Win32 / macOS >= 7.3.0.3。SkiaSharp / HarfBuzzSharp 上述包元数据为 MIT，但 vendor 主包为专有 EULA；native 组件 notices、许可配置、体积和加载行为均需单独审核。来源：[SkiaSharp nuspec](https://api.nuget.org/v3-flatcontainer/skiasharp/2.88.9/skiasharp.nuspec)、[HarfBuzzSharp nuspec](https://api.nuget.org/v3-flatcontainer/harfbuzzsharp/7.3.0.3/harfbuzzsharp.nuspec)。

## .csv Candidates

| 项目 | C1: CsvHelper 33.1.0 | C2: .NET 10 TextFieldParser + 自维护 StreamWriter writer |
| --- | --- | --- |
| Parsing | 提供字段解析、引号及字段内换行支持；有可配置解析行为 | 提供分隔字段、引号字段解析及错误信息；不是完整读写库 |
| Quoting / escaping | writer 提供字段引用和转义，减少自写状态机 | writer 由项目负责实现和测试双引号转义、字段引用及记录分隔 |
| License | `MS-PL OR Apache-2.0`；建议采用 Apache-2.0 路径，保留所需 notices | .NET runtime 为 MIT，随分发保留其 notices；自写部分按仓库约定，不添加外部库许可 |
| Core 顶层新依赖 | `CsvHelper 33.1.0` | 无额外第三方 CSV NuGet；使用现有 .NET 10 本地程序集 |
| 包一级 / 传递依赖 | net8.0 / net9.0 组为空；Core net10.0 实际选组仍需 restore 核实 | 无新增 CSV 包闭包；不预先添加 CodePages 包 |
| Offline constraints | 本地 TextReader / TextWriter，无 Office 或在线处理需要；断网实测 NOT VERIFIED | 本地文件与运行时，无 Office 或在线处理需要；断网实测 NOT VERIFIED |
| .NET 10 verification status | **NOT VERIFIED**；框架资产兼容推断不是项目实测 | **NOT VERIFIED**；.NET 10 文档存在不是本项目实现已验证 |
| 风险 | 库默认行为不能当作产品政策；必须按批准规则配置 | parser 对空白/注释行、空格的既有行为可能不符合未来政策；自写 writer 与解析配合需额外验证 |
| 增加依赖是否值得 | 推荐：统一读写机制，降低 quoting/escaping 维护负担 | 可减少一个包，但增加 writer 和边界测试维护成本；需确认 parser 不强加未批准语义 |

依据：[CsvHelper 33.1.0 元数据](https://api.nuget.org/v3-flatcontainer/csvhelper/33.1.0/csvhelper.nuspec)、[CsvHelper 官方读写说明](https://joshclose.github.io/CsvHelper/getting-started/)、[TextFieldParser .NET 10 文档](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualbasic.fileio.textfieldparser?view=net-10.0)、[.NET 10 runtime license](https://github.com/dotnet/runtime/blob/v10.0.0/LICENSE.TXT)。

TextFieldParser 的 EndOfData/ReadFields 面向有数据的行，并有 TrimWhiteSpace 等配置；不能将其默认值用作批准的 CSV 空记录或空白政策。若未来已批准政策无法由该 parser 表达，需要重新评估，不能静默跳过记录。CsvHelper 同样不能通过默认配置替 Owner 决定编码、方言或坏行处理。

## Recommendation

**PROPOSED — PROJECT OWNER DECISION REQUIRED**

推荐组合：现有 ClosedXML 0.105.1 处理 `.xlsx`，新增 NPOI 2.7.6 的 HSSF 路径处理 `.xls`，新增 CsvHelper 33.1.0 处理 `.csv`。

理由：保留现有 `.xlsx` 技术约束；NPOI 提供无需 Office 的 XLS 读写模型，2.7.6 包声明为 Apache-2.0；CsvHelper 覆盖 CSV reader/writer 两端，减少自维护 quoting/escaping 的成本。该建议不是功能保真或安全审计通过结论。

建议仅在 Core 内隔离格式读写差异并复用现有处理规则；本 ADR 不定义 interface、DTO 或 Contract shape。按格式写回，不通过先转为 XLSX 再转回的链路实现。对 Excel 工作簿优先评估打开原工作簿、局部修改、另存新文件的路径，保真能力以后续证据为准。

NPOI 2.8.0 不作为自动升级目标；其许可和 native 依赖变化需独立决策。GemBox 为商业备选，不通过 Free/Trial 限制规避采购。仅为读取的库不能独立满足原 XLS 写回，因此不作为完整替代；本轮不引入额外 reader 做生产依赖。

## Dependency Decision

**实现 #34 是否需要新增 NuGet package？YES — 按本 ADR 推荐方案需要。**

这是工程推荐的依赖需求，不声称所有理论实现都必须使用第三方包。自写 CSV 可以少一个包，但当前没有已验证的零新增依赖 XLS 完整读写方案；自行实现二进制工作簿读写成本和风险不合理。

| 精确 package / 推荐 version | Purpose | License | Direct dependencies / relevant transitives | Offline deployment implications |
| --- | --- | --- | --- | --- |
| `NPOI` / `2.7.6` | 真实 XLS 读取、局部修改和 XLS 写回 | Apache-2.0（精确包元数据） | Core 新直接引用；包的 11 项一级依赖及进一步依赖见 D1 | 发布需携带最终解析依赖、许可/NOTICE；不得运行时访问包源；不能只分发 HSSF 相关 DLL 就假定完整 |
| `CsvHelper` / `33.1.0` | CSV parsing / quoting / escaping / writing | MS-PL OR Apache-2.0；拟选择 Apache-2.0 | Core 新直接引用；net8.0/net9.0 组无一级依赖 | 随应用分发；本地读写；未证实需要新增编码包，不作隐式授权 |

主要已知风险与限制：

1. NPOI 与 ClosedXML 可能共享字体等依赖；最终版本统一结果及其许可证未核实。最低版本清单不能直接当作发布锁定清单。
2. 旧版本维护、间接依赖漏洞和破坏性升级风险仍存在。完整漏洞审查 **NOT VERIFIED**，不声明“无漏洞”；单包未返回漏洞元数据也不能证明整体安全。
3. 输出保真、字体/图像资源需求、异常与取消后的临时文件清理、win-x64 发布均 **NOT VERIFIED**。
4. 不保证 CSV 读写库默认值符合产品规则；不通过选择库批准 CSV 类型/编码政策。
5. 软件许可元数据不等于完整发布合规审查；未获批前不引入包，审批也不能替代后续验证。

Alternatives considered：NPOI 2.8.0、GemBox.Spreadsheet 2026.1.100 Professional、TextFieldParser + 自维护 writer。差异及不推荐原因见候选表。C2 仅解决 CSV，不能消除 XLS 库决策。

请 Owner 决定是否接受上述两个精确顶层包及依赖风险，允许后续依赖解析与项目验证。该决策不自动批准其他包、版本升级、Contract 变更或 Open Questions；若解析结果要求新的重大选择或触及冲突，再 STOP 返回决策。

## Open Questions / Future Technical Decision Inputs

下列保持 GitHub Gate 1 packet 的未批准状态。本 ADR 不指定支持或拒绝政策，不把它们写成已批准 Expected Result：

- CSV 编码范围（含 UTF-16 / GBK / GB18030）及编码保持策略。
- CSV 方言、分隔符、坏行、不齐行、空记录政策。
- CSV 行号语义，包括字段内换行是否影响 HeaderRow / Preview 行号。
- CSV 类型写回、日期/数字文本、公式外观文本及跨格式类型表达。
- CSV 无 Sheet 在 Contract 的具体表达、额外 Sheet 参数的处理。
- BIFF8 / 更早 BIFF、加密文件、宏、嵌入对象及其他未定义 XLS 边界。

库能力或能力缺口只作为未来决策输入。依赖选择获批不解决这些问题；相关行为确定前不得把默认值或猜测写进实现。

## Verification Plan

下表仅设计 Owner 决策后的验证。本轮全部 **NOT VERIFIED / NOT RUN**，不执行 restore/build/test、不合成文件、不启用 GUI 自动化。Gate 1 的 FT/VC 编号和内容不在本 ADR 中重写。

| 验证 | 批准后执行方式及证据 | 当前状态 |
| --- | --- | --- |
| Restore / dependency graph | 在批准范围内引入精确顶层版本后，运行 `dotnet restore TabularStudio.sln`；保存实际解析版本、TFM/RID 资产、冲突和许可证/NOTICE 清单，核查漏洞信息；开发环境恢复与终端离线运行分开验证 | NOT VERIFIED / NOT RUN |
| Build (VC34-03) | 仓库根目录 `dotnet build TabularStudio.sln`；记录 exit code、日志与 exact SHA | NOT VERIFIED / NOT RUN |
| Automated tests (VC34-04) | `dotnet test TabularStudio.sln`；新增批准行为覆盖，保留 `.xlsx` 回归；记录测试数量、失败/跳过和日志 | NOT VERIFIED / NOT RUN |
| Runtime-generated files | 运行时生成非敏感 XLS/CSV，覆盖 Gate 1 FT34-01～16 适用路径；记录具体样本参数，不以样本格式扩展批准范围 | NOT VERIFIED / NOT RUN |
| Actual output reopen | 独立于 service 成功标志重新打开结果；核对真实格式、扩展名、字段、行序、匹配结果；CSV 增加明确期望字段的核对，避免同一 reader/writer 缺陷互相掩盖 | NOT VERIFIED / NOT RUN |
| Cross-format matching | 执行已批准 FT34-04 的 9 种主/对照表组合，核对输出跟主表；执行同文件不同 Sheet 及禁止自动转换用例 | NOT VERIFIED / NOT RUN |
| Formula / style / Sheet capability | 合成普通公式、样式、行高列宽及多个 Sheet；逐项记录保留结果和能力缺口，不运行公式计算；不将宏/BIFF/嵌入对象边界偷偷扩成验收项 | NOT VERIFIED / NOT RUN |
| Input SHA256 | 格式统一输入、匹配两侧输入在执行前后逐文件比较 SHA256；同时检查输出冲突和未授权覆盖；沿用 FT34-12/13/16 | NOT VERIFIED / NOT RUN |
| Offline run | 依赖随包准备后，在无 Office、断网的 Windows 环境执行 Core 主/失败路径；核查无网络/在线授权/运行时包恢复需求和缺失 DLL；不启用桌面 GUI 自动化 | NOT VERIFIED / NOT RUN |
| Document verification | 后续按授权同步关联文档，验证 VC34-01/02；本次 Proposed ADR 不表示 AC1/AC6 已全部满足 | NOT VERIFIED / NOT RUN |
| Functional QA | FT34-01～16 逐条实际执行并保留结果；Core automated tests 不能替代 Functional QA，更不能宣称 UI QA 通过 | NOT VERIFIED / NOT RUN |

合并后仍须按 development-process 在最新 main 重跑规定的 Build / Test / Functional QA / Regression；本 ADR 不提前请求合并或发布验收。

## Consequences and scope guard

- 只提交这份 Proposed ADR；不创建实现 PR，不安装/restore 新依赖，不继续开发。
- 未修改 Contract / Baseline；后续同步须按既有流程 Review，不能用本 ADR 自动批准 shape。
- 六项 bool 默认值差异只作 Out-of-Scope Observation，不在 #34 修复。
- 不开始 #31 / #32 / #33，不增加批量、WPF、输出目录记忆、网络或其他格式。
- 撤回本提案不影响运行时：当前没有代码或依赖变更；若 Owner 不接受，后续只修订本 ADR，不据此继续实现。

## Decision checkpoint

**STOP — PROJECT OWNER THIRD-PARTY LIBRARY DECISION REQUIRED**

依据 Issue #34 In Scope 2、Gate 1 批准评论及 development-process 第 6 节：推荐方案需要新增第三方库，必须先获得 Project Owner 决策。当前只提交草案供审阅，不把 Proposed 写成 Accepted。
