# Architecture Decision Records

本目录保存 TabularStudio 的 ADR（技术决策记录）。

Owner：Codex 起草。影响产品范围或引入新依赖时，需 Project Owner 确认。

## 什么时候写 ADR

- 选择或更换库
- 调整工程拆分
- 改变离线约束的实现方式
- 任何不能从 `docs/architecture.md` 一眼看懂的技术取舍

## 什么时候不写 ADR

- 已确认需求本身（写 `docs/requirements.md`）
- UI 细节（写 `docs/ui-spec.md`）
- 格式统一 / 数据匹配的业务规则（写 `docs/processing-rules.md`）

## 文件命名

`ADR-YYYYMMDD-short-title.md`

示例：`ADR-20260902-use-closedxml-for-xlsx.md`

## 建议结构

```md
# ADR-YYYYMMDD Title

- Status: Proposed | Accepted | Superseded
- Date:
- Owner: Codex
- Confirmed by:

## Context
## Decision
## Consequences
## Out of scope
```

当前目录为空，表示尚未记录额外技术决策。已确认技术方向见 `docs/architecture.md` 与 `docs/requirements.md`。
