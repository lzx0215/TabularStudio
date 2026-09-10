# Issue #56 配置服务契约与实现交接

- Issue：https://github.com/lzx0215/TabularStudio/issues/56；基线 9b608adea1fe6f1a79b14ec7a58dc476605fcaf7；PR 未创建。
- Owner 已于2026-09-09回复“批准”：需求/Scope/AC/FT、两行 UI 增量及本次合成样例 GUI 验收。正式范围在 requirements §15；批准包在 saved-processing-config.md；UI 在 saved-config-ui-approved.md。
- Codex 契约评审：同意下列服务边界。Antigravity 需先阅读并在独立 `issue56-ui-handoff.md` 写明同意或具体冲突；无冲突即继续 UI 实现，不等待 Owner。
- Antigravity 会签证据：实际 CLI 会话 `b1f6932e-b02d-40ee-b89d-df9ff4cf0fcf` 首轮最终响应明确表示“完全赞同 ProcessingProfileContracts.cs 及 issue56-contract.md 中定义的服务边界与数据模型”，并记录已在 UI Handoff 会签。原始响应保存在仓库外 `issue56-execution-20260909/ui-files-only/ui-stream.jsonl`。后续 UI 修复重写 Handoff 时未保留原会签段，协调者在此保留来源记录；不是代替 Antigravity 新签署。

## 服务边界

源文件 `src/TabularStudio.Core/Contracts/Profiles/ProcessingProfileContracts.cs` 为准确签名。新增 DTO 不替换原有处理服务/请求。

- ProcessingProfile(1, name, kind, format, matching)：两类配置互斥；FormatProfileSettings 六个布尔值全部显式传入；MatchingProfileSettings 保存 Conditions/ReturnFields/NormalizeComparisonKeys/ProfileStatusSettings/可选ProfileFilterSettings。筛选保存 Kind/RawValue/HasTime。
- `IProcessingProfileStore.List(kind)` 返回合法配置及错误列表；坏文件报错但不影响其他合法配置。列表中选择不自动应用。应用前必须 `Load(kind,name)` 重新读取，不使用旧列表缓存冒充成功。
- `Save(profile, overwrite:false)` 返回 NameConflict 时由 UI 明确确认后重试 overwrite:true；否则不覆盖。名称 trim+OrdinalIgnoreCase，具体文件名为程序生成的哈希。保存/删除失败明确返回 Error，不静默吞错。同步本机小 JSON 操作，用户选定名称不能变成文件路径。
- 默认实现 `TabularStudio.Core.Services.JsonProcessingProfileStore(string? directory = null)`：默认 `%LOCALAPPDATA%/TabularStudio/processing-profiles`，允许测试注入其他目录。服务由 Codex 实现；UI 保持可注入接口，不能自己实现 JSON 存储。
- `IProcessingProfileValidator.Validate(profile)` 做结构/版本/业务选项有效性检查。UI 仍须确保当前预览有效、字段选择是当前对象、筛选选择是当前候选，并阻止无效保存。
- 默认实现 `TabularStudio.Core.Services.ProcessingProfileValidator(IWorkbookInspectionService inspection)` 由 Codex 实现。
- `PrepareMatchingAsync(profile, masterSource, masterColumns, referenceColumns, token)` 先核对所有引用列的物理列号与原表头。只检查引用列，重复表头不按名称重映射。启用筛选时重读当前列全部候选，并按完整类型值查找。成功返回新 FilterValues 与 SelectedFilterValue；不改变 UI 状态，也不执行处理。不筛选时返回空列表和 null。
- 任何缺列/改名/空表头、筛选缺值、候选读取失败：Success=false，Errors 非空，不应用部分设置。取消抛 OperationCanceledException；UI 丢弃过时任务并恢复操作。

## UI 原子应用与职责

1. 应用前记录当前两个源文件/Sheet/Header/预览代次，锁定会冲突的输入和开始/配置按钮，并显示校验状态。
2. Load → Validate → PrepareMatchingAsync；失败只更新提示，原页面设置不动。异步返回再次检查代次及当前来源。
3. 成功后在 UI 抑制中途候选加载/状态重算的批次内一次性提交 Conditions、ReturnFields、开关、状态及新 FilterValues/SelectedFilterValue。现有 SelectedMasterFilterColumn setter 会触发异步清空，不得在提交时再次发起此加载；须通过受控抑制绕过，之后统一更新 Ready 状态。
4. 所有实际列选择使用当前 AvailableColumnItem 对象，物理列号定位已由服务校验；不要在 UI 写表格读写/匹配/候选推断算法。
5. 应用不改来源、输出路径、不启动业务服务。成功提示“已应用：名称”，其后手动修改显示“当前设置已修改”。删除保存项不清空已应用的页面设置。
6. 保持旧构造函数可用：新增可选注入参数放末尾，已有 App.Tests 不应意外访问真实配置路径。配置列表可在 UI Loaded/实际打开时加载，或在提供明确服务注入时加载；测试需注入临时目录/假服务。默认服务仅在真实 Composition Root 创建。
7. 测试构建配置隔离：如需 GUI 验收，仅在 DEBUG 编译下读取 `TABULARSTUDIO_TEST_DATA_DIRECTORY` 并把配置与输出目录偏好一起重定向到其子路径。Release 构建不读取该环境变量；不得将测试后门加入发布用户流程。

## 文件所有权与连续任务

- Codex：上述 Core Contracts（变更需双方协商）、Core/Services 两服务及 tests/TabularStudio.Tests/ProcessingProfileTests.cs、技术文档。
- Antigravity：仅 `src/TabularStudio.App/**`（WPF/VM/注入/命名框）、`tests/TabularStudio.App.Tests/*Profile*`、`docs/ui-spec.md`、`docs/handoffs/issue56-ui-handoff.md`。不改 Core、requirements、开发流程、依赖、Git 或其他任务文件。
- 已有未提交变更是本 Issue 的批准文档/契约工作，不是外来冲突。双方共享同一隔离工作树，写入范围不重叠；禁止 reset/restore/clean/commit/push/merge/创建PR/发布。
- 每方完成后给出实际文件、验证命令和未验证项。Contract 若有冲突先在 Handoff 明确，协调后再改，不让用户搬运消息。
