# Issue #56 UI 契约会签与修复交接 (Round 3)

- **角色**：Antigravity（UI Developer）
- **关联 Issue**：https://github.com/lzx0215/TabularStudio/issues/56
- **对应合同与审查**：`docs/handoffs/issue56-contract.md`、`docs/handoffs/issue56-ui-review.md`
- **实测状态（来自 Codex 实际反馈）**：
  - **整体验证构建**：PASS（0 warnings / 0 errors）。
  - **Core 测试**：324 PASS，0 FAIL / SKIP。
  - **App 测试（第 2 轮反馈）**：46 PASS，4 FAIL（均在 `ProcessingProfileViewModelTests`，属于测试装置中成功路径用例缺少 validator 注入及候选晚到响应验证非确定性）。
  - **当前修复状态（第 3 轮）**：已修正 4 项单测装置配置与晚到模拟，修剪 `ui-spec.md` 尾部空白；严格遵守无终端命令与无 Git 发布权限限制，测试执行委托 Codex 统一运行，未声称自测通过或桌面 GUI 通过，等待后续联调与桌面 QA。

---

## 1. 编译错误修复记录 (6 处错误全部修复)

1. **`SelectedMasterFilterValue` 属性访问错误（3 处）**：
   - 原因：`SelectedMasterFilterValue` 为 `ColumnValueOption` 包装对象，其内部实际值属性为 `.Value`。
   - 修复：在 `DataMatchingViewModel.cs` 中，将原 `SelectedMasterFilterValue.Kind/.RawValue/.HasTime` 修正为访问 `.Value.Kind`、`.Value.RawValue`、`.Value.HasTime`。
2. **`Application` 命名空间未明确限定（3 处）**：
   - 原因：对话框辅助方法直接引用 `Application.Current?.MainWindow`，在未完全导入 WPF 命名空间时产生歧义与编译失败。
   - 修复：在 `DataMatchingViewModel.cs` 与 `BatchFormatViewModel.cs` 中全部明确限定为 `System.Windows.Application.Current?.MainWindow`。

---

## 2. 审查反馈修复明细 (6 项集成问题全部落地)

### 2.1 问题 1：损坏配置错误展示与保存反馈
- **两页 `RefreshProfiles()`**：不再仅静默过滤合法配置，当 `_profileStore.List(kind).Errors` 包含损坏文件错误时，在配置状态区显式提示：`$"发现 {result.Errors.Count} 个损坏配置：{string.Join("；", result.Errors)}"`；合法配置依然正常加入下拉列表供选用。
- **首次保存成功反馈**：`SaveProfile` 执行成功后，明确提示 `$"已保存：{name}"`，不再误显为“已应用”。

### 2.2 问题 2：局部限高滚动与长名称对话框保护
- **两页配置状态提示区**：在 `BatchFormatView.xaml` 与 `DataMatchingView.xaml` 中，将 `ProfileStatusMessage` 均包裹在 `ScrollViewer` 中，设置 `MaxHeight="96"`、`VerticalScrollBarVisibility="Auto"`、`HorizontalScrollBarVisibility="Disabled"`，长错误信息局部纵向滚动，不撑开卡片布局。
- **`ConfirmProfileDialog.xaml` 模态确认框**：消息区域使用独立 `ScrollViewer`（`MaxHeight="240"`），保证无论配置名称或覆盖/删除提示多长，消息均局部滚动，底部“确认/取消”操作按钮始终固定停靠且清晰可达，不新增名称业务截断限制。

### 2.3 问题 3：匹配保存/应用前置有效性与变更通知
- **`CanApplyProfile` 强化**：除 `SelectedProfile != null` 外，必须满足双表路径非空、Sheet/表头有效、双表预览数据有效且**预览未处于加载中**（`!IsMasterPreviewLoading && !IsReferencePreviewLoading`）。
- **`CanSaveProfile` 强化**：
  - 必须满足双表路径、Sheet/Header、预览有效且预览未处于加载中。
  - 条件列表每一项的 `SelectedMasterColumn` 必须仍属于当前 `MasterAvailableColumns`，`SelectedReferenceColumn` 必须仍属于当前 `ReferenceAvailableColumns`。
  - 带回字段勾选项必须存在且全部属于当前 `ReferenceAvailableColumns`。
  - 主表筛选若启用，筛选列必须属于当前可用列、候选值未在加载中、筛选值非空且属于候选集合。
