# 浅色侧栏导航工作台视觉重构交付 (ui/light-workbench-redesign)

- **角色**：Antigravity（UI Developer）
- **分支**：`ui/light-workbench-redesign`
- **关联 Issue**：Issue #54（视觉工作台布局与控件风格）及 `docs/ui-spec.md` 浅色雾面工作台规格
- **验证基线**：.NET 10.0 / WPF / 完全离线
- **自动化测试状态**：
  - **总计**：408 项测试全部通过（Core 346 + App 62），0 FAIL / 0 SKIP
  - **编译状态**：Release / Debug 构建 0 警告 / 0 错误

---

## 1. 改动背景与设计目标

根据 `docs/ui-spec.md` 第 2.1 节已确认的“浅色雾面工作台”基线规范，将此前过渡的暗色/顶部标签式布局重构为浅色高质感的 Windows 原生桌面工作台：

1. **左侧垂直导航 App Shell**：
   - 移除顶部 Tab，采用左侧轻量固定侧边栏（宽度 200 DIP），包含产品标题、Logo 标头、三项核心功能入口（格式统一、数据匹配、表格对比）及完全离线安全常驻提示。
   - 页面切换时保留各 ViewModel 的独立输入与处理状态（ViewModel 实例不销毁）。
2. **暖灰绿 + 古铜金浅色雾面视觉体系**：
   - 应用背景：`#F5F5F0` / `#F1F3EC` 渐变。
   - 侧边栏：`#ECEFE8`。
   - 工作卡片/主内容：`#FCFCF9`，边框线 `#D5D9D1`。
   - 主文字：`#2D3631`，次要文字 `#626B65`，提示文字 `#7A847D`。
   - 品牌强调色：古铜金 `#826B40`（悬停 `#715B34`），仅用于当前激活导航、主执行按钮、连接图示及选中态，保持克制专业。
   - 语义反馈：成功 `#3F705B`、警告 `#94631C`、危险/错误 `#A84842`。
3. **三功能主视图精细化适配**：
   - **格式统一 (`BatchFormatView`)**：三栏式工作区（文件列表、文件数据预览与设置、处理配置与整批共用规则）；内置响应式折叠（< 900 DIP 宽度下自适应将规则面板下置并保持双栏操作）；有界局部滚动（MaxHeight 96 DIP 错误区）；固定底部执行底栏。
   - **数据匹配 (`DataMatchingView`)**：双表等宽并排预览，支持拖拽加载；条件区域紧凑排列并附带连接图示；右侧合并处理配置与返回字段选择；有界候选/错误提示；固定底部执行底栏。
   - **表格对比 (`TableComparisonView`)**：表一/表二对称选区与 CSV 自动适配说明；居中比较口径提示卡片；差异结果 DataGrid 坐标、类型、值清晰对比；固定底部导出与对比控制。
4. **模态对话框统一换肤**：
   - `SaveProfileDialog`、`ConfirmProfileDialog`、`ExistingOutputDialog` 统一采用新调色板、统一按钮边框与有界滚动提示。

---

## 2. 契约与业务逻辑完整性（无 Core / Contract 破坏）

- **UI / Core 契约未变**：未改动 `src/TabularStudio.Core` 的任何接口、DTO、处理算法与业务规则。
- **命令与数据绑定保持一致**：所有原有 ViewModel 的命令（Browse、Start、Cancel、Export、SaveProfile、ApplyProfile、DeleteProfile、Refresh 等）均完整继承与保留，无删减任何业务能力。
- **离线安全边界严格恪守**：不引入任何网络请求、云端服务或外部组件。

---

## 3. 文件变更清单

| 文件路径 | 变更类型 | 说明 |
| :--- | :---: | :--- |
| `docs/ui-spec.md` | 修改 | 更新视觉规范说明（浅色雾面工作台配色、左侧导航） |
| `docs/handoffs/light-workbench-redesign.md` | 新增 | 本交接文档 |
| `src/TabularStudio.App/Converters/InverseBooleanToVisibilityConverter.cs` | 新增 | 控件显隐取反转换器 |
| `src/TabularStudio.App/Styles/Icons.xaml` | 新增 | 统一矢量图标资源库（导航、操作、锁、状态等） |
| `src/TabularStudio.App/Styles/Theme.xaml` | 修改 | 全套浅色雾面控件样式、调色板、按钮、表格与面板定义 |
| `src/TabularStudio.App/MainWindow.xaml` | 修改 | 落地左侧垂直导航 Shell、页面标题栏与底部全局状态栏 |
| `src/TabularStudio.App/Views/BatchFormatView.xaml` | 修改 | 适配浅色卡片、发丝线分割与执行底栏 |
| `src/TabularStudio.App/Views/BatchFormatView.xaml.cs` | 修改 | 添加响应式双栏/三栏布局适配逻辑 |
| `src/TabularStudio.App/Views/DataMatchingView.xaml` | 修改 | 适配浅色双表预览、紧凑匹配条件与选项卡片 |
| `src/TabularStudio.App/Views/TableComparisonView.xaml` | 修改 | 适配浅色对比工作区与差异结果表 |
| `src/TabularStudio.App/Views/FormatStandardizationView.xaml` | 修改 | 单文件旧页保留浅色兼容 |
| `src/TabularStudio.App/Dialogs/*.xaml` | 修改 | 统一三个对话框的配色与按钮排版 |
| `tests/TabularStudio.App.Tests/*` | 修改 | 调整测试装置以匹配新资源字典与视觉树定位 |

---

## 4. 自动化验证记录

1. `dotnet build --configuration Release`：通过（0 警告，0 错误）。
2. `dotnet test`：
   - `TabularStudio.Core.Tests`：346 PASS，0 FAIL，0 SKIP。
   - `TabularStudio.App.Tests`：62 PASS，0 FAIL，0 SKIP。
   - 包含 WPF 离屏组件渲染验证，生成 944 / 1264 尺寸下各功能页面组件图。

---

## 5. 接收方下一步

1. 提交 PR 并完成 Actual Diff Verification 与 Scope Review。
2. 报送 Project Owner 审查 exact PR head SHA，进行 Gate 2 Merge Approval 确认。
