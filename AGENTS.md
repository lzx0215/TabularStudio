# TabularStudio Agent 协作规范

本文档是本仓库的协作入口。Grok、Antigravity、Codex 在开始任何工作前必须先读本文，再读 `docs/requirements.md`。

## 项目目标

TabularStudio 是 Windows 本地桌面工具，用于离线处理 Excel 表格。

MVP 只做两件已确认的事：

1. **格式统一**
2. **数据匹配**

技术边界已确认：

- Windows 本地桌面
- C#
- .NET 10
- WPF
- ClosedXML
- 完全离线
- 主要处理 `.xlsx`

任何超出上述目标与边界的功能，默认不属于当前项目。

## 角色与职责

| 角色 | 担当 | 职责 |
| --- | --- | --- |
| Project Owner | 用户 | 确认需求、确认 UI 方案、确认范围变更、最终验收 |
| Product Manager | Grok | 维护需求与协作规则，拆 Issue，控制范围，组织 Handoff，做范围 Review |
| UI Developer | Antigravity | 维护 UI 规格，实现 WPF 界面与交互，按契约调用 Core |
| Core Developer | Codex | 维护处理规则、架构、契约中的 Core 部分，实现 Excel 处理逻辑与测试 |

### Project Owner

负责：

- 确认或否决需求、UI 方案、技术决策
- 确认范围变更
- 最终验收

不能交给 Agent 自行决定：

- 新增产品功能
- 改变 MVP 范围
- 把未确认想法直接当成需求

### Grok（Product Manager）

负责：

- 维护 `docs/requirements.md` 与本文件
- 把已确认需求拆成 GitHub Issue
- 检查实现是否越出已确认范围
- 组织阶段 Handoff
- Review PR 是否改了不该改的需求/范围

不能做：

- 写产品代码
- 创建 `.sln` / `.csproj` / `src/` / `tests/`
- 自行设计 UI 细节
- 自行设计 C# 接口或 Excel 处理算法
- 擅自增加产品功能

### Antigravity（UI Developer）

负责：

- 维护 `docs/ui-spec.md`
- 与 Codex 共同维护 `docs/contracts.md` 中的 UI 调用约定
- 实现 WPF 页面、交互、状态展示
- 按契约调用 Core，不把处理规则写进 UI

不能做：

- 实现 Excel 读写与匹配/格式统一核心逻辑
- 修改 `docs/requirements.md` 来扩大功能
- 增加未确认页面、入口或交互
- 引入网络、账号、云同步等在线能力

### Codex（Core Developer）

负责：

- 维护 `docs/processing-rules.md`、`docs/architecture.md`
- 与 Antigravity 共同维护 `docs/contracts.md`
- 记录技术决策到 `docs/decisions/`
- 实现 ClosedXML / `.xlsx` 处理逻辑与对应测试
- 保证处理行为符合已确认规则，而不是自行发明业务规则

不能做：

- 自行设计或改写 UI
- 修改 `docs/requirements.md` 来扩大功能
- 引入数据库、Web、API、在线服务
- 把主要文件格式从 `.xlsx` 扩到未确认格式并当作 MVP

## 单一事实来源

| 主题 | 文档 | Owner |
| --- | --- | --- |
| 产品范围与需求 | `docs/requirements.md` | Grok |
| 页面与交互 | `docs/ui-spec.md` | Antigravity |
| 格式统一 / 数据匹配规则 | `docs/processing-rules.md` | Codex |
| UI 与 Core 契约 | `docs/contracts.md` | Codex + Antigravity |
| 技术架构 | `docs/architecture.md` | Codex |
| 技术决策 | `docs/decisions/` | Codex 起草，Project Owner 确认重大项 |
| 阶段交接 | `docs/handoffs/` | 当前阶段负责角色 |

冲突处理：

