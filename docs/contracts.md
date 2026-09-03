# UI / Core 契约

- Status: Draft for UI Review
- Owner: **Codex + Antigravity**
- Draft author: Codex（Core Developer）
- Required reviewer: Antigravity（UI Contract Review）
- Reviewer for scope: Grok（Product Manager）
- Related: `docs/requirements.md`、`docs/ui-spec.md`、`docs/processing-rules.md`、`docs/architecture.md`、GitHub Issue #7

## 1. 文档目的与状态

本文定义 WPF UI / ViewModel 与本地 Core 之间的第一版调用契约草案，使双方能独立实现已批准的工作簿检查、格式统一和数据匹配能力。

本文只定义接口、请求、结果、错误和进度的数据形状，不包含 ClosedXML 业务实现、WPF 页面实现或处理算法。当前状态不是批准基线；必须经 Antigravity 从 UI 角度 Review，并按 Issue #7 的双方 Review 流程确认后，才能进入业务实现。

事实来源与冲突优先级：

1. 产品范围以 `docs/requirements.md` 为准；
2. 页面和交互以 `docs/ui-spec.md` 的 Approved UI Baseline 为准；
3. 处理行为以 `docs/processing-rules.md` 的 Approved Processing Baseline 为准；
4. 本文只负责表达上述输入与结果，不得反向改变三份基线。

## 2. 调用方向与技术边界

调用方向固定为：

```text
WPF View / ViewModel
        ↓
UI / Core Contract
        ↓
Core Service
        ↓
ClosedXML / Excel Processing
```

边界要求：

- UI 只引用普通 .NET 契约，不引用 ClosedXML，不解析 Excel，不实现格式统一或匹配算法。
- Core 不引用 WPF，不知道 `Button`、`DataGrid`、`Dialog`、`ViewModel` 等 UI 类型。
- Contract 不出现 `IXLWorkbook`、`IXLWorksheet`、`IXLCell`、`IXLRange` 或其它 ClosedXML 类型。
- Contract 不出现 WPF 专属类型，不返回最终 UI 文案，不规定弹窗或控件行为。
- Contract 只服务同一进程中的 WPF UI ↔ 本地 Core，不是网络 API 或序列化协议。

## 3. 责任矩阵

| 责任 | UI / ViewModel | Contract | Core |
| --- | --- | --- | --- |
| 文件、Sheet、表头行选择 | 收集并维护用户选择 | 表达路径、Sheet、表头行 | 校验文件、Sheet、表头行并读取工作簿 |
| 数据预览 | 请求并展示只读预览 | 表达普通 .NET 预览数据 | 读取表头、字段与前 20 行数据 |
| 格式统一选项 | 展示并提交已批准的 8 项开关 | 表达 8 个布尔值 | 按 Processing Baseline 执行规则 |
| 格式统一安全底线 | 展示已批准说明 | 不提供关闭安全底线的参数 | 始终保护公式、业务格式和输入文件 |
| 匹配条件 | 配置 1～N 条字段映射 | 表达条件列表，不表达 AND / OR 操作符 | 固定按 AND 精确匹配 |
| 返回字段 | 选择一个或多个对照表字段 | 表达字段列表 | 生成实际唯一输出列名并返回映射 |
| 比较标准化 | 提交是否启用 | 表达一个总开关 | 使用批准算法，不接受 UI 自定义算法 |
| 匹配状态列 | 配置启用状态与期望列名 | 表达启用状态与列名 | 写固定状态值并生成实际唯一列名 |
| 输出路径 | 选择路径 | 表达路径与一次执行的覆盖意图 | 写输出并始终拒绝覆盖输入 |
| 已有输出文件 | 询问覆盖 / 另存为 / 取消并决定是否重试 | 表达覆盖意图与结构化冲突错误 | 检测冲突，未授权时拒绝覆盖 |
| 处理进度 | 映射阶段并展示进度 | 表达稳定阶段、百分比和行数 | 报告不含 ClosedXML 细节的进度 |
| 成功与错误 | 显示摘要、统计和可操作错误 | 表达结构化结果与错误 | 返回结果；不控制 Dialog |
| 打开结果 / 文件夹 | 成功后执行桌面操作 | 只返回结果文件路径 | 不启动外部程序 |
| 格式统一结果送入匹配 | 切换页面并把结果路径作为主表输入 | 通过结果路径支持流转 | 不负责页面导航或自动开始匹配 |

## 4. 共享约定与模型

### 4.1 成功 / 失败约定

