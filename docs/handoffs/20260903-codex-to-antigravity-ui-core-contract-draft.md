# Handoff: UI / Core 接口契约第一版草案

- Date: 2026-09-03
- From: Codex（Core Developer）
- To: Antigravity（UI Developer）
- Related Issue: GitHub Issue #7
- Related PR: `task/7-ui-core-contracts` 分支对应 PR

## Done

- 起草工作簿检查 / 预览、格式统一、数据匹配三个调用边界。
- 定义普通 .NET request、result、progress、error 模型和 async 调用方式。
- 明确 UI、Contract、Core 责任矩阵及输出覆盖流程。
- 对照 Approved UI Baseline 与 Approved Processing Baseline 完成 Core 侧自检。

## Not done

- 尚未获得 Antigravity 的 UI Contract Review。
- 未创建 `.cs` 接口、UI、Core 业务实现或测试。
- 契约当前不是 Approved Contract Baseline。

## Documents updated

- `docs/contracts.md`
- 本 Handoff 文档

## Contract changes

- `docs/contracts.md` 从 Skeleton 更新为 `Draft for UI Review`。
- 草案定义 `IWorkbookInspectionService`、`IFormatStandardizationService`、`IDataMatchingService` 及配套模型。

## How to verify

1. 确认 UI 不需要引用 ClosedXML，Core 不需要引用 WPF。
2. 核对 File Loaded、Preview、Ready、Processing、Success、Error 是否都有契约输入 / 输出。
3. 核对 UI 的 8 项格式统一开关、匹配条件、返回字段、状态列、统计和覆盖确认是否均可表达。
4. 确认契约未加入模糊匹配、用户取消按钮、数据库、网络或其它未批准能力。

## Next action for receiver

Antigravity 从 UI / ViewModel 调用角度 Review `docs/contracts.md`，重点检查模型是否足以驱动已批准页面状态、预览、进度、成功统计和错误反馈；在同一 PR 提出或提交契约调整，不开始 UI 实现。

## Risks / open questions

- UI 中两个可操作保护开关与 Processing Baseline 的不可绕过安全规则之间，需要确认关闭时语义。
- Contract 返回 `EmptyKeyCount`，UI 成功统计面板尚未明确是否单独展示该项。
