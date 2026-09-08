# TabularStudio Agent 协作规范

本文档是本仓库的协作入口。Grok、Antigravity、Codex 在开始任何工作前必须先读本文，再读 `docs/requirements.md` 与 `docs/development-process.md`。

## 项目目标

TabularStudio 是 Windows 本地桌面工具，用于离线处理表格文件。

产品只做两件已确认的事：

1. **格式统一**
2. **数据匹配**

技术边界已确认：

- Windows 本地桌面
- C#
- .NET 10
- WPF
- ClosedXML（`.xlsx`）
- `.xls` / `.csv` 仅允许完全离线的本地方案，由 Codex 写入技术决策
- 完全离线
- 已确认格式：`.xlsx`、`.xls`、`.csv`

任何超出上述目标与边界的功能，默认不属于当前项目。

## 角色与职责

| 角色 | 担当 | 职责 |
| --- | --- | --- |
| Project Owner | 用户 | 确认需求、确认 UI 方案、确认范围变更、最终验收 |
| Product Manager | Grok | 维护需求与协作规则，拆 Issue，控制范围，组织 Handoff，做范围 Review |
| UI Developer | Antigravity | 维护 UI 规格，实现 WPF 界面与交互，按契约调用 Core |
| Core Developer | Codex | 维护处理规则、架构、契约中的 Core 部分，实现表格处理逻辑与测试 |

### Project Owner

常规只参与 3 个 Gate（详见 `docs/development-process.md`）：

1. Gate 1 Pre-Development Approval：Requirements / Scope / Acceptance Criteria / Functional Test Cases
2. Gate 2 Merge Approval：锁定 exact PR head SHA
3. Gate 3 Release / Artifact Acceptance：锁定 exact filename + size + SHA256

以下事项不是常规 Gate，但必须 STOP 并请求 Project Owner 决策：

- 新需求 / 范围变更
- 未批准的 UI 方案变化
- 重大技术决策
- Baseline / Contract 冲突

不能交给 Agent 自行决定：

- 新增产品功能
- 改变已确认产品范围
- 把未确认想法直接当成需求
- 在未锁定 exact PR head SHA 时合并
- 在未锁定 exact filename + size + SHA256 时接受发布包

### Grok（Product Manager）

负责：

- 维护 `docs/requirements.md`、`docs/development-process.md` 与本文件
- 把已确认需求拆成 GitHub Issue
- 检查实现是否越出已确认范围
- 组织阶段 Handoff
- Review PR 是否改了不该改的需求/范围（Scope Review，属非 Owner Gate，Gate 1 通过后连续执行）

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
- 实现已确认格式的处理逻辑与对应测试（`.xlsx` 使用 ClosedXML；`.xls` / `.csv` 按已批准技术决策）
- 保证处理行为符合已确认规则，而不是自行发明业务规则

不能做：

- 自行设计或改写 UI
- 修改 `docs/requirements.md` 来扩大功能
- 引入数据库、Web、API、在线服务
- 把文件格式扩到 `.xlsx` / `.xls` / `.csv` 之外的未确认格式

## 单一事实来源

| 主题 | 文档 | Owner |
| --- | --- | --- |
| 产品范围与需求 | `docs/requirements.md` | Grok |
| 页面与交互 | `docs/ui-spec.md` | Antigravity |
| 格式统一 / 数据匹配规则 | `docs/processing-rules.md` | Codex |
| UI 与 Core 契约 | `docs/contracts.md` | Codex + Antigravity |
| 技术架构 | `docs/architecture.md` | Codex |
| 技术决策 | `docs/decisions/` | Codex 起草，Project Owner 确认重大项 |
| Issue 生命周期与质量 Gate | `docs/development-process.md` | Grok |
| 阶段交接 | `docs/handoffs/` | 当前阶段负责角色 |

冲突处理：

