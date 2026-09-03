# Handoff: UI / Core 契约 UI Review 结论

- Date: 2026-09-03
- From: Antigravity（UI Developer）
- To: Codex（Core Developer）
- Related Issue: GitHub Issue #7
- Related PR: `task/7-ui-core-contracts` 分支对应 PR #8

## Review 结论

- **UI Contract Review 结论**：通过（经纯契约人机工效微调后，完全满足 WPF ViewModel 消费需求）。
- Antigravity 确认：后续编写 WPF ViewModel 与交互时，可仅依赖 `docs/contracts.md`，无需引用 ClosedXML、无需猜测 Core 方法、无需在 UI 实现 Excel 规则、无需在 UI 解析结果、无需定义第二套 DTO。
- 三大调用边界（工作簿检查/预览、格式统一、数据匹配）及 6 大 UI 页面状态（Initial、File Loaded、Ready、Processing、Success、Error）契约表达完整，职责未发生泄漏。

## Contract 修改

在 `docs/contracts.md` 进行了以下纯 Contract 技术与工效微调（不改变任何产品行为）：
1. **状态更新**：文档状态更新为 `UI Reviewed - Pending Owner Decision`，记录 UI Review 已完成。
2. **1 起始行号对齐**：明确 `WorksheetSource.HeaderRowNumber` 与 `PreviewRow.WorksheetRowNumber` 均为 1 起始物理行号（与 Excel 及 UI 一致）。
3. **空集合约定（防 NRE）**：明确 `WorkbookInspectionResult.Worksheets` 与 `DataMatchingResult.ReturnedFields` 在 `Success = false` 时为空集合 `[]`（而非 `null`）。
4. **表格预览对齐保证**：明确 `PreviewColumn.Name` 在空单元格时提供占位列名（如「第 A 列」）；明确 `PreviewRow.Cells` 与 `PreviewTable.Columns` 在列数和顺序上完全对齐，空白单元格为 `DisplayValue = null`，便于直接绑定 DataGrid。
5. **匹配统计耗时对称性**：在 `DataMatchingSummary` 中补齐 `TimeSpan Elapsed` 属性，使格式统一与数据匹配在耗时呈现上保持对称。
6. **记录 Review 建议**：在第 13 节详细记录了 UI Developer 对 REVIEW-001 与 REVIEW-002 的分析与给 Project Owner 的明确建议。

## Project Owner 决策与落实

1. **REVIEW-001（采用方案 C）**：“保留前导 0 编号”与“保留长数字文本”在 UI 列表中默认勾选并置灰锁定（不可取消）；`FormatStandardizationOptions` 移除这两个布尔参数，Core 始终按 Processing Baseline 强制执行底线保护。
2. **REVIEW-002（采用方案 A）**：数据匹配 Success 统计固定展示【总计 / 匹配成功 / 未匹配 / 重复 / 匹配键为空】5 项，即使为空也显示 0 行，严禁合并进未匹配。

`docs/contracts.md` 与 `docs/ui-spec.md` 均已完成同步更新，契约状态进入 `UI Reviewed - Pending Core Final Review`。

## Codex 下一步需要复核什么

1. 复核 `docs/contracts.md` 中最新确定的请求与结果模型（`FormatStandardizationOptions` 移除 2 个非开关参数、`DataMatchingSummary.EmptyKeyCount` 与 `Elapsed`、集合非空保证、`PreviewRow.Cells` 保证对齐）在 Core 端实现是否存在障碍。
2. 进行 Core Final Review，确认契约无误后流转至 Grok 进行范围 Review 并推进 PR #8 合并。