- **属性通知全面绑定**：
  - `_masterFilePath`、`_selectedMasterWorksheet`、`_masterHeaderRowNumber`、`_hasMasterPreviewData`、`_isMasterPreviewLoading` 添加 `[NotifyPropertyChangedFor(nameof(CanApplyProfile))]` 与 `[NotifyPropertyChangedFor(nameof(CanSaveProfile))]`。
  - 对照表对应源属性与预览加载属性同样绑定通知。
  - 条件添加、删除、选中项变化、带回字段勾选切换、筛选启用/选中项变化均即时触发 `OnPropertyChanged(nameof(CanSaveProfile))`。

### 2.4 问题 4：校验中操作禁用与异步过时核验保护
- **禁用全面生效**：
  - `_isProfileValidating` 添加对 `CanBrowseMaster` 与 `CanBrowseReference` 的属性变更通知。
  - `CanBrowseMaster` 与 `CanBrowseReference` 明确加入 `!IsProfileValidating` 判定。
  - 界面上工作表下拉、表头行号增减、同文件复选框、条件增删、带回字段选择、筛选与状态列配置均通过 `CanConfigure` / `CanUseSameFile` 在校验中全面禁用；开始匹配按钮通过 `CanStart` 禁用。
- **异步返回环境核验与代次保护**：
  - 引入 `_profileValidationGeneration`。
  - 在 `await _profileValidator.PrepareMatchingAsync(...)` 返回后，严格核对校验代次、预览代次、主表/对照表路径、工作表名、表头行号。
  - 若数据源在核验期间发生变更或启动了新校验，安全丢弃晚到结果，并退出校验状态，设置明确提示 `“未应用：数据源已发生变更。”`，绝不给页面留下“正在校验”死锁假状态。

### 2.5 问题 5：在途筛选候选加载取消与原子应用
- **取消并作废在途候选加载**：
  - `ApplyProfileAsync` 启动时，立即递增 `_masterFilterValuesGeneration`，并对 `_masterFilterValuesCancellation` 执行 `Cancel()` 与 `Dispose()`，彻底切断前置慢请求的晚到回调覆盖风险。
- **原子应用与状态抑制**：
  - 校验失败（结构错误或 `PrepareMatchingAsync` 失败）时直接退出，**完全保留页面现有条件、返回字段、筛选候选与选中值（All-or-Nothing）**。
  - 准备就绪后，在 `_isApplyingProfile = true` 与 `_suppressFilterColumnLoad = true` 保护下一次性原子写入所有条件、返回字段与 Core 预审好的筛选候选/选中值。
  - 在 `_isApplyingProfile` 期间抑制修改提示、候选重查以及 `UpdateReadyState` / `ResetSuccess` 的中间重算，全部属性设置完成后统一触发最终状态结算。

### 2.6 问题 6：空校验器防御与向下兼容
- **未初始化拦截**：若未注入 `_profileValidator`，`SaveProfile` 与 `ApplyProfileAsync` 立即阻断并分别提示：
  - `"未保存：配置校验服务未初始化。"`
  - `"未应用：配置校验服务未初始化。"`
- **旧测试兼容**：保持既有构造函数参数可选默认值（`= null`），不强制旧测试传递存储或校验器实例，旧单测构造不会意外创建真实本机磁盘存储。

---

## 3. 测试套件扩充与装置修正 (`ProcessingProfileViewModelTests.cs`)

在 `tests/TabularStudio.App.Tests/ProcessingProfileViewModelTests.cs` 中落实的自动化测试与测试装置修正：

