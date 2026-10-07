# TIA-Guard

**中文** | [English](README.en.md)

**让受支持的 TIA Portal 工程可阅读、可用 Git 管理，并能通过验证后重建。**

TIA-Guard 是独立的 **pre-alpha** 工程工具，面向本机 **Siemens TIA Portal V21 / S7-1200**。它通过本地 Openness API 将工程转换成确定性的工程源，提供 Windows GUI、CLI，以及供本机 AI Agent 使用的 API Gateway / MCP 接口。

## 项目概览：TIA Portal 工程版本化与 AI 工程 Gateway

- **工程可读化与 Git 协作**：基于 TIA Portal V21 / Openness API，将受支持的 S7-1200 工程转换为结构化工程源，保留 PLC 变量与 LAD 逻辑，便于 Git 比较、审查和复用；已实现中文 WPF GUI 的 GitHub 导入重建、导出提交及多工程管理。
- **受支持范围内的往返重建与验证**：完成 `.ap21 → 可读工程源 → 新 .ap21` 的闭环；自建 V21 / S7-1200 fixture 已有真实 Build / Compile / Verify 证据，校验受支持对象的逻辑、结构及工程语义；不支持或证据不足的对象阻断通过。
- **本地 AI 工程 Gateway**：已实现 localhost HTTP / MCP、AI 工程视图与专用 Skill，供 Codex、Cursor、Claude Code 查询变量、有限 LAD 逻辑和引用；支持修改预览、GUI 单次确认及编译、重建、验证、失败回滚控制。GUI / CLI / Gateway 源码已公开，152 项契约测试及 45 项 Host 测试通过；最新集成版本的真实 TIA 修改闭环待复验。

## 下载与使用说明

