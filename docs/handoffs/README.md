# Handoffs

本目录保存阶段性交接文档。跨角色传递工作必须以这里的文件为准，不把聊天记录当作交接完成。

## 谁写

| 交接 | 作者 |
| --- | --- |
| 需求已拆 Issue，交给 UI / Core | Grok |
| UI 规格完成，交给 Core 对契约 | Antigravity |
| Core 规则 / 契约完成，交给 UI 联调 | Codex |
| 某一 Issue 实现完成，交给另一方接续 | 该 Issue 的实现角色 |

## 文件命名

`YYYYMMDD-<from>-to-<to>-<topic>.md`

示例：`20260902-grok-to-antigravity-ui-spec-kickoff.md`

## 建议结构

```md
# Handoff: <topic>

- Date:
- From:
- To:
- Related Issue:
- Related PR:

## Done
## Not done
## Documents updated
## Contract changes
## How to verify
## Next action for receiver
## Risks / open questions
```

没有对应 Handoff 文件，不得声称已经交给另一角色。

Handoff 仍是跨角色交接证据，但 **不是 Project Owner Gate**。写完 Handoff 后，接收方按 `docs/development-process.md` 连续执行，不得把 Handoff 当成逐步等待 Owner 的关卡。
