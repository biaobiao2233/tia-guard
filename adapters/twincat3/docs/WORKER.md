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
