# UI / Core 契约

- Status: Approved Contract Baseline
- Owner: **Codex + Antigravity**
- Initial draft author: Codex（Core Developer）
- UI Reviewer: Antigravity（UI Developer）- Review completed (2026-09-03)
- Core Reviewer: Codex（Core Developer）- Final Review completed (2026-09-03)
- Reviewer for scope: Grok（Product Manager）
- Related: `docs/requirements.md`、`docs/ui-spec.md`、`docs/processing-rules.md`、`docs/architecture.md`、GitHub Issue #7

## 1. 文档目的与状态

本文定义 WPF UI / ViewModel 与本地 Core 之间的第一版正式调用契约，使双方能独立实现已批准的工作簿检查、格式统一和数据匹配能力。

本文只定义接口、请求、结果、错误和进度的数据形状，不包含 ClosedXML 业务实现、WPF 页面实现或处理算法。Antigravity 的 UI Contract Review 与 Codex 的 Core Final Review 均已完成，Project Owner 对 REVIEW-001 / REVIEW-002 的决定也已落实，本文现为 `Approved Contract Baseline`。

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
| 格式统一选项 | 展示 8 项规则（提交已批准的 6 项可操作开关，前导 0 / 长数字保护 UI 置灰锁定） | 表达 6 个布尔值 | 按 Processing Baseline 执行规则（强制执行前导 0 与长数字保护） |
| 格式统一安全底线 | 展示已批准说明与锁定项 | 不提供关闭安全底线的参数 | 始终保护公式、业务格式、前导 0、长数字和输入文件 |
| 匹配条件 | 配置 1～N 条字段映射 | 用稳定物理列身份表达条件列表，不表达 AND / OR 操作符 | 按物理列定位字段并固定执行 AND 精确匹配 |
| 返回字段 | 选择一个或多个对照表字段 | 用稳定物理列身份表达字段列表 | 按物理列取值，生成实际唯一输出列名并返回映射 |
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
- 契约中的所有 `IReadOnlyList<T>` 集合均不得为 `null`；没有元素时使用空集合 `[]`。
- Request 中要求至少一项的集合若为 `null` 或空集合，Core 返回 `InvalidConfiguration`；Result 中的集合按本节及具体结果约定返回空集合。
- Core 返回结构化数据；UI 决定中文文案、视觉状态和 Dialog 形式。
- 文件路径均为本机路径；Core 必须再次校验，不信任 UI 已做过的拦截。

各 Result 的不变量：

| Result | `Success = true` | `Success = false` |
| --- | --- | --- |
| `WorkbookInspectionResult` | `Worksheets` 非 `null`，`Error = null` | `Worksheets = []`，`Error` 存在 |
| `WorksheetPreviewResult` | `Preview` 存在，`Error = null` | `Preview = null`，`Error` 存在 |
| `FormatStandardizationResult` | `OutputFilePath` 非空、`Summary` 存在、`Error = null` | `OutputFilePath = null`、`Summary = null`、`Error` 存在 |
| `DataMatchingResult` | `OutputFilePath` 非空、`Summary` 存在、`ReturnedFields` 与请求顺序对应、`Error = null` | `OutputFilePath = null`、`Summary = null`、`ReturnedFields = []`、`ActualStatusColumnName = null`、`Error` 存在 |

### 4.2 工作表来源

```csharp
namespace TabularStudio.Core.Contracts;

public sealed record WorksheetSource(
    string FilePath,
    string? WorksheetName,
    int HeaderRowNumber);

public sealed record ColumnReference(
    int ColumnNumber,
    string? HeaderText);
```

`WorksheetSource` 同时用于格式统一、主表和对照表。`HeaderRowNumber` 对 `.xlsx/.xls` 为 1 起始的工作表物理行号；对 `.csv` 为 1 起始的解析记录号，引用字段内部换行不增加记录号。主表与对照表来自同一个工作簿时，两个 `FilePath` 直接相同；Contract 不接收「与主表使用同一个文件」CheckBox 状态，也不增加额外业务模式。

`ColumnReference` 是预览、匹配条件和返回字段共用的唯一列引用模型：

