# Issue #56 验证记录

Issue：https://github.com/lzx0215/TabularStudio/issues/56。实现基线：`9b608adea1fe6f1a79b14ec7a58dc476605fcaf7`。分支：`codex/saved-processing-config`。Owner 已批准需求、UI 与本功能合成文件桌面自动化；尚未批准提交、推送、合并或发布。

## 当前阶段

本地实现、自动化测试、FT01–FT18 桌面功能验收与实际 Grok 增量复审均已完成。本记录不代表合并或发布验收。仓库外完整日志与合成样例在 `D:\aiproject\TabularStudio\issue56-execution-20260909`。

2026-09-10 继续时核对：远端 `main` 与上述基线 identical；Debug App/Core DLL SHA256 与前次完整测试和桌面验收版本一致；产品源文件与已完成 Grok 首次审查快照一致。没有因中断重新生成或覆盖已完成的结果。

| 验证层 | 已执行结果 | 限制 |
| --- | --- | --- |
| Core 编译 | PASS，0 warning/error | 首次未还原依赖产生 NETSDK1004，正常还原后修复 |
| Core 配置专项 | PASS，57 项 | 不等同于 UI 功能验收 |
| 完整 Core 测试 | PASS，324 项，0失败/跳过；test-results/core-final.trx | 包含57项配置专项，仍不等同于桌面 QA |
| 桌面样例自检 | PASS，43 个输入、63 项 Core 断言，输入 SHA256 43/43 一致 | 不是实际窗口操作 |
| 首次整体验证 | FAIL，6 个 UI 编译错误，已定位并交回 Antigravity 修复 | 不能据此进入桌面验收 |
| 第二次完整构建 | PASS，0 warning/error；build-second.log | 已修复首次6个编译错误 |
| Release 完整构建 | PASS，0 warning/error；build-release-final.log | 包含最终测试装置修复，验证不包含 DEBUG 测试目录分支的构建；不是安装包或发布验收 |
| 第二次完整测试 | Core 324 PASS；App 46 PASS、4 FAIL；test-second.log | 4项新测试未注入必需校验器，已交 Antigravity 修正测试设置；修复前不进入桌面验收 |
| 测试设置修复中间检查 | App 48 PASS、2 FAIL；app-third.log | 格式测试先修复，匹配测试设置尚未写完；这次中间结果不计通过，之后等待完整修复再跑全量 |
| 修复后完整测试 | PASS，Core 324 + App 50 = 374 项，0失败/跳过；test-fourth.log 及 full-fourth*.trx | 修正测试依赖注入；新增确定性旧候选延迟响应测试通过。实际 UI QA 单独执行 |
| 实际桌面 FT01–FT18 | PASS，18/18；gui-report.md / gui-matrix.json | 正常 Debug App 与 FT12/FT16 故障注入 host 分开记录；仅本机96 DPI两种窗口尺寸 |
| 实际 GUI 产物独立只读核验 | PASS，38份报告、27个不同产物、2386项内容断言；GuiOutputCheck/verification-summary.json | 报告包含重复内容检查，不是新增FT数量；35份正常程序报告、3份故障注入host报告 |
| 配置与输入保护 | PASS，15份实际配置JSON字段白名单；43份源SHA256保持，真实用户数据基线保持 | desktop-run/profile-json-scope.json 与 preservation-check.json；所有测试窗口关闭、锁释放，无hold标记遗留 |
| 首次 Grok Actual Diff/Scope Review | CHANGES_REQUIRED，grok-scope-195119/review.json | 未发现范围扩张或契约主路径破裂；要求修正4项App测试、完成桌面FT、同步Handoff。三项已落实，由下方增量复审关闭 |
| Grok 增量复审 | PASS，0 findings；grok-followup-082138/review.json | 实际 CLI exit 0，前次三项问题关闭；4处引用与快照逐字一致，审查期间快照未变。审查者未亲自执行测试/GUI |
| 证据引用审计 | PASS，18项用例全部子项均有证据，无丢失文件；evidence-audit.json | FT06一条重启记录缺当时即时树，已明确关联历史操作日志、后续重启树及事后只读目录清单；不冒称事后清单为历史截图 |

## 多 Agent 交接真实性

