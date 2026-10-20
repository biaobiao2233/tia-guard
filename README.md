# TIA-Guard

**中文** | [English](README.en.md)

**连接 AI 与工程软件，让 Agent 能读取工程、创建或修改代码、调用编译，并根据真实诊断继续修复。**

TIA-Guard 是面向本机工程软件的开源 AI 接入工具。目前提供 **TIA Portal** 和 **TwinCAT 3** 两个适配器，通过 MCP、官方工程 API 和软件自身的编译/验证能力，把 Agent 的操作落实到真实工程。项目处于 **alpha 预览阶段**，各适配器的环境前提和已验证范围分别说明。

项目保留现有名称、命令和配置；主线从 TIA 工程版本管理扩展为 **AI 与工程软件的连接及操作验证**。TIA 工程文本化、Git 管理与往返重建继续作为附加功能维护。

## 适配原则：优先接口与命令行

所有现有和未来的软件适配器都遵循同一原则：**遇到需要 GUI 的操作，先核查并复用官方 API、SDK、COM、脚本或 CLI，在任务范围内推进可重复调用的 CLI / MCP / API 适配。** GUI 依赖应成为需要解决的适配缺口，不能长期依赖截图点坐标或让用户反复手动操作。

确无可用接口时允许受限 GUI 备选，但必须说明已核查入口、实现依赖及后续替换方向；把鼠标包装成 API 不代表原生接口已适配。详见[项目长期适配原则](docs/AUTOMATION-PRINCIPLES.md)、[仓库 Agent 规则](AGENTS.md)和[贡献指南](CONTRIBUTING.md)。这些是开发要求，不提升当前能力表中的支持状态。

## 按工程软件下载

