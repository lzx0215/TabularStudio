# Issue #19：数据匹配 Core 交接

- 日期：2026-09-05
- 来源 Issue：https://github.com/lzx0215/TabularStudio/issues/19
- 来源分支：`feature/19-data-matching-service`
- 来源 PR：本文件所属 PR（标题 `feat(core): implement data matching service`，关联 `Closes #19`）
- 交出角色：Codex（Core Developer）
- 接收角色：Grok（范围 Review）；后续已批准 UI Issue 的 Antigravity
- 状态：Core 实现与自动化验证完成，等待 Review；不合并、不启动 UI

## 已完成

- `DataMatchingService` 实现现有 `IDataMatchingService.ExecuteAsync`，支持两文件和同文件两个 Sheet。
- 字段只按 `ColumnNumber` 定位。Core 重新读取实际表头，返回字段映射保留物理列号、真实表头和实际输出名。
- 按条件顺序构造带类型分量的复合键，以字典索引对照表，固定 1～N 条 AND 精确匹配；不做逐主表行全表搜索。
- `NormalizeComparisonKeys` 控制整组内存比较标准化；不回写任一原始字段。保留大小写和普通内部空格差异。
- 数字转换检查整列反证，复用批准的文本、安全数字和日期 helper。前导零、超过 15 位有效数字的文本保持完整文本比较；额外核对原始十进制表示，防止极小值在 decimal 解析阶段被舍入后产生错误相等。该额外检查仅用于匹配。
- 日期文本只解析四种批准格式，使用 invariant culture。真实 Excel 日期结合单元格日期/时间格式区分日与秒粒度；日期时间忽略秒以下，关闭标准化时保留原始类型和值。字段名不参与类型推断。
- 所选匹配列及返回列的全部数据行先做公式检查；任何公式使任务失败，包含不能命中、空键行中的返回公式。未选主表公式保留，不新增公式计算。
- 对照空键不入索引；唯一匹配才返回；重复不选首尾，即使返回值相同也算重复；未匹配、重复和空键均留空返回值。
- 按主表表头后到最后内容行的物理行顺序处理，保留范围内全空行。四类计数之和等于主表总行数，明确返回零值。
- 返回字段按请求顺序追加；状态列可关闭，启用时追加在最后。命名按原名、`_匹配`、`_匹配2`……依次避让原字段及已追加字段。
- 输出以主表工作簿为基础，不主动复制外部对照 Sheet；保留主表其它 Sheet、原始值、行序、公式及既有样式。
- 拒绝覆盖任一输入；覆盖已有结果需显式授权。结果先写同目录 staging，再提交；已有结果在提交之前保持原样。
- 失败清理 staging；清理失败返回 `IncompleteOutputCleanupFailed` 并附不完整路径，失败结果所有成功数据字段为空。
- Progress 使用 Reading → Preparing → Processing → Writing → Completed，Completed 只在提交成功后报告 100%。
- CancellationToken 在 I/O 和循环边界检查，OperationCanceledException 向上传播；如取消时清理失败，不吞掉取消，在异常 Data 中附 `IncompleteOutputCleanupFailed` 路径。

## 代码与兼容边界

- 新增 `src/TabularStudio.Core/Services/DataMatchingService.cs`。
- 新增内部静态 helper `ApprovedValueNormalization.cs` 和 `WorkbookFileOperations.cs`；从格式统一服务机械抽取已有方法，避免维护重复算法和文件写入逻辑。
- `FormatStandardizationService.cs` 只改为调用上述内部 helper；既有格式统一行为保持不变。
- 新增 `tests/TabularStudio.Tests/DataMatchingServiceTests.cs`。
- Contract changes：none（包括可编译 Contracts 和 `docs/contracts.md`）。
- Processing Baseline changes：none。
- Requirements / UI Baseline / Architecture changes：none。
- App / UI changes：none。未消费 `PendingMasterFilePath`，未修改 Composition Root。
- 未发现需要修改 Contract 或 Processing Baseline 才能实现的冲突。