- `ColumnNumber` 是 1 起始的 Excel 物理列号，也是 UI 选中列与 Core 定位列的稳定身份；Core 不得仅凭 `HeaderText` 查找列。
- `HeaderText` 是预览时从所选表头行实际读取到的原始表头文本快照，仅用于展示和结果说明，不作为唯一身份；空表头明确返回 `null`，重复表头允许返回相同文本。
- UI 必须原样保留 `ColumnReference`；执行时 Core 仍按 `ColumnNumber` 重新读取对应物理列及实际表头，并以工作簿中的实际表头作为返回字段命名依据，不信任 UI 回传的 `HeaderText` 改写业务输出。
- 两个同名表头通过不同的 `ColumnNumber` 区分，不引入 GUID、数据库 ID 或 Schema Registry。
- Contract 能表示空表头列不等于新增「空表头可参与匹配或返回」产品行为；可选字段范围继续由 Approved UI Baseline 与 Core 配置校验约束。

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

public sealed record PreviewCell(
    int ColumnNumber,
    string? DisplayValue);

public sealed record PreviewRow(
    int WorksheetRowNumber,
    IReadOnlyList<PreviewCell> Cells);

public sealed record PreviewTable(
    string? WorksheetName,
    int HeaderRowNumber,
    IReadOnlyList<ColumnReference> Columns,
    IReadOnlyList<PreviewRow> Rows);

public sealed record WorksheetPreviewResult(
    bool Success,
    PreviewTable? Preview,
    OperationError? Error);
```

预览约定：

- `WorkbookInspectionResult`、`WorksheetPreviewResult` 遵守 4.1 节的成功 / 失败不变量。
- `HeaderRowNumber`、`ColumnReference.ColumnNumber`、`PreviewCell.ColumnNumber` 与 `WorksheetRowNumber` 均为大于等于 1 的行列号。Excel 使用物理行列；CSV 使用解析记录号与字段序号，字段内换行不增加记录号。
- `Columns` 来自指定表头行，供 UI 显示列头、配置匹配条件和选择返回字段；即使表头文本重复或为空，每一列仍由 `ColumnNumber` 唯一定位。
- Core 返回实际 `HeaderText`，空表头为 `null`，不生成「第 A 列」等占位文案。UI 可按 Approved UI Baseline 的实现建议选择占位显示文本，但必须保留并回传原 `ColumnReference`，不得把占位文案当作物理列身份或原始表头名。
- `Rows` 从表头之后的数据行开始，最多 20 行；预览上限不是请求参数，UI 不能通过 Contract 扩大。
- `Columns`、`Rows` 与 `PreviewRow.Cells` 始终为非 `null` 集合；成功预览没有数据行时 `Rows = []`。
- `PreviewRow.Cells` 与 `PreviewTable.Columns` 在列数和顺序上完全对齐，且对应位置的 `ColumnNumber` 相同。若某单元格在 Excel 中为空白，`DisplayValue` 为 `null`，便于 UI 直接绑定 DataGrid 或转为 `DataTable`，无需在 UI 层做稀疏列索引对齐。
- `WorksheetRowNumber` 保留 Excel 原行号；CSV 为 1-based 解析记录号。
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
}

public sealed record FormatStandardizationRequest(
    WorksheetSource Source,
    string OutputFilePath,
    FormatStandardizationOptions Options,
    bool OverwriteExistingOutput);
```

请求约定：

- `Source` 表达输入文件、选定 Sheet 和表头行。
- `Options` 只表达 Approved UI Baseline 的 6 项用户可操作开关，不允许 UI 传入具体 Unicode、数字或日期算法。
- 前导 0 编号保护与长数字文本保护属于不可关闭的底线安全保护（UI 列表默认勾选且置灰锁定，用户不可取消），不作为请求布尔参数暴露；Core 始终按 Approved Processing Baseline 强制执行该两项保护。
- `OverwriteExistingOutput` 只表达本次调用是否已获得用户对既有结果文件的明确覆盖意图。
- Contract 不提供 `PreserveFormula`、`PreserveStyle`、`AllowOverwriteInput`；公式、业务格式和输入保护是 Core 必须始终执行的安全底线。
- 表头及之前的行不处理，处理范围由 Processing Baseline 决定，不作为 UI 参数。