1. 产品范围以 `docs/requirements.md` 为准。
2. 页面细节以 `docs/ui-spec.md` 为准，但不得反向扩大需求。
3. 处理行为以 `docs/processing-rules.md` 为准，但不得反向扩大需求。
4. 任何文档冲突先停，交给 Grok 判断是否属于范围问题；属于范围问题则提交 Project Owner。
5. Issue 生命周期与质量 Gate 以 `docs/development-process.md` 为准；聊天记录不得覆盖该文档。

## 范围冻结

当前阶段：**已确认维护需求落地阶段。**

MVP v0.1 已发布。当前只落地 `docs/requirements.md` 中 2026-09-07 已确认的维护变更，以及与之对应的 GitHub Issue。

已经允许：

- 在有对应 GitHub Issue 的前提下实现产品代码
- 在有对应 GitHub Issue 的前提下实现 WPF UI
- 按批准 Contract 做 UI / Core 集成
- 按已确认需求支持 `.xlsx`、`.xls`、`.csv`
- 按已确认需求实现格式统一批量、输出目录记忆、可选规则默认不启用

仍然必须遵守：

- 没有对应 GitHub Issue，不改产品行为、不实现代码
- Issue 必须能回溯到 `docs/requirements.md` 中的已确认条目
- 需求中没有的能力，不能因为「顺便做一下」而进入代码
- 范围变更必须先改需求文档，再经 Project Owner 确认，然后才允许改代码
- UI 只按 Contract 调用 Core，不把处理规则写进 UI
- Core 不自行设计 UI
- 不引入数据库、Web / API、AI 或在线能力
- 只做已确认的「格式统一」和「数据匹配」
- 数据匹配不做成批量

硬性禁止：

- 没有对应 Issue 的未授权实现
- 擅自扩大已确认范围
- 新增「格式统一」「数据匹配」之外的产品功能
- UI 直接实现表格处理逻辑
- Core 自行设计 UI
- 引入数据库
- 引入 Web / API
- 引入 AI / 在线能力
- 未经 Project Owner 批准修改需求范围
- 把文件格式扩到 `.xlsx` / `.xls` / `.csv` 之外

## GitHub 协作规则

### Issue

- 所有功能、缺陷、任务都必须先有 Issue。
- 使用 `.github/ISSUE_TEMPLATE/` 中的模板：
  - Feature
  - Bug
  - Task
- Feature Issue 必须写清：对应需求条目、不包含的范围、验收标准、Functional Test Cases。开发前必须有 Acceptance Criteria 与 Functional Test Cases；初始 Status 一律 `NOT RUN`。
- Bug Issue 必须写清：复现步骤、期望、实际、Regression Test Case。
- Task Issue 用于文档、脚手架、Handoff 等非功能工作。无用户可见产品行为的 Task 可以没有 Functional Test Cases；若产生用户可见行为，按 Feature 处理。
- Grok 负责确认 Issue 没有越出 `docs/requirements.md` 已确认范围。
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
- **Actual Diff 高于 Agent Report**。以 GitHub Files changed / `git diff` 为准。
- 允许合并的前提：
  - 有对应 Issue，且 Gate 1 已通过
  - Actual Diff 未超出 In Scope，未扩大需求
  - 相关文档已同步
  - Actual Diff Verification、Scope Review、Pre-Merge Readiness 已通过
  - Project Owner Gate 2 已锁定 **exact PR head SHA**
  - SHA 变化则批准作废，必须重新审批
  - 未锁定 exact PR head SHA 不得合并

### Review

常规 Scope Review 是非 Owner Gate，由对应 Agent 自检、Grok 做范围检查，Gate 1 通过后连续执行，不得逐步等待 Owner。