- [发行版](https://github.com/biaobiao2233/tia-guard/releases)：预览包与校验值。
- [快速开始](docs/GETTING-STARTED.md)：环境前提、解压、GUI 流程与常见问题。
- [AI Gateway 指南](docs/TIA-AI-BRIDGE.md)：本机 Agent、MCP 与受控离线修改。

首个发行版为 pre-alpha 预览版。GUI 当前为中文；页面顶部链接切换的是仓库文档语言。

## 当前源码状态

当前源码已经包含以下实现。功能边界和验证证据以本仓库的代码、测试及[本次集成记录](docs/verification/publication-20261001.md)为准。

| 模块 | 当前实现 |
| --- | --- |
| Windows GUI | 中文 WPF 界面、自定义标题栏；GitHub → TIA 重建、TIA → GitHub 导出并提交；一个仓库可管理多个工程 |
| CLI | `doctor`、`--version`、`export`、`build`、`verify`、`ai-view` |
| AI 工程视图 | 从受验证的 canonical source 派生 JSON / Markdown、变量表、有限 LAD 图和来源引用 |
| API Gateway | GUI 自动启动本机 `127.0.0.1:18761` 服务；提供能力发现、工程上下文、程序图、引用查询和受控 patch |
| MCP | stdio 与 Streamable HTTP；默认只读工具，显式启用的写工具保留 preview / apply 验证 |
| Gateway Skill | [专用 Skill](skills/tia-guard-gateway/SKILL.md)，供本机 Codex、Cursor、Claude Code 等 Agent 按统一流程调用 |
| Windows 打包 | GUI + CLI + Gateway + 独立 net48 worker；不打包 Siemens DLL |

这是源码集成状态，不是稳定版发布声明。本次检查覆盖自动化测试、构建、打包和本机 HTTP 控制边界；真实 TIA 工程往返证据另见下文。发行包及发布状态以 Releases 页面为准；历史记录不自动视为最新集成 GUI/Gateway 的真实 TIA 验收。

## 两个 GUI 主流程

### GitHub → TIA

粘贴 Git 仓库 URL → clone / pull → 选择工程 → 从工程源构建新的 `.ap21` → 编译与受支持语义校验。

新工程写到用户选择的输出目录，不覆盖原工程。清晰显示阶段、耗时、环境或 Git 认证错误；源文件身份信息存在时保留原始文件名并检查相应身份信息。

### TIA → GitHub

选择自己的 `.ap21` 和 Git 仓库 URL → 选择已有工程或新增工程槽位 → 导出并验证 → 更新所选工程源 → commit / push。

一个仓库可包含多个工程：

```text
repo/
  README.md
  tia-projects/
    motor-reversing/
      tia-source/
      ai/
    conveyor-control/
      tia-source/
```

仍兼容旧的 `repo/tia-source/` 单工程布局。发布操作只替换所选工程的 `tia-source/`，保留其他工程及普通仓库文件；使用系统 Git、现有 Git Credential Manager / SSH 配置，不内嵌 GitHub token，也不 force-push。

`ai/` 是可重新生成的派生视图。当前 GUI Git 发布流程不会自动生成或提交它；需要时单独运行 `ai-view`。

## CLI

```powershell
dotnet build src/TiaGuard.Cli/TiaGuard.Cli.csproj -c Release

tia-guard doctor
tia-guard --version
tia-guard export C:\demo\original.ap21 C:\repo\tia-source
tia-guard build C:\repo\tia-source --output C:\demo\rebuilt
tia-guard verify C:\demo\original.ap21 C:\demo\rebuilt\original.ap21
tia-guard ai-view C:\repo\tia-source
```

`doctor` 只读检查本机前提。Export 打开受控离线副本；Build 严格验证 canonical source，再创建、保存和编译新的工程；Verify 返回 `pass`、`mismatch` 或 `blocked`。短内部 staging 路径将 TIA 的工程创建路径限制与普通 Git / 输出目录长度分开。

## 本机 AI API Gateway

打开 GUI 后自动启动 Gateway。Agent 先读取 `/capabilities` 和 `/openapi.json`，按实际提供的接口调用：

| 接口 | 用途 |
| --- | --- |
| `GET /health`、`GET /capabilities` | 健康与能力发现 |
| `GET /api/v1/gateway/status` | Gateway、AI 连接、TIA 绑定与待确认状态 |
| `POST /api/v1/open-offline` | 打开工程的受控离线副本 |
| `GET /api/v1/ai/context`、`program-graph`、`network`、`where-used` | 有来源依据的工程、网络和变量引用查询 |
| `POST /api/v1/ai/patches/preview`、`apply` | 预览并申请一次受控修改 |

HTTP patch 在真正写入前等待 GUI 中的单次确认。操作控制接口使用 GUI 生成的随机进程密钥，独立启动的只读 Gateway 无法自行批准或关闭服务。预览 token 有有效期、绑定工程及请求状态，并且只能使用一次。

当前结构化 patch 支持根变量表中的 `upsert_tag` 和有限 LAD 的 `replace_output_condition`。成功 apply 要完成 Compile、导出确定性检查、round-trip Verify 和 AI 语义检查；失败回滚受控副本。输入的原始 `.ap21` 不会被保存。

MCP 的默认工具集合只读。需要 headless 写模式时显式传 `--allow-write`；该模式省略 GUI 确认，仍保留 preview token 与离线副本验证。完整协议见 [TIA AI Gateway](docs/TIA-AI-BRIDGE.md)。

Gateway 服务只绑定 localhost。本机桌面 Agent 可以连接；纯云端聊天页面仅安装 Skill 并不能访问用户电脑。

## 范围和验证边界

- 目标是已支持的 V21 / S7-1200 单 PLC、Main OB1 LAD 和根变量表工程子集。当前 CPU profile 为 `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`。
- 自建演示工程已经有 Export → 新建 Build → Compile → 再导出 → Verify 的真实 TIA 证据；后续自建非空 LAD fixture 覆盖六个 Bool 变量、三个网络。见[架构与覆盖](docs/ARCHITECTURE.md)、[LAD 验证记录](docs/verification/lad-graph.md)和[源格式契约](docs/contracts/roundtrip-source-v1.md)。
- LAD 图与 patch 只接受已验证的普通触点、取反读取、普通线圈和串联 AND 等有限结构；未知指令、歧义拓扑及不支持内容不能产生完整表达式或虚假的 PASS。
- OR 重汇合、任意并联改写、定时器/计数器等有状态指令、S7-1500、HMI、Safety、驱动和多 PLC 工程尚未覆盖。
- 没有在线 PLC 下载、启停、force 或在线变量写入。工程语义校验不等于任意运行时行为证明，也不保证所有 `.ap21` 二进制字节相同。

旧验收记录对应其注明的候选版本；不自动视为最新集成版本的真实 TIA 验收。现有 Doctor/SARIF 与 provider-neutral advisory AI 提案仍在独立 PR #7 / #5，未由本次源码发布合并。

## 未来展望与社区适配

长期希望把“工程可读化、Git 版本管理、审查与受验证的重建”逐步扩展到更多 PLC 工程，让不同平台的工程能够纳入统一的项目索引、版本历史与协作流程。适配方向会跟随实际接触的平台、可获得的开发环境和验证条件推进，也欢迎社区基于本项目贡献适配。

### 候选平台与工程环境

以下是未来可评估的方向，均为待适配、待验证的候选；具体型号、软件版本和功能范围在开展适配时确定。清单保持开放，后续接触到的新平台也欢迎加入。

| 平台 / 厂商 | 候选工程范围与参考入口 |
| --- | --- |
| Siemens | S7-1500；逐步评估更多 TIA Portal CPU 与工程对象。 |
| Beckhoff / 倍福 | TwinCAT 3 PLC 工程、ST 源码与工程配置。 |
| Mitsubishi / 三菱 | MELSEC 工程；从 [GX Works3](https://www.mitsubishielectric.com/fa/products/cnt/plceng/smerit/gx_works3/index.html) 等实际使用的工具版本评估。 |
| Omron / 欧姆龙 | NJ/NX 与 [Sysmac Studio](https://www.ia.omron.com/products/category/automation-systems/machine-automation-controllers/software/) 等工程环境。 |
| Rockwell / Allen-Bradley / 罗克韦尔 | ControlLogix、CompactLogix 与 [Studio 5000](https://www.rockwellautomation.com/en-us/products/hardware/programmable-controllers.html) 工程。 |
| Schneider Electric / 施耐德 | Modicon；分别评估 [Machine Expert](https://www.se.com/uk/en/product-range/2226-ecostruxure-machine-expert/) 与 [Control Expert](https://www.se.com/us/en/product-range/548-ecostruxure-control-expert-software/) 工程。 |
| B&R / 贝加莱 | [Automation Studio](https://www.br-automation.com/en/products/software/automation-studio/) 工程与 PLC 程序对象。 |
| ABB | AC500 与 [Automation Builder](https://www.abb.com/global/en/areas/motion/digital-tools/automation-builder/engineering) 工程。 |
| CODESYS 生态 | [CODESYS](https://www.codesys.com/ecosystem/discover-codesys/codesys-inside/) 工程与 SoftPLC；按厂商扩展、设备描述、库和版本分别验证。 |
| WAGO / 万可 | [CODESYS 工程环境](https://www.wago.com/global/products/automation-technology/discover-software/codesys-v3)及对应控制器项目。 |
| Bosch Rexroth / 博世力士乐 | [ctrlX PLC](https://apps.boschrexroth.com/microsites/ctrlx-automation/en/portfolio/ctrlx-plc/) 及实际接触的工程环境。 |
| Festo / 费斯托 | [CODESYS 控制器](https://www.festo.com/media/catalog/204060_documentation.pdf)及相应厂商工程配置。 |
| Phoenix Contact / 菲尼克斯电气 | PLCnext 与 [PLCnext Engineer](https://www.phoenixcontact.com/en-de/products/programming-software-plcnext-engineer-1046008) 工程。 |
| Panasonic / 松下 | FP 系列及 [Control FPWIN Pro](https://industry.panasonic.eu/products/automation-devices-solutions/programmable-logic-controllers-plc/plc-software/programming-software-control-fpwin-pro) 工程。 |
| KEYENCE / 基恩士 | KV 系列与 [KV STUDIO](https://www.keyence.com/support/user/controls/plc/) 工程。 |
| LS ELECTRIC | XGT/XGB 等 PLC 与 [XG5000](https://sol.ls-electric.com/ww/en/product/category/476) 工程。 |
| Delta / 台达 | DVP、AS、AH 等系列与 [ISPSoft](https://www.deltaelectronicsindia.com/en-IN/products/PLC-Programmable-Logic-Controllers/15402) 等工程环境。 |
| FATEK / 永宏 | FBs/B1 与 [WinProladder](https://www.fatek.com/en/product.php?act=view&id=162) 等工程环境。 |
| Inovance / 汇川 | [PLC 产品](https://www.inovance.com/product)及实际使用的编程软件；按系列与版本评估。 |
| XINJE / 信捷 | [可编程控制器](https://www.xinje.com/web/downloadCenter/index)及相应工程软件；按系列与版本评估。 |
| Kinco / 步科 | [PLC 产品](https://www.kinco.cn/company-introduction)与对应编程环境；按系列与版本评估。 |
| HollySys / 和利时 | [PLC 工程](https://www.hollysys.com/products/industrial-intelligence/control-safety-systems/plc)与对应工具链；按系列与版本评估。 |
| SUPCON / 中控 | [PLC 平台](https://www.global.supcon.com/control-safety-systems/plc)及相应工程环境；按系列与版本评估。 |

### 如何逐步纳入管理

1. **可读化与版本管理**：优先研究只读解析、源码或工程导出、工程索引和确定性比较，让适配范围内的程序、变量、配置与变更能被 Git 审查。
2. **工程重建与验证**：在平台工具链允许且证据充分时，再增加导入、创建新工程、编译和往返语义校验，逐项声明支持能力。
3. **统一协作与 AI 工程视图**：逐步探索跨平台的工程检索、来源引用和 AI 辅助审查；平台特有的工程对象与验证规则仍由各自适配器处理。

推进原则是：**实际接触什么，就评估适配什么；社区先贡献经过验证的适配，也欢迎纳入。** 清单不是固定排期，以上能力尚未实现或验证，也没有承诺固定交付日期。各平台保留自己的工程格式、编译工具和验证规则；同属 IEC 61131-3 或 CODESYS 生态也需要分别验证。统一管理不代表不同厂商的程序可以直接互相转换，也不能从一个平台的测试结果推断另一个平台已支持。

### 欢迎适配 PR

适配可以从只读解析、工程导出或 Git 管理开始，再逐步增加重建和校验；每项能力都需要写清支持与不支持的范围。PR 应附可复现的测试步骤、自建或脱敏 fixture，以及对应平台的实际验证证据；涉及重建时需要工程工具中的编译和往返校验证据，涉及运行行为时另需真实运行或仿真证据。测试通过、边界清楚并经维护者审查后，可考虑合并。具体要求见 [贡献指南](CONTRIBUTING.md#plc-platform-adapters)。

## 开发与打包

运行目标：Windows x64、TIA Portal V21 / Openness、有效的 Siemens TIA Openness 组权限、Git for Windows。GUI / Openness worker 使用 .NET Framework 4.8；Gateway host 使用 .NET 8，打包时生成 self-contained host。

```powershell
dotnet build src/TiaGuard.Gui/TiaGuard.Gui.csproj -c Release
pwsh -File scripts/package-windows.ps1 -Version 0.1.0-prealpha.1
pwsh -File scripts/package-gateway-skill.ps1
```

Windows ZIP 含 GUI、CLI、Gateway、隔离在 `bridge/worker/` 下的 net48 worker 、TIA-Guard core 及双语入门/Gateway 说明。Gateway 可单独通过 `scripts/package-bridge.ps1` 打包；CLI 可通过 `scripts/package-cli.ps1` 单独打包。

```powershell
dotnet test tests/TiaGuard.Contracts.Tests/TiaGuard.Contracts.Tests.csproj -c Release
dotnet test tests/TiaGuard.Bridge.Host.Tests/TiaGuard.Bridge.Host.Tests.csproj -c Release
python -m pip install jsonschema==4.26.0
python scripts/test-contract-schemas.py
```

纯测试不需要 Siemens DLL，不能替代本地真实 Openness 操作证据。

## 公开源码与工程数据

仓库只保存应用源码、测试、文档、应用图标及脱敏 / 自建 canonical fixtures，不提交 TIA 工程二进制、Siemens DLL、许可证、凭据、环境配置或构建产物。参见 [ignore policy](.gitignore)。工程注释和 canonical XML 仍可能包含工程信息，导出本身不是自动脱敏工具。

## Disclaimer

This is an independent open-source project and is not affiliated with, authorized by, or endorsed by Siemens AG. Siemens, TIA Portal, SIMATIC, STEP 7, WinCC, and related names are trademarks of their respective owners.