## 验证

运行环境：Windows，仓库现有 .NET 10 / ClosedXML 依赖。

| 检查 | 结果 |
| --- | --- |
| `dotnet build TabularStudio.sln` | Passed，0 warnings / 0 errors |
| `dotnet test TabularStudio.sln --no-build` | Passed，203 passed / 0 failed / 0 skipped |
| 新增匹配测试 | 128 个执行用例（含 Theory 数据行），全部为运行时 synthetic `.xlsx` |
| 原有测试回归 | 75 个全部通过，包括格式统一、工作簿检查、契约与工程测试 |
| 输入保护 | 成功及失败路径以 SHA256 验证主表、对照表未改变 |
| `git diff --check` | Passed |
| UI 运行 / 手工 UI 验收 | Not Run，本 Issue 无 UI 改动 |

### Issue #19 测试场景追溯

| Issue 场景 | 测试覆盖 |
| --- | --- |
| 1～3：两文件、同文件、单条件 | `MatchesTwoFilesOrTwoSheetsAndPreservesInputs` |
| 4～5：AND、无 OR / 模糊、复合键碰撞 | `CompositeAndKeysDoNotCollideOrMatchPartially` |
| 6～12：标准化开关、数字保护、日期 | `ComparisonRulesAreTypedExactAndInMemory`；两侧整列反证测试；日期粒度与秒以下测试 |
| 13～18：空键、唯一、未匹配、重复 | `AllFourStatusesPreserveBlankPhysicalRowsAndDoNotChooseDuplicates`；空复合分量和空白开关测试 |
| 19～24：返回列顺序、重名、状态列 | `ReturnedFieldsRespectPhysicalIdentityOrderAndNameCollisions`；`StatusColumnCanBeDisabledOrAvoidOriginalAndReturnedNames` |
| 25～26：四状态与零计数 | 所有成功结果统一断言完整 Summary；表头无数据测试 |
| 27～28：公式任务失败 | `FormulaInAnySelectedDataRowFailsEvenWhenRowCannotMatch`，主表键 / 对照键 / 返回字段三侧 |
| 29～31：原字段、行序、输入字节 | 行序、样式、非选中公式保留测试；成功和失败 SHA256 断言 |
| 32～34：输入冲突、已有结果覆盖 | `OutputNeverOverwritesEitherInput`；`ExistingOutputRequiresExplicitOverwrite` |
| 35～37：错误配置与文件错误 | `InvalidRequestsReturnStructuredFailures`；空真实表头、损坏、锁定、稀疏列、列容量测试 |
| 38：Progress | `ProgressUsesApprovedOrderedStagesAndCompleteSummary`；失败无 Completed |
| 39：Cancellation | 预取消、四阶段取消、处理中取消、取消且清理失败 |
| 40：部分输出 | 失败清理、锁定导致清理失败、序列化后的提交锁定、已有结果竞争保护 |

## 未完成、风险与接收方下一步

- 尚未做 Grok 范围 Review、Owner 验收和 PR 合并；不声称已完成这些步骤。
- Grok 检查是否严格限于 Issue #19，并核对原始规则、输入保护与测试追溯。
- 后续 UI 可依赖现有 `IDataMatchingService`、完整 Summary / ReturnedFields / ActualStatusColumnName 及现有错误码；只有收到对应 UI Issue 后才接入。
- 使用 ClosedXML 内存模型，未新增大文件流式架构；没有真实业务数据或性能上限验证。仅对合成 `.xlsx` 做自动化验证。
- 输入文件内容通过 SHA256 验证不变；输出的其它工作簿内容按 ClosedXML 能力尽量保留，不声称整个输出文件与输入字节一致。
- 清理失败的 staging 不是有效结果；需人工处理 Error.Detail 路径。技术取消异常中也保留清理失败路径供宿主诊断。
