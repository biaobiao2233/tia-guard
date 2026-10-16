# 编译诊断工作包（#46）

此包提供独立诊断类型与编译服务，基于协调提交 `8b5039fc80e57c543ccb69b8b304fbfde67f7995`。旧 Builder、Session、Verifier、Bridge.Worker 的接入由协调者串行完成；本包没有改变它们现有的行为。纯测试/SDK 构建不能视为真实 V21 工程编译或 #43 总体验收。

## 接口与结果

命名空间 `TiaGuard.Openness`：

```csharp
CompilationReport CompilationMessageCollector.Collect(
    object compilerResult, CompilationCollectionOptions options = null);
CompilationReport CompilationService.Compile(
    Siemens.Engineering.IEngineeringServiceProvider target,
    CompilationCollectionOptions options = null);
string CompilationReportJson.Serialize(CompilationReport report);
```

`Collect` 只读取已经返回的 `CompilerResult`，不会调用 Compile、启动或连接 TIA。纯故障注入对象须具有同名公开属性 `State/ErrorCount/WarningCount/Messages`；消息具有 `State/Description/Path/Messages`。真实 SDK 的 `CompilerResult` 可直接传入。

`Compile` 是**主动操作**，一次调用 `target.GetService<ICompilable>()`，一次调用其 `Compile()`，然后读取结果。它不初始化运行环境、选择工程、启动/连接/关闭 Portal，也不重试。调用者必须先执行已有离线副本/所有权/权限/确认守卫。不能把它列为只读检索，不能仅凭目标对象没有直接实现 `ICompilable` 判断不能编译。

结果有 `State`（`Success|Warning|Error|Unknown|Unavailable`）、`int? ErrorCount/WarningCount`、`DiagnosticsComplete`、只读 `Messages/CollectionFailures` 和 `IsSuccessful`。`RequireSuccess()` 在失败/未知/不完整时抛出；警告可单独报告，不阻止完整的零错误编译通过。通过只说明编译，不代表工程等价或控制行为正确。

外部 JSON 的必需字段是 `state/errorCount/warningCount/diagnosticsComplete/messages/collectionFailures`。消息为 `id/parentId/severity/description/engineeringPath`，严重性为 `error|warning|information|unknown`；`engineeringPath` 和根消息的 `parentId` 可空。扁平数组保持深度优先、父先于子及原有重复行；可依 id 恢复来源关系。结果只保存投影，不持有 Siemens 代理。

```json
{"state":"Unavailable","errorCount":null,"warningCount":null,"diagnosticsComplete":false,"messages":[],"collectionFailures":[{"code":"RESULT_UNAVAILABLE","location":"result","exceptionType":null}]}
```

JSON 必须通过 `CompilationReportJson.Serialize` 或由消费者显式按本字段投影；不要直接依赖其他序列化器的 PascalCase/忽略 null 策略。未知计数不得映射成 0。读取异常仅记录解包后的异常类型和结果树位置，避免异常文字泄露私有目录；工程诊断描述/工程路径本身需要消费者按现有访问控制传递。

## 采集和计数策略

采用 [Czarnak 的固定实现改编](../../third-party/compilation-diagnostics/README.md)，只保留一套采集逻辑和 MIT 版权。服务获取、递归消息、State/Description/Path 与总计数的 API 形状已核对 [Siemens V21 官方编译文档](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-for-projects-and-project-data/compiling-a-project)。这些是文档/编译形状证据，不是本候选的 V21 运行证据。

API 的顶层 `ErrorCount/WarningCount` 是结果总数。消息树可能包含汇总父节点和叶节点，不能把每个 Error 状态节点算成一个新增错误，也不能累加所有节点的 ErrorCount。测试中一个实际错误有三层 Error 消息，仍保留 API 总数 1。未根据描述文本/语言猜测汇总并删行。

保守一致性检查：零计数与任何对应 Error/Warning 行冲突，非零计数却没有任何对应严重性行，以及 Success/Warning/Error 状态与总计数冲突都使结果不完整。**不强制总数等于扁平行数/叶行数**；API 文档未保证一条叶行只表示一个错误，严格数目重算需要真实 V21 样例证据。本服务保留 API 总计数，不制造新的总数。

每个字段独立读取；某条 Description/State/Path 或子 Messages 读取失败仍可保留该条其他字段、子节点和后续兄弟。枚举器获取、MoveNext、Current 或 Dispose 失败均记录 `ENUMERATION_FAILED`，保留已收到消息。可读的空 Path 为 null；不可读/缺失 Path 是明确失败。Messages 为 null/非集合不能视为空集合。未知状态、不合法计数、null 消息、循环或无法读完整结果均不能通过。致命 CLR 异常不转为普通诊断。

