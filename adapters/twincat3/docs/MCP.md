# TwinCAT MCP 接入与 Agent 流程

状态：#53 的集成候选已绑定真实 Windows Worker，并完成一次本机 MCP 错误修复、真实 XAE 编译和独立回读观察。新增独立 Usermode 仿真接口；运行验收必须另有原生证据，纯 SDK/fake-worker 测试不能替代。主机配置见 [WORKER.md](WORKER.md)，交付和验收状态以 [#53](https://github.com/biaobiao2233/tia-guard/issues/53) 为准。

## 安装与默认只读

Node >=22；依赖已在 package-lock.json 中固定。进入 `adapters/twincat3/mcp` 后运行：

```powershell
npm ci --ignore-scripts
npm run check
npm test
node index.js
```

MCP host 的 stdio 配置示例（路径替换为自己的独立 checkout，不修改全局 Skill 或系统模型配置）：

```json
{
  "mcpServers": {
    "twincat-offline": {
      "command": "C:/Program Files/nodejs/node.exe",
      "args": ["E:/AGENT/Workspace/Worktrees/TIA-Guard/tc3-57-01a12f92/adapters/twincat3/mcp/index.js"],
      "env": { "TC3_OFFLINE_WRITES": "0" }
    }
  }
}
```

该配置能发现工具，但 `doctor_twincat` 明确返回 `missing-provider` 和 `isError:true`。工具注册表示适配器知道该动作；`tools/list` 的 `tia-guard/backendState` 与 Doctor 中的 capability 区分 `supported`、`unavailable`、`untested`。COM 注册、握手或 fakeprovider 不能把真实工程能力升级为 supported。

真实 sidecar 交付后，由 host 明确配置以下环境变量：

| 变量 | 含义 |
| --- | --- |
| `TC3_WORKER_COMMAND` | 可信本地 worker 的绝对可执行路径；不经 shell |
| `TC3_WORKER_ARGS` | JSON 字符串数组，例如 dotnet 的本地 worker DLL 参数；不含 token |
| `TC3_OFFLINE_WRITES` | 默认 `0`；只有 host 明确设置 `1` 才允许 owned 离线授权/执行/回读 |
| `TC3_RUNTIME_WRITES` | 默认 `0`；同时启用离线写才允许新建自有软件仿真、状态控制和标量读写 |
| worker 自己的 allocation 配置 | 后端拥有的 workspace/scratch；MCP 请求不能赋予路径所有权 |

Node 每次创建随机 `TC3_PIPE` 和 `TC3_IDENTITY`，通过子进程环境传给自有 worker；不接受用户指定共享/default pipe，不 detached，不附着既有 daemon，不复用 `te1000-mcp`。真实 Worker 需使用本机原生绑定构建；不要把测试 fake-worker 用于实际工程。worker 启动默认 hidden，并懒加载 fresh owned COM，不能因为只读工具发现就附着用户 IDE。

## 工具与操作顺序

| 工具 | 参数 | 行为 |
| --- | --- | --- |
| `doctor_twincat` | `{}` | 只读检测环境/能力/不可用原因 |
| `inspect_twincat_project` | `projectPath` | 只读用户输入，不构建、不修改、不 attach 用户 IDE |
| `preview_twincat_generation` | `operation`, `inputDirectory`, `outputDirectory`；compile 还需 `projectPath` | 冻结请求/源码/环境、预览新输出；不写工程 |
| `authorize_twincat_preview` | `previewId` | host 已开启离线写时，从 B 执行服务申请单次 grant |
| `build_twincat_generation` | `previewId`, `grantId` | 创建 owned stage；编译并独立回读通过后才发布新输出 |
| `compile_twincat_project` | `previewId`, `grantId` | 只能构建授权绑定的 owned disposable copy，提供完整诊断 |
| `verify_twincat_project` | `executionId` | 仅回读本会话已有 owned execution；缺真实独立回读服务则 unavailable |

`operation` 接受 `build-generation`、`compile` 或 `start-simulation`。仿真预览额外要求本会话成功发布的 `executionId`、对应 `projectPath`、实际变化的 UDINT `probeSymbol`，以及 scratch 中尚不存在的 `outputDirectory`。工具参数严格拒绝未知字段，不能注入任意 COM 命令、worker token 或扩大路径权限。

| 仿真工具 | 参数 | 行为 |
| --- | --- | --- |
| `start_twincat_simulation` | `previewId`, `grantId` | 对自有副本重新编译、核验，在新分配的本地 Usermode 进程激活并验证真实执行 |
| `read_twincat_simulation` | `simulationId` | 读取实际系统/PLC 状态 |
| `set_twincat_simulation_state` | `simulationId`, `state` | PLC Run/Stop 并等待实际状态反馈 |
| `read_twincat_symbol` | `simulationId`, `symbol`, `type` | 读取 frozen source 中 Program/GVL 的标量 |
| `write_twincat_symbol` | 同上及 `value` | 写入显式声明的顶层 GVL 标量并回读；拒绝 Program 输出和指针 |
| `stop_twincat_simulation` | `simulationId` | 结束本会话创建的进程并确认退出 |

