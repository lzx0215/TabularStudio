# Issue #56：旧结果状态修复与 v0.3.0 整合评估

日期：2026-09-10。关联 PR：<https://github.com/lzx0215/TabularStudio/pull/57>。

Owner 本轮要求：修复应用配置后旧统计残留、补测试、解决与 v0.3.0 的冲突、验证功能共存并评估合并。本轮不提交、推送或合并到 main。

## 基线与工作区

- 配置分支：`codex/saved-processing-config`，原 HEAD `a0306d7bd298cb71e72eedb2c60aaa0b27983608`。
- main / v0.3.0：`60d648628d0c6c66a10e07b5313c66500de2756e`。
- 在配置工作树内执行 `git merge --no-commit --no-ff main`，保留原分支历史；三个冲突均已解决并标记。整合结果尚待提交，不能称工作树干净或 PR 已更新。
- 本轮结束前 `git ls-remote` 确认远端上述两个分支 HEAD 未变化，main 工作树仍干净。

## 缺陷与修复

匹配完成后应用另一份配置时，`_isApplyingProfile` 抑制了属性回调中的 `ResetSuccess()`；原子应用结束后没有补做结果失效处理，导致旧成功统计、完成提示及进度继续显示。现有 `ResetSuccess()` 还遗漏了 `ResultSkippedCount`。

修复仅在完整配置成功应用后调用 `ResetSuccess()`，并补齐未参与行数的清理。校验失败不清理当前设置或结果；不删除旧输出文件，不修改当前输出位置，不自动执行匹配。

新增的 `MatchingProfile_ApplyAfterSuccess_ClearsOldResultAndUsesNewSettingsOnlyOnStart` 先在未修复代码上复现失败：预期 `HasSuccess=false`，实际 `true`。修复后通过；使用真实 CSV 与 Core 执行，覆盖旧结果含未匹配和未参与行、应用后全部统计失效、旧文件不变、显式重新执行得到新结果。

新增 `MatchingProfile_RejectedAfterSuccess_PreservesCurrentSettingsAndResult` 验证缺列导致应用失败时，原条件、成功统计、输出和打开结果状态保留。

## 冲突处理

| 文件 | 处理 |
| --- | --- |
| `docs/requirements.md` | 保留 main 第 15 节“表格对比”；完整追加“保存处理配置”为第 16 节，并同步契约交接中的引用。没有改变需求内容。 |
| `App.xaml.cs` | 同时注入对比服务、配置存储和配置校验器；新增参数采用命名传参。 |
| `MainWindowViewModel.cs` | 保留 main 已有的第五参数 `comparisonService`，后接可选配置服务；保留三个页面及原页面导航。 |

Actual Diff 对照 main：表格对比 Core、ViewModel、View、主窗口导航和既有对比契约未被配置实现覆盖；不新增依赖、不修改匹配或格式统一算法。表格对比继续严格比较，不使用匹配的标准化选项。界面说明补充成功应用配置后的结果失效规则。

## 本轮实际验证

所有样例、构建和测试输出位于 `%TEMP%/TabularStudio-profile-fix-20260910`，没有写入真实用户配置目录。

| 验证 | 结果 |
| --- | --- |
| 修复前新增回归 | 1 FAIL、1 PASS；失败准确复现旧结果残留 |
| 修复后新增回归 | 2/2 PASS |
| 整合后 Release 全套测试 | Core 346 + App 62 = **408/408 PASS**，0 失败、0 跳过 |
| Release 完整解决方案构建 | PASS，0 警告、0 错误 |
| Debug 完整解决方案构建 | PASS，0 警告、0 错误 |
| Debug App 全套测试 | 62/62 PASS，0 失败、0 跳过 |
| 三格式共存集成 | `.xlsx`、`.xls`、`.csv` 各 1 项 PASS；包含于上述 App 测试 |
| 扩展后的 WPF 资源检查 | Release 单独 1/1 PASS；Debug 全套中同样 PASS |
| 原有 Issue33 集成验证 | PASS：混合格式批量、输出重读、CSV 结果送入 XLS 对照匹配、重试、输入哈希保护 |
| 冲突与差异检查 | `git ls-files -u` 为空；无冲突标记；暂存区和工作树的 `git diff --check` 均通过 |

`ProfileComparisonIntegrationTests` 从真实 MainWindowViewModel 组合入口注入全部服务，在三种格式上验证：

- 两个功能可保存同名但不同类型的配置，格式规则和匹配规则各自应用。
- 格式统一按原格式生成输出；匹配标准化可匹配带首尾空格的编号。
- 同时存在匹配标准化配置时，位置对比仍将空格差异识别为 `A2` 不一致；统一格式后的输出再对比为一致。
- 差异报告可以导出并重新读取；页面来回切换保留各自状态。
- 重新创建服务和窗口模型能读取保存项，但不自动应用或执行；输入及配置 JSON 哈希不变。

WPF 检查使用真实模板、绑定和控件，在对应 960×640 / 1280×720 窗口的客户端尺寸下切换三个页面，核对配置列表绑定与对比结果，生成离屏 PNG。已目视检查小尺寸格式统一和数据匹配页面。这是组件验证，**不是桌面人工操作验收**。

证据文件：`tests/integrated-release*.trx`、`tests/coexist-resource*.trx`、`tests/integrated-debug-app*.trx`；图像位于 `build/bin/TabularStudio.App.Tests/release/profiles-*.png`。重现命令在配置工作树执行：

```powershell
dotnet test TabularStudio.sln -c Release --artifacts-path "$env:TEMP\TabularStudio-profile-fix-20260910\build" --logger 'trx;LogFilePrefix=integrated-release' --results-directory "$env:TEMP\TabularStudio-profile-fix-20260910\tests" -v minimal
dotnet build TabularStudio.sln -c Release --artifacts-path "$env:TEMP\TabularStudio-profile-fix-20260910\build" --no-restore -v minimal
dotnet build TabularStudio.sln -c Debug --artifacts-path "$env:TEMP\TabularStudio-profile-fix-20260910\build" --no-restore -v minimal
dotnet test tests/TabularStudio.App.Tests/TabularStudio.App.Tests.csproj -c Debug --artifacts-path "$env:TEMP\TabularStudio-profile-fix-20260910\build" --no-build -v minimal
```

## 合并评估与边界

原来已确认的代码缺陷和三处冲突已解决；本轮检查未发现新的技术合并阻塞，可进入提交和合并审批准备。

目前仍不能直接合并远端 PR：本轮修改尚未提交和推送，远端仍是旧 SHA。后续需提交并更新 PR、核对新 head 的 Actual Diff 和增量审查，再按项目 Gate 2 锁定 exact SHA。正式合并前建议补做集成版桌面验收：旧结果清理、配置复用与三个页面切换。

本轮未执行新的 Grok 角色审查、人工桌面验收、发布打包或无 SDK 干净机器验证。历史 FT01–FT18 的 PASS 仅作为既有记录；旧产物清理后原始 GUI 日志和截图已不在原目录，不能冒充本轮验收证据。本记录不代表 main 已合并、PR 已批准或新版本已发布。
