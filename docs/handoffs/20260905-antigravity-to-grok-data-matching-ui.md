# Handoff: 数据匹配页面实现与 Core 集成完成

- Date: 2026-09-05
- From: Antigravity（UI Developer）
- To: Grok（Product Manager）, Codex（Core Developer）
- Related Issue: GitHub Issue #21
- Related PR: (Feature branch `feature/21-data-matching-ui-integration`)

## 1. Done

按照已批准的 `docs/ui-spec.md` 与 `docs/contracts.md`，完成了「数据匹配」WPF 页面的全部功能实现，并接入了现有的 `IWorkbookInspectionService` 与 `IDataMatchingService`，形成了 TabularStudio 第二个完整可运行的 MVP 功能闭环：

1. **App Composition Root 与 Shell 架构**：
   - 在 `App.xaml.cs` 中实例化 `IDataMatchingService matchingService = new DataMatchingService()` 并注入 `MainWindowViewModel`。
   - 正式替换原先的 `DataMatchingPlaceholderView` 与 `DataMatchingPlaceholderViewModel` 为真实的 `DataMatchingView` 与 `DataMatchingViewModel`。
   - 维持泛化 Windows 标准浅色主题（Standard Windows Light Theme）与 8px 网格规范。

2. **跨功能交接能力（格式统一 → 数据匹配）**：
   - 完整保留并深化了 `PendingMasterFilePath` / `ReceiveMasterFilePath(filePath)` 语义。
   - 当用户在格式统一页面点击「作为主表送入数据匹配 ->」时，自动切换至数据匹配导航，填入主表路径，自动触发工作簿检查、解析 Sheet 并默认选定首个工作表，读取前 20 行只读预览；**严格禁止自动开始匹配**，等待用户继续配置对照表与匹配条件。

3. **双表输入选择与“与主表使用同一个文件”模式**：
   - 主表与对照表均支持独立选择 `.xlsx` 文件、拖拽载入、Sheet 选择与表头行微调（>= 1）。
   - 主表与对照表预览均读取 Core 返回的前 20 行真实只读快照，空表头列在标题显示占位提示（如“第 A 列”），但不修改原始 `HeaderText`。
   - 提供「与主表使用同一个文件」复选框：
     - 勾选时：对照表路径自动同步为主表路径并锁定置灰，对照表浏览按钮禁用；对照表 Sheet 独立选择并独立预览。
     - 取消勾选时：对照表路径清空并恢复独立浏览状态，旧 Reference 预览立即失效。

4. **字段物理身份与重复表头支持**：
   - 匹配条件与返回字段统一采用 `ColumnReference`，以物理 `ColumnNumber` 作为稳定唯一身份。
   - 严格过滤空表头列（仅 `HeaderText != null` 的列进入候选）。
   - 同名重复表头（如第 4 列“科室”与第 5 列“科室”）在 UI 下拉与勾选列表中显示清晰列号标记（如 `科室 (D列)` 与 `科室 (E列)`），保证用户无歧义区分，不修改写回 Contract 的物理列号。

5. **动态匹配条件配置 (1～N 条)**：
   - 支持动态添加与删除匹配条件，每行配置 `主表字段 = 对照表字段`，各条件之间显式呈现 `(且 / AND)` 固定标识。
   - 默认初始化 1 条条件；仅剩 1 条条件时删除按钮禁用（防止删至 0 条）。
   - 当主表或对照表重新载入/切换 Sheet 时，自动重验已选字段，失效列安全置空。

6. **对照表返回字段选择与搜索过滤**：
   - 完整列出对照表可用字段（排除空表头）。
   - 提供即时搜索框（仅过滤列表视觉呈现，不改变物理身份与勾选状态）。
   - 提供「全选」与「反选」快捷文本按钮，以及 `已选 M/N 项` 指示。
   - 至少勾选 1 个带回字段才允许进入 Ready；重名字段常驻提示将由 Core 自动以 `_匹配` 命名处理。

7. **匹配选项与输出路径**：
   - `NormalizeComparisonKeys`：单一复选框“自动修复常见匹配差异 (空格/全半角/文本数字/日期统一，仅用于比较，不改源数据)”，默认勾选。
   - `StatusColumn`：默认勾选，默认列名“匹配状态”，支持就地重命名与禁用。
   - 输出路径默认自动推导为 `<主表文件名>_匹配结果.xlsx`；支持 `SaveFileDialog` 更改保存路径。
   - 即时冲突拦截：当输出路径与主表或对照表路径相同时，显示红字警告并禁用开始按钮。

8. **输出已存在确认交互（复用 ExistingOutputDialog）**：
   - 复用既有原生模态对话框 `ExistingOutputDialog`，提供「覆盖」「另存为...」「取消」三项选择。
   - 仅在用户明确选择覆盖时以 `OverwriteExistingOutput = true` 重新调用；若在另存为对话框中取消，安全返回 Ready 状态，不调用 Core 且不重复弹窗。

9. **异步版本控制 (Generations)**：
   - Master 拥有独立的 `_masterFileLoadGeneration` 与 `_masterPreviewGeneration`。
   - Reference 拥有独立的 `_referenceFileLoadGeneration` 与 `_referencePreviewGeneration`。
   - 在发起 `InspectAsync` 前立即提交新文件状态并清理旧文件 UI 状态，旧异步结果晚返回时直接安全丢弃。
   - 在 Same File 模式下快速切换 Master 时，Reference 最终稳定跟随最新 Master 文件。
   - 快速切换 Sheet / 表头行时，新 Preview 未返回前立即失效旧 Preview 并退出 Ready，杜绝乱序覆盖。

