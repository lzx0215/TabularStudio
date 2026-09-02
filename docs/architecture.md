# 架构

- Status: Initial engineering skeleton（Issue #1）
- Owner: **Codex**
- Reviewer for scope: Grok
- Related: `docs/requirements.md`、`docs/contracts.md`、`docs/decisions/`

## 文档用途

记录 TabularStudio 的实现结构，并保证结构只服务已确认 MVP。本次仅建立可编译、可测试的工程骨架，不实现格式统一、数据匹配、Excel 业务逻辑或最终 UI。

## 已确认技术方向

- 形态：Windows 本地桌面工具
- 语言：C#
- 运行时：.NET 10
- UI：WPF
- Excel：ClosedXML
- 网络：完全离线
- 文件：MVP 只处理 `.xlsx`

## 工程结构

```text
TabularStudio.sln
├─ src/
│  ├─ TabularStudio.App/       WPF 桌面应用入口
│  └─ TabularStudio.Core/      Excel 处理与业务规则的 Core 边界
└─ tests/
   └─ TabularStudio.Tests/     Core 自动化测试
```

| 项目 | Target Framework | 职责 | 项目引用 |
| --- | --- | --- | --- |
| `TabularStudio.App` | `net10.0-windows` | WPF 应用入口；后续承载 UI 与应用编排 | `TabularStudio.Core` |
| `TabularStudio.Core` | `net10.0` | 后续承载处理规则与 Excel 访问；不依赖 WPF | 无 |
| `TabularStudio.Tests` | `net10.0` | Core 的 xUnit 自动化测试 | `TabularStudio.Core` |

依赖方向固定为：

```text
TabularStudio.App ──────> TabularStudio.Core
TabularStudio.Tests ────> TabularStudio.Core
```

Core 不反向引用 App。UI 与 Core 的具体调用契约在后续对应 Issue 中维护到 `docs/contracts.md`，Issue #1 不预先定义业务接口。

## 技术依赖

| 依赖 | 所属项目 | 用途 |
| --- | --- | --- |
| WPF | `TabularStudio.App` | Windows 桌面应用框架 |
| CommunityToolkit.Mvvm | `TabularStudio.App` | 后续支持 MVVM；Issue #1 仅配置依赖 |
| ClosedXML | `TabularStudio.Core` | 后续处理 `.xlsx`；Issue #1 不实现 Excel 逻辑 |
| xUnit | `TabularStudio.Tests` | 自动化测试框架 |

`global.json` 将 SDK 基线设为 .NET SDK `10.0.100`，并允许在 .NET 10 的更新 feature band 上构建。仓库不引入 Web 框架、数据库或在线 SDK。

## 运行与数据边界

- 应用在 Windows 本地运行。
- 无服务端、无数据库、无远程 API。
- 输入输出均为本地文件。
- App 负责桌面交互与调用编排；Core 负责处理行为，避免把处理规则写入 UI。

## Issue #1 明确不实现

- 格式统一与数据匹配算法
- Excel 读写业务逻辑
- 最终页面、交互和 ViewModel
- UI/Core 业务契约
- `.xlsx` 之外的文件格式

## 决策记录

Issue #1 仅落实需求和 Issue 已确认的技术栈及最小项目拆分，没有产生需要长期单独记录的重要技术取舍，因此不新增 ADR。后续若出现影响范围、依赖方向或可替换性的重大决策，再记录到 `docs/decisions/`。