### 6.3 结果

```csharp
public sealed record FormatStandardizationSummary(
    string? ProcessedWorksheetName,
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
- `Summary.ProcessedWorksheetName` 是实际处理的 Sheet；CSV 为 null。
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
    ColumnReference MasterColumn,
    ColumnReference ReferenceColumn);

public sealed record MatchingStatusColumnOptions
{
    public bool Enabled { get; init; } = true;
    public string ColumnName { get; init; } = "匹配状态";
}

public sealed record DataMatchingRequest(
    WorksheetSource Master,
    WorksheetSource Reference,
    IReadOnlyList<MatchingCondition> Conditions,
    IReadOnlyList<ColumnReference> ReturnFields,
    bool NormalizeComparisonKeys,
    MatchingStatusColumnOptions StatusColumn,
    string OutputFilePath,
    bool OverwriteExistingOutput)
{
    public MasterRowFilter? MasterFilter { get; init; }
}

public sealed record MasterRowFilter(ColumnReference Column, string EqualsValue);
```

请求约定：

- #45：`MasterFilter = null`（默认）表示不筛选；非空时指定主表物理列与非空文本值。仅等于值的行进入联合匹配，其余保留并标记未参与匹配。筛选列也校验有效表头、物理列与公式；筛选比较沿用 `NormalizeComparisonKeys`。

- `Conditions` 至少 1 条，全部固定为 AND；每一侧都通过 `ColumnReference.ColumnNumber` 定位用户实际选择的物理列。Contract 不提供 AND / OR、模糊、包含或相似度操作符。
- `ReturnFields` 至少 1 个，按用户选择顺序使用 `ColumnReference` 表达对照表物理列；UI 不计算最终输出列名。
- `NormalizeComparisonKeys` 只控制是否启用 Approved Processing Baseline 的整组比较标准化；不开放前导 0、日期白名单、Unicode、大小写、相似度等算法参数。
- `StatusColumn.Enabled` 默认 `true`，`ColumnName` 默认「匹配状态」；状态值集合不是请求参数。
- 主表与对照表同文件不同 Sheet 时，只需让 `Master.FilePath == Reference.FilePath`。
- Core 必须校验所有 `ColumnNumber` 均为大于等于 1 且存在于对应工作表的物理列、配置完整、匹配键与返回字段不包含公式数据；字段定位不得退化为按 `HeaderText` 搜索。

### 7.3 返回字段与状态列结果

```csharp
public sealed record ReturnedFieldMapping(
    ColumnReference RequestedColumn,
    string ActualOutputColumnName);

public sealed record DataMatchingSummary(
    int TotalMasterDataRowCount,
    int MatchedCount,
    int UnmatchedCount,
    int DuplicateCount,
    int EmptyKeyCount,
    TimeSpan Elapsed)
{
    public int SkippedCount { get; init; }
}

public sealed record DataMatchingResult(
    bool Success,
    string? OutputFilePath,
    DataMatchingSummary? Summary,
    IReadOnlyList<ReturnedFieldMapping> ReturnedFields,
    string? ActualStatusColumnName,
    OperationError? Error);
```

成功结果约定：

