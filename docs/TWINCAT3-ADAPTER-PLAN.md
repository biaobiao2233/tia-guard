# TwinCAT 3：AI 工程能力与复用计划

核查日期：2026-10-12。状态：**研究和任务派发；尚无本项目 TwinCAT 工程闭环验收。**

本方向唯一 Handoff / 任务入口为 [Issue #53](https://github.com/biaobiao2233/tia-guard/issues/53)。博图验证在另一对话推进，#9/#41/#43/#52 不由本方向接管。实现边界见 [TwinCAT engineering bridge v1](contracts/twincat3-engineering-bridge-v1.md)。

## 结论与接入选择

TwinCAT 3 的官方工程接口已经支持从零创建工程。AI 可以自己编辑 TwinCAT 文本/XML 源文件，但可靠的工程操作还需要环境/模板/库管理、厂家编译、诊断和独立回读。没有必要先复制博图的二进制双向翻译或 Git 管理。

建议首轮复用已有代码，做本地执行器加 MCP；HTTP Gateway 以后确有其他客户端需要时再增加。MCP、HTTP 或一个薄 CLI 都可以调用同一执行器，不应形成三套不同的工程逻辑。

首轮目标是：

`空目录 → Solution / TwinCAT / PLC → MAIN + FB + GVL + DUT + Task → 编译报错 → 修复 → 编译完成 → 关闭重开回读 → 交付新工程`

接口有某能力、移植代码有某方法、该方法在本机运行成功和整个流程验收通过是四个不同结论。本文不把官方能力或上游 README 当作本项目验收。

## 官方能力上限

| 能力 | 官方入口 | 判断与边界 |
| --- | --- | --- |
| 从零 Solution / TwinCAT 工程 | DTE `Solution.Create` / `AddFromTemplate` | 明确支持，不必复制用户旧业务工程。需要匹配本机模板和 XAE 环境。 |
| 从零 PLC 工程 | `TIPC.CreateChild`，Empty/Standard PLC Template | 明确支持；与仅打开现有 `.sln` 不同。 |
| PLC 对象与代码 | Program / Function / FB / Method / Property / Interface / GVL / DUT；DeclarationText / ImplementationText | 首轮选择 ST；能新增、读写和回读，文件与 API 均可使用。 |
| Task 与库引用 | PLC Task、系统 Task、TaskReference、LibraryManager | 可设置周期/调用关系和明确库版本；首轮需要检查 MAIN 真正进入周期 Task。 |
| EtherCAT 与 I/O 组态 | TIID、ESI、ProduceXml / ConsumeXml、LinkVariables | API 支持离线配置；真实设备仍需要正确拓扑、NIC、ESI 和地址。 |
| 构建和诊断 | DTE SolutionBuild、Build Output、Error List | 可实现编译报错后的 AI 修复循环；未开始或未完成构建不能视为成功。 |
| 激活、PLC Login/Start/Stop、Boot Project | ActivateConfiguration、StartRestartTwinCAT、ITcPlcOnline 等 | 接口存在，但与离线编辑不同，受目标身份、权限和许可证约束；本轮不执行。 |
| 变量、通知、RPC 与 Runtime 状态 | ADS | 通信能力，不是 XAE 工程创建或编译能力。 |

官方来源：[创建配置](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/242721803.html)、[创建 PLC](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842439947.html)、[对象类型](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842468363.html)、[读写代码](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842480395.html)、[Task](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/242740107.html)、[库](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21841119243.html)、[编译](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842451979.html)、[诊断](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842703115.html)、[在线接口](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21841270795.html)、[ADS](https://infosys.beckhoff.com/content/1033/tcadsnetref/7313399947.html)。

### 新版无 VS 路线、官方 CLI 与官方 AI

- [Standalone System Manager](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842560779.html) 从 Build 4026 提供（[官方手册 v1.7](https://download.beckhoff.com/download/document/automation/twincat3/TC3_Automation_Interface_EN.pdf)，印刷第 297 页）。`TcSysManagerRM → CreateSysManager15 → OpenConfiguration` 不需要 Visual Studio 实例；`ITcSysManager16.BuildTargetPlatform` 是[同步构建接口](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/21842566923.html?id=2442775766648514350)。仍需 TwinCAT 工程组件、STA 和匹配接口。服务下 LocalSystem 的注册条件不能理解为所有 Agent 都应提升为 LocalSystem。Standalone 的从零创建、PLC 操作及完整诊断覆盖需要分别验证，不能由 COM 注册推出全部可用。
- [TwinCAT PLC++](https://www.beckhoff.com/en-en/products/automation/twincat-plc/) 官方已经介绍独立命令行编译器，不能再笼统说没有官方 CLI。本次未核实具体命令、可安装包和本机版本；它也不是传统 XAE 所有工程/硬件/IDE 功能的统一 CLI。当前基线仍以 COM/DTE 为兼容路线，Doctor 发现新能力后再选择。
- [CoAgent 官方说明](https://www.beckhoff.com/en-us/company/press/open-model-independent-ai-assistance-for-automation-programming-and-controls-engineering-2026-06.html) 采用 MCP Client + 专用 Servers。核查时 [TE1700](https://www.beckhoff.com/en-en/products/automation/twincat/texxxx-twincat-3-engineering/te1700.html) 仍标注产品公告/交付时间询价；没有核实到能让外部 Agent 直接安装使用的公开 endpoint、认证与配置。它是官方方向，暂不能当作当前可直接部署的桥。

### 困难和无法直接获得的能力

- 图形对象可以创建/处理 XML，但一般 LD/FBD/CFC/SFC 的连接、布局与语义，比 ST 难得多；不是绝对不能做，也不是写任意 XML 即可完整编辑。
- 本次未找到 Automation Interface 全覆盖 IDE 断点、单步、调用栈的通用接口。先用编译诊断、回读和后续运行断言；不要宣称能自动完成 GUI 的一切。
- 受保护源码、对象权限和商业许可不会被 MCP 解锁。无源码的库也不能靠更高 Windows 权限恢复。
- 无硬件/合适运行环境，无法证明 EtherCAT、轴和实际机械行为。编译或源码回读一致，不等于扫描周期或实时性能正确。
- 普通试用许可证包含人工安全码；禁止自动绕过。[Restricted Runtime](https://infosys.beckhoff.com/content/1033/tc399x_tc3_restricted_runtime/20433049099.html?id=890694769559420132) 和 [Usermode Runtime](https://infosys.beckhoff.com/content/1033/tc170x_tc3_usermode_runtime/11319883275.html) 可以作为以后专用测试环境的研究方向，需按实际许可证/模式验证。

## 复用清单与实际审计

| 固定上游 | 许可 / 可复用内容 | 本轮选择 |
| --- | --- | --- |
| [Beckhoff/TC_AI_DOTNET_Samples@48fd388](https://github.com/Beckhoff/TC_AI_DOTNET_Samples/tree/48fd388784b1b58d89e5f786ae5f3cd9ff4227ca) | 0BSD；真正创建 Solution、TwinCAT/PLC、程序对象和 Task 的官方样例 | 优先用作 API 调用依据。旧示例中的 Activate/Restart 不属于本轮可执行动作，不整段运行。 |
| [Edge-JB/TwinCAT-XAE-MCP@30d8e293](https://github.com/Edge-JB/TwinCAT-XAE-MCP/tree/30d8e2938eeed1c41ee24765b469d13430ab0a32) | MIT；Node MCP、C# STA worker、POU/PLC 编辑、Task、诊断 | 挑选离线 handler、STA/retry、MCP 工具/客户端代码并保留版权。不是可直接信任的完整从零成品。 |
| [rassrollers/TcAutomationInterface@94826e4](https://github.com/rassrollers/TcAutomationInterface/tree/94826e4f9e456849ca4adefc1c08e952ebe81b32) | MIT；net8.0-windows x64、STA/DTE、模板新建、程序对象操作 | 挑选创建/线程代码。避免引入整套 Git、许可注入、部署和有缺陷的编译/TcUnit 门禁。 |
| [eponce00/twincat-mcp@588106e](https://github.com/eponce00/twincat-mcp/tree/588106ea2b8d51f0ce0b39355501ea2afcf79b99) | MIT；build、LastBuildInfo、Error List、Build Output、MCP/job/授权 scope | 参考完整诊断和具体动作授权；现有入口需要已有工程，不能单独解决从零。 |
| [tcunit/TcUnit@425c629](https://github.com/tcunit/TcUnit/tree/425c62939bb81810879a244270d47d1b610a3bd3) | MIT；PLC 单元测试框架 | 以后运行测试可依赖，本轮只准备设计/自有样例，不执行 Runtime 部署。 |
| [tcunit/TcUnit-Runner@6b16921](https://github.com/tcunit/TcUnit-Runner/tree/6b1692187c08cfda5cea70b1e01ae3e9c33c8042) | MIT；旧自动部署、TcUnit 报告收集 | 2025-10 已归档，只选择性参考 collector，不执行其自动部署工作流。 |
| [Beckhoff/ADS@24e598a](https://github.com/Beckhoff/ADS/tree/24e598a113edfdc0d34414bfefb847930d94cb87) | MIT；ADS 库与官方 adstool CLI | 后续运行通信层；不能代替工程 API。也可按实际语言选 pyads / ADS.NET，无需同时引入多套。 |
| [Beckhoff-USA-Community/TcAutomation@ebf8b9a](https://github.com/Beckhoff-USA-Community/TcAutomation/tree/ebf8b9a0229d851c6d7d2ccbecde057632846ab7) | 未发现 LICENSE；只有 Automation.cs、MessageFilter.cs、README | 只参考公开 API/模式，不搬源码。其编译/诊断方法目前要求 shell mode，不证明新版 standalone 构建覆盖。 |

Edge 固定版用 Node 20+ / CommonJS，C# daemon 为 .NET Framework 4.7.2 x64。rassrollers 为 .NET 8 Windows x64。A 先冻结本机能用的宿主和引用策略，不要求把两套完整依赖堆到一起；优先 net8，必要时保留清楚隔离的 net472 worker。不得提交或随包再分发本机 Beckhoff/Visual Studio 安装程序集。

### 已发现的缺口与必须修复项

1. Edge 的 `PlcProjectActions.Create` 能向已打开的 System Manager 增加标准 PLC，但没有从空目录 `Solution.Create/AddFromTemplate` 的 action；需补齐最前面的创建步骤。[源码](https://github.com/Edge-JB/TwinCAT-XAE-MCP/blob/30d8e2938eeed1c41ee24765b469d13430ab0a32/daemon/Actions/PlcProjectActions.cs)。
2. Edge 新 C# daemon 的[验证记录](https://github.com/Edge-JB/TwinCAT-XAE-MCP/blob/30d8e2938eeed1c41ee24765b469d13430ab0a32/docs/csharp-daemon-validation.md) 将 live XAE 留作后续工作；不能把 Node 语法/source-regex CI 或旧 PowerShell 记录当作新 daemon 完整工程验收。
3. Edge 危险动作主要在 Node 用固定 `ALLOW_*` 字符串校验，daemon 未重查；本项目后端必须自己限制工作区、会话、具体动作和目标。绕开 Node 不能直接获得删除/下载/运行/许可操作。
4. rassrollers 的 `GetErrorItems` 对 COM 1-based 集合用 `i=1; i<Count`，漏最后一条；一条错误可能得到空列表，原 build 判断有假成功风险。[源码](https://github.com/rassrollers/TcAutomationInterface/blob/94826e4f9e456849ca4adefc1c08e952ebe81b32/AutomationInterface.core/Environment/VisualStudioEnvironment.cs)。C 必须补单条/最后一条错误、未启动构建、异步超时、部分诊断等回归。
5. rassrollers TcUnitRunner 对 .NET List 的索引也有跳首项/越界风险，不移植其结果门禁。[源码](https://github.com/rassrollers/TcAutomationInterface/blob/94826e4f9e456849ca4adefc1c08e952ebe81b32/AutomationInterface.core/TwinCAT/TcUnitRunner.cs)。[eponce BuildCommand](https://github.com/eponce00/twincat-mcp/blob/588106ea2b8d51f0ce0b39355501ea2afcf79b99/TcAutomation/Commands/BuildCommand.cs) 的诊断加失败项目数双检查可作对照，仍须实际完成构建并核验诊断范围。

移植交付必须记录仓库、完整 commit、具体文件、改动及 LICENSE/NOTICE；设计参考与复制源码分别说明。无许可、商业文档仓库或受保护安装包不列为可搬代码。

## 首轮四个工作包

GitHub #53 及最新派发评论记录实际 Issue、CLAIM、源版本、依赖和结果；本文只定义稳定分工，不维护第二份状态表。

| 工作包 | 输出 | 可先独立做的工作 |
| --- | --- | --- |
| A：契约 / 环境 / COM 宿主 | schema/DTO/服务接口；Doctor；安装/模板/版本探测；STA、重试、owned session 生命周期 | 先发布 CONTRACT_READY 和宿主选择，再做隔离 probe。 |
| B：从零工程 / ST 编辑 | Solution/tsproj/PLC 创建、MAIN/FB/GVL/DUT、Task/库引用、执行端门禁 | 研究/移植固定上游离线操作、准备自有输入；绑定 A 后实现。 |
| C：编译诊断 / 独立回读 | 确认构建启动完成、完整结构化错误、close/reopen capture、输入期望比较 | 纯诊断/比较正反例先做；绑定 A/B 后取得真实 XAE 证据。 |
| D：MCP 入口 / Agent 流程 | 工具发现、preview、一次性授权、动作 scope、结构化失败、配套用法 | Node 骨架与 fake provider 测试先做；绑定 A/B/C 后真实端到端。 |

不同可写 Agent 使用独立分支/worktree和不重叠文件范围。A 的共享契约由 A 单写；中央总入口/根 CI/README 与最终组合由协调者串行整合。四个候选不是四个已经接通的功能；父任务验收需要实际组合的从零→编译→回读→MCP 闭环。

## 环境观察与运行边界

2026-10-12 仅只读观察到安装目录 `D:/Beckhoff/TwinCAT/3.1`、TcXaeShell 17.0、已注册 TcSysManagerRM/TcXaeShell.DTE.17.0。Shell 的 17.0 和 TcSysUI 的文件版本不是 XAE build 的充分证据，需 Doctor 分别确认。

当前 token 非管理员，已有 TwinCAT SystemServiceUm/UI 进程。没有启动 COM 工程实例，没有打开/改写用户工程，没有激活/重启/停止现有 Runtime，也没有读取或修改许可证。现有进程与现有 AMS 目标不属于测试者。

每个实测者在自己的 Issue CLAIM 中明确 fresh IDE/COM 对象、独占 scratch/input/output 与清理责任，默认 hidden。COM 生命周期只释放自建对象，不杀共享服务。禁止使用 ROT/GetActiveObject 接管用户 IDE；不得用上游 `ActivateConfiguration/StartRestartTwinCAT` 样例做隐式测试收尾。

阶段二才设计独立 Usermode/Restricted Runtime、TcUnit 与 ADS 行为验证，先证明目标是隔离实例，再单独授权激活/写入。当前工作包不得顺手改路由、开机启动、Windows 权限、实时内核、NIC 或许可证。
