# Issue #41 数据匹配重试恢复

- Owner 于当前任务批准 Gate 1；本次授权 Codex 独立处理 UI/Core、验证与 Review，不需要跨角色交接。
- Baseline: `c0e91cb3f2f79b82d2c8ae80ce22374efdc7b801`。
- 分支：`fix/41-matching-error-recovery`。

## 原因与修改

`CanStart` 仅接受 Ready；执行结束设置 Success 或 Error，即使 finally 清除了 IsProcessing 仍不能再次开始。
允许配置有效的 Success 与来源为 Execute 的 Error 再次开始。预览/输入错误不在放行范围。
错误信息保留至下次开始或配置调整。运行中仍锁定配置并阻止重复执行。
文件占用提示涵盖 WPS / Excel，说明可以关闭文件重试或更改路径。

## 验证

- `dotnet build TabularStudio.sln`：PASS，0 warnings / 0 errors。
- `dotnet test TabularStudio.sln --no-build`：PASS，Core 203 + App 3。
- App 测试直接调用实际 ViewModel、Inspection 和 MatchingService，合成工作簿并用 FileShare.None 独占输出文件。
- 覆盖连续两次占用失败、释放锁原配置重试、成功后再次执行、取消覆盖、另存、输出原文件字节保持、输入路径保护、配置缺失与预览加载/失败阻断。
- 运行中阻断通过状态断言验证；未声称完成 WPF 按钮点击或 WPS 人工验收。
- UI Functional QA / Issue RT-01～RT-06 人工用例：NOT RUN，合并后在最新 main 和实际桌面程序上执行。
- 不启用桌面 GUI 自动化。

## 范围与下一步

无 Core 契约、处理算法、需求范围变更。新增 App 测试项目引用 App；原 Core 测试项目仍仅引用 Core。
不包含条件筛选匹配或 #31～#34。无跨角色 Handoff，本文为验证记录。
Gate 2 须锁定 PR exact head SHA；批准前不合并。尚未发布或完成最终验收。