- `ReturnedFields` 按请求字段顺序返回 `RequestedColumn` → `ActualOutputColumnName` 映射；`RequestedColumn.ColumnNumber` 标识请求的对照表物理列，`RequestedColumn.HeaderText` 返回 Core 执行时重新读取并确认的原始表头文本，实际列名由 Core 按 Processing Baseline 确定性生成。
- 成功时 `ReturnedFields.Count == request.ReturnFields.Count`，每一项按相同索引对应；失败时保证为空集合 `[]`。
- 状态列启用时，`ActualStatusColumnName` 返回 Core 生成的最终唯一列名；关闭时或处理失败时为 `null`。
- `Summary` 返回主表总数据行、匹配成功、未匹配、重复、匹配键为空数量及耗时 `Elapsed`；UI 固定展示这 5 项指标（即使 `EmptyKeyCount = 0` 也正常显示 `0 行`），严禁将“匹配键为空”合并进“未匹配”；`Success = false` 时为 `null`。
- #45 增加 `SkippedCount`（默认 0），UI 在原五项指标之外显示「未参与匹配」数量。五种行级结果数量之和必须等于 `TotalMasterDataRowCount`，不得把跳过行并入其它计数。
- 状态文本固定为「匹配成功」「未匹配」「重复」「匹配键为空」「未参与匹配」，不由 UI 自定义。
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
| `UnsupportedFileType` | 输入不属于已确认格式，或输出格式与输入/主表不一致 | 提示支持 `.xlsx/.xls/.csv` 且按原格式写回 |
| `FileLocked` | 文件被其它程序独占 | 提示关闭占用后重试 |
| `WorkbookUnreadable` | 工作簿损坏或无法安全读取 | 展示错误并允许重新选文件 |
| `WorksheetNotFound` | 指定 Sheet 不存在 | 刷新选择并重新预览 |
| `InvalidHeaderRow` | 表头行小于 1、越界或无法作为已选 Sheet 表头 | 修正表头行 |
| `ColumnNotFound` | 请求的 1 起始物理列号在对应工作表中不存在 | 刷新预览与字段配置 |
| `InvalidConfiguration` | 条件为空、返回字段为空或其它契约必填项无效 | 修正配置，不开始处理 |
| `OutputConflictsWithInput` | 输出与任一输入文件相同 | 强制更换输出路径 |
| `OutputAlreadyExists` | 输出已存在且未授权覆盖 | 询问覆盖 / 另存为 / 取消 |
| `OutputDirectoryNotWritable` | 输出目录不可写 | 更换目录或处理权限 |
| `FormulaCellNotAllowedForMatching` | 匹配键或返回字段实际含公式单元格 | 提示改选字段或在外部准备数据 |
| `IncompleteOutputCleanupFailed` | 任务失败且不完整输出删除失败 | 明确警告该路径不是可用结果 |
| `ProcessingFailed` | 其它处理失败 | 展示 Message，可附 Detail 供排查 |

## 12. 契约不变量与自检

### 12.1 格式统一

- UI 可以通过检查、预览、6 项可操作开关（及 2 项置灰锁定保护说明）、输出路径、进度和结果完成 Approved UI Baseline 的交互。
- 公式保持、业务格式保护和输入文件保护不可关闭。
- 文本、数字和日期算法不暴露为 UI 可定制策略。

### 12.2 数据匹配

- UI 可以独立选择主表 / 对照表的文件、Sheet、表头行并预览。
- Request 可以通过无歧义的 `ColumnReference` 表达 1～N 条固定 AND 条件、一个或多个返回字段，以及比较标准化、状态列和输出路径。
- Result 可以表达全部行级统计、请求物理列到实际返回列名的映射和实际状态列名。
- 公式检测、重复判断、状态判定、唯一列名和输入保护由 Core 完成。
- 文本 / 数字安全等价、前导 0、长数字、日期、空键、重复键、公式字段、表头之前不处理等规则均由 Core 根据 `WorksheetSource`、稳定列引用和 Processing Baseline 执行，不泄漏为 UI 算法参数。

### 12.3 依赖与范围

- UI 不需要引用 ClosedXML；Core 不需要引用 WPF。
- 没有 Repository、数据库、网络 API、MediatR、CQRS、Event Bus、Plugin、Domain Event 或序列化协议。
- 没有用户取消按钮、模糊匹配、公式计算、未确认格式文件或第三个产品功能。

## 13. Project Owner 决策记录 (Decision Log)

### REVIEW-001 两个保护开关关闭时的语义（已决：采用方案 C）

- **Project Owner 决策**：采用方案 C。“保留前导 0 编号”与“保留长数字文本”继续保留在格式统一的 8 项规则列表中，但在 UI 上默认勾选并置灰锁定（用户不可取消），明确标识为始终开启的数据安全保护。这两项不是可关闭的处理策略。
- **契约落实**：`FormatStandardizationOptions` 移除 `PreserveLeadingZeroIdentifiers` 与 `PreserveLongNumericText` 两个布尔字段，不再向 UI 暴露关闭参数。Core 始终按照 Approved Processing Baseline 强制执行这两项保护；其余 6 项格式统一规则仍然作为用户可操作开关。