10. **执行控制与结果五项统计**：
    - Processing 状态下冻结所有配置控件（浏览、Sheet、表头、条件、返回字段、选项、输出与开始按钮），通过 `OperationProgress` 展示 5 个标准阶段文案与百分比。
    - 成功后进入 Success 状态，固定完整展示 5 项统计指标：【总计 / 匹配成功 / 未匹配 / 重复 / 匹配键为空】（即使为 0 亦正常显示 0 行，严禁将“匹配键为空”合并进“未匹配”），展示耗时与输出路径。
    - 提供「打开生成文件」与「打开所在文件夹」快捷按钮。

11. **错误映射与容错**：
    - 严格将 Core 的 `OperationErrorCode`（如 `FormulaCellNotAllowedForMatching`, `OutputConflictsWithInput`, `FileLocked` 等）映射为清晰的中文提示与指引。
    - 公式错误明确提示改用普通值列后重试，不将底层技术异常直接甩给用户。

## 2. Not done

- 本 Issue 仅专注 Issue #21 范围内的数据匹配 UI 实现与 Core 集成。
- 不修改 Core 任何业务逻辑。
- 不引入网络、数据库、第三方 DI、在线服务或非 `.xlsx` 格式。

## 3. Documents updated

- 新增本交接文档：`docs/handoffs/20260905-antigravity-to-grok-data-matching-ui.md`。
- 产品基线与契约文档保持 100% 对齐（未修改）：
  - `docs/requirements.md`（无修改）
  - `docs/ui-spec.md`（无修改）
  - `docs/contracts.md`（无修改）
  - `docs/processing-rules.md`（无修改）

## 4. Contract changes

- **无**（0 契约改动，严格消费现有的 `TabularStudio.Core.Contracts`）。

## 5. How to verify

1. **编译与自动化测试**：
   ```pwsh
   dotnet build TabularStudio.sln
   dotnet test TabularStudio.sln --no-build
   ```
   - Build：0 警告，0 错误。
   - Tests：203 个现有 Core 测试持续全部通过。

2. **Synthetic Smoke Test**：
   - 运行针对 Issue #21 与 #15 的全量 Smoke Test，覆盖全部 49 项业务与边界场景（含 Execute Error 恢复、隔离与状态一致性验证）：
     - App 启动与 Composition Root
     - 数据匹配页面导航
     - 格式统一成功后送入主表交接（不自动开始匹配）
     - 主表 / 对照表独立选择与预览（前 20 行只读）
     - 同文件双工作簿模式切换
     - 空表头过滤与重复表头通过物理 ColumnNumber 区分
     - 动态匹配条件 1～N 条及删除保护
     - 返回字段搜索过滤、多选与未选拦截
     - 比较标准化与状态列配置
     - 默认输出路径推导与冲突即时拦截
     - 目标文件已存在确认（覆盖 / 另存为 / 取消）
     - Processing 期间配置全面锁定（条件选择、带回字段多选、状态列配置、浏览按钮等）
     - 真实执行数据匹配生成结果文件
     - 五项统计指标（总计、匹配成功、未匹配、重复、匹配键为空）核实
     - 公式单元格 Core 拦截与友好错误映射
     - Master / Reference / Sheet 异步 generation 防乱序覆盖保护
     - 同文件模式切换竞争与异步取消保护（取消勾选时不被延迟 Inspect 覆写）
     - 独立错误来源跟踪（Master / Reference 独立错误与独立恢复，互不误清）
     - 成功状态下另存为立即退出 Success 状态并重置指标
     - 另存为选择输入文件冲突时立即拦截并阻断 Core 执行
     - 切换不同 Master 文件重置自定义输出路径标记并重新推导
     - 匹配条件行间 AND 标识首项隐藏/次项及后续项展示
     - Existing Output → SaveAs 合法新路径自动由 UI 触发再次执行 Core 生成结果（无需用户二次点击）
     - Existing Output → SaveAs 选择 Master 或 Reference 路径被拦截阻断 Core 执行且 State 不残留 Processing
     - Existing Output → SaveAs 对话框取消后安全恢复 Ready 状态
     - Formula Matching Key Error 恢复测试（修改匹配条件为普通值列后清除 Execute Error 并重新执行成功）
     - Formula Return Field Error 恢复测试（改选普通值返回字段后清除 Execute Error 并重新执行成功）
     - Preview Error 隔离保护（修改无关 Execute 配置绝不误清 Master 或 Reference 真实 Preview Error）
     - Execute Error 未改配置保持测试（不修改任何请求配置时 Execute Error 稳固保持不自行消失）
     - 原 Master / Reference 输入文件完整未被篡改

3. **架构边界自检**：
   - 在 `src/TabularStudio.App` 中搜索 `ClosedXML` / `IXL` / `XLWorkbook`，结果恒为 0。

## 6. Next action for receiver

- **Grok（Product Manager）**：
  - 对 Issue #21 与对应 PR 进行范围 Review，确认实现严格符合 MVP 边界且与已批准基线一致。