- `Success = true` 时，`Error` 必须为 `null`，对应成功数据必须存在。
- `Success = false` 时，`Error` 必须存在，不得返回可当作成功结果使用的输出路径或统计。
- Core 返回结构化数据；UI 决定中文文案、视觉状态和 Dialog 形式。
- 文件路径均为本机路径；Core 必须再次校验，不信任 UI 已做过的拦截。

### 4.2 工作表来源

```csharp
namespace TabularStudio.Core.Contracts;

public sealed record WorksheetSource(
    string FilePath,
    string WorksheetName,
    int HeaderRowNumber);
```

`WorksheetSource` 同时用于格式统一、主表和对照表。主表与对照表来自同一个工作簿时，两个 `FilePath` 直接相同；Contract 不接收「与主表使用同一个文件」CheckBox 状态，也不增加额外业务模式。

### 4.3 通用错误

```csharp
public enum OperationErrorCode
{
    FileNotFound,
    UnsupportedFileType,
    FileLocked,
    WorkbookUnreadable,
    WorksheetNotFound,
    InvalidHeaderRow,
    ColumnNotFound,
    InvalidConfiguration,
    OutputConflictsWithInput,
    OutputAlreadyExists,
    OutputDirectoryNotWritable,
    FormulaCellNotAllowedForMatching,
    IncompleteOutputCleanupFailed,
    ProcessingFailed
}

public sealed record OperationError(
    OperationErrorCode Code,
    string Message,
    string? Detail = null);
```

- `Code` 是 UI 可稳定分支处理的值。
- `Message` 是可供 UI 显示或包装的简明错误信息，但不是完整 Dialog 文案。
- `Detail` 只承载必要的补充上下文，例如字段名、Sheet 名或未完整清理的输出路径；UI 不应解析 `Detail` 来决定业务分支。
- 不为每个底层异常创建枚举；未能映射到稳定业务类别的处理失败使用 `ProcessingFailed`。

### 4.4 通用进度

```csharp
public enum OperationStage
{
    Reading,
    Preparing,
    Processing,
    Writing,
    Completed
}

public sealed record OperationProgress(
    OperationStage Stage,
    int? Percent,
    int? ProcessedRows,
    int? TotalRows);
```

- `Percent` 的有效范围是 `0`～`100`；不能可靠计算时为 `null`，UI 使用不确定进度表现。
- `ProcessedRows`、`TotalRows` 只在当前阶段能够可靠给出时填写，否则为 `null`。
- `Completed` 只用于成功完成，`Percent` 为 `100`。
- Stage 不暴露 ClosedXML 内部步骤；UI 自行把 enum 映射为阶段文案。

## 5. 工作簿检查与前 20 行预览契约

### 5.1 接口

```csharp
public interface IWorkbookInspectionService
{
    Task<WorkbookInspectionResult> InspectAsync(
        WorkbookInspectionRequest request,
        CancellationToken cancellationToken = default);

    Task<WorksheetPreviewResult> GetPreviewAsync(
        WorksheetPreviewRequest request,
        CancellationToken cancellationToken = default);
}
```

职责拆分：

- `InspectAsync` 只检查工作簿是否可读并返回 Sheet 列表，不替 UI 自动选择第一个 Sheet。
- `GetPreviewAsync` 校验用户指定的 Sheet 与表头行，返回字段和固定最多 20 条数据行预览。
- 格式统一页面调用一组检查 / 预览；数据匹配页面对主表和对照表分别独立调用。

### 5.2 请求与结果

```csharp
public sealed record WorkbookInspectionRequest(string FilePath);

public sealed record WorksheetInfo(string Name);

public sealed record WorkbookInspectionResult(
    bool Success,
    IReadOnlyList<WorksheetInfo> Worksheets,
    OperationError? Error);

public sealed record WorksheetPreviewRequest(WorksheetSource Source);

public sealed record PreviewColumn(
    int ColumnNumber,
    string Name);

public sealed record PreviewCell(
    int ColumnNumber,
    string? DisplayValue);

public sealed record PreviewRow(
    int WorksheetRowNumber,
    IReadOnlyList<PreviewCell> Cells);

public sealed record PreviewTable(
    string WorksheetName,
    int HeaderRowNumber,
    IReadOnlyList<PreviewColumn> Columns,
    IReadOnlyList<PreviewRow> Rows);

public sealed record WorksheetPreviewResult(
    bool Success,
    PreviewTable? Preview,
    OperationError? Error);
```

预览约定：

