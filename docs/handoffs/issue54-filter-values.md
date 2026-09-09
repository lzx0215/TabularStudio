# Issue #54 / PR #55 主表实际值下拉修复与验证

日期：2026-09-08。来源：Owner 对筛选使用主表列实际值下拉的反馈，以及对最小 Core/Contract 扩展确认稿的“确认”。沿用当前分支/PR；不新增 Issue/PR、不合并、不替换 v0.2.0 发布包。

## 完成内容与范围

- 两页进度条已从3加粗至8 DIP（本PR之前的2798060提交）。
- 筛选值改为不可编辑下拉，无默认“是”；候选来自完整主表列，不限预览20行，保留类型及原始精度。
- 新增只读候选服务和带类型筛选值；旧文本契约仍可使用。输入读取与匹配继续共用既有格式适配、类型与规范化规则。
- 文件/Sheet/Header/列变化及关闭筛选清空旧选择；取消+代次检查防止旧返回覆盖，失败/无值可见且不能使用旧值执行。
- 同步 requirements、contracts、processing-rules、ui-spec。这是Owner已批准的局部范围扩展，不再声称当前PR仅有XAML变化。
- 不改变AND、重复键、状态统计、原文件保护、输出覆盖许可、匹配批量或发布流程。

## 验证结果

最终 Release Build：PASS，0 warnings / 0 errors。Core：267 PASS，0 FAIL，0 SKIP；App：31 PASS，0 FAIL，0 SKIP，共298。首轮构建发现新增测试使用错误的XLError枚举名称，修正为项目现有NoValueAvailable后重新构建与测试，最终结果如上。

| 验收项 | 真实执行与证据 | 状态 |
| --- | --- | --- |
| FV-01 完整列与去重 | ColumnValuesTests：3格式Header=2，第25条数据独有值被选入，预览仍只有20行；输出仅选定行带回结果、输入SHA256不变 | 自动化 PASS |
| FV-02 原值类型 | 文本前导零/长数字、文本数字与数值、布尔、日期、时长、Excel错误；开关规范化均实际执行并重新打开结果；xls日期/布尔另测 | 自动化 PASS |
| FV-03 来源切换与乱序 | FilterValueViewModelTests：文件/Sheet/Header、列切换，旧读取忽略取消仍不能覆盖；加载/未选择/外来选项不能开始 | 自动化 PASS |
| FV-04 失败与恢复 | xlsx/xls第20行以外公式整体拒绝且有真实坐标；文件锁定/丢失、无值、非法Header/列、预取消；VM错误展示与恢复 | 自动化 PASS |
| FV-05 真实控件与输出 | STA WPF ComboBox的Items/SelectedItem绑定实际选值；VM→Core运行CSV，6行统计2成功/1未匹配/1重复/1空键/1未参与；3格式共9种配对回归 | 组件/自动化 PASS |
| FV-06 原行为回归 | 全量旧文本筛选、无筛选、输出锁定/覆盖保护及格式统一测试 | 自动化 PASS |
| FV-07 桌面下拉鼠标/键盘、滚动、原生对话框与DPI | 未启用桌面GUI自动化；本轮未实际操作桌面 | NOT RUN |

原 AC01～AC13 的视觉验收映射继续使用 issue54-rework-acceptance.md；本轮WPF组件验证覆盖三种尺寸底栏可见与多错误不挤出底栏，不能证明完整Desktop验收。原 FT-01～FT-08 **全部仍为 NOT RUN**；不把辅助自动化汇总为整条FT通过。Desktop Manual QA：PASS 0 / FAIL 0 / NOT RUN 8。新版截图为实际WPF组件渲染，**不是原生桌面完整窗口截图**。本轮原生完整窗口新证据：NOT RUN。

## 本地证据与复现

- 构建：`dotnet build TabularStudio.sln -c Release --artifacts-path artifacts/filter-values/build`。
- 测试：`dotnet test TabularStudio.sln -c Release --artifacts-path artifacts/filter-values/build --no-build --logger trx`。
- `artifacts/filter-values/build.log`、`test-final.log`、`test-results/filter-values-final_*.trx`。
- `TABULAR_UI_RENDER_DIR=artifacts/filter-values/renders`：组件测试生成两页WPF图、三个尺寸布局图及真实CSV输入输出fixture。
- `artifacts/filter-values/renders/shell-component-matching.png`、`shell-component-format.png`。
- 证据基于本提交产品源码；本地后续报告记录exact commit和二进制hash。未将输入夹具/构建产物提交源码仓库。

## Actual Diff / Scope / Readiness

实际差异包含已批准视觉返工、进度条加粗，以及本次候选服务、带类型筛选契约、UI绑定、测试和文档。无依赖/工程结构/在线能力变化，未新增产品入口。旧视觉阶段报告不代表当前全量差异。Core与UI调用由Codex按授权完成自检，未声称有独立Grok或Antigravity签字。

Implementation / Automated Verification：PASS。Actual Diff Verification / Scope Review：按已批准增量自检，通过；PR更新后需以GitHub实际head及Files changed复核。Pre-Merge Readiness：自动化通过，完整桌面QA待执行；**不宣称可直接合并或Issue Done**。Owner Gate 2尚未授予新exact head批准，旧SHA一律不适用。

接收方下一步：按原FT-01～08及FV-07实际操作新版WPF，保留完整窗口证据；所有必测项完成且Owner锁定新的exact PR head后才可合并。发布包继续独立验收，不自动更新已发布第二版。

## 2026-09-09 验证工具兼容修复（Owner 已授权）

发布就绪检查在 `042e7bbae24f5e2687041524f807e06f331198eb` 发现 `Issue33.FunctionalQA/Program.cs:44` 编译错误 CS1061：仍引用已移除的 `MasterFilterValue`。该工具未包含在 solution 中，因此此前 solution 构建及298项测试没有覆盖其编译。

- 沿用 #54 / PR #55 的验证修复范围；工具改为等待 `MasterFilterValuesLoadTask`，从实际候选中按 Text 类型及原值 `001` 选择 `SelectedMasterFilterValue`。
- 验证候选加载结束、未选择时禁止执行、显式选择后可以执行；匹配和批量重试后再次核对全部输入 SHA256。
- 将现有 `Issue33.FunctionalQA` 项目加入 `TabularStudio.sln` 的 tools 分组，后续 solution 构建会检查该调用方。构建工具不等于自动执行功能场景，仍须单独 `dotnet run`。
- 本轮 Release solution build：PASS，0 warnings / 0 errors，包含 Issue33.FunctionalQA；Core 267 + App 31 全部 PASS，0 FAIL / 0 SKIP。
- 修复后的工具在两个全新目录中分别运行，均 PASS：三格式批量部分失败后继续、输出重新打开、选中 CSV 结果发送到匹配、与 XLS 按实际候选条件匹配、CSV 输出、批量重试及输入哈希保护。
- 证据：本工作树 `artifacts/issue54-tool-fix/` 下的 build.log、test.log、test-results、functional-run-1.log、functional-run-2.log 及 run-1/run-2 合成样例。
- 本轮测试对象为上述 exact commit 加本节工具修复；最终提交 SHA 及提交后的复核结果以 PR #55 记录为准。本次修复未修改 src、Contract、产品需求或发布包。原桌面 FT-01～08、FV-07 仍为 NOT RUN，不能把工具恢复视作桌面QA、Gate 2/3或正式发布验收通过。