- Core：Codex 编写模型、存储、校验；独立 Codex 子代理编写 Core 测试和合成样例，分别记录证据。
- UI：实际 `D:\AntigravityCLI\app\agy.exe` 执行，会签与实现见 `issue56-ui-handoff.md`。第1次未指定工作树读取被拒，第2次已允许读取但终端命令被拒；第3次使用明确工作树与文件工具完成首轮修改。没有开启全局自动批准或更改权限设置。
- 外部 CLI 的 exit 0 不作为任务完成证据。前两次有 denied_actions，不计成功；第三次产生实际文件与会签，但集成编译未通过，必须修复后重验。
- Grok Build 范围审查使用实际 CLI，无工具权限，基于保存的精确文件和 diff 快照；结果需由协调者核对引用与快照哈希。
- 首轮 Grok 调用在读取大材料时达到3轮上限，返回占位内容、exit 1，不计审查通过。已使用同样无工具权限重新运行，增加材料读取轮数；不得把过程中的 PASS 占位符当作结论。
- 第二轮 Grok 完成结构化审查；4处引用均与审查快照原文精确一致（audit.json）。审查期间只变更了测试与交接/验证文档，产品源文件保持该审查快照；后续变更与 QA 结果需增量复审。
- 独立 Codex Core 只读审查未发现新的可复现缺陷；此次审查未重复执行测试，不计为 Grok 范围审查或 UI QA。

## 验收矩阵

批准的步骤和预期见 `saved-processing-config.md`，以下只登记实际桌面状态。

| 用例 | 桌面状态 | 实际证据 |
| --- | --- | --- |
| FT01 格式配置重启复用 | PASS | 已记录 3 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT01 |
| FT02 批量共用规则 | PASS | 已记录 3 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT02 |
| FT03 匹配复用与修改提示 | PASS | 已记录 7 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT03 |
| FT04 功能隔离 | PASS | 已记录 1 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT04 |
| FT05 命名与覆盖 | PASS | 已记录 3 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT05 |
| FT06 删除及取消 | PASS | 已记录 3 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT06 |
| FT07 无效配置阻止 | PASS | 已记录 3 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT07 |
| FT08 引用列变化拒绝 | PASS | 已记录 4 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT08 |
| FT09 重复表头与无关列变化 | PASS | 已记录 2 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT09 |
| FT10 筛选复用与统计 | PASS | 已记录 3 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT10 |
| FT11 筛选缺值、类型冲突、读取失败 | PASS | 已记录 3 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT11 |
| FT12 校验忙碌与过时响应 | PASS | 已记录 2 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT12 |
| FT13 损坏配置拒绝 | PASS | 已记录 5 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT13 |
| FT14 存储失败后恢复 | PASS | 已记录 4 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT14 |
| FT15 来源/输出与覆盖保护 | PASS | 已记录 1 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT15 |
| FT16 处理中禁用 | PASS | 已记录 4 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT16 |
| FT17 窗口布局与键盘 | PASS | 已记录 6 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT17 |
| FT18 原流程回归与配置内容边界 | PASS | 已记录 14 个子项；具体操作、截图与断言见执行目录 gui-matrix.json / FT18 |

## 验证边界

- 普通桌面案例使用本次 Debug App；FT12/FT16 使用加载同一产品程序集、只延迟服务响应的独立故障注入 host。两者均用合成文件与隔离配置目录，故障注入 host 不进入产品或发布包。
- FT12 的真实窗口证明校验期间冲突操作禁用、结束后换输入再应用成功。强制改变内部来源状态及忽略取消的晚到结果由确定性 App 测试单独验证，不冒称通过 GUI 修改了已禁用控件。
- 布局验收范围为本机 96 DPI 下的 960×640 和 1280×720。未修改全局 DPI，不代表其他 DPI 环境已验收。
- 输出比较检查值、原生类型、公式与表结构；不是要求 XLSX ZIP 字节完全相同。匹配样例没有公式，不声称额外完成匹配公式场景。
- 这是未提交工作树上的本地功能验收，尚无 PR head SHA；不等于合并后 main 验证、安装包验收或正式发布。GitHub PR 的 Actual Diff 和 exact SHA 仍需提交后重新核对。
- 结构校验按批准的“物理列号＋原始表头”进行；同名同位置列的数据含义变化无法自动识别。

## 证据入口

执行目录内：`test-fourth.log`、`build-release-final.log`、`gui-matrix.json`、`gui-report.md`、`desktop-run/gui-cases.jsonl`、`GuiOutputCheck/verification-summary.json`、`continuation-baseline.json`。最终截图、每步请求和实际 UIA 结果由 `gui-matrix.json` 逐案例索引；没有把 Core 自检输入生成当作实际桌面输出。

## 协调者最终回执

2026-09-10：实际 Grok Build 已关闭上次三项问题，结论 PASS。本回执在审查后由 Codex 写入；审查后的仓库修改仅为本验证文档记录该结果，不改变已审产品代码、测试、契约或批准范围。精确审查快照、结构化结果与引用校验见 `grok-followup-082138/snapshot.json`、`review.json`、`audit.json`；最终26个变更/新增文件的路径、大小、SHA256见 `final-source-manifest.json`。

尚未执行 commit、push、创建 PR、merge 或 release。下一步需 Owner 单独授权提交/推送/创建关联 Issue #56 的 PR；之后核对 GitHub Actual Diff、CI 与 exact PR head SHA，再进入 Gate 2。当前没有可供合并审批的提交 SHA，不把本地验证标为流程 Done。
