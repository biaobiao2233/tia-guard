# 普通 PLCSIM 自动化（实验功能）

本功能属于 `feat/43-plcsim-automation` 候选，尚未合并或发布。完整的下载、RUN、扫描、输入输出断言尚未获得真实运行验收；查看 [OpenAPI](../src/TiaGuard.Bridge.Host/Gateway.openapi.json) 和 `/capabilities` 中的实验状态。构建或模拟后端测试通过不代表 PLC 程序运行通过。

目标是把 [从零创建工程](PROJECT-GENERATION.md) 生成的 `.ap21` 接入仿真验证，供 Agent 调用：打开副本、启用仿真编译、读取完整编译诊断、创建并上电仿真器、下载、RUN/STOP、写输入、扫描、读输出、比较预期、关闭。

## 当前范围

- Windows、TIA Portal V21、普通 S7-PLCSIM V21 的 Standard S7-1200 实例。
- 单一 CPU 1212C/V4.7；最多分别声明 64 个 Bool I/Q 地址，例如 `I0.0`、`Q0.0`。
- 使用工程原件的临时副本。附加到用户打开的工程不能用于仿真下载。
- 固定使用本机 `PLCSIM` 适配器和唯一虚拟实例。已有实例受到保护，接口不会猜选目标。
- 不提供真实 PLC 操作、强制、Safety、DB/优化块内部值读取或任意硬件配置。

软件须已正常安装、授权并具备 Openness 使用条件。普通 S7-1200 仿真还使用 32 位 .NET 8 运行时及 ASP.NET Core 运行时；Bridge 本身使用 64 位 .NET。候选启动逻辑只调整自己的 PLCSIM 子进程环境，避免继承 Bridge 的不匹配 .NET 路径，不修改用户/系统环境或软件授权。

由 Codex 等打包的桌面应用启动 Bridge 时，Worker 会通过 Windows 公开的桌面子进程策略按普通桌面进程启动，并核验其不继承调用方的应用包身份。这保留同一账户、环境、直接父子关系和标准输入输出管道，使博图可以搜索本机已安装的西门子 OAM 依赖；不复制 DLL、不修改系统 PATH 或 Openness 授权。普通桌面调用方仍使用原有启动路径。此修复已通过依赖加载、Worker 协议及进程通信验证，实际下载仍需独立验收。

## 启动

从当前候选构建 Bridge；现有公开 Release 不包含这一实验功能。安装在非默认目录时显式传入路径，例如：

```powershell
.\tia-guard-bridge.exe --transport http --port 18761 `
  --allow-write --enable-simulation `
  --plcsim-executable 'D:\Siemens\PLCSIM\PLCSIM_V21\S7PLCSIMV21.exe' `
  --public-api 'D:\Siemens\Portal V21\PublicAPI\V21\net48'
```

HTTP 只监听 `127.0.0.1`，MCP 地址为 `http://127.0.0.1:18761/mcp`。stdio 模式使用同样的功能开关。仅 `--allow-write` 不开启仿真操作；无需开启新的系统权限或全局代理网关。

## HTTP 与 MCP

### 原生桌面控制

桌面控制已接入现有 Bridge，不需要新网关、独立 CLI 或浏览器。Worker 使用 FlaUI/UI Automation 持续读取当前窗口的控件、父级标识、可用状态和输入值，再执行明确的语义动作。使用稳定标识选择控件；界面语言、禁用控件和手工改名不会被忽略。

| 操作 | HTTP | MCP |
|---|---|---|
| 查询能力与本地绑定，不启动软件 | GET `/api/simulation/desktop/status` | `get_plcsim_desktop_status` |
| 启动本 Bridge 独占的 PLCSIM | POST `/api/simulation/desktop/open` | `open_plcsim_desktop` |
| 获取当前控件快照 | GET `/api/simulation/desktop/{sessionId}` | `inspect_plcsim_desktop` |
| 创建、详情、改名、上电、关电、删除 | POST `/api/simulation/desktop/act` | `act_plcsim_desktop` |
| 自动关电、删除并正常关闭 | POST `/api/simulation/desktop/close` | `close_plcsim_desktop` |

