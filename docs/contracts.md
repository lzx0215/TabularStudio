# UI / Core 契约

- Status: Skeleton
- Owner: **Codex + Antigravity**
- Reviewer for scope: Grok
- Related: `docs/ui-spec.md`、`docs/processing-rules.md`、`docs/architecture.md`

## 文档用途

约定 WPF UI 与 Excel 处理 Core 之间的边界：谁负责触发、谁负责处理、双方交换什么信息。

当前阶段 **不设计具体 C# 接口、类名、方法签名或 DTO 代码**。  
等 UI 规格与处理规则回填后，由 Codex 与 Antigravity 共同落契约，再开始编码。

## 职责划分

| 侧 | 角色 | 允许 | 不允许 |
| --- | --- | --- | --- |
| UI | Antigravity | 选文件、收集已确认输入、展示进度/结果/错误、调用契约 | 解析 Excel、实现格式统一或匹配 |
| Core | Codex | 读 `.xlsx`、按规则处理、写结果、返回进度与错误 | 决定页面布局、增加未确认功能 |

## 待双方回填

未确认前保持 `TBD`。不要提前写 C# 代码块。

### 1. 用例入口

- 格式统一：UI 需要提供什么输入，Core 返回什么
- 数据匹配：UI 需要提供什么输入，Core 返回什么

### 2. 输入约定

- 文件路径
- 用户在界面上确认过的选项
- 不允许 UI 把未确认参数偷偷传给 Core

### 3. 输出约定

- 结果文件路径
- 成功摘要
- 失败信息
- 是否包含统计（仅在已确认时填写）

### 4. 进度与取消

- 是否需要进度（TBD）
- 是否需要取消（TBD）

### 5. 错误约定

- 文件不存在、文件不是 `.xlsx`、文件被占用
- 规则校验失败
- 处理中断

### 6. 契约变更

- 改契约必须同步本文
- PR 必须勾选 Contract Changes
- 单方不得默契改调用方式

## 当前阶段禁止

- 不写 C# interface / class / record
- 不发明未确认的 API 形状
- 不把契约当成新增需求的入口
