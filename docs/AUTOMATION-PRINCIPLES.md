# 接口与命令行适配原则 / Automation principles

## 中文

本原则适用于 TIA Portal、TwinCAT、CODESYS、Factory I/O 以及未来接入的软件，覆盖工程编辑、配置、编译、诊断、运行时命令和反馈读取。它是长期开发方向，不是对当前能力已全部实现的声明。各平台的已验证范围仍以对应能力报告和证据为准。

### 遇到 GUI 操作，就评估并推进接口适配

每次任务遇到需要 GUI 的步骤，先确认目标动作及软件版本，再检查已有适配器和厂商入口。不能直接把截图、点坐标或让用户手动操作当作长期方案。在已有任务授权和资源边界内，找到可用接口后应完成适配、验证和说明；确实受阻或需要扩展范围的部分，留下具体缺口和可执行的下一步。

入口选择遵循以下顺序：

1. 复用已经验证的适配器及其 CLI、MCP、API，避免重复造轮子。
2. 优先使用官方 API / SDK / COM、脚本、CLI、插件以及文档化文件接口。先确认它是否覆盖目标动作、版本、许可和操作方向。
3. 评估成熟项目的许可证、维护状态、版本兼容性和实际覆盖范围；可复用时优先复用。
4. 将可用入口接成适合 Agent 调用的语义操作。按需提供 CLI、MCP 或 API，不要求每个适配器同时提供三种入口，也不预设新增统一 HTTP Gateway。
5. 若目标动作确无可用接口，再采用受限 GUI 备选或必要人工步骤，同时明确已核查的入口、限制和替换方向。其他有接口的步骤继续使用接口。

### 什么算无 GUI 适配

调用工程 API、脚本或运行时协议完成动作，并读取软件实际结果，属于接口适配。工程软件进程或窗口仍可能存在；无鼠标不等于无需安装 IDE，也不自动意味着支持无头、锁屏或远程桌面运行。

把鼠标、键盘、UI Automation、界面 DOM 或坐标脚本包装为 CLI / MCP / HTTP，调用方虽然更方便，底层仍是 GUI 备选。应如实说明窗口、语言、前台、相机、分辨率或控件依赖，不得以“已有 API”掩盖这些限制，也不得静默从原生接口退回点击操作。

命令应面向工程语义，如“编译自有工程”“读取诊断”“发送启动请求”，而不是开放任意坐标、按键、脚本或内存写入。适配目标是可重复、可组合、可验证的操作，不是把人工点击逐条改名。

### 操作合同与实际验证

- 在文档及能力报告中逐项区分官方能力、当前实现方式、已验证范围、GUI 备选、未适配和受阻原因；不能以某个平台支持 API 推断整个适配器已可用。
- 优先结构化结果、诊断、退出状态和实际读回；截图用于辅助证据。写操作明确目标、归属和结果；有副作用的动作不确定时先查状态，不盲目重放。
- 写入和反馈继续遵循对应适配器的授权、预览、资源隔离与证据合同；“改成命令行”不构成绕过确认、权限、许可、凭据、发布限制或设备边界的理由。已有授权覆盖的常规操作不额外增加反复确认。
- 工程编译、下载、运行时变量读写是不同能力；PLC/HMI 命令和三维按钮交互也要分别报告。正常启动/停止可以通过经过验证的 PLC 命令通道实现，但不能据此声称三维按钮已被按下。
- 正常控制由 PLC 与驱动执行；Agent 发送操作级命令并观察结果。强制输入、故障注入及直接写执行器不能替代正常 PLC 控制验收，必要诊断须单独记录、隔离并释放。
- 新传输方式需有自己的验证证据；GUI 原型通过、纯单元测试通过、文档列出接口或成功建立连接，都不能替代真实工程编译、回读或软件仿真。

### 已有平台的适配方向