`act` 请求示例：`{"sessionId":"返回的句柄","action":"create","name":"TiaGuard_Motor"}`。动作限于 `create`、`open-details`、`rename`、`power-on`、`power-off`、`prepare-io`、`delete`；MCP 的动作参数放在 `request` 对象中。`inspect` 返回当前已展开界面的控件，后续打开的新对话框需要重新读取。控件快照并不意味着允许任意点击、脚本或物理 PLC 操作。

`prepare-io` 接收 `inputAddresses` 和 `outputAddresses`，例如 `["I0.0","I0.1"]` 与 `["Q0.0"]`。它创建新的 SIM 视图及表格、绑定独占实例、配置地址并回读，不编辑默认表、不修改 I/Q 值。`io.prepared` 只表示配置完成，`io.valuesQualified=false` 表示实际监视值尚未验收。在 SIM 视图中看不到实例卡片时，`instanceCount` 和 `powered` 返回 `null`；关闭、上电等操作会先回到实例页重新核验。

创建和电源切换均只发出一次；不确定时读取结果，不盲目重试。电源回读核验独占实例 GUID、稳定电源控件、虚拟 CPU 进程的父子关系/启动时间/安装路径/Siemens 签名；`powered=null` 表示状态不明。未组态 CPU 即使已上电，RUN/STOP 按钮也可能禁用。关闭会先确认关电，再删除该实例；额外实例或手工改名会阻止破坏性操作，保留会话供检查。

2026-10-16 已实测 HTTP 和 MCP 的中文创建/改名/删除闭环，以及正式 HTTP 上电、关电、重复上电幂等和带电关闭清理。完整仿真链路现采用同一原生后端；RUN、SIM 表的值监视和实际 I/O 尚待验收，当前明确报未验证，不回退到浏览器或填入虚构结果。旧浏览器适配器仅保留在本地调查文件中，不随本次成果提交。

### 工程仿真

| 操作 | HTTP | MCP |
|---|---|---|
| 当前阶段/会话 | GET `/api/simulation/status` | `get_simulation_status` |
| 创建、编译并下载 | POST `/api/simulation/create` | `create_simulation_session` |
| 重试下载失败 | POST `/api/simulation/load` | `load_simulation_session` |
| 创建并执行断言测试 | POST `/api/simulation/test` | `run_simulation_test` |
| RUN/STOP | POST `/api/simulation/run` | `set_simulation_run` |
| 写声明的输入 | POST `/api/simulation/inputs` | `set_simulation_inputs` |
| 扫描 | POST `/api/simulation/scan` | `step_simulation` |
| 读实际输出 | GET `/api/simulation/outputs/{sessionId}` | `read_simulation_outputs` |
| 关闭并清理 | POST `/api/simulation/close` | `close_simulation_session` |

`create` 成功后可以分别执行 RUN、输入写入、扫描和读输出。`test` 会自行创建会话，所以不要先执行 `create` 再执行 `test`。会话独占操作，完成或失败后都须显式关闭。

输入示例（将工程路径换成实际的生成结果；这是请求格式示例，不是已通过的运行回执）：

```json
{
  "projectPath": "E:\\PLC\\MotorControl\\MotorControl.ap21",
  "inputAddresses": ["I0.0", "I0.1", "I0.2", "I0.3"],
  "outputAddresses": ["Q0.0"],
  "steps": [
    {
      "name": "initial",
      "inputs": {"I0.0": false, "I0.1": false, "I0.2": false, "I0.3": false},
      "expectedOutputs": {"Q0.0": false},
      "scans": 1
    },
    {
      "name": "start",
      "inputs": {"I0.0": true, "I0.1": false, "I0.2": false, "I0.3": false},
      "expectedOutputs": {"Q0.0": true},
      "scans": 1
    }
  ]
}
```

测试为 1–1000 步，每步 1–100 扫描。每步必须提供所有声明的输入，预期输出必须为声明输出的非空子集。接口不会把预期值填成实际值，任何缺失输出、比较失败、下载失败或最终 STOP 失败都会使 `qualified=false`。

## 失败与恢复

先看 `success`、`phase` 和诊断，不把监听端口、编译成功或创建实例当成运行通过。

