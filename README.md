# TIA-Guard

**中文** | [English](README.en.md)

**让受支持的 TIA Portal 工程可阅读、可用 Git 管理，并能通过验证后重建。**

TIA-Guard 是独立的 **pre-alpha** 工程工具，面向本机 **Siemens TIA Portal V21 / S7-1200**。它通过本地 Openness API 将工程转换成确定性的工程源，提供 Windows GUI、CLI，以及供本机 AI Agent 使用的 API Gateway / MCP 接口。

## 项目概览：TIA Portal 工程版本化与 AI 工程 Gateway

- **工程可读化与 Git 协作**：基于 TIA Portal V21 / Openness API，将受支持的 S7-1200 工程转换为结构化工程源，保留 PLC 变量与 LAD 逻辑，便于 Git 比较、审查和复用；已实现中文 WPF GUI 的 GitHub 导入重建、导出提交及多工程管理。
- **受支持范围内的往返重建与验证**：完成 `.ap21 → 可读工程源 → 新 .ap21` 的闭环；自建 V21 / S7-1200 fixture 已有真实 Build / Compile / Verify 证据，校验受支持对象的逻辑、结构及工程语义；不支持或证据不足的对象阻断通过。
- **本地 AI 工程 Gateway**：设计参考[嘉立创 EDA 专业版 Run API Gateway 扩展](https://github.com/easyeda/eext-run-api-gateway)的本机桥接与配套 Skill 模式。已实现 localhost HTTP / MCP、AI 工程视图与专用 Skill，供 Codex、Cursor、Claude Code 查询变量、有限 LAD 逻辑和引用；支持修改预览、GUI 单次确认及编译、重建、验证、失败回滚控制。GUI / CLI / Gateway 源码已公开，152 项契约测试及 45 项 Host 测试通过；最新集成版本的真实 TIA 修改闭环待复验。

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

## 已有工具与设计参考

PLC 工程文本化、版本管理和 AI 工程接口已有有价值的工具。TIA-Guard 聚焦受支持的 TIA V21 / S7-1200 子集，将 canonical 源、Git 协作、全新工程重建和工程语义验证连接起来。各项目的平台、能力和验收范围不同。

| 参考方向 | 项目与可借鉴的优势 |
| --- | --- |
| PLC 工程与 Git | [CODESCRIBE](https://github.com/greenforge-labs/codescribe)：CODESYS 文本导出/导入、对象级审查和派生图形视图；官方 CODESYS File-Based Storage 也是值得评估的入口。 |
| 语言与诊断 | [iec-checker](https://github.com/iec-checker/iec-checker)、[RuSTy](https://github.com/PLC-lang/rusty)、[IronPLC](https://github.com/ironplc/ironplc)、[radevgit/plc](https://github.com/radevgit/plc)：静态检查、编译/解析、测试与来源定位。它们是生态研究对象，尚未接入本项目。 |
| TIA MCP 桥接 | [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp) 与 [TIA_Portal_Openness_MCP](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP)：已记录的设计研究来源，分别提供工程绑定/worker 和广泛工程能力等参考。 |
| 本机 AI Gateway | [嘉立创 EDA 专业版 Run API Gateway](https://github.com/easyeda/eext-run-api-gateway)：Gateway 的设计参考；其外部 Agent、本机 Bridge、工程软件与配套 Skill 模式启发了本项目的本机 AI 接入流程。 |

详见[相关项目、设计来源与后续改进](docs/RELATED-PROJECTS.md)：包含固定版本来源、各自范围、已有实现和待验证计划。先完成 [#41：最新 GUI/Gateway 真实 TIA 离线复验](https://github.com/biaobiao2233/tia-guard/issues/41)，再改善 Git 变更摘要、逐项扩展 LAD，并在环境齐备后评估一个只读跨平台适配；这些后续能力尚未完成。

## 未来展望与社区适配

长期希望把“工程可读化、Git 版本管理、审查与受验证的重建”逐步扩展到更多 PLC 工程，让不同平台的工程能够纳入统一的项目索引、版本历史与协作流程。适配方向会跟随实际接触的平台、可获得的开发环境和验证条件推进，也欢迎社区基于本项目贡献适配。

### 候选平台与工程环境

以下是未来可评估的方向，均为待适配、待验证的候选；具体型号、软件版本和功能范围在开展适配时确定。清单保持开放，后续接触到的新平台也欢迎加入。

| 平台 / 厂商 | 工程软件 / 目标范围 | 软件桥接的候选入口 |
| --- | --- | --- |
| Siemens | TIA Portal（博图），S7-1500 扩展 | 现有 [Openness](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api?contentId=~~8wSiwSWV3Triktc6ADDw) 桥接；新 CPU/对象逐项验证。 |
| Beckhoff / 倍福 | TwinCAT 3 XAE / TcXaeShell / Visual Studio 集成 | 评估 [Automation Interface / COM](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/242718859.html)。 |
| Mitsubishi / 三菱 | [GX Works3 / GX Works2](https://www.mitsubishielectric.com/fa/products/software/plc/index.html)，MELSEC 工程 | 工程自动化 API 待调研；评估官方导入导出与源码文件。 |
| Omron / 欧姆龙 | [Sysmac Studio](https://www.ia.omron.com/products/category/automation-systems/machine-automation-controllers/software/)（NJ/NX）；[CX-One / CX-Programmer](https://industrial.omron.eu/en/products/cx-one) | 两类工程分别评估；公开工程 API、脚本与文件入口待调研。 |
| Rockwell / Allen-Bradley / 罗克韦尔 | [Studio 5000 Logix Designer](https://www.rockwellautomation.com/en-us/products/software/factorytalk/designsuite/studio-5000/studio-5000-logix-designer.html)（ControlLogix / CompactLogix） | 评估 [Logix Designer SDK](https://literature.rockwellautomation.com/idc/groups/literature/documents/gr/ldsdk-gr001_-en-p.pdf) 与工程导入导出。 |
| Schneider Electric / 施耐德 | [EcoStruxure Machine Expert](https://www.se.com/uk/en/product-range/2226-ecostruxure-machine-expert/) / [Control Expert](https://www.se.com/us/en/product-range/548-ecostruxure-control-expert-software/)（Modicon） | Machine Expert 有 [Python / Script Engine](https://product-help.schneider-electric.com/Machine%20Expert/V2.1/en/SoMProg/SoMProg/D-SE-0083846.html) 可评估；Control Expert 接入单独调研。 |
| B&R / 贝加莱 | [Automation Studio](https://www.br-automation.com/en/products/software/automation-studio/) | 评估厂商公布的 Agentic Bridge / MCP，以及具体版本的构建和工程接口。 |
| ABB | [Automation Builder](https://www.abb.com/global/en/areas/motion/digital-tools/automation-builder/engineering)（AC500） | 评估厂商工程扩展；脚本、API 和导入导出能力按版本调研。 |
| CODESYS 生态 | CODESYS Development System / SoftPLC 工程 | 评估 [CODESYS Scripting / ScriptEngine](https://content.helpme-codesys.com/en/CODESYS%20Scripting/_cds_access_cds_func_in_python_scripts.html)、命令行与工程导入导出。 |
| WAGO / 万可 | [CODESYS V3.5](https://www.wago.com/global/products/automation-technology/discover-software/codesys-v3)；已有 e!COCKPIT 工程 | 评估设备包、库及脚本；旧工程迁移另行验证。 |
| Bosch Rexroth / 博世力士乐 | [ctrlX PLC Engineering](https://apps.boschrexroth.com/microsites/ctrlx-automation/en/portfolio/ctrlx-plc/) / ctrlX WORKS | 评估 CODESYS 工程脚本与 Rexroth 扩展；运行时 REST 接口另行区分。 |
| Festo / 费斯托 | [CODESYS provided by Festo](https://www.festo.com/media/catalog/204137_documentation.pdf) 等配套工程环境 | 评估对应版本的脚本、设备描述和厂商库。 |
| Phoenix Contact / 菲尼克斯电气 | [PLCnext Engineer](https://www.phoenixcontact.com/en-nl/products/programming-software-plcnext-engineer-1046008) | 评估厂商列出的 Application Control Interface（ACI）及工程文件接口。 |
| Panasonic / 松下 | [Control FPWIN Pro](https://industry.panasonic.eu/products/automation-devices-solutions/programmable-logic-controllers-plc/plc-software/programming-software-control-fpwin-pro)（FP 系列） | 工程 API/脚本待调研；先评估官方导入导出与源码。 |
| KEYENCE / 基恩士 | [KV STUDIO](https://www.keyence.com/support/user/controls/plc/)（KV 系列） | 工程 API/脚本待调研；先评估工程文件与官方导出。 |
| LS ELECTRIC | [XG5000](https://sol.ls-electric.com/ww/en/product/category/476)（XGT / XGB 等） | 工程 API/脚本待调研；按系列验证文件和导入导出。 |
| Delta / 台达 | [ISPSoft](https://www.deltaww.com/en-US/products/PLC-Programmable-Logic-Controllers/3598?categoryCode=060301) / [DIADesigner](https://filecenter.deltaww.com/Products/download/06/060301/Manual/DELTA_IA-PLC_AS_HOM_EN_20220530.pdf)（按系列） | 工程自动化入口待调研；不同软件和 PLC 系列分别验证。 |
| FATEK / 永宏 | [WinProladder](https://www.fatek.com/en/product.php?act=view&id=162)（FBs / B1 等） | 工程 API/脚本待调研；先评估源码及工程文件。 |
| Inovance / 汇川 | [InoProShop / AutoShop](https://portal-file.inovance.com/owfile/ProdDoc/CY/19120152-CY/A01/19120152-CY_A01%E3%80%8AExpansion%20Module%20and%20HMI%E3%80%8B-EN-202221116_Web.pdf)（按系列） | 按软件版本研究工程 API、脚本、源码与厂商扩展。 |
| XINJE / 信捷 | [XDPPro](https://www.xinje.com/web/productInfo/index?indexGroup=0&seriesId=103) / [XCPPro](https://en.xinje.com/web/search/searchData?val=o) 等配套工具 | 公开工程自动化接口待调研；按系列评估文件导出。 |
| Kinco / 步科 | [KincoBuilder](https://www.kinco.cn/product/155?classification_id=35) 等配套工具 | 工程 API/脚本待调研；验证项目结构与导入导出。 |
| HollySys / 和利时 | [FA-AutoThink](https://www.hollysys.com/download/products?kw=plc&tp=1&wd=1)（按 PLC 系列） | 工程自动化接口待调研；源码、配置与工具链分别验证。 |
| SUPCON / 中控 | [G3 / G5 等平台](https://www.global.supcon.com/control-safety-systems/plc)配套工程软件：名称与版本待核实 | 先确认对应工程环境；公开工程 API 与文件入口待调研。 |

### 各家工程软件的 API 桥接

长期适配对象既包括 PLC 工程，也包括创建、编辑和验证这些工程的软件。当前本项目实现的工程软件桥接是 **TIA Portal V21 / Openness**；上表其他软件及接口均为候选方向，尚未在 TIA-Guard 中接入和验证。厂商已有某个 API，不等于本项目已适配。

计划按“工程软件 + 版本 + 设备系列 + 支持能力”建立适配边界，将工程读取、变量/程序查询、修改预览、受控离线修改、编译、导出、重建与验证逐步接到共用的 GUI / CLI / HTTP API / MCP 流程。共用接口与适配器契约需要随实际实现逐步设计，不能假定现有 TIA 接口已经通用于其他软件。

优先评估公开的工程 API / SDK、COM、脚本和插件接口；再按工具实际能力评估命令行、官方导入导出及工程源码文件。没有确认接口的条目先保持待调研，适配器只暴露已验证的能力。还需记录软件版本、所需许可、依赖和可复现的环境条件。

工程软件自动化与 PLC 运行通信分别管理。例如 TwinCAT 的 [Automation Interface](https://infosys.beckhoff.com/content/1033/tc3_automationinterface/242685835.html) 面向 XAE 工程环境，而 [ADS](https://infosys.beckhoff.com/content/1033/tc3_grundlagen/116157835.html) 是 TwinCAT 的通信接口；仅连通 ADS、OPC UA 或 Modbus 不能作为工程软件读取、修改或重建的验收证据。

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
