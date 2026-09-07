# TabularStudio 开发流程

- Status: Process baseline（已确认）
- Owner: Grok（Product Manager）
- Approver: Project Owner
- Last confirmed: 2026-09-07
- Related Issue: #37

本文是 Issue 生命周期与质量 Gate 的单一事实来源。  
聊天记录、Agent Report、Handoff 说明不得覆盖本文。

本文不改变产品范围。产品行为仍以 `docs/requirements.md` 为准。

## 1. 文档定位

1. Issue 怎么立项、怎么开发、怎么合并、怎么验收、何时可标 Done，以本文为准。
2. 产品范围以 `docs/requirements.md` 为准；本文不得新增产品功能。
3. 页面细节以 `docs/ui-spec.md` 为准，但不得反向扩大需求。
4. 处理行为以 `docs/processing-rules.md` 为准，但不得反向扩大需求。
5. UI / Core 契约以 `docs/contracts.md` 为准。
6. Windows x64 发布包的具体构建与验收步骤以 `docs/release-win-x64.md` 为准。本文只锁定 Owner Gate 3，不放宽那份文档的规则。

## 2. 常规 Owner Gate（仅 3 个）

Project Owner 常规只参与以下 3 个 Gate。其余质量关卡不是 Owner Gate，不得逐步等待 Owner。

| Gate | 名称 | Owner 必须确认的内容 | 未通过时 |
| --- | --- | --- | --- |
| Gate 1 | Pre-Development Approval | Requirements / Scope / Acceptance Criteria / Functional Test Cases | 不得开始实现 |
| Gate 2 | Merge Approval | 锁定 **exact PR head SHA** | 不得合并 |
| Gate 3 | Release / Artifact Acceptance | 锁定 **exact filename + size + SHA256** | 不得宣称发布包验收通过 |

### 2.1 Gate 1：Pre-Development Approval

开发前必须同时具备：

- 已确认需求，或明确写明本 Issue **不对应**产品功能变更
- In Scope
- Out of Scope
- Acceptance Criteria
- Functional Test Cases（见 2.1.1 的例外）

Feature Issue：开发前必须有 Acceptance Criteria 与 Functional Test Cases。Functional Test Cases 的初始 Status 一律 `NOT RUN`。

Bug Issue：必须有复现步骤与 Regression Test Case。Regression Test Case 的初始 Status 一律 `NOT RUN`。

#### 2.1.1 Task 与 Functional Test Cases

- 无用户可见产品行为的 Task：可以没有 Functional Test Cases。
- 会产生用户可见产品行为的 Task：按 Feature 处理，开发前必须有 Acceptance Criteria 与 Functional Test Cases，初始 Status 一律 `NOT RUN`。

Gate 1 未批准前，不得改产品行为、不得实现代码、不得把未确认想法当成需求。

### 2.2 Gate 2：Merge Approval

- 必须锁定 **exact PR head SHA**。
- 未锁定 exact PR head SHA，不得合并。
- PR head SHA 一旦变化，既有 Merge Approval **作废**，必须重新审批。
- Agent 自述、PR 描述、聊天记录都不能替代 SHA 锁定。

### 2.3 Gate 3：Release / Artifact Acceptance

- 必须锁定 **exact filename + size + SHA256**。
- 规则不得弱于现有 `docs/release-win-x64.md`。
- 不得只凭 build 成功、测试通过或 Agent Report 宣称发布包验收通过。
- 不得在未锁定上述三元组时接受 artifact。

## 3. Agent 连续执行（非 Owner Gate）

Gate 1 通过后，负责 Agent **自动连续执行**非 Owner Gate，**不得逐步等待 Owner**。

仅以下情况可以中断连续执行：

1. 到达下一个 Owner Gate
2. 命中第 5 节 STOP 条件
3. 命中第 6 节必须升级给 Project Owner 的事项

### 3.1 合并前连续段

Implementation → Automated Verification → Pull Request → Actual Diff Verification → Scope Review → Pre-Merge Readiness Verification → **停在 Gate 2**

### 3.2 合并后连续段

