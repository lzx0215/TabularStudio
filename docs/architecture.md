# 架构

- Status: Skeleton
- Owner: **Codex**
- Reviewer for scope: Grok
- Related: `docs/requirements.md`、`docs/contracts.md`、`docs/decisions/`

## 文档用途

记录 TabularStudio 的实现结构。编码前由 Codex 填写，并保证结构服务已确认 MVP，而不是预先设计未确认能力。

**Grok 不在本文设计模块或工程拆分。**  
**Antigravity 不在本文决定 Core 内部结构。**

## 已确认技术方向

- 形态：Windows 本地桌面工具
- 语言：C#
- 运行时：.NET 10
- UI：WPF
- Excel：ClosedXML
- 网络：完全离线
- 文件：主要处理 `.xlsx`

## 待 Owner 回填

Codex 在进入工程脚手架之前填写。未确认项保持 `TBD`。

### 1. 逻辑分层

- UI、应用编排、处理规则、Excel 访问如何分开
- 哪些边界对应 `docs/contracts.md`

### 2. 工程结构

- 解决方案与项目如何拆分
- 哪些项目属于 UI，哪些属于 Core
- 测试放在哪里

当前阶段 **不创建** `.sln`、`.csproj`、`src/`、`tests/`。

### 3. 依赖

- 允许：.NET 10、WPF、ClosedXML
- 新增依赖必须先写 ADR
- 默认禁止：Web 框架、数据库、在线 SDK

### 4. 运行与数据

- 无服务端
- 无数据库
- 输入输出均为本地文件

### 5. 非目标架构

- 浏览器应用
- 客户端 + API
- 多用户服务
- 插件市场 / 扩展系统（未确认）

### 6. 决策记录

架构中的重要取舍写入 `docs/decisions/`，本文只引用，不重复展开。