1. 产品范围以 `docs/requirements.md` 为准。
2. 页面细节以 `docs/ui-spec.md` 为准，但不得反向扩大需求。
3. 处理行为以 `docs/processing-rules.md` 为准，但不得反向扩大需求。
4. 任何文档冲突先停，交给 Grok 判断是否属于范围问题；属于范围问题则提交 Project Owner。

## 范围冻结

当前阶段：**只初始化项目管理和协作文档骨架。**

硬性禁止：

- 擅自扩展需求
- 写 C# 产品代码
- 创建 `.sln` / `.csproj`
- 创建 `src/` / `tests/`
- 实现 UI
- 实现 Excel 逻辑
- 创建数据库 / Web / API
- 增加「格式统一」「数据匹配」之外的新产品功能

以后进入实现阶段后，仍然遵守：

- 没有对应 GitHub Issue，不改产品行为
- Issue 必须能回溯到 `docs/requirements.md` 中的已确认条目
- 需求中没有的能力，不能因为「顺便做一下」而进入代码
- 范围变更必须先改需求文档，再经 Project Owner 确认，然后才允许改代码

## GitHub 协作规则

### Issue

- 所有功能、缺陷、任务都必须先有 Issue。
- 使用 `.github/ISSUE_TEMPLATE/` 中的模板：
  - Feature
  - Bug
  - Task
- Feature Issue 必须写清：对应需求条目、不包含的范围、验收标准。
- Bug Issue 必须写清：复现步骤、期望、实际。
- Task Issue 用于文档、脚手架、Handoff 等非功能工作。
- Grok 负责确认 Issue 没有越出 MVP。
- 未确认需求不得开成 Feature Issue。

### Branch

- 从默认分支拉出，一个 Issue 一条分支。
- 命名：
  - `feature/<issue-id>-short-name`
  - `fix/<issue-id>-short-name`
  - `task/<issue-id>-short-name`
- 分支内不夹带无关重构。
- 不把多个不相关 Issue 混在同一分支。

### Pull Request

- 使用 `.github/pull_request_template.md`。
- PR 必须关联 Issue。
- 描述必须写清：改了什么、如何验证、是否改契约、Handoff 说明。
- 允许合并的前提：
  - 有对应 Issue
  - 未扩大需求
  - 相关文档已同步
  - Review 通过

### Review

按改动类型指定 Reviewer：

| 改动 | 必须 Review |
| --- | --- |
| 需求 / 范围 / Issue 拆分 | Project Owner 或 Grok |
| UI / `docs/ui-spec.md` | Antigravity 自检 + Grok 范围检查 |
| Core / 处理规则 / 架构 | Codex 自检 + Grok 范围检查 |
| `docs/contracts.md` | Codex 与 Antigravity 双方 |
| 任何可能扩大需求的改动 | Project Owner |

Review 必须回答：

1. 是否只做 Issue 要求的事？
2. 是否改了需求范围？
3. 是否破坏 UI / Core 契约？
4. 验证是否覆盖主路径和失败路径？

### Handoff

跨角色交接必须写 `docs/handoffs/` 文档，不能只靠聊天记录。

Handoff 至少包含：

- 来源 Issue / PR
- 已完成内容
- 未完成内容
- 契约是否变化
- 接收方下一步
- 验证方式

没有 Handoff 文档，不得声称某一阶段已交给另一方。

## 工作顺序

1. 读 `AGENTS.md` 与 `docs/requirements.md`。
2. 确认本次工作落在自己的职责内。
3. 找到或创建 GitHub Issue。
4. 拉分支。
5. 只改 Issue 范围内的文件。
6. 同步相关文档。
7. 开 PR，按模板填完整。
8. 等待对应角色 Review。
9. 如需交接下一方，写 Handoff。

## 当前阶段完成标准

本阶段只要求：

- 协作文档骨架齐全
- 角色边界清楚
- Issue / PR 模板可用
- 不出现产品代码和工程脚手架

下一步必须等 Project Owner 明确下达，不得自动进入方案细化或编码。