### REVIEW-002 匹配键为空数量的 UI 展示（已决：采用方案 A）

- **Project Owner 决策**：采用方案 A。数据匹配 Success 统计固定展示【总计 / 匹配成功 / 未匹配 / 重复 / 匹配键为空】5 项指标，即使 `EmptyKeyCount = 0` 也正常显示 `0 行`。不得把“匹配键为空”合并进“未匹配”。
- **契约落实**：`DataMatchingSummary` 保留 `EmptyKeyCount`，UI 成功统计卡片与契约的 5 项指标完全对应；Core 与 UI 均严禁将“匹配键为空”合并进“未匹配”。

## 14. Core Final Review 结论

- **Review 结论**：通过。Antigravity 的 UI Contract Review 与 Codex 的 Core Final Review 均已完成。
- **字段身份修正**：预览、匹配条件与返回字段统一使用 `ColumnReference`；1 起始 `ColumnNumber` 是物理列身份，`HeaderText` 仅保存原始表头上下文。
- **空表头职责**：Core 返回 `HeaderText = null`，UI 可自行显示实现建议中的占位文案；Contract 不把占位文案升级为业务字段名。
- **Owner 决策闭环**：REVIEW-001 / REVIEW-002 已落实，无待 Project Owner 确认的产品规则。
- **技术一致性**：三个 Service 边界、集合空值语义、Result 不变量、async / `CancellationToken`、Progress、输入保护和输出列命名责任均可直接实现，未发现其它阻断问题。

## 15. 非目标与变更规则

本文不包含：

- 任何 `.cs` 接口或业务实现文件；
- ClosedXML 处理代码、格式统一算法或数据匹配算法；
- WPF 页面、控件、ViewModel 或 Dialog 实现；
- Repository、数据库、网络层、REST API DTO 或序列化协议；
- 模糊匹配、AI、云服务、账号、第三个功能或`.xlsx/.xls/.csv` 以外格式；
- UI 可配置的日期白名单、数字算法、Unicode 算法、状态值集合或输入覆盖逃生参数。

后续修改本契约必须：

1. 由 Codex 与 Antigravity 双方 Review；
2. 不得反向修改 Requirements / UI / Processing Baseline；
3. 若 Review 发现产品行为冲突，提交 Grok / Project Owner 确认；
4. PR 明确勾选 Contract Changes 并说明兼容影响。

## Issue #34 多格式契约增量（2026-09-08）

- WorksheetSource.WorksheetName 对 CSV 可为 null/空；非空值也忽略，不产生虚拟 Sheet。xlsx/xls 仍要求有效的工作表名。
- CSV InspectAsync 成功返回 Worksheets=[]；不是读取失败。CSV PreviewTable.WorksheetName 和 FormatStandardizationSummary.ProcessedWorksheetName 返回 null。
- CSV HeaderRowNumber / WorksheetRowNumber 是解析记录号，字段内换行不增加记录号。物理列身份、空/重复表头规则不变。
- 同文件不同 Sheet 只适用于 xlsx/xls；同一个 CSV 同时作为两侧输入返回 InvalidConfiguration。
- 不增加 OperationErrorCode，不改变接口方法和参数位置，不将任何库类型暴露到 Contract。
- 实际读写边界见 processing-rules 第 11 节；不以库默认行为代替规则。
- 已在“Antigravity UI 开发”协作任务取得 UI-side design review APPROVE；该任务声明不代表独立 Antigravity 身份，实际 diff review 证据随 PR 归档，不冒称独立身份会签。

## Issue #31 UI 多格式调用确认

两页对 CSV 传 WorksheetSource.WorksheetName=null；不要求或显示虚构 Sheet。Excel 从 inspection.Worksheets 选实际 Sheet。表头行与预览沿用原契约；输出跟输入/主表扩展名。CSV 不允许同文件不同 Sheet 模式。由 Codex 按 Owner 本轮跨角色授权完成 UI/Core 调用自检。

