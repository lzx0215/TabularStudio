# Issue #54 / PR #55 视觉返工交付

> 2026-09-08 后续状态：本文件保留视觉返工的原始计划/历史证据。Owner 随后批准完整主表列实际值下拉的最小 Core/Contract 扩展，最新范围及验证见 [issue54-filter-values.md](issue54-filter-values.md)。此处“无 Core/Contract 变更”等描述仅适用于原视觉阶段，不代表当前 PR 全量差异。

Owner已批准确认包第3–11节及AC01–13/FT01–08；错误详情直接展开显示且内部滚动。此轮停在Gate 2，不合并。新exact head见PR与本地 artifacts/issue54-rework/gate2-report.md，不再沿用94d1f5d。

## 实现与范围检查

- 固定底栏为工作区的兄弟Grid行，两页不再有整个页面的MinWidth=1010/外层ScrollViewer。
- 格式三栏、文件计数、合并预览标题、独立配置与逐文件输出；匹配双表紧凑输入、条件后紧接添加、右侧配置合并；各区域内部滚动。
- 错误区MaxHeight=96，完整只读文本可选取复制，不用Expander或新菜单。开始与结果操作始终独立于错误区。
- UI-only OutputDirectoryDisplay从每文件输出路径派生并发送属性通知，混合目录明确显示“多个位置”；未修改路径、Core调用或处理逻辑。
- Core、Contract、processing-rules、其它ViewModel、依赖均无改动。现有命令入口保留（逐行预览按钮整合为行选择/标题选择器）。不将隐藏图示功能删除。
- MainWindow保留原生窗口；固定全局附属状态行20 DIP，导航44 DIP；不新增Logo/主题/框架。

## Build / Automated Verification

Release build 0 warnings / 0 errors。最终完整测试250 Core + 26 App，全部通过，无skip。
新增目录显示通知测试覆盖添加、逐项改目录、替换、整批目录、移除，验证不改其它输出路径。
WPF组件测试覆盖预览值/列头切换、三个页面尺寸、开始按钮边界、20条1000字错误不移动底栏；实际VM→Core的六行CSV双字段筛选匹配得到2/1/1/1/1且总数6。
初次测试发现只读FilePath的TextBox默认TwoWay绑定错误，已显式OneWay并重跑通过。无掩盖失败。首次脚本遇XML注释节点异常亦已修正，未发布部分生成结果。

完整客户区WPF组件图在 renders/shell-component-*.png，仅证明真实组件布局，未显示原生窗口，不能替代Desktop Manual QA。实际EXE窗口截图及运行路径、hash/DPI单独记录于本地交付目录。

## AC coverage（实现/自动化证据与人工验收分开）

| AC | 本轮覆盖 | 尚缺证据 |
| --- | --- | --- |
| 01 | 固定骨架、紧凑导航、基准组件图 | 两页基准实际桌面空/加载全流程 |
| 02 | 多失败时footer位置不变、开始边界断言通过 | 桌面滚动全流程 |
| 03 | 三栏/计数、逐项预览组件测试 | 四份混合格式人工验证 |
| 04 | 沿用#52配置与命令，既有回归通过 | Sheet/Header/替换对话框人工验证 |
| 05 | 零规则既有自动化通过，默认值不改 | 桌面0→1→0与输出检查 |
| 06 | 完整错误绑定、20条长错误fixture布局通过 | 实际锁文件部分失败/重试桌面流程 |
| 07 | 双表等宽顶齐、两条件紧凑、添加紧随列表 | 大集合及双表桌面操作 |
| 08 | 输入/输出/字段/跨页命令入口静态自检，VM回归 | 键盘、原生对话框及跨页完整人工验证 |
| 09 | 实际VM→Core输出六行2/1/1/1/1、既有Core回归 | 筛选关/状态列关/覆盖等完整FT05/08 |
| 10 | 有界区域实现，20条长错误组件检查 | 100文件/20条件/100返回字段FT06全量 |
| 11 | 三种页面区域尺寸布局与footer边界通过 | 系统125%/150%及实际最小窗口人工验证 |
| 12 | 紧凑模板、按下/禁用/焦点反馈 | 实际键盘/鼠标与原生弹窗状态人工检查 |
| 13 | 新代码测试及组件证据隔离，旧图不归新head | 桌面各FT完整证据头仍未齐全 |

## FT与Desktop Manual QA真实状态

| FT | 总体状态 | 已执行的辅助验证 |
| --- | --- | --- |
| FT-01 | NOT RUN | WPF组件两页布局/完整客户区渲染 |
| FT-02 | NOT RUN | 两CSV预览组件、既有追加/替换VM测试 |
| FT-03 | NOT RUN | 既有零规则命令回归 |
| FT-04 | NOT RUN | 20条长错误fixture有界显示、底栏位置断言 |
| FT-05 | NOT RUN | S3等价CSV数据VM→Core实际运行并核对六项统计 |
| FT-06 | NOT RUN | 有界滚动结构自检；未执行全部大集合操作 |
| FT-07 | NOT RUN | 三种组件页面尺寸；不是实际系统DPI验证 |
| FT-08 | NOT RUN | 命令映射静态检查及既有VM回归；未模拟桌面输入 |

Desktop Manual QA：PASS 0 / FAIL 0 / NOT RUN 8。并非称UI无失败，只是未进行人工桌面用例；不启用禁止的GUI自动化。启动截图不升级为FT PASS。

## Actual Diff / Scope Review / Pre-Merge Readiness

实际变更仅MainWindow、Theme、两页XAML、一个UI-only属性、两份App测试、ui-spec与handoff/AC测试计划；相对main的exact文件清单随PR发布。没有另建Issue/PR。
源代码/构建/自动化验证可提交review；完整Functional QA尚缺，不能宣称视觉验收或Issue Done。Gate 2未批准，不合并；建议先补齐人工QA再决定合并，旧SHA不作为本轮审批对象。
