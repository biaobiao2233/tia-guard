# 原版 MCP：Classic HMI 实际工程作业与阻塞清单

2026-11-15。使用原版 `TIA_Portal_Openness_MCP` v2.7.11、TIA Portal V21，在独立实例与自有工程副本中完成画面修改、编译、保存和重开验证。直接通过官方 MCP SDK 调用原版 stdio 服务；没有新增 MCP 包装、CLI 或 Gateway，没有修改上游源码与程序文件。

本轮结果为 **离线编译、保存及重开读回完成；Runtime 未运行，触摸屏控制 PLC 未验收**。未接管用户博途窗口，未下载 PLC，未安装组件或更改许可、服务、注册表。

## 已完成的工程修改

现有 Classic HMI 软件 `WinCC RT HMI` 有 Startbild、Line_Control、Line_Diagnostics 三个画面；Line_Panel 表含 36 个标签。

在 Line_Control 上补充三个操作按钮，复用 Startbild 原有按钮的事件和请求标签：

| 新按钮 | 请求标签 | 沿用的事件 |
|---|---|---|
| CmdStart | ReqStart | Click → InvertBit |
| CmdStop | ReqStop | Click → InvertBit |
| CmdReset | ReqReset | Click → InvertBit |

这是既有请求协议的画面入口迁移；PLC 程序和握手语义未改。原页面按钮保留，Line_Control 的按钮总数由 8 增至 11；调整清场按钮、互锁说明与复位提示的位置，保留模式操作和导航。

正常工作流为 `ExportHmiScreen` → 修改导出的 XML → `ImportHmiScreen` → `CompileAndDiagnoseHmi` → `SaveProject` → `CloseProject` → `OpenProject` → 再导出并编译。没有使用鼠标、固定坐标或前台窗口操作。

实际验证：

- 基线、修改后和重开后编译均为 **0 错误、26 个已有警告**；没有把 Warning 状态写成无警告。
- 独立 XML 解析确认三个按钮的事件、请求标签和位置在重开后仍存在，11 个按钮、366 个对象 ID 无重复。
- 原工程 71 个文件逐 SHA-256 核对未变；本轮独立 MCP/TIA 会话已正常关闭，用户原有三个博途进程及创建时间保留。
- 未启动 HMI Runtime；未通过画面进行运行时或视觉排版验收。XML 绑定存在不代表通信已经成功。

## 待专门解决的阻塞

| 编号 | 当前观察和影响 | 下一步与完成条件 |
|---|---|---|
| HMI-01：连接未解析 | 标签 XML 引用 `HMI-Verbindung_1`；GetHmiConnections 返回空，ExportHmiConnection 对该名返回 NotFound。经准确网卡节点诊断，PLC 与 HMI 均在 Line_Simulation 子网；上游白名单服务扫描未发现可用的 ConnectionComposition。同一子网不能证明 HMI 通信成立，也不能仅据工具空结果断言工程绝无连接。 | 核对 Classic HMI 的实际逻辑连接、驱动与目标软件 CPU。优先复用有效连接模板或上游现成连接导入。需在副本读回连接参数，再由 Runtime 读到真实 PLC 变量并验证请求/应答；不要把 Unified 专用创建工具当 Classic 修复。 |
| HMI-02：旧报警缺触发标签 | 原有离散报警 ID 1–25 均报触发标签缺失或无效，属于原有 26 个警告中的 25 个。本轮没有删除报警来压低计数。 | 为每项报警明确故障来源和触发位，再绑定、编译，并做真实故障触发与清除；不随意绑定到同一个占位变量。 |
| HMI-03：Runtime 尚未验证 | 本次安装清单含 V21 Runtime Advanced Simulator、V21 Driver 与旧 V17 Runtime。不能直接归因为没有 V21 仿真器；本轮没有重新复现历史 Qt/DirectX 弹窗。 | 单独核对匹配 V21 的受支持启动入口、进程路径、诊断和许可；连接修复后在专属软件 CPU 实测。此前“暂不下载 Unified Runtime”仍有效；不附带修改共享 COM、系统 DLL 或许可证。 |
| HMI-04：中文字形警告 | 另 1 个编译警告指出 Tahoma 不支持部分中文。已有部分画面对象使用宋体，不能以全局替换字符串就认定问题已解决。 | 定位 Runtime 语言和实际使用该字体的对象，使用目标支持的中文字体，重编译并在 Runtime 观察文字。 |

HMI-01 是实际控制验收的前置项。后续采用新工程副本逐项处理；需要新的组件下载、许可或共享系统变更时，将具体动作和影响留在任务中供用户集中解决。普通查询、画面编辑、编译与保存继续直接使用原版 MCP。

## 已有替代流程，不需要重复开发

| 使用中遇到的限制 | 实际采用的现成办法 |
|---|---|
| GetHmiScreenItemDetails 明确只支持 Unified，拒绝 Classic 画面 | 按工具提示使用 ExportHmiScreen；编辑后 ImportHmiScreen、真实编译及重开读回。本轮已用通。 |
| GetHmiTagDetails 的默认属性在 Classic 上返回部分 null | 通过 ExportHmiTagTable 的 XML 读取地址与连接链接；不把 null 当变量未配置。 |
| 硬件诊断使用含 `/` 的设备展示名时无法解析 PLC | 根据 GetProjectTree 改用 PLC_1 和 IE_CP_1，读到双方准确网卡及子网；属于调用参数修正。 |

## 操作和后续验收顺序

1. 使用本机交接记录指向的完整工程副本；原工程及用户正在编辑的工程不动。
2. 先解决 HMI-01。复用请求标签时保留现有 InvertBit 事件，不擅自改成持续写输出或未经确认的置位/复位协议。
3. Runtime 正常启动且连接读回后，在界面核对已有接管/自动模式，再操作新增启动、停止、复位按钮，记录请求位、PLC 应答、运行状态与输出。当前尚不能给出“按钮已可控制产线”的操作承诺。
4. 实测停止撤输出、复位条件和至少一个报警；确认物料、计数和复位策略后再恢复自动运行。禁止通过 Factory API 覆盖 PLC 输出来制造成功证据。

原始工程、XML、完整编译和 MCP 记录、安装清单、进程记录与备份清单保留本机，不入 Git。用于准确定位本轮证据的 SHA-256：

| 证据 | SHA-256 |
|---|---|
| 最终原始 MCP 工程作业记录 | `1fcacf2052aba8df5cb49cc99a105e5864f6f4081dcf976499761dcfe5877a82` |
| 重开后 Line_Control XML | `81c992eef5f5b1cb24ba635192d90227b26e122825e2b1199f6192d7c86c82ed` |
| 实际导入的 Line_Control XML | `7b98a995491460fdd40eb9587d726f9123bceb9ff1fb46af25116d3cf9123465` |

上游固定源提交 `70764a118bc74eb11adc39e0936d0181882acfa3`，原版 EXE SHA-256 `a2c6406920c42ebc9d248f9489aa3d1c211b71fe4177bb7e474cdbed02d3c9ea`。本记录只覆盖所列操作，不等于全部 238 个工具已经实测或 Runtime 已验收。