## 格式统一批量（Issue #32）

- IBatchFormatStandardizationService.ExecuteAsync(BatchFormatRequest, IProgress<BatchFormatProgress>?, CancellationToken) 返回 BatchFormatResult。
- BatchFormatRequest(Items, Options)：Items 为 BatchFormatItem 列表，每项 Source、OutputFilePath、OverwriteExistingOutput；整个请求共用一套 FormatStandardizationOptions。调用方按记忆目录与原文件名_格式统一+原扩展名生成路径。
- BatchFormatResult.Items 按输入顺序返回 BatchFormatItemResult(Index, Item, Result)；Result 为原单文件结果，SucceededCount/FailedCount 汇总。BatchFormatProgress 为 CompletedCount/TotalCount。
- Core 快照列表，逐文件调用单文件服务，单文件失败继续且不回滚已提交结果。取消抛 OperationCanceledException，保留此前提交结果。
- 任一输出与整批任一输入同路径时该项 OutputConflictsWithInput；批内重复输出的全部冲突项 InvalidConfiguration。Windows 路径按绝对路径、忽略大小写比较。已有输出仍逐项需要覆盖许可，保护检查优先于许可。
- 不扩展数据匹配批量，不增加网络、格式转换或合并结果。UI 只收集参数/确认覆盖/显示结果，不实现处理算法。

## Issue #33 批量 UI 调用确认

BatchFormatViewModel 收集每文件 Source/OutputFilePath/OverwriteExistingOutput，构造一套共享 Options 后调用 IBatchFormatStandardizationService。按结果 Index 对应原列表显示结果；完成比例使用 CompletedCount/TotalCount。CSV 无 Sheet；失败继续由 Core 实现。UI 不能把覆盖某项的确认当作整批许可。取消覆盖保留该文件，使用未覆盖许可的请求返回逐项失败。由 Codex 按本轮授权完成 UI/Core 自检。


## Issue #54 Owner 确认增量：主表实际值候选（2026-09-08）

本节是 Owner 在当前任务审阅 `artifacts/filter-feedback/column-value-proposal.md` 后明确“确认”的最小 Core/Contract 扩展，覆盖此前 #54 纯视觉返工中对本增量的禁止。由 Codex 按 Owner 跨角色授权完成 UI/Core 调用自检；不冒称独立人员会签。

- `IWorkbookInspectionService.GetColumnValuesAsync(ColumnValuesRequest, CancellationToken)`：只读完整列，返回 `ColumnValuesResult(Success, Values, Error)`。请求使用 `WorksheetSource` 和物理 `ColumnReference`；CSV 无 Sheet。失败返回空 Values；取消抛 OperationCanceledException。接口默认实现返回 InvalidConfiguration，不支持新入口的旧实现不会伪造候选。
- `ColumnValueOption(DisplayText, Value)`：DisplayText 仅用于展示，匹配必须传 Value；`ColumnFilterValue(Kind, RawValue, HasTime=false)` 不暴露 ClosedXML/NPOI 类型。
- Kind：Text / Number / Boolean / DateTime / TimeSpan / Error。RawValue 分别为原文本、Invariant double round-trip、布尔文本、Invariant DateTime O、TimeSpan c、受支持的 Excel 错误枚举名称；HasTime 仅影响日期规范化的比较粒度，按源格式沿用现有规则。
- `MasterRowFilter.SelectedValue` 为可选属性。非空时优先使用带类型值；空时继续使用现有 EqualsValue 文本路径，位置参数不变。无效带类型值返回 InvalidConfiguration。
- 列候选按表头之后全部数据的首次出现顺序、原值类型/值/日期粒度去重，不根据显示文字去重，不预先执行比较规范化。显示冲突追加类型和原值说明；空白不作为候选，所选列任何数据公式导致整体失败并附真实 File/Sheet/Header/Cell。
- 执行时仍重新读取输入，遵守既有比较规范化、公式拒绝、输出保护和五类统计；候选快照不作为输入文件锁，不保证文件被外部修改后的候选仍存在。
