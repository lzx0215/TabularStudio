# UI 规格

- Status: Skeleton
- Owner: **Antigravity**
- Approver: Project Owner
- Reviewer for scope: Grok
- Related: `docs/requirements.md`、`docs/contracts.md`

## 文档用途

记录已确认 UI 页面方案，作为 WPF 实现的唯一界面规格。

Project Owner 已确认 UI 页面方案。本文由 Antigravity 回填已确认内容。

**Grok 不在本文设计 UI 细节。**  
**Codex 不在本文改页面结构。**

## 约束

- 桌面：Windows
- UI 技术：WPF
- 离线：无登录、无在线依赖
- 功能入口只服务 MVP 两个功能：格式统一、数据匹配
- 不得增加未确认页面、导航或能力

## 待 Owner 回填

Antigravity 回填时只整理已确认方案，不新增需求。建议按下列标题写，未确认的标题保持空白并标明 `TBD`。

### 1. 页面清单

- 待回填已确认页面名称与用途。

### 2. 信息架构与导航

- 待回填页面之间如何进入、返回、切换。

### 3. 格式统一页面

- 待回填布局、必要控件、文件选择方式、操作步骤、结果展示。
- 不在此定义 Excel 处理规则。

### 4. 数据匹配页面

- 待回填布局、必要控件、文件选择方式、操作步骤、结果展示。
- 不在此定义匹配算法。

### 5. 状态与反馈

- 待回填空态、进行中、成功、失败等已确认反馈。

### 6. 非目标界面

- 待回填明确不做的页面或控件。

### 7. UI 验收

- 待回填可勾选的界面验收项。

## 与契约的关系

页面可以触发 Core 能力，但调用形状写在 `docs/contracts.md`。  
Antigravity 回填 UI 规格时，如发现需要新的 Core 能力，先开 Issue 并由 Grok 判断是否属于已确认需求，不得直接让 Codex 加功能。