- `Columns` 来自指定表头行，供 UI 显示列头、配置匹配条件和选择返回字段。
- `Rows` 从表头之后的数据行开始，最多 20 行；预览上限不是请求参数，UI 不能通过 Contract 扩大。
- `WorksheetRowNumber` 保留原工作表行号，便于 UI 说明样本位置。
- `DisplayValue` 是普通可显示字符串，不是 Excel 对象，也不授权 UI 根据显示字符串推断处理类型。
- 表头行非法时返回 `InvalidHeaderRow`；Sheet 不存在时返回 `WorksheetNotFound`。

## 6. 格式统一契约

### 6.1 接口

```csharp
public interface IFormatStandardizationService
{
    Task<FormatStandardizationResult> ExecuteAsync(
        FormatStandardizationRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

### 6.2 请求

```csharp
public sealed record FormatStandardizationOptions
{
    public bool TrimOuterWhitespace { get; init; } = true;
    public bool RemoveTabsNewLinesAndHiddenCharacters { get; init; } = true;
    public bool NormalizeFullWidthHalfWidth { get; init; } = true;
    public bool NormalizeUnicode { get; init; } = true;
    public bool NormalizeSafeNumbers { get; init; } = true;
    public bool NormalizeUnambiguousDates { get; init; } = true;
    public bool PreserveLeadingZeroIdentifiers { get; init; } = true;
    public bool PreserveLongNumericText { get; init; } = true;
}

public sealed record FormatStandardizationRequest(
    WorksheetSource Source,
    string OutputFilePath,
    FormatStandardizationOptions Options,
    bool OverwriteExistingOutput);
```

请求约定：

- `Source` 表达输入文件、选定 Sheet 和表头行。
- `Options` 只表达 Approved UI Baseline 的 8 项用户可见开关，不允许 UI 传入具体 Unicode、数字或日期算法。
- `OverwriteExistingOutput` 只表达本次调用是否已获得用户对既有结果文件的明确覆盖意图。
- Contract 不提供 `PreserveFormula`、`PreserveStyle`、`AllowOverwriteInput`；公式、业务格式和输入保护是 Core 必须始终执行的安全底线。
- 表头及之前的行不处理，处理范围由 Processing Baseline 决定，不作为 UI 参数。

### 6.3 结果

```csharp
public sealed record FormatStandardizationSummary(
    string ProcessedWorksheetName,
    int ProcessedDataRowCount,
    TimeSpan Elapsed);

public sealed record FormatStandardizationResult(
    bool Success,
    string? OutputFilePath,
    FormatStandardizationSummary? Summary,
    OperationError? Error);
