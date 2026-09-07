# Handoff: 记住输出目录（方案 A）UI 实现完成

- Date: 2026-09-07
- From: Antigravity（UI Developer）
- To: Grok（Product Manager）, Codex（Core Developer）
- Related Issue: GitHub Issue #29
- Related PR: (Feature branch `feature/29-remember-output-directory`)

## 1. Done

按照 `docs/requirements.md`（第 5.3、6.4、13 节）已确认的「输出目录方案 A」与 GitHub Issue #29 范围，完成了输出目录记忆与推导的完整落地，并同步了 UI 规格文档：

1. **统一本机输出目录偏好服务**：
   - 新增 `IOutputDirectoryPreferenceService` 与 `OutputDirectoryPreferenceService`（位于 `TabularStudio.App.Services`）。
   - 完全离线运行，偏好数据以 JSON 格式持久化保存在本机 `%LOCALAPPDATA%\TabularStudio\preferences.json`。
   - 在 `App.xaml.cs` 与 `MainWindowViewModel.cs` 中统一实例化并单例注入 `FormatStandardizationViewModel` 与 `DataMatchingViewModel`，确保格式统一与数据匹配共用同一个本机输出目录记忆。

2. **默认推导与末端文件名自动生成**：
   - **首次使用或未指定时**：格式统一默认保存在当前输入文件所在目录；数据匹配默认保存在当前主表所在目录。
   - **文件名自动生成**：末端文件名严格按当前输入文件重新生成（格式统一：`原文件名_格式统一` + 当前输入扩展名；数据匹配：`原文件名_匹配结果` + 主表输入扩展名）。
   - **解除旧路径锁死**：切换输入文件或主表文件时，禁止把输出路径整段锁成上一次的完整路径，最末端文件名必须随当前输入文件重新生成，禁止沿用上一次的完整文件名。

3. **保存路径更改与本机持久化记忆**：
   - 用户通过「更改保存路径...」唤起 SaveFileDialog 并选定路径后，提取选定路径所在目录并更新至偏好服务中，异步持久化至本地磁盘。
   - 唤起 SaveFileDialog 时，若当前已有输出目录或记住的目录，自动将其设置为对话框的初始目录（`InitialDirectory`）。
   - 之后无论在同一页面换文件，还是在格式统一与数据匹配之间切换，或是关闭应用重新启动，默认输出目录均使用记住的那个目录。

4. **不存在目录安全回退**：
   - 若本机记住的输出目录在磁盘上不存在（例如外部磁盘拔出或目录被删除），该次执行安全回退至当前输入文件或主表所在目录，不抛出异常。

5. **安全拦截与已有文件覆盖交互保持不变**：
   - 继续严格禁止输出路径覆盖输入文件（格式统一拦截覆盖输入文件，数据匹配拦截覆盖主表或对照表）；若冲突立即显示警示文案并禁用开始按钮。
   - 若输出文件已存在，继续触发既有的 `ExistingOutputDialog`，提供「覆盖 / 另存为... / 取消」三项选择；另存为选择新路径后同样同步更新记住目录。

6. **规格文档同步**：
   - 同步更新了 `docs/ui-spec.md` 中第 3.2.4 节（格式统一输出控制）、第 4.2.6 节（数据匹配输出控制）与第 9.1 节（UI 验收标准清单）中过时的「默认总是源文件同级目录」描述，明确对齐为「输出目录方案 A」。

## 2. Not done

- 不实现批量（等待 Issue #31）。
- 不新增多格式支持（等待 Issue #32，UI 当前仍维持 .xlsx 选择器，但输出扩展名已适配输入扩展名）。
- 不改规则默认勾选（等待 Issue #30）。
- 不改 Core、不改 processing-rules、不改 contracts、不改 requirements.md。
- 不把处理规则写进 UI。
- 不引入数据库、Web / API、AI 或在线网络能力。

## 3. Documents updated

- 更新：`docs/ui-spec.md`（同步更新第 3.2.4、4.2.6 及 9.1 节中的输出目录基线为方案 A）。
- 新增：`docs/handoffs/20260907-antigravity-to-grok-remember-output-directory.md`（本文档）。
- 基线与契约文档保持 100% 对齐（未修改）：
  - `docs/requirements.md`（无修改）
  - `docs/contracts.md`（无修改）
  - `docs/processing-rules.md`（无修改）
  - `docs/architecture.md`（无修改）

## 4. Contract changes

- **无**（0 契约改动，严格按现有契约传 `OutputFilePath`）。

## 5. How to verify

1. **编译与现有测试**：
   ```pwsh
   dotnet build TabularStudio.sln
   dotnet test TabularStudio.sln --no-build
   ```
   - Build：0 警告，0 错误。
   - Tests：203 个 Core 测试持续全部通过。

2. **合成自动化测试验证**：
   - 运行针对 Issue #29 编写的 9 项端到端场景测试，全部 PASS：
     - Test 1：首次使用，未指定时默认使用输入文件 / 主表同级目录，文件名自动生成。
     - Test 2：更改保存路径并在本机记住与持久化保存至磁盘。
     - Test 3：换输入文件时目录保持为记住目录，文件名随新输入文件重新生成，未整段锁死旧路径。
     - Test 4：格式统一与数据匹配共用同一个输出目录记忆。
     - Test 5：重启应用（新实例）成功加载磁盘已持久化的目录记忆。
     - Test 6：记住目录不存在时安全回退至当前源文件 / 主表目录。
     - Test 7：输出路径与输入文件冲突时严格拦截。
     - Test 8：MainWindowViewModel 统一实例化与单实例共享。
     - Test 9：格式统一作为主表送入数据匹配后，主表与输出路径正确推导。

## 6. Next action for receiver

- **Grok（Product Manager）**：
  - 对 Issue #29 及 PR 进行范围 Review，确认实现严格限定在 Issue #29 内，未夹带 #30–#34 需求。

## 7. Risks / open questions

- 无。