- `prepare`：工程或编译未完成；检查完整编译报告，修改源码后重新生成。
- 准备和下载前分别执行硬件与软件编译。`hardwareCompilation` 是 CPU 硬件配置结果，`compilation` 是软件结果；硬件失败时不启动虚拟 CPU，软件报告为 null。软件编译零错误不代表工程可以下载。
- 缺少机密 PLC 配置保护密码会使硬件编译失败，并可能同时报告通信证书无法配置。可在操作者明确批准后，在请求中设置 `configureTemporaryPlcPassword=true`，只为本次离线工程副本的“启用保护但尚未配置密码”状态生成临时随机密码。默认 false，不修改原件、现有密码或已配置安全设置，不关闭保护，不调用在线密码设置；不返回或记录密码，副本随会话清理。该选项已在一次授权的 V21 副本上确认配置成功并清除对应两项编译错误，但后续仍因缺少具备 Full access 运行时权限的用户而失败；不能据此宣称完整编译、下载或运行已通过。
- `configureTemporaryPlcUser=true` 是另一项独立、默认关闭且需要操作者明确批准的选项：仅在离线副本创建新专用用户和自建角色，绑定目标 CPU 的稳定权限标识 `ProtectionLevelFullAccess`，不添加工程管理、Web 或 Anonymous 权限。密码只在内存生成，不输出；现有用户启用状态、角色关联及已有角色在该 CPU 的权限会做前后核对。一次创建请求不自动重放。两个选项同时启用的 V21 副本已通过硬件和软件编译（均 0 错误、0 警告），不代表下载或运行已通过。该现场案例原本没有本地用户或自建角色，非空既有用户/角色场景尚未单独现场验收。
- 状态中的 `requiredInteraction.state="waiting-for-openness-access"`：当前任务的 TIA 正等待用户确认本次 Openness 访问。状态接口只观察该窗口，不代点许可；`/api/v1/gateway/status` 的 `tia.requiredInteraction` 也提供相同信息。超时取消的请求需要关闭保留的仿真句柄，再重新开始。
- `create-instance`：PLCSIM 启动、实例创建或上电未完成；检查报告及软件的活动日志。
- `load`：已准备并创建实例，但下载失败；报告保留编译信息、唯一虚拟目标、回调类型和 SDK 异常。可用返回的 `sessionId` 调用 `load`，再次核验目标后只重试下载。
- `initialize-io`：下载后的监视表未初始化；不允许继续 RUN/输入/扫描。
- 已成功下载而后续 I/O 初始化失败时，失败响应仍保留 `load` 报告；整体 `success=false`，不会据此开放 RUN 或重放下载。
- S7-1200 V4.7 的 SIM 表读写另有权限前提：[官方 V21 手册](https://cache.industry.siemens.com/dl/files/161/109997161/att_1348284/v1/S7-PLCSIM_Help_en-US.pdf) §8.2 要求关闭访问控制，或为已启用的 Anonymous 用户赋 Full/Read access。当前临时专用用户选项不代替这个前提；候选未自动修改 Anonymous 或关闭保护，尚不能宣称已支持实际 I/O。该要求不等于当前下载连接失败的已证原因。
- `closeRequired=true`：清理或 RUN/STOP 确认没有完成；禁止继续写入，使用同一个句柄重试关闭。`running=null` 表示上一次 RUN/STOP 的状态不确定。

协调器返回的失败报告使用 HTTP 422；无效参数、未启用功能或会话不匹配使用 HTTP 400。尚未验证的输入/扫描/读输出后端异常仍可能返回 HTTP 500；完整运行链路不能据此视为可用。关闭会停止仿真、等待断电、只删除自身实例，再释放工程副本；界面删除失败时仍尝试释放工程资源，并保留可重试的句柄。原件和其他仿真实例保持受保护。

最近一次真实验证见 [2026-10-16 验证记录](verification/plcsim-native-product-20261016.md)：原生 HTTP/MCP 生命周期、上电及 SIM 表地址配置已经通过；两个安全配置选项通过硬件和软件编译（均 0 错误、0 警告）。继承桌面应用包身份导致的 DLL 搜索问题已复现并加入启动修复。随后授权的一次实际 HTTP 下载仍在连接虚拟 CPU 时失败，未进入下载回调；实例和副本已正常清理，无新增崩溃记录。下载、RUN、扫描及输入输出断言仍未验收。