| 平台 | 应优先核查和复用的入口 | 需要分别验证的边界 |
|---|---|---|
| TIA Portal / PLCSIM | Openness 工程 API；匹配版本的仿真接口及运行时变量通道 | 工程 API 不等于标准 PLCSIM 所有界面操作和在线变量写入均已覆盖 |
| TwinCAT 3 | Automation Interface / DTE 做工程操作，ADS 做运行时通信 | 工程配置、编译、实例管理和命令变量读写分别绑定实际目标及证据 |
| CODESYS | ScriptEngine、`--runscript` / `--noUI` 及官方在线对象 | IDE Simulation、外部 Runtime 和真实 I/O 的能力不能互相替代 |
| Factory I/O | 官方驱动、Web API、Engine I/O SDK、控制台及官方场景/模板 | 场景控制、PLC/HMI 命令、普通按钮输入和三维编辑分别核查；Force 不冒充正常按钮接口 |

这张表说明研究顺序，不提升任何适配器的支持状态。具体缺口可通过 Issue 跟踪，例如 [#69](https://github.com/biaobiao2233/tia-guard/issues/69)；本原则独立于单个 Issue，后续适配器同样遵循。

## English

These principles apply to every current, candidate and future adapter, including TIA Portal, TwinCAT, CODESYS and Factory I/O. They cover engineering, configuration, compilation, diagnostics, runtime commands and feedback. They are development policy, not a claim that all capabilities already work.

Whenever a task needs a GUI step, identify the intended action and software version, inspect existing adapters, and assess supported vendor interfaces before using screenshots, coordinates or repeated manual work. Within the authorized task and resource boundaries, implement and verify an available interface; record precise gaps and next steps when blocked or outside scope.

Prefer, in order:

1. Existing verified adapters and their CLI, MCP or API operations.
2. Official APIs, SDKs, COM, scripting, command-line tools, plugins and documented file interfaces, checked against the intended action, version, license and direction of access.
3. Mature implementations whose licenses, maintenance and compatibility have been checked.
4. Small semantic CLI / MCP / API wrappers around those interfaces. Each adapter need not expose all three transports or add a shared HTTP gateway.
5. Bounded GUI fallback or necessary human interaction only for the uncovered steps, with researched interfaces, limitations and replacement direction documented. Keep other steps on their available APIs.

Calling an engineering API or script may still start an IDE process or show a window. Avoiding mouse interaction does not establish headless, locked-desktop or remote-desktop support. A CLI, MCP or HTTP wrapper around mouse, keyboard, UI Automation, UI DOM or coordinates remains a GUI fallback. Disclose its dependencies and never silently fall back from a native interface to clicks.

Expose semantic actions and structured diagnostics with actual readback, rather than arbitrary clicks, scripts or memory writes. Declare vendor capability, implementation transport, verified scope, GUI fallback and unavailable or blocked features separately. For uncertain side effects, inspect the result before retrying. Screenshots are supporting evidence.

Preserve the adapter's authorization, preview, ownership and evidence contracts. A command-line interface does not bypass permissions, licenses, credentials, publication controls or device boundaries; do not add repeated confirmations for routine actions already authorized. Engineering operations, simulation management and runtime communication require separate capability and evidence claims.

Prefer Openness for TIA engineering; Automation Interface / DTE and ADS for TwinCAT engineering and runtime communication; ScriptEngine with `--runscript` / `--noUI` for CODESYS; and official drivers, Web API, Engine I/O SDK and console for Factory I/O. Each requires platform-specific verification: standard PLCSIM is distinct from engineering APIs, IDE Simulation from external runtimes, and scene control from ordinary button interaction or 3D editing.

A verified PLC/HMI command channel may provide normal start/stop without clicking a scene button, but it does not prove that button was pressed. The PLC and driver retain the control loop. Forced inputs, fault injection or direct actuator writes do not qualify normal PLC control; separate diagnostic tests must be recorded, isolated and released.

Bind acceptance to the actual source, environment and new transport. GUI prototype success, unit tests, documented API availability or a connected endpoint do not substitute for real compilation, engineering readback or simulation evidence. Issues such as [#69](https://github.com/biaobiao2233/tia-guard/issues/69) track individual gaps; this repository-wide policy remains in force beyond any one issue.