在[同一个 Release](https://github.com/biaobiao2233/tia-guard/releases) 中选择需要的适配包；使用两种软件就安装两个包，无需安装统一 Gateway 或公共核心包。

| 适配包 | 用途 | AI 入口 |
| --- | --- | --- |
| `tia-guard-tia-v<版本>-windows-x64.zip` | TIA Portal V21：GUI、CLI、AI Gateway 和隔离 Worker；包含工程生成及可选 Git 工作流 | MCP stdio、Streamable HTTP、本机 HTTP API |
| `tia-guard-twincat-v<版本>-windows-x64.zip` | TwinCAT 3：独立 MCP、已构建的 Windows Worker、生产 Node 依赖和示例 | MCP stdio |
| `tia-guard-bridge-v<版本>-windows-x64.zip` | TIA 的无 GUI 接入包；已包含在 TIA 完整包内，不必重复下载 | MCP stdio、Streamable HTTP、本机 HTTP API |
| `skill.zip` | TIA Gateway 的可选 Agent 操作说明；不能代替适配器或工程软件 | 配套 Skill |

**需要自己安装相应工程软件及官方组件。** 安装包不包含 TIA Portal、TwinCAT XAE、用户工程、许可证或从厂商安装目录复制的 DLL。TwinCAT 包带有按各自许可证分发的公开 NuGet/Node 依赖，需要 Node.js ≥22 和 .NET 8 Windows Desktop Runtime x64。

先看[适配包安装与 MCP 配置](docs/ADAPTER-PACKAGES.md)。旧版 `v0.1.0-prealpha.1` 使用旧文件名，不能据此推定它包含下表的新能力。

## 当前可以做什么

| 能力 | TIA Portal V21 / S7-1200 | TwinCAT 3 |
| --- | --- | --- |
| 环境检测与工程读取 | CLI doctor、工程上下文、变量、有限 LAD 图与引用 | Doctor、静态工程检查、实际后端能力报告 |
| 从零创建工程 | SCL → 新 `.ap21`，支持已验证的 OB1 / FC / FB / DB / UDT / 变量子集 | ST → 新工程，支持已验证的 Program / FB / GVL / 结构体 / 任务与库子集 |
| 修改和错误修复 | 修改生成输入后重新编译；现有工程副本支持有限 tag / LAD patch | 修改 ST 输入后重新预览、授权、生成或编译自有副本 |
| 调用软件编译 | 实际 Openness 编译与完整诊断 | 实际 XAE 编译与诊断，包含错误位置（软件能提供时） |
| 独立验证 | 独立比对冻结输入与实际工程读回；S7-1500 候选另加保存关闭重开关卡 | 保存、关闭、重开，独立核对对象、源码与配置 |
| 软件仿真 | **实验、默认关闭**：已验证实例生命周期、电源控制、SIM 表地址配置与编译准备；完整下载 / RUN / 扫描 / 实际 I/O 尚未验收 | 已验证自有 Usermode PLC 仿真、RUN/STOP、标量反馈与有效许可复用；另有单轴 NC/PTP 软件仿真证据 |
| Git 工程管理 | 附加功能：工程文本化、差异比较、GitHub 导入重建、导出提交和多工程管理 | 可沿用原生文本工程及已有 Git 工具，无需 TIA 的二进制转换流程 |

这些是限定软件版本、工程对象及测试环境下的能力，不能推广到任意工程或真实设备。**CODESYS 适配器仍在 [PR #64](https://github.com/biaobiao2233/tia-guard/pull/64) 中，未合并，也不包含在本次发行包。**

## AI 的使用流程

1. 在 AI 客户端配置所需软件的 MCP，先读取 Doctor / capabilities，确认实际环境和可用功能。
2. Agent 读取工程或编写受支持的 SCL / ST 输入，预览目标与修改范围。
3. 明确启用写模式后，创建新工程或操作自有工程副本；不直接覆盖用户原工程。
4. 调用工程软件编译，读取真实错误并修复；修改源码后重新预览和执行。
5. 独立回读通过后交付新工程；需要仿真时，再进入该适配器明确支持的仿真流程。

MCP 是主要的 Agent 接口。TIA 同时提供 HTTP / CLI；TwinCAT 直接通过独立 stdio MCP 接入，不需要先运行 TIA Gateway。本项目提供工程工具和验证结果，Agent 可使用自己已有的模型与客户端。

## TIA Portal

S7-1500 离线工程扩展 [PR #68](https://github.com/biaobiao2233/tia-guard/pull/68) 已在 V21 通过 CPU 1511-1 PN/V2.9 的实际新建、软件编译、关闭重开、源码核验及错误诊断/修复。现有 `v0.2.0-alpha.1` 发布包不包含此能力；验证范围不涵盖全系列、硬件编译或下载运行。详情见[CPU 配置与状态](docs/PROJECT-GENERATION.md#cpu-配置与验证状态)。

需要 Windows x64、TIA Portal V21、Openness，以及当前登录用户有效的 Openness 访问权限。首次访问确认由用户在博图中处理；工具不自动修改用户组、访问许可或软件许可证。当前 CPU profile 为 `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`。

- [安装和 GUI 入门](docs/GETTING-STARTED.md)：中文 WPF GUI；打开 GUI 可自动启动本机 Gateway。
- [AI Gateway / MCP 指南](docs/TIA-AI-BRIDGE.md)：工程读取、受控 patch、单次预览授权、编译、回滚和新副本发布。
- [从零创建 PLC 工程](docs/PROJECT-GENERATION.md)：`init`、`build-generation` 和对应 HTTP / MCP 工作流。
- [PLCSIM 自动化实验](docs/PLCSIM-AUTOMATION.md)：精确范围、默认关闭的开关和未完成项。
- [Git 往返契约](docs/contracts/roundtrip-source-v1.md)：`.ap21 → 可读工程源 → 新 .ap21` 的对象和语义边界。

```powershell
# 解压 TIA 完整包后，在该目录执行
.\tia-guard.exe doctor
.\tia-guard.exe init E:\PLC\input
New-Item -ItemType Directory -Path E:\PLC\scratch
.\tia-guard.exe build-generation E:\PLC\input --output E:\PLC\new-project --scratch E:\PLC\scratch
```

Git GUI 保留 GitHub → TIA 重建、TIA → GitHub 导出提交及 `tia-projects/<slot>/tia-source/` 多工程布局。它复用系统 Git 和现有凭据，不内嵌 GitHub token、不 force-push。Git 功能无需参与 AI 从零创建工程流程。

现有 LAD patch 仅覆盖已验证的触点、取反读取、普通线圈和有限串联条件；任意并联、有状态指令、S7-1500、HMI、Safety、驱动和多 PLC 工程不在当前验证范围。真实 GUI/Gateway 修改闭环的 [Issue #41](https://github.com/biaobiao2233/tia-guard/issues/41) 仍单独跟踪，不能由纯测试替代。

## TwinCAT 3

[适配器说明](adapters/twincat3/README.md)及[发行包配置](docs/ADAPTER-PACKAGES.md#twincat-3)提供安装入口。已验证的原生环境为 Windows x64、XAE 3.1.4026.27 / `TwinCAT OS (x64)`；NC 用户态模块为 3.1.5049.0，NC 示例固定 Tc2_MC2 3.3.75.0。其他版本需要另行验证。

- [MCP 工具与操作流程](adapters/twincat3/docs/MCP.md)
- [Worker、本机 interop 与环境前提](adapters/twincat3/docs/WORKER.md)
- [ST 电机逻辑示例](examples/twincat3-generation/motor/twincat-generation.json)
- [单轴 NC/PTP 示例及限制](examples/twincat3-generation/nc-axis.md)

真实 MCP 已有“错误 ST 编译失败 → 修复 → 新预览和授权 → 编译通过 → 独立重开核验”记录。自有 PLC 软件仿真、跨 Worker 的专用实例/有效许可复用，以及 30→80→0 mm 单轴 NC 定位分别有[运行](adapters/twincat3/evidence/runtime-20261015.json)、[许可复用](adapters/twincat3/evidence/persistent-runtime-20261015.json)和[运动](adapters/twincat3/evidence/nc-motion-20261015.json)证据。

软件仿真需要对应官方组件与有效许可，官方试用验证码由用户本人处理。仅操作适配器自有的 Usermode 实例；不操作真实电机、EtherCAT、用户已有 Runtime 或全局路由。单轴坐标置零不等于机械回零，仿真不代表安全或实时性验证。TwinCAT 尚未集成进 TIA GUI / HTTP 接口。

## 验证、贡献与来源

新发行包的源码、构建和验证说明见 [v0.2.0-alpha.1 发布说明](docs/releases/v0.2.0-alpha.1.md)；压缩包字节身份见 Release 的 `SHA256SUMS.txt` 和 `release-manifest.json`。工程软件运行证据始终绑定其注明的源码与环境；打包测试不会补发未做过的原生验收。

欢迎贡献新的软件适配器，或扩大现有适配器已验证的对象范围。优先复用官方 API / 脚本入口和成熟项目，分别提供环境检测、诊断、验证与操作边界；不要求所有软件都采用同一种工程文件格式或 Git 流程。

设计参考包括 [嘉立创 Run API Gateway](https://github.com/easyeda/eext-run-api-gateway)、[Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp)、[TIA_Portal_Openness_MCP](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP) 等；具体来源、复用范围和许可证见[相关项目](docs/RELATED-PROJECTS.md)及 TwinCAT 适配器的许可证目录。Doctor/SARIF 与 advisory AI 的旧提案 [#7](https://github.com/biaobiao2233/tia-guard/pull/7) / [#5](https://github.com/biaobiao2233/tia-guard/pull/5) 仍未合并。

[MIT License](LICENSE)。本项目独立开发，不代表 Siemens、Beckhoff 或其他厂商。