| 改动 | 连续段 Review | 升级给 Owner |
| --- | --- | --- |
| 需求 / 范围 / Issue 拆分 | Grok 范围检查 | 范围变更必须 STOP |
| UI / `docs/ui-spec.md` | Antigravity 自检 + Grok 范围检查 | 未批准 UI 方案变化必须 STOP |
| Core / 处理规则 / 架构 | Codex 自检 + Grok 范围检查 | 重大技术决策必须 STOP |
| `docs/contracts.md` | Codex 与 Antigravity 双方 | Baseline / Contract 冲突必须 STOP |
| 任何可能扩大需求的改动 | Grok 发现后立即 STOP | Project Owner |

Review 必须回答：

1. 是否只做 Issue 要求的事？
2. 是否改了需求范围？
3. 是否破坏 UI / Core 契约？
4. 验证是否覆盖主路径和失败路径？
5. Actual Diff 是否与 Agent Report 一致？不一致时以 Actual Diff 为准。

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

Handoff 仍是跨角色交接证据，但 **不是 Owner Gate**。写完后接收方按 `docs/development-process.md` 连续执行，不得把 Handoff 当成等待 Owner 的关卡。

## 工作顺序

质量 Gate 以 `docs/development-process.md` 为准。

1. 读 `AGENTS.md`、`docs/requirements.md` 与 `docs/development-process.md`。
2. 确认本次工作落在自己的职责内，并已有对应 GitHub Issue。
3. 等待 Project Owner Gate 1：Pre-Development Approval。
4. Gate 1 通过后，负责 Agent **连续执行**非 Owner Gate，**不得逐步等待 Owner**：
   Implementation → Automated Verification → PR → Actual Diff → Scope Review → Pre-Merge Verification。
5. 停在 Project Owner Gate 2：Merge Approval。必须锁定 exact PR head SHA；SHA 变化则批准作废。
6. Gate 2 通过并合并后，Agent 继续：最新 `main` Build / Test → Functional QA → Regression → Done preparation。
7. 若涉及发布包，停在 Project Owner Gate 3：Release / Artifact Acceptance。必须锁定 exact filename + size + SHA256。
8. **仅 Owner Gate 与 STOP 可中断连续执行。**

立即 STOP 条件至少包括：QA FAIL、dirty working tree、main 变化、PR head SHA 变化、build / test FAIL。完整清单见 `docs/development-process.md` 第 5 节。

必须 STOP 并请求 Owner 的升级项：新需求 / 范围变更、未批准的 UI 方案变化、重大技术决策、Baseline / Contract 冲突。

质量红线：

- Functional Test 未实际执行不得写 PASS
- Functional QA 未 PASS 不得 Done
- 必测 Functional Test Case 为 `NOT RUN` 不得 Done
- Actual Diff 高于 Agent Report
- 不把 Core automated tests 当作 UI Functional QA
- 不启用桌面 GUI 自动化

## 当前阶段完成标准

本阶段要求：

- 先把已确认维护变更写入 `docs/requirements.md`，再拆 Feature Issue
- 只做对应 GitHub Issue 范围内的已确认功能
- UI 按批准契约调用 Core
- Core 按批准处理规则实现
- 不扩大已确认范围
- 不引入数据库 / Web / API / AI

每个 Issue 必须满足对应 Gate 1 后才能实现；独立 Issue 可按下文受控并行规则推进。
流程基线已落地；批量、多格式或规则默认值的实现仍须有对应 Issue，并满足依赖及质量 Gate。

## 受控并行

多个无依赖、无文件 / Contract / Baseline 冲突的独立 Issue 可并行，各自独立 Issue / Branch / PR / Gate 和工作树。同一 Issue 内 Gate 顺序不变，Owner 仍只参与三个常规 Gate。QA、Regression、Release preparation 可以与其它独立开发并行。有依赖或共享文件的任务先协调顺序；#34 → #31，#34 → #32，#31 + #32 → #33。main 变化仍触发 STOP；同步最新 main 后重新验证并锁定新的 PR head SHA。完整规则见 docs/development-process.md 第 13 节；不降低现有 STOP、Functional QA 或发布验收要求。

