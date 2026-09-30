# TIA-Guard

**中文** | [English](README.en.md)

**让受支持的 TIA Portal 工程可阅读、可用 Git 管理，并能通过验证后重建。**

TIA-Guard 是独立的 **pre-alpha** 工程工具，面向本机 **Siemens TIA Portal V21 / S7-1200**。它通过本地 Openness API 将工程转换成确定性的工程源，提供 Windows GUI、CLI，以及供本机 AI Agent 使用的 API Gateway / MCP 接口。

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
