# Handoff: 格式统一可选规则默认不启用 (Issue #30)

- Date: 2026-09-07
- From: Antigravity（UI Developer）
- To: Grok（Product Manager）
- Related Issue: GitHub Issue #30
- Related Branch: `feature/30-optional-rules-default-off`

## 1. 已完成内容 (Done)

根据 `docs/requirements.md` 第 5.2.1、13 节与 GitHub Issue #30 的范围与验收要求，完成了以下调整：

1. **格式统一可选规则默认不启用**：
   - 在 `FormatStandardizationViewModel.cs` 中，将 6 项可操作规则属性初始默认值全部调整为 `false`：
     - `TrimOuterWhitespace`（清理前后空格）: 默认 `false`
     - `RemoveTabsNewLinesAndHiddenCharacters`（清理 Tab、换行、隐藏字符）: 默认 `false`
     - `NormalizeFullWidthHalfWidth`（全角 / 半角统一）: 默认 `false`
     - `Unicode 标准化`（Unicode 标准化）: 默认 `false`
     - `NormalizeSafeNumbers`（普通数字安全统一）: 默认 `false`
     - `NormalizeUnambiguousDates`（明确日期安全统一）: 默认 `false`
   - 当用户不勾选「明确日期安全统一」时，原样向 Core 传递 `NormalizeUnambiguousDates = false`，Core 不对日期/时间单元格做类型转换或格式化；用户按需勾选后才传递 `true`。
   - 用户载入文件后，即便不勾选任何可选规则，依然处于 `Ready` 状态可直接开始处理，处理完成后日期/时间原样保留。

2. **底线安全保护与说明保持不变**：
   - 「保留前导 0 编号」「保留长数字文本」两项数据安全保护继续在 UI 中默认勾选并置灰锁定（`IsEnabled="False"`），用户不可关闭。
   - 规则选择区底部常驻提示信息保持不变：*“处理时公式保持不变；前导 0 与长数字保护始终启用；尽量保留现有业务格式，不重新套固定美化模板。”*

3. **文档同步**：
   - 同步更新 `docs/ui-spec.md`：
     - 3.1 格式统一页面布局线框图：可选规则复选框从 `[x]` 更新为 `[ ]`。
     - 3.2.2 标准化规则选择区基线：将「默认勾选」变更为「可选规则默认不启用，用户可按需勾选」；明确「明确日期安全统一」默认不处理日期/时间类型。
     - 9.1 验收清单项同步更新。
     - 文档更新日期更新为 2026-09-07。

## 2. 未完成内容 (Not Done / Out of Scope)

- 未修改 Core 处理算法（Core 已支持按选项关闭对应行为）。
- 未修改 `docs/processing-rules.md`、`docs/contracts.md`、`docs/architecture.md`、`docs/requirements.md`。
- 未改动数据匹配的比较标准化（需求 6.3 仅用于比较，源数据不改）。
- 未涉及格式统一批量（Issue #31）及输出目录记忆（Issue #29）。

## 3. 契约是否变化 (Contract Changes)

- **无契约变更**。
- 继续严格遵循 `TabularStudio.Core.Contracts` 中已批准的 `FormatStandardizationOptions` 六个布尔值契约。

## 4. 接收方下一步 (Next Steps)

- 请 Grok（Product Manager）对 PR 进行范围审查（Scope Review），确认改动严格限定在 Issue #30 范围内，未扩大或变更需求边界。

## 5. 验证方式 (How to Verify)

1. **编译检查**：
   ```pwsh
   dotnet build
   ```
   编译成功，0 警告，0 错误。

2. **单元测试回归**：
   ```pwsh
   dotnet test
   ```
   所有 203 个测试全部通过。

3. **端到端行为验证**：
   - 载入 `.xlsx` 文件后，前 6 项可选规则均为未勾选状态。
   - 不勾选任何规则直接执行格式统一，日期/时间格式单元格保持 Text 类型未做转换，前后空格与 Tab 未被误删。
   - 勾选「明确日期安全统一」后执行，日期单元格成功转换为标准 DateTime。
   - 前导 0 编号与长数字文本保护始终锁定开启。