Gate 2 通过并合并 **已锁定 SHA** 的 PR 之后：

最新 `main` Build / Test → Functional QA → Regression → Done preparation

若本 Issue 产生发布包，再 **停在 Gate 3**。

### 3.3 各关卡要求

| 关卡 | 执行方 | 要求 |
| --- | --- | --- |
| Implementation | 对应角色 Agent | 只改 Issue In Scope 文件；不夹带无关重构 |
| Automated Verification | 对应角色 Agent | 产品代码 Issue：`dotnet build` / `dotnet test` 失败即 STOP。纯协作文档 Task：做文档自检对照 Acceptance Criteria；`dotnet build` / `dotnet test` 不是产品验收项 |
| Pull Request | 对应角色 Agent | 必须关联 Issue；按 PR 模板填写；描述不得与 Actual Diff 冲突 |
| Actual Diff Verification | 对应角色 Agent + Grok | **Actual Diff 高于 Agent Report**。以 `git diff` / GitHub Files changed 为准 |
| Scope Review | Grok | 确认只做 Issue 要求的事、未改需求范围、未破坏契约 |
| Pre-Merge Readiness Verification | 对应角色 Agent + Grok | 工作树干净、In Scope 文件集合正确、Acceptance Criteria 可逐条追溯、exact PR head SHA 已记录、无 STOP 项 |
| Merge 后最新 main Build / Test | 对应角色 Agent | 在 **已合并后的最新 `main`** 上执行。失败即 STOP |
| Functional QA | 对应角色 Agent | 必须实际执行必测用例。未实际执行不得写 PASS。失败即 STOP |
| Regression | 对应角色 Agent | Bug 必须跑 Regression Test Case。不得降低既有回归要求 |
| Done preparation | Grok | 仅当必测用例不是 `NOT RUN`、Functional QA 已 PASS（或本 Issue 明确无用户可见行为、不适用 Functional QA）时，才可准备标 Done |

Handoff 仍是跨角色交接证据，但 **不是 Owner Gate**。写完 Handoff 后，接收方继续按本节连续执行，不得停下来等 Owner。

## 4. Issue 生命周期

```
创建 Issue（Feature / Bug / Task 模板）
    → Gate 1 Pre-Development Approval
    → 合并前连续段（第 3.1 节）
    → Gate 2 Merge Approval（锁定 exact PR head SHA）
    → 合并已锁定 SHA
    → 合并后连续段（第 3.2 节）
    → 如有发布包：Gate 3 Release / Artifact Acceptance
    → Done
```

硬性约束：

- 所有功能、缺陷、任务都必须先有 GitHub Issue。
- Feature Issue 必须能回溯到 `docs/requirements.md` 中的已确认条目，或先改需求并经 Project Owner 确认。
- 未确认需求不得开成 Feature Issue。
- 没有对应 Issue，不改产品行为、不实现代码。
- 一个 Issue 一条分支。命名：`feature/<issue-id>-short-name`、`fix/<issue-id>-short-name`、`task/<issue-id>-short-name`。

## 5. 立即 STOP 条件

命中任一条，立即停止连续执行，并通知 Project Owner。不得自行“先合再说”。

至少包括以下各项：

| 条件 | 说明 |
| --- | --- |
| QA FAIL | Functional QA 或必测用例实际执行失败 |
| dirty working tree | 开始实现前、开 PR 前、请求 Gate 2 前，工作树必须干净 |
| main 变化 | 工作过程中 `origin/main` 相对本 Issue 开始时的基线发生变化 |
| PR head SHA 变化 | Gate 2 已锁定的 SHA 与当前 PR head 不一致；既有批准作废 |
| build / test FAIL | 已执行的 `dotnet build` / `dotnet test` 失败 |

同时适用：

- Actual Diff 出现 Issue Out of Scope 文件
- Actual Diff 出现未授权产品代码或需求范围变更
- Agent Report 与 Actual Diff 冲突，且按 Actual Diff 已越出范围
- 必测 Functional Test Case 仍为 `NOT RUN`，却要把 Issue 标为 Done
- Functional QA 未 PASS，却要把 Issue 标为 Done
- 未锁定 exact PR head SHA，却要合并
- 未锁定 exact filename + size + SHA256，却要接受发布包
- 启用桌面 GUI 自动化