默认上限：深度 16、访问节点 200、单字段 1024 UTF-16 字符、诊断文本总计 32000 字符、失败条数 64。限额按一次采集计算；无效节点也消耗访问预算。深度/消息/文本截断和失败列表省略都有代码，都会令完整性为 false；恰好达到节点/深度边界且后续为空不会误报截断。文本不拆 UTF-16 代理对。`CompilationCollectionOptions` 在主动操作之前校验并复制；最大允许值只控制本地采集，消费者还需按自身 HTTP/JSON 限额选择更小配置，不能无声删行后保持完整。

## 协调者精确接入请求

本工作包只增加规定的新文件、新测试项目及来源说明。SDK-style Openness csproj 默认包含新源文件，无需本包改共享项目文件。协调者需要将独立测试命令加入 CI；既有 solution/CI/csproj 保持其治理归属。

1. `RoundTripBuilder.cs` 的 `BuildCore`：把 `plc.GetService<ICompilable>()` 到 `compileErrors != 0` 的编译段替换为：

   ```csharp
   var report = CompilationService.Compile(plc);
   report.RequireSuccess(); // 必须早于 project.Save 和 publish
   var compileErrors = report.ErrorCount.Value;
   var compileWarnings = report.WarningCount.Value;
   ```

   后续 legacy int 结果仅在 RequireSuccess 后使用 `.Value`；需把完整 report 带回 Agent 的失败响应，不能只丢出汇总数。不要在日志或异常路径再次调用 Compile。

2. `TiaProjectSession.cs:CompilePlcForVerification`：保留 `ThrowIfDisposed`、`_scratchDirectory != null` 和 exactly-one-PLC 守卫，新增返回 `CompilationReport` 的 `CompilePlcWithReportForVerification()`；最后一段使用 `CompilationService.Compile(softwares[0])`。旧 `CompilePlcForVerification()` 应成为此方法的兼容包装，先 `RequireSuccess()`，再填 `PlcCompileObservation.Errors/Warnings`，避免在默认值上判 PASS。此包装遇到真实编译错误会抛出，协调者须同步处理下列调用点，而不是只加一个类型。

3. `RoundTripVerifier.cs`：`VerifyProjects`（当前 `observedCompile.Errors != 0`）、`VerifyRebuilt`/`VerifyCore` 的 `compile.Errors == 0` 导出条件改用完整 `report.IsSuccessful`。未知/不完整必须返回 Blocked/失败，保持 compile 的可空计数和失败证据，不导出后比较并判 pass。源比较语义保持不变。

4. `Bridge.Worker/BridgeAiWorkspace.cs:ApplyMutation`：在当前 `session.CompilePlcForVerification()` 点获取 report；任何 `!report.IsSuccessful` 走 `RejectMutation` 和已有回滚。`RejectMutation` 目前接收 int，协调者需增加结构化 report/nullable 计数参数或独立诊断字段。不能用 `?? 0` 兼容旧签名。回滚路径同文件的 Compile 调用也必须检查完整 report，失败不能宣称 rollback 成功。

5. `TiaProjectSession.cs:PublishOfflineCopyForBridge` 的编译检查同样使用完整成功条件；保留已有新目录/离线工程保存/发布守卫。`RoundTripVerifier`/`Bridge.Worker` 不得通过丢弃报告复用旧 int 结果取得成功。

6. #45 的新创建/导入引擎：在拥有的 staging PLC 上 `CompilationService.Compile(plc)`，保留 report，只有 `report.IsSuccessful` 后发布/进入回读。#47 同理，不需要等待 #44 获得诊断模型。失败时 Compile 已可能修改离线工程；不得作为只读操作，不得自动重试或操作真实 PLC。

## 检查与剩余证据

独立纯测试命令：

```powershell
dotnet test tests/TiaGuard.Compilation.Tests/TiaGuard.Compilation.Tests.csproj -c Release
dotnet build src/TiaGuard.Openness/TiaGuard.Openness.csproj -c Release -p:TiaPortalPublicApi="<installed V21 PublicAPI/net48>"
dotnet test tests/TiaGuard.Contracts.Tests/TiaGuard.Contracts.Tests.csproj -c Release
python scripts/test-contract-schemas.py
git diff --check
```

新测试直接 link 生产纯源码，不需要 Siemens DLL，也不构造 TIA 会话；调用 seam 检查 Compile 一次、不重试及配置先校验。`NullResultShowsMinimalRegressionForLegacyCoalescing` 明确复现上游 `McpServer.ProjectSession.cs:362` 的 `(ErrorCount ?? 0) == 0` 会对未知计数判成功，随后证明 Collect(null) 保留 null、完整性 false 且 RequireSuccess 拒绝。这是最小回归，不是声称已运行上游宿主或已经修复旧共享入口。

当前交付是可整合候选。确切候选 SHA、实际命令/结果与 draft PR 记录在 #46 RESULT。真实 V21 编译结果（含零错误、警告、多层错误和 Path）、共享调用点接入/失败回滚以及组合候选的独立只读审查仍需要协调者分配资源后验证；#41 资格关卡不因本包测试通过而完成。不包含 merge、Release 或真实 PLC 操作。