1. `FormatProfile_Refresh_DisplaysErrorsWhenCorruptedFilesExist_WhileRetainingValidProfiles`：验证格式列表存在损坏文件时展示错误，合法项仍可选中。
2. `FormatProfile_NullValidator_RejectsSaveAndApply`：验证未注入校验器时阻止格式配置保存与应用。
3. `FormatProfile_OverwriteConflict_CancelRetainsOld_ConfirmOverwrites`：注入 `FakeProfileValidator`，成功路径覆盖确认正常工作。
4. `FormatProfile_Delete_RetainsCurrentPageSettings`：注入 `FakeProfileValidator`，成功路径删除保留当前页面规则。
5. `MatchingProfile_Refresh_DisplaysErrorsWhenCorruptedFilesExist_WhileRetainingValidProfiles`：验证匹配列表存在损坏文件时展示错误且不隐藏合法项。
6. `MatchingProfile_NullValidator_RejectsSaveAndApply`：保留显式空 validator 构造，验证未注入校验器时匹配保存与应用提示服务未初始化。
7. `MatchingProfile_Save_ValidatesScopeAndStoresNoPaths` 与 `MatchingProfile_Delete_PreservesPageSettings`：通过 `CreateMatchingVm` 默认提供 `FakeProfileValidator`，成功路径用例通过。
8. `MatchingProfile_CanSaveAndApply_BlockedWhilePreviewIsLoading`：验证双表预览加载中（`IsMasterPreviewLoading` / `IsReferencePreviewLoading`）时保存与应用按钮均禁用。
9. `MatchingProfile_CanSave_RequiresValidColumnSelectionAndFilterCandidate`：验证条件列不存在于可用列、返回字段全清空、筛选启用但无有效候选时，`CanSaveProfile` 准确返回 `false`。
10. `MatchingProfile_ValidationBusy_DisablesNavigationAndConfiguration`：通过挂起 `CustomPrepare` 异步任务，实测验证 `IsProfileValidating == true` 期间浏览、配置、开始、保存、应用、删除全部禁用，完成后恢复。
11. `MatchingProfile_Apply_StaleSourceChangeDiscardsResultWithoutHanging`：验证异步核验期间变更数据源路径后，过时结果被安全丢弃且不残留“正在校验”状态。
12. `MatchingProfile_Apply_CancelsInFlightFilterLoading`：引入 `DeferredInspectionService` 包装器，启动有意忽略取消的旧候选查询，应用新配置后完成旧请求并 await；断言晚到结果被安全废弃，新候选/选择保持不变。
13. 已有 `Save` 断言修正：更新已有测试中保存后的状态提示断言为 `"已保存：{name}"`。

---

## 4. 交付文件与工作区规范确认

- **工作区路径**：`D:\aiproject\TabularStudio\saved-processing-config`。
- **修改范围**：
  - `src/TabularStudio.App/Dialogs/ConfirmProfileDialog.xaml`
  - `src/TabularStudio.App/Views/BatchFormatView.xaml`
  - `src/TabularStudio.App/Views/DataMatchingView.xaml`
  - `src/TabularStudio.App/ViewModels/BatchFormatViewModel.cs`
  - `src/TabularStudio.App/ViewModels/DataMatchingViewModel.cs`
  - `tests/TabularStudio.App.Tests/ProcessingProfileViewModelTests.cs`
  - `docs/ui-spec.md`（已修剪文件尾部空白行，消除 `git diff --check` 警告）
  - `docs/handoffs/issue56-ui-handoff.md`
- **Core 目录**：未改动任何 `src/TabularStudio.Core/` 代码。
- **Git 操作**：未执行任何 Git 命令或修改仓库配置。
- **构建与测试状态明确**：构建与测试一律由 Codex 统一执行并报告；UI 开发者在此无终端命令权限，未自行执行测试，未声称自测全部通过或实际桌面 GUI 验收通过。代码与装置修复已全部就绪，等待 Codex 重新执行测试并推进桌面验收。

## Codex 集成验证回执

针对 Antigravity Round 3 的最终代码与测试装置，协调者 Codex 已实际执行整套测试：Core 324 项、App 50 项全部通过，0失败、0跳过（执行目录 `test-fourth.log` 与 `test-results/full-fourth*.trx`）。Debug 与 Release 完整构建均为0警告、0错误。前述4项测试设置失败已经修复，旧日志保留用于追溯。桌面操作由独立 Codex QA 子代理在已批准隔离环境执行，完整状态和限制统一以 `issue56-verification.md` 为准；此回执不代替 Antigravity 自测或 Owner Gate 2/3。