## 6. 必须升级给 Project Owner 的事项

以下情况 **不属于**常规 3 个 Gate，但必须 STOP，并请求 Project Owner 决策：

1. 新需求 / 范围变更
2. 未批准的 UI 方案变化
3. 重大技术决策
4. Baseline / Contract 冲突

Agent 不得自行批准这些事项，也不得把它们写进实现后的 PR 再“顺带确认”。

## 7. 证据规则

1. **Actual Diff > Agent Report**。Files changed / `git diff` 与 Agent 自述冲突时，以 Actual Diff 为准。
2. GitHub 证据必须自行归档到对应 Issue 或 PR 评论，不能只留在本地聊天。
3. Functional Test 未实际执行，不得写 `PASS`。
4. 不得把 Core automated tests 当作 UI Functional QA。
5. 不得把未实际执行的测试写成 `PASS`。
6. 不得降低 Bug Issue、Regression Test、Functional QA、Release Artifact 的既有验收要求。
7. 不引入数据库、Web / API、AI、在线能力或桌面 GUI 自动化来“加快验收”。

## 8. Functional QA 与 Done

- Functional Test 未实际执行，不得写 `PASS`。
- Functional QA 未 PASS，不得 Done。
- 必测 Functional Test Case 为 `NOT RUN` 时，不得将 Issue 标为 Done。
- 无用户可见产品行为的 Task：Functional QA 不适用，不得写成 `PASS`；在 Acceptance Criteria 全部满足、且 Actual Diff 未改产品代码 / 产品需求时，可以准备 Done。
- Core `dotnet test` 通过，只证明 Core 自动化测试通过，不构成 UI Functional QA PASS。

## 9. Merge SHA 锁定

Gate 2 请求必须同时给出：

1. PR 链接
2. **exact PR head SHA**
3. Actual Diff / Scope Review 结果
4. Acceptance Criteria coverage
5. Pre-Merge Readiness 结果
6. 是否存在任何 blocker

合并时校验：当前 PR head SHA 必须与批准 SHA **完全一致**。不一致则 STOP，批准作废。

## 10. Release artifact 锁定

Gate 3 请求必须锁定：

1. exact filename
2. exact size
3. exact SHA256

并遵守 `docs/release-win-x64.md` 的既有步骤与 checklist。本文不替换、不减少、不放宽那些规则。

`docs/release-win-x64.md` 中“不可用检查必须标 NOT RUN / NOT YET VERIFIED、不得在必测项 pending 时报告 overall PASS”的要求继续有效。

## 11. 角色在本流程中的位置

| 角色 | 常规工作 | 不是 |
| --- | --- | --- |
| Project Owner | 只做 Gate 1 / 2 / 3，以及第 6 节升级项 | 不逐步确认每一个非 Owner Gate |
| Grok | 维护需求与流程、拆 Issue、Scope Review、组织 Handoff、控制范围 | 不写产品代码；不代替 Owner 做 3 个 Gate |
| Antigravity | 按契约实现 UI，完成 UI 侧连续段 | 不实现 Core 处理逻辑；不扩大需求 |
| Codex | 按处理规则实现 Core，完成 Core 侧连续段 | 不设计 UI；不扩大需求 |

## 12. 模板与文档同步

本流程落地到：

- `AGENTS.md`：入口摘要，并指向本文
- `.github/ISSUE_TEMPLATE/feature.md`
- `.github/ISSUE_TEMPLATE/bug.md`
- `.github/ISSUE_TEMPLATE/task.md`
- `.github/pull_request_template.md`
- `docs/handoffs/README.md`
- `docs/release-win-x64.md`（仅增加 Gate 3 指向，不放宽验收）

模板不得删掉本文已经要求的 Gate 1 字段、复现步骤、Regression Test Case、Actual Diff 或 Merge SHA 锁定字段。