```

成功时：

- `OutputFilePath` 是已写入的新结果文件路径。
- `Summary.ProcessedWorksheetName` 是实际处理的 Sheet。
- `Summary.ProcessedDataRowCount` 只统计表头之后实际纳入处理范围的数据行。
- `Summary.Elapsed` 是本次 Core 执行耗时，供 UI 按 Approved UI Baseline 展示；UI 根据这些结构化数据生成最终成功文案。

## 7. 数据匹配契约

### 7.1 接口

```csharp
public interface IDataMatchingService
{
    Task<DataMatchingResult> ExecuteAsync(
        DataMatchingRequest request,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

### 7.2 匹配条件、状态列与请求

```csharp
public sealed record MatchingCondition(
    string MasterColumn,
    string ReferenceColumn);

public sealed record MatchingStatusColumnOptions
{
    public bool Enabled { get; init; } = true;
    public string ColumnName { get; init; } = "匹配状态";
}

public sealed record DataMatchingRequest(
    WorksheetSource Master,
    WorksheetSource Reference,
    IReadOnlyList<MatchingCondition> Conditions,
    IReadOnlyList<string> ReturnFields,
    bool NormalizeComparisonKeys,
    MatchingStatusColumnOptions StatusColumn,
    string OutputFilePath,
    bool OverwriteExistingOutput);
```

请求约定：

- `Conditions` 至少 1 条，全部固定为 AND；Contract 不提供 AND / OR、模糊、包含或相似度操作符。
- `ReturnFields` 至少 1 个，只表达用户选择的对照表字段；UI 不计算最终输出列名。
- `NormalizeComparisonKeys` 只控制是否启用 Approved Processing Baseline 的整组比较标准化；不开放前导 0、日期白名单、Unicode、大小写、相似度等算法参数。
- `StatusColumn.Enabled` 默认 `true`，`ColumnName` 默认「匹配状态」；状态值集合不是请求参数。
- 主表与对照表同文件不同 Sheet 时，只需让 `Master.FilePath == Reference.FilePath`。
- Core 必须校验所有字段存在、配置完整、匹配键与返回字段不包含公式数据。

### 7.3 返回字段与状态列结果

```csharp
public sealed record ReturnedFieldMapping(
    string RequestedField,
    string ActualOutputColumnName);

public sealed record DataMatchingSummary(
    int TotalMasterDataRowCount,
    int MatchedCount,
    int UnmatchedCount,
    int DuplicateCount,
    int EmptyKeyCount);

public sealed record DataMatchingResult(
    bool Success,
    string? OutputFilePath,
    DataMatchingSummary? Summary,
    IReadOnlyList<ReturnedFieldMapping> ReturnedFields,
    string? ActualStatusColumnName,
    OperationError? Error);
```

成功结果约定：

- `ReturnedFields` 按请求字段顺序返回 `RequestedField` → `ActualOutputColumnName` 映射；实际列名由 Core 按 Processing Baseline 确定性生成。
- 状态列启用时，`ActualStatusColumnName` 返回 Core 生成的最终唯一列名；关闭时为 `null`。
- `Summary` 返回主表总数据行、匹配成功、未匹配、重复和匹配键为空数量。
- 四种行级结果数量之和必须等于 `TotalMasterDataRowCount`。
- 状态文本固定为「匹配成功」「未匹配」「重复」「匹配键为空」，不由 UI 自定义。
- Core 保留主表字段和行顺序，不把内存比较值写回任一输入。

## 8. 输出已存在与输入保护流程

Core 对输出冲突保留最终防线，推荐调用流程如下：

1. UI 首次调用时使用 `OverwriteExistingOutput = false`。
2. 如果输出已存在，Core 返回 `OutputAlreadyExists`，不写文件。
3. UI 显示已批准的「覆盖 / 另存为 / 取消」选择：
   - 覆盖：保持路径并以 `OverwriteExistingOutput = true` 重新调用；
   - 另存为：更新路径并以 `false` 调用；
   - 取消：不再调用 Core。
4. 即使 `OverwriteExistingOutput = true`，输出路径与任一输入路径冲突时，Core 必须返回 `OutputConflictsWithInput`。

Contract 永远不提供 `AllowOverwriteInput`。输出写入中断时，Core 按 Processing Baseline 优先删除部分文件；删除失败返回 `IncompleteOutputCleanupFailed`，并在 `Detail` 中提供不完整文件路径。

## 9. async 与 CancellationToken

- 所有工作簿读取、预览和处理入口均返回 `Task<T>`，UI 必须异步等待，避免阻塞 WPF UI 线程。
- 处理入口通过 `IProgress<OperationProgress>` 报告阶段和可用进度；调用方负责将回调切换到合适的 UI 上下文。
- 方法保留 `CancellationToken cancellationToken = default`，只作为标准 .NET 生命周期边界，用于宿主关闭或安全中断。
- 当前 MVP UI 不提供用户可见的主动取消能力，不新增 Cancel 按钮，不新增 `Cancelled` 业务状态。
- 取消触发时允许标准 `OperationCanceledException` 传播给宿主生命周期处理；它不进入可由用户主动触发的产品流程。

## 10. UI 页面状态与契约映射

| UI 状态 | 契约输入 / 输出 |
| --- | --- |
| Initial / Empty | 尚未调用，或 UI 已清空当前配置 |
| File Loaded | `InspectAsync` 成功取得 Sheet；`GetPreviewAsync` 成功取得字段和最多 20 行预览 |
| Ready | UI 已收集完整 Request，并通过可在 UI 判断的必填校验；Core 仍在执行时做最终校验 |
| Processing | `ExecuteAsync` 尚未完成，UI 使用 `OperationProgress` 更新进度并冻结配置 |
| Success | Result 的 `Success = true`，UI 使用路径、摘要、统计和实际列名启用后置操作 |
| Error | Result 的 `Success = false` 及 `OperationError`，或宿主捕获无法转为 Result 的生命周期异常 |

格式统一成功后的 `OutputFilePath` 足以支持 UI 将结果送入数据匹配主表；Core 不负责页面切换、Sheet 自动选择或自动开始匹配。

## 11. 错误代码与 UI 处理意图

| Error Code | Core 含义 | UI 责任 |
| --- | --- | --- |
| `FileNotFound` | 输入路径不存在 | 提示重新选择文件 |
| `UnsupportedFileType` | 输入或输出不是 `.xlsx` | 提示 MVP 只支持 `.xlsx` |
| `FileLocked` | 文件被其它程序独占 | 提示关闭占用后重试 |
| `WorkbookUnreadable` | 工作簿损坏或无法安全读取 | 展示错误并允许重新选文件 |
| `WorksheetNotFound` | 指定 Sheet 不存在 | 刷新选择并重新预览 |
| `InvalidHeaderRow` | 表头行小于 1、越界或无法作为已选 Sheet 表头 | 修正表头行 |
| `ColumnNotFound` | 请求字段不存在 | 刷新字段配置 |
| `InvalidConfiguration` | 条件为空、返回字段为空或其它契约必填项无效 | 修正配置，不开始处理 |
| `OutputConflictsWithInput` | 输出与任一输入文件相同 | 强制更换输出路径 |
| `OutputAlreadyExists` | 输出已存在且未授权覆盖 | 询问覆盖 / 另存为 / 取消 |
| `OutputDirectoryNotWritable` | 输出目录不可写 | 更换目录或处理权限 |
| `FormulaCellNotAllowedForMatching` | 匹配键或返回字段实际含公式单元格 | 提示改选字段或在外部准备数据 |
| `IncompleteOutputCleanupFailed` | 任务失败且不完整输出删除失败 | 明确警告该路径不是可用结果 |
| `ProcessingFailed` | 其它处理失败 | 展示 Message，可附 Detail 供排查 |

## 12. 契约不变量与自检

### 12.1 格式统一

- UI 可以通过检查、预览、8 项开关、输出路径、进度和结果完成 Approved UI Baseline 的交互。
- 公式保持、业务格式保护和输入文件保护不可关闭。
- 文本、数字和日期算法不暴露为 UI 可定制策略。

### 12.2 数据匹配

- UI 可以独立选择主表 / 对照表的文件、Sheet、表头行并预览。
- Request 可以表达 1～N 条固定 AND 条件、一个或多个返回字段、比较标准化、状态列和输出路径。
- Result 可以表达全部行级统计、实际返回列名和实际状态列名。
- 公式检测、重复判断、状态判定、唯一列名和输入保护由 Core 完成。

### 12.3 依赖与范围

- UI 不需要引用 ClosedXML；Core 不需要引用 WPF。
- 没有 Repository、数据库、网络 API、MediatR、CQRS、Event Bus、Plugin、Domain Event 或序列化协议。
- 没有用户取消按钮、模糊匹配、公式计算、非 `.xlsx` 文件或第三个产品功能。

## 13. 待 Project Owner / UI Review 确认

### REVIEW-001 两个保护开关关闭时的语义

Approved UI Baseline 把 `PreserveLeadingZeroIdentifiers`、`PreserveLongNumericText` 列为默认勾选且可由用户操作的标准化开关；Approved Processing Baseline 同时规定前导 0 和长数字保护不得被数字 / 日期转换绕过。

本草案为完整表达 UI 配置而保留两个布尔字段，但在双方 Review 前不固化 `false` 为「允许丢弃前导 0 / 允许长数字精度失真」。需要 Antigravity 确认 UI 语义，并由 Grok / Project Owner 判断是否需要调整 UI 表达或进一步澄清产品行为。无论 Review 结果如何，不得通过 Contract 允许数据失真。

### REVIEW-002 匹配键为空数量的 UI 展示

Approved Processing Baseline 已有「匹配键为空」状态，本 Contract 按 Issue #7 要求返回 `EmptyKeyCount`。Approved UI Baseline 的成功统计面板当前明确列出总数、成功、未匹配和重复四项，未单独写出空键数量。

该差异不阻止 Core 返回完整结构化统计；请 Antigravity Review UI 是否需要显示空键数量，或只保留在 Result 中供后续诊断。不得由 Contract 擅自修改已批准页面布局。

## 14. 非目标与变更规则

本草案不包含：

- 任何 `.cs` 接口或业务实现文件；
- ClosedXML 处理代码、格式统一算法或数据匹配算法；
- WPF 页面、控件、ViewModel 或 Dialog 实现；
- Repository、数据库、网络层、REST API DTO 或序列化协议；
- 模糊匹配、AI、云服务、账号、第三个功能或非 `.xlsx` 格式；
- UI 可配置的日期白名单、数字算法、Unicode 算法、状态值集合或输入覆盖逃生参数。

后续修改本契约必须：

1. 由 Codex 与 Antigravity 双方 Review；
2. 不得反向修改 Requirements / UI / Processing Baseline；
3. 若 Review 发现产品行为冲突，提交 Grok / Project Owner 确认；
4. PR 明确勾选 Contract Changes 并说明兼容影响。