标量类型限定 BOOL/INT/UINT/DINT/UDINT/REAL/LREAL，数值需有限且不溢出。不提供用户指定 AMS 地址、端口、PID、原始 ADS 内存或任意在线工程选择。

生成输入消费 A 的 `GenerationInputLoader.Load(inputDirectory)`，固定文件名是 `twincat-generation.json`，其结构以 `../contracts/generation.schema.json` 为准。源码必须恰好等于 manifest 引用的 UTF-8 文件集合；Node 不复制该 manifest schema、不自己计算替代 source hash。MCP 的 `inputDirectory` 映射到 B 的 `GenerationRequest.SourceDirectory`，`outputDirectory` 映射到 `OutputDirectory`，所有 canonical path/ownership/environment 检查仍由 B 执行端完成。

Agent 流程：

1. 调用 Doctor，核实该组合当前可用能力和证据状态。缺 compiler/verifier 时停止写操作。
2. 准备自有输入目录，调用 `preview_twincat_generation`，review 请求、环境、动作、目标路径和 `inputHash`。
3. host 明确启用 owned 离线写后，调用 `authorize_twincat_preview`。后端签发实际 grant；Node 只返回本 MCP 会话内的 `grantId`，真实 token 保留在内存，不放进工具输出/日志。
4. 用 `previewId` 和 `grantId` 调用 build 或 compile。Node 在提交前删除本地 grant 句柄；超时/断线也不重试写操作。后端再次验证 grant 单次/expiry/sourceHash/request/provider/session/input/output，并检查新输出不碰撞。
5. 编译失败读取 `structuredContent.compilation.diagnostics` 的完整错误、file/line/column；位置缺失保持 null。根据错误修改自有 ST 输入。旧 preview/grant 无法用于改后的源码，重新执行 preview→authorize→build。
6. 只有真实 compilation 和 independent verification 全部通过、cleanup 无失败，才交付 `published:true` 的新工程；需要再核实时用成功 execution 的 `executionId` 独立 verify。

离线流程到此结束。另开仿真预览→授权→启动才能进入运行流程；它仅针对新建的自有 Usermode 软件进程。全局 ADS 路由、许可、真实硬件及既有 PLC 操作均未开放。

## 结果、诊断与退出

每次 tool call 同时返回 JSON text 和 `structuredContent`，保留 `phase`、`success`、`published`、`compilation`、`verification`、`failures`、`cleanup`。业务错误、UNKNOWN、partial capture、count/identity 不一致或 cleanupfailure 都是 MCP `isError:true`，不能用 transport `ok:true` 或进程 exit 0 判定编译成功。

Node 的编译/回读判定与 A 的真实 .NET `CompilationReport.Passed` / `VerificationReport.Passed` 做 23 项正反例比对。该比对验证前端语义一致；不能证明 C 的原生 capture 完整。发布时还检查结果与本 preview/session/input hash 的绑定。

可发布输出读取后端返回的 `outputDirectory` / `solutionPath`。失败 stage/原始证据只保留在后端分配的 owned scratch；诊断中路径或证据 pointer 不是读任意文件的许可。Node 不新增通用文件读取/执行工具。敏感字段和已知 token/identity 在两个 MCP 输出中都会脱敏；raw worker stderr 不转发给 Agent。

stdin EOF / SIGINT / SIGTERM 结束会话时，只对已认证的 worker 请求 dispose、关闭 pipe，并等待自有子进程退出；不为了清理重新连接或启动 worker。清理超时只终止直接持有的 own worker 子进程，结构化 cleanupfailure 写 stderr；不按名称杀 XAE/Runtime，不关闭用户 IDE。worker 必须在 pipe 断开时释放自身 STA/COM，仅 dispose fresh owned 对象。

## 必须继续的真实检查

- A 冻结 worker operation envelope，协调者将已发布的 B/C 服务映射到本地 sidecar；真实 compile disposable-copy 与独立 verify 不能以 fake 实现代替。
- SDK 经真实 B 服务的 grant/expiry/sourceHash/input/output/provider/session 绑定，直接 worker 绕过前端、重用/重启/碰撞测试。
- Windows 实际组合 Doctor→preview→错误 ST→build fail→修复→新 preview/grant→build→close/reopen verify，记录 exact commits、环境/证据和 owned COM 清理。
- 通过真实独立只读审查与协调者组合验收前保持 CANDIDATE；本任务不 merge/Release。

Node 无 TwinCAT 安装也能跑 `npm test`。可选 .NET 对比：

```powershell
dotnet restore ../tests/Mcp/ContractProbe/ContractProbe.csproj --configfile ../tests/Mcp/NuGet.offline.config
dotnet build ../tests/Mcp/ContractProbe/ContractProbe.csproj -c Release --no-restore
dotnet ../tests/Mcp/ContractProbe/bin/Release/net8.0/ContractProbe.dll > contract-fixtures.json
node ../tests/Mcp/contract-parity.js contract-fixtures.json
```

`contract-fixtures.json` 是临时证据，应放到自有 scratch，勿提交。仓库无 TwinCAT 原生程序集、许可、用户工程或凭据。
