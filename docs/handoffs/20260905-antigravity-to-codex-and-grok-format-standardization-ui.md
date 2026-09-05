# Handoff: 格式统一页面实现与 Core 集成完成

- Date: 2026-09-05
- From: Antigravity（UI Developer）
- To: Grok（Product Manager）, Codex（Core Developer）
- Related Issue: GitHub Issue #15
- Related PR: (Feature branch `feature/15-format-standardization-ui`)

## 1. Done

按照已批准的 `docs/ui-spec.md` 与 `docs/contracts.md` 完成了格式统一页面的全部功能实现，并接入了现有的 `IWorkbookInspectionService` 与 `IFormatStandardizationService`，形成了 TabularStudio 第一个完整可运行的 MVP 功能闭环：

1. **App Shell 架构**：
   - 泛化 Windows 标准浅色主题（Standard Windows Light Theme）。
   - 推荐尺寸 1100×720，居中启动，支持最小化/最大化/还原/自适应伸缩。
   - 左侧导航栏提供「格式统一」与「数据匹配」两项入口。
   - 底部全局状态栏展示左侧就绪/处理提示文案，右侧常驻“离线环境已就绪”。
   - 采用简单 Composition Root (`App.xaml.cs`) 负责创建与传递 Core Service 实例，未引入任何第三方 DI 容器。

2. **格式统一页面核心链路**：
   - **数据源选择与文件加载并发一致性保证**：通过 Windows 原生 `OpenFileDialog` 限制仅选择 `.xlsx` 文件，支持文件拖拽载入；引入 `_fileLoadGeneration` 版本控制与 `++_previewGeneration` 级联失效，确保快速连续选择/拖入文件时，旧文件异步结果绝对不覆盖或污染最新文件的状态与 Preview；调用 `InspectAsync` 解析并填充工作表列表，默认选中首个 Sheet；支持表头所在行（>= 1）数字微调。
   - **数据预览与状态一致性保证**：调用 `GetPreviewAsync` 获取最多 20 行只读数据；动态生成 DataGrid 列，表头显示实际原始快照（空表头显示“第 A 列”等占位标签，不污染字段身份）；仅用于核对概貌，不限制实际处理行数。配置修改时立即失效旧 Preview 并退出 Ready 状态，防止以未验证状态执行；引入 `_previewGeneration` 版本控制，杜绝快速切换时的异步乱序覆盖。
   - **8 项标准化规则**：前 6 项提供可操作复选框（默认全部勾选）；后 2 项（保留前导 0 编号、保留长数字文本）默认勾选并置灰锁定（`IsEnabled=false`），明确标注为始终开启的数据安全保护；底部常驻说明“处理时公式保持不变；前导 0 与长数字保护始终启用；尽量保留现有业务格式，不重新套固定美化模板”。
   - **输出与执行控制**：自动推导默认保存路径为源文件同级目录下的 `源文件名_格式统一.xlsx`；支持 `SaveFileDialog` 更改保存路径；即时拦截输出路径覆盖源文件并红字警示；执行时支持 `OperationProgress` 阶段（Reading/Preparing/Processing/Writing/Completed）与百分比动态映射；Processing 状态下严格锁定所有输入控件，避免并发改动。
   - **输出文件已存在确认**：实现原生模态弹窗 `ExistingOutputDialog`，提供已批准的三选项「覆盖」「另存为...」「取消」；仅当用户明确选择覆盖时传递 `OverwriteExistingOutput = true`；若在「另存为...」文件对话框中取消，安全返回 Ready 状态，不调用 Core 且不重复弹窗。
   - **执行结果与后置操作**：成功完成后进入 Success 状态，展示耗时、实际处理行数与输出路径；激活「打开生成文件」（系统关联程序打开）、「打开所在文件夹」（资源管理器定位选中）与「作为主表送入数据匹配 ->」快捷按钮。
   - **错误映射**：严格将 Core 的 `OperationErrorCode`（如 `FileLocked`, `UnsupportedFileType`, `OutputConflictsWithInput` 等）映射为清晰的中文提示与指引，并将 `Detail` 作为补充信息呈现，不将底层技术异常直接甩给用户。

3. **数据匹配跨功能交接能力**：
   - 实现 `DataMatchingPlaceholderView` 与 `DataMatchingPlaceholderViewModel` 最小占位视图。
   - 点击格式统一成功面板的「作为主表送入数据匹配 ->」时，自动将结果文件路径传递给 App Shell，切换导航至「数据匹配」，并展示“已接收主表：...”占位提示，为后续数据匹配 Issue 预留了干净的接入点。

4. **架构与技术边界自检**：
   - UI / ViewModel 绝对零引用 ClosedXML（未出现 `ClosedXML`, `IXL`, `XLWorkbook` 等）。
   - ViewModel 构造函数仅依赖 `IWorkbookInspectionService` 与 `IFormatStandardizationService` 两个接口。
   - ViewModel 未编写任何 Excel 处理或清洗逻辑。

## 2. Not done

- 数据匹配业务页面与逻辑（根据 Issue #15 范围，明确不在此 Issue 中实现）。
- `IDataMatchingService` 调用与条件配置（留待后续 Issue）。

## 3. Documents updated

- 新增本交接文档：`docs/handoffs/20260905-antigravity-to-codex-and-grok-format-standardization-ui.md`。
- 产品基线文档未作任何修改（保持 100% 对齐）：
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
   - Tests：75 个现有 Core 测试持续全部通过。

2. **Smoke Test**：
   - 验证了包含 13 个关键场景的合成测试流程，覆盖初始状态、文件载入、表头微调、前 20 行限制、默认路径推导、覆盖源文件拦截、文件存在覆盖确认、真实执行生成新文件、前导 0 保护、错误文件拦截及跨功能送入数据匹配状态交接。

3. **边界检查**：
   - 在 `src/TabularStudio.App` 中搜索 `ClosedXML` / `IXL` / `XLWorkbook`，确认 0 匹配。

## 6. Next action for receiver

- **Grok（Product Manager）**：
  - 对 Issue #15 与 PR 进行范围 Review，确认实现未超出 MVP 边界。
- **Codex / Grok**：
  - 启动下一个 Issue（如数据匹配 Core 或数据匹配 UI 阶段）。
  - 在后续实现数据匹配 UI 时，可直接从 `MainWindowViewModel` / `DataMatchingPlaceholderViewModel.PendingMasterFilePath` 读取已传递的主表路径。

## 7. Risks / open questions

- 无。UI 基线与 Core 契约之间无冲突，所有设计均已闭环落地。
