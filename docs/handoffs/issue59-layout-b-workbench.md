# TabularStudio 方案 B 布局重排交接文档 (Issue #59)

- **角色**：Antigravity（UI Developer）
- **分支**：`feature/59-layout-b-workbench`
- **关联 Issue**：[Issue #59: [Feature] 采用方案 B 顶部导航并重排三页工作区](https://github.com/lzx0215/TabularStudio/issues/59)
- **基线**：`5dc36141a5e8652070a38494f5062ab80a597539` (PR #58 合并后最新 `main`)
- **执行方案**：`docs/ui-spec.md` 2026-09-12 方案 B 增量规范及 `layout-b-execution-plan.md`
- **自动化测试状态**：
  - **总计**：409 项测试全部通过（Core 346 + App 63），0 FAIL / 0 SKIP
  - **构建状态**：Release / Debug 构建 0 警告 / 0 错误

---

## 1. 改动背景与设计目标

为释放原 196 DIP 左侧导航占用的横向空间，优先用于表格预览与差异明细展示，根据 Project Owner 批准的方案 B 执行规划，完成全局外壳与三大功能页面的布局重构：

1. **全局顶部导航外壳 (`MainWindow.xaml`)**：
   - 移除左侧 196 DIP 侧边栏列与贯穿竖线，水平空间 100% 赋予主工作区。
   - 实现 48 DIP 顶部导航栏：左侧产品 Logo 与名称 `TabularStudio`，中间三项功能标签（格式统一、数据匹配、表格对比），右侧完全离线安全标识。
   - 保留原有 `CurrentViewViewModel` 路由机制，切换标签时保持各页面已加载数据与配置状态。
   - 底部保留 28 DIP 全局状态条。

2. **顶部导航与工作区折叠样式 (`Theme.xaml`)**：
   - 新增 `TopNavTabStyle` 导航标签样式（选中古铜金高亮边框与微底色，悬停反馈，文字与图标居中对齐）。
   - 新增 `WorkbenchExpanderStyle` 折叠卡片样式（复用既有暖灰绿/古铜金调色板与 Chevron 图标，折叠态完全移除子控件焦点）。

3. **格式统一页面布局重排 (`BatchFormatView.xaml` & `.xaml.cs`)**：
   - 页头（Row 0）：左侧标题与副标题，右侧集成处理配置条（配置下拉、应用、保存配置…、删除及有界状态消息）。
   - 主工作区（Row 2）：
     - 宽屏（W ≥ 1000 DIP）：文件列表（220 DIP）、文件预览（*）、整批共用规则（260 DIP）三栏排布。
     - 窄屏（W < 1000 DIP）：自适应切换为双栏，文件列表（188 DIP），右侧上方预览、下方规则（3:2 比例），规则面板内部滚动。
   - 错误区与固定底栏保持独立行，不随工作区滚动。

4. **数据匹配页面布局重排 (`DataMatchingView.xaml` & `.xaml.cs`)**：
   - 页头（Row 0）：左侧标题与副标题，右侧集成处理配置条。
   - 左侧（Column 0）：主表（上）与对照表（下）上下垂直排列，各占预览区约一半高度；各自包含文件选择、Sheet、表头行微调及前 20 行只读预览 DataGrid。
   - 分割线（Column 1）：发丝线分割。
   - 右侧设置列（Column 2, x:Name="SettingsColumn"）：独立纵向滚动面板（W ≥ 1040 DIP 占 340 DIP，W < 1040 DIP 占 300 DIP）：
     - 匹配条件列表：每条条件按分行显示（序号/删除 -> 主表字段 -> 等于对照表字段），底部紧接“＋ 添加匹配条件”。
     - 带回字段列表：搜索框、全选、反选、已选计数及限高（140 DIP）局部滚动列表。
     - 可折叠区域：“主表筛选”与“比较与状态列”默认折叠并展示真实摘要；筛选启用、候选读取出错或配置应用涉及筛选时自动展开；折叠/展开只改变可见性，不改变选项值。
   - 固定底栏与按需错误区保留在最下方。

5. **表格对比页面全宽工作区 (`TableComparisonView.xaml`)**：
   - 页头（Row 0）：左侧标题与副标题。
   - 顶部对称表一/表二文件与 Sheet 选择。
   - 比较口径提示卡片与差异结论摘要。
   - 全宽差异明细 DataGrid 占满工作区宽度（MinHeight 80 DIP），支持横纵局部平滑滚动。
   - 底部固定取消、导出与开始操作栏。

6. **ViewModel 纯显示属性增量 (`DataMatchingViewModel.cs`)**：
   - `FilterSummary`：根据筛选启用状态、所选列名与筛选值动态派生摘要（“未启用”、“待选择”或“列名 = 实际值”）。
   - `OptionsSummary`：反映当前差异自动修复与状态列配置状态。
   - `IsFilterExpanded`：筛选开启或候选加载失败时自动展开。
   - `IsOptionsExpanded`：比较与状态列折叠状态控制。

---

## 2. 契约与业务逻辑完整性（无 Core / Contract 破坏）

- **UI / Core 契约未变**：未改动 `src/TabularStudio.Core` 的任何接口、DTO、处理算法与业务规则。
- **命令与数据绑定完全保留**：所有原有 ViewModel 的命令（BrowseFiles、BrowseMaster、BrowseReference、Start、Cancel、Export、SaveProfile、ApplyProfile、DeleteProfile、RefreshPreview 等）及控件 `x:Name` 均完整保留，无删减任何业务能力。
- **配置与输出行为保持一致**：配置原子校验、保存/覆盖/删除、输出目录记忆规则及同名覆盖保护逻辑完全沿用。
- **完全离线**：不引入任何网络、云服务或外部依赖。

---

## 3. 文件变更清单

| 文件路径 | 变更类型 | 说明 |
| :--- | :---: | :--- |
| `docs/ui-spec.md` | 修改 | 添加 2026-09-12 方案 B 增量规范（顶部导航、各页工作区重排） |
| `docs/handoffs/issue59-layout-b-workbench.md` | 新增 | 本交接文档 |
| `src/TabularStudio.App/Styles/Theme.xaml` | 修改 | 添加 `TopNavTabStyle` 与 `WorkbenchExpanderStyle` 样式定义 |
| `src/TabularStudio.App/MainWindow.xaml` | 修改 | 落地 48 DIP 顶部导航 Shell，移除 196 DIP 侧边栏列 |
| `src/TabularStudio.App/ViewModels/DataMatchingViewModel.cs` | 修改 | 增加折叠摘要与展开控制纯显示属性 |
| `src/TabularStudio.App/Views/BatchFormatView.xaml` | 修改 | 页头配置条、三栏/双栏自适应工作区 |
| `src/TabularStudio.App/Views/BatchFormatView.xaml.cs` | 修改 | 断点更新为 1000 DIP，适配宽/窄模式行列尺寸与发丝线 |
| `src/TabularStudio.App/Views/DataMatchingView.xaml` | 修改 | 页头配置条、上下双表排布、右侧分行条件与折叠设置列 |
| `src/TabularStudio.App/Views/DataMatchingView.xaml.cs` | 修改 | 添加 1040 DIP 断点下右设置列尺寸自适应 |
| `src/TabularStudio.App/Views/TableComparisonView.xaml` | 修改 | 全宽差异表、对称文件选择与紧凑边距适配 |
| `tests/TabularStudio.App.Tests/BatchViewResourceTests.cs` | 修改 | 适配上下双表坐标断言，添加方案 B 样式与断点验证 |
| `tests/TabularStudio.App.Tests/SchemeBLayoutTests.cs` | 新增 | 验证数据匹配折叠摘要派生与展开状态机行为 |

---

## 4. 验证方式与测试报告

### 4.1 自动化测试

```powershell
dotnet build TabularStudio.sln -c Release
dotnet test TabularStudio.sln -c Release --no-build
```

- 构建结果：0 警告，0 错误。
- 测试结果：
  - `TabularStudio.Core.Tests`: 346 通过，0 失败，0 跳过。
  - `TabularStudio.App.Tests`: 63 通过，0 失败，0 跳过。
  - 总计：409 项测试全部通过。

### 4.2 质量红线与验收标准自检 (AC01–AC12)

- [x] **AC01**：三个顶部导航标签完整，默认进入格式统一，切换后保留页面状态。
- [x] **AC02**：1280×720 和 960×640 DIP 下三页控件无重叠，底栏执行入口始终可达，无整页滚动。
- [x] **AC03**：数据匹配主表在上、对照表在下、配置在右；同文件不同 Sheet 交互完整。
- [x] **AC04**：格式统一在 1000 DIP 前后平滑切换三栏与双栏，文件与规则设置完整保留。
- [x] **AC05**：表格对比全宽差异表展示完整，导出与取消操作可达。
- [x] **AC06**：两页配置选择/应用/保存/删除及状态提示功能完好。
- [x] **AC07**：多条件、带回字段搜索/全选/反选、筛选、标准化、状态列设置完好，折叠不影响数据值。
- [x] **AC08**：局部滚动不遮挡固定底栏。
- [x] **AC09**：处理中、完成、失败、取消沿用既有按钮状态与禁用保护。
- [x] **AC10**：三格式、格式统一批量、结果送匹配、保存配置回归通过。
- [x] **AC11**：无越出批准范围之修改，无破坏契约或 Core 逻辑。
- [x] **AC12**：实际 diff 与 Issue #59 及方案 B 执行方案完全一致。

---

## 5. 下一步建议

1. 提交代码并推送分支 `feature/59-layout-b-workbench`。
2. 发起 Pull Request 并关联 Issue #59。
3. 记录 exact PR head SHA，提交 Project Owner 进行 **Gate 2 Merge Approval** 审批。
