# TabularStudio 旧流程废止说明

- Status: 已废止
- 生效日期：2026-09-10
- 依据：Project Owner 指示“把之前设定的流程全部废除”。

此前设定的全部项目协作与开发流程不再执行，包括：

- 三道 Owner Gate 及其开发前、合并前、发布包验收审批。
- 强制先建 Issue、模板必填、一个 Issue 一条分支及固定分支命名。
- Grok / Antigravity / Codex 的强制角色限制、双方审查及跨角色 Handoff。
- 固定 Implementation / Review / QA / Done 生命周期和前置条件。
- 因 dirty working tree、main 或 PR head 变化、测试失败而必须中断并等待 Owner 的旧 STOP 程序。
- exact PR head SHA、filename / size / SHA256 的强制审批锁定。
- 受控并行流程、历史任务协调顺序及桌面 GUI 自动化的项目流程禁令。
- “聊天记录不得覆盖流程文档”及不允许简化旧流程的条款。

后续直接按用户当前任务指令执行，不另设替代审批流程。废除旧流程不意味着测试已经通过、历史待验收项已通过，也不改变产品需求或实现。

其它文档中的旧流程文字仅保留为历史记录或技术参考，不具有流程约束力。旧版全文可通过 Git 历史查阅。
