# Issue #56 UI 集成审查反馈

来源：Codex 对 Antigravity 首轮实现中的实际文件只读检查。此记录不是 Owner Gate，也不代替实际桌面 QA。接收方 Antigravity 在原批准范围内修正后继续，不需要用户搬运信息。

## 待复核与修正

1. 两页 `RefreshProfiles()` 必须展示 `ProfileListResult.Errors`；当前仅枚举有效项会把损坏配置静默隐藏。合法项继续可选用。首次保存成功也需要明确“已保存”反馈。
2. 两页新配置状态区必须限制高度（最多96 DIP），长错误局部滚动可读。`ConfirmProfileDialog` 中配置名称可很长；确认消息也需受限滚动，固定按钮保持可达，不通过新增名称业务限制回避。
3. 匹配 `CanApplyProfile` / `CanSaveProfile` 需要当前完整来源和预览有效、预览未加载。保存条件/返回项必须仍是当前可用列，筛选必须是当前有效候选；输入读取中不能用残留预览保存。相关源属性、预览/候选加载、条件/返回选择变化要通知按钮状态。
4. 匹配校验中 `CanBrowseMaster` / `CanBrowseReference` 也要禁用并通知；工作表/表头/同文件模式/输出及条件编辑通过相应绑定禁用。异步返回须核对来源路径、Sheet、Header、预览代次，过时任务不给当前页面留下“正在校验”假状态。
5. 不能让此前 `LoadMasterFilterValuesAsync` 的晚响应覆盖新配置候选。开始准备配置时取消并作废旧候选加载代次；应用中抑制候选加载、状态重算及修改提示，全部准备好后提交。校验失败时原条件、返回项、筛选候选/选择不变；取消/过时结果可恢复操作。不能仅设置 `_suppressFilterColumnLoad` 而不处理已经在途的旧请求。
6. 匹配没有注入 validator 时不能略过结构兼容与筛选验证就应用；应返回明确的未初始化错误。保持旧构造函数可用，不在测试中意外创建真实本机存储。

## 已有 Core 验证

Codex Core 新增49项测试和全套316项已通过，含三格式配置重载输出等价、五类统计、类型差异、损坏与锁定恢复；随后补充存储目录异常回归。这些是自动化证据，桌面 FT 仍待运行。

## 接收方下一步

Antigravity 完成首轮实现和 App 测试后阅读本记录、逐项修正，在 `issue56-ui-handoff.md` 如实记录。不要运行终端命令（当前 headless 权限不允许）；Codex 负责构建/运行并回传具体问题。不得改 Core、批准范围、依赖或执行 Git 发布操作。

## 首次集成构建失败（待修复）

`dotnet build TabularStudio.sln -v minimal --nologo`：6 errors、0 warnings。DataMatchingViewModel.cs:1897–1899 的 SelectedMasterFilterValue 是 ColumnValueOption，Kind/RawValue/HasTime 应读取 `.Value.Kind/.Value.RawValue/.Value.HasTime`。2131/2146/2161 的 Application 未引入，应按现有代码使用 System.Windows.Application。此处修复不改变契约。构建失败后仅进行定位/修复，尚未进行桌面验收。
