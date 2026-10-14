# Windows Worker 与现场验证

Worker 入口：`src/Worker/TwinCat3.Worker.csproj`。MCP 会启动一个私有 Worker，使用随机命名管道和身份绑定；不连接共享 daemon。每次原生生成、编译或核验都使用新的自有 IDE，避免继承上次的错误列表。只读 doctor、inspect 和 preview 不创建 COM。

主机配置：

| 环境变量 | 含义 |
| --- | --- |
| `TC3_WORKER_COMMAND` | Worker exe 的绝对路径 |
| `TC3_WORKER_ARGS` | JSON 字符串数组，通常为 `[]` |
| `TC3_WORKSPACE` | 本会话独立目录，预先创建 |
| `TC3_SCRATCH` | workspace 内的独立临时目录，预先创建 |
| `TC3_INTEROP` | 本机生成的 TCatSysManagerLib DLL，不能提交到仓库 |
| `TC3_BUILD` | 明确选择的已安装版本，例如 `4026.27` |
| `TC3_OFFLINE_WRITES` | 显式设置为 `1` 才允许自有目录中的离线工程写入 |
| `TC3_RUNTIME_WRITES` | 默认 `0`；与 offline-write 同时为 `1` 才开放新建自有 Usermode 仿真实例 |

`TC3_PIPE` 与 `TC3_IDENTITY` 由 MCP 每次自动生成，不应配置固定值。

在具备同版 XAE 的 Windows 编译机上构建：

```powershell
dotnet build adapters/twincat3/src/Worker/TwinCat3.Worker.csproj -c Release -m:1 /p:UseSharedCompilation=false /p:TwinCatInteropAssemblyPath="<machine-local-interop.dll>" /p:TwinCatVsPublicAssemblies="<XAE-PublicAssemblies>" --disable-build-servers
```

不要把供应商 DLL、Runtime 配置、许可证或整份现场报告上传到公共 CI。没有提供本机 DLL 时只能构建纯逻辑版本，它会明确拒绝原生操作。

原生闭环验证入口：`tests/Worker/native-mcp.test.js`。它必须显式启用，创建全新目录并保留真实报告；默认 CI 不执行：

```powershell
$env:TC3_NATIVE_TEST='1'
$env:TC3_NATIVE_TEST_ROOT='<new-absolute-qualification-directory>'
$env:TC3_INTEROP='<machine-local-interop.dll>'
node --test adapters/twincat3/tests/Worker/native-mcp.test.js
```

验证包括故意 ST 编译错误、拒绝重复授权、修复后创建新预览、完整编译和独立保存重开核验。错误列表不完整时维持 UNKNOWN；日志里的实际 C0046 等诊断仍可用于修复，不把 UNKNOWN 算作成功构建。

电机逻辑示例显式依赖 Tc2_Standard 3.4.7.0、Tc2_System 3.10.2.0、Tc3_Module 3.4.7.0。空 PLC 模板不会替这个示例补齐所有依赖。4026 自动生成的 Tc3_GlobalTypes 系统引用、TMC 与 ADT 任务模块需要核验身份、默认字段和时序；额外未知配置仍拒绝。

## 独立软件运行验证

`TC3_RUNTIME_TEST=1` 在上述原生入口中选择运行验证：先真实创建、编译并独立核验正确示例，再创建独立 Usermode 进程，复制供应商模板到新 scratch，分配新 AMS 身份。仅在该进程和配置身份仍匹配、工程没有硬件/NC 配置时激活；本切片支持 PLC 软件仿真，不包含运动轴或 EtherCAT 验证。

启动必须观察实际 `MAIN.Motor.Status.CycleCount` 变化并核对运行应用名称。后续测试通过 ADS 验证 Start/Stop/Fault/Reset、PLC STOP 时计数冻结、RUN 恢复以及自有进程退出。任何超时、许可问题、部分编译诊断或回读不完整都保留失败证据，不改写为 PASS。

应用身份核对区分官方 `_AppInfo.AppName`（本切片为 `Port_851`）、`ProjectName`（冻结输入的 PLC 工程名）与 `AdsPort`（851）。变量元数据通过已安装 ADS 4 的官方 SymbolLoader 和公共接口读取，校验原始类型、字节数、指针/引用和只读标记；旧版 `ReadSymbolInfo` 的 Type/Size 契约不能按新 SDK 的 TypeName/ByteSize 动态访问。字段定义见 [官方 PlcAppSystemInfo](https://infosys.beckhoff.com/content/1033/tc3_plc_intro/714818699.html)。

`TC3_RUNTIME_HUMAN_LICENSE=1` 让原生运行测试使用 prepare/resume 流程。prepare 保留本会话拥有的进程与 IDE，返回明确的 `executionReady=false`；用户处理官方许可并确认后，验证操作者在本次新测试根目录写入 `runtime-license-ready.txt`，测试才调用 resume。等待最多 10 分钟，超时维持失败并清理自有资源。该文件是测试协调信号，不是 MCP 接口或许可证。

运行库使用官方 NuGet `Beckhoff.TwinCAT.Ads` 7.0.389 的状态契约；在已验证主机上，通信复用本机 XAE 安装的官方 ADS 4 客户端和 `Common64/TcAdsDll.dll`。现代 SDK 的默认路由通道未能解析该主机的新建 Usermode 地址，因此不能仅凭 NuGet 恢复成功认为通信可用。旧版客户端隔离加载，禁用 GUI 同步；供应商 DLL 不随项目分发。Worker 需要 .NET 8 Windows Desktop Runtime，Usermode 可执行文件与 ADS 客户端需来自官方安装。

2026-10-14 的实际 MCP 复测完成从零生成、真实编译和独立核验；仿真副本也达到 0 错误/0 警告。启动没有通过：独立实例退回 Config，PLC 的 851 端口不可用。随后在另一个自有诊断实例中，官方 IDE 明确报告 `license not found`。精确源版本、证据哈希及未验证项见 [运行观察记录](../evidence/runtime-20261014.json)。这不是运行验收 PASS。

本工具不安装驱动、配置系统路由或生成/复制许可证。新建实例必须具备相应的运行许可；已有 Servo 演示的许可状态不能推定适用于新实例。许可阻塞需要用户在该实例对应的官方许可页面处理，试用验证码由用户本人输入。处理后还需完整重跑 MCP 原生测试，观察实际 PLC 计数变化、应用身份、命令反馈、Run/Stop 和进程退出；只消除许可错误不能替代这些验收。
