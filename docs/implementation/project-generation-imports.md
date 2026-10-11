# Project generation engine — Issue #45 candidate

本组件是独立的从零工程创建路径；不改变旧 RoundTripBuilder/Input/Profile/Verifier，也未接入 Worker/Gateway/GUI。工作树起点是协调提交 `8b5039fc80e57c543ccb69b8b304fbfde67f7995`，不是已合并的功能。真实 V21 创建、导入、编译、回读均未执行，因此结果为 **CANDIDATE**。

## 精确依赖与入口

- A/#44：`072c14f24fff1dbce712827886c1ba23e17cf5d5`。`PROJECT_GENERATION_SPEC_V1` 启用 `ProjectGenerationBuilder.FromValidatedSpec(ProjectGenerationSpecInput, IReadOnlyDictionary<string, ProjectGenerationObject>, string identityRevision)`。它使用 A 的 `ValidateUnchanged`、`GetSourceBytes` 和 `SourceSha256`；导入私有副本直接来自快照，绝不在检查后重新读取原路径作为导入内容。
- C/#46：`be8453e79914457901b01eb62420d1699833e492`。`PROJECT_GENERATION_COMPILATION_V1` 启用 `FromCompilationReport(CompilationReport)` 和 `BuildWithCompilation(plan, outputDirectory, ownedScratchRoot, publicApiDirectory)`，内部调用 `CompilationService.Compile(plc)`。完整 report 保留在 `FullResult`，不从嵌套消息重复累计计数，不把 null 当 0。
- 未绑定依赖时，可检查低层 `ProjectGenerationImporter.Plan`、`ProjectGenerationBuilder.Build` 和 `BuildOffline`。这是组件/替身边界，不是公共 JSON loader 或已验收的 CLI/MCP 操作。
- `ProjectGenerationEngineTag/TagTable/Artifact` 是 B 的组件 DTO，避免与 A 的同名不可变公共声明相冲突。

A 的严格 v1 模型没有块编号、编程语言、实例 FB 名称字段。适配器要求额外的**源绑定身份集合与版本**，不会从 `Main` 名称推断 OB1，或猜测自动分配编号。每个 artifact id 的 `ProjectGenerationObject` 必须与 A 声明的 kind/name 对齐；程序块还需明确 number/language，实例 DB 需 `InstanceOfName` 及 dependsOn 中的对应 FB。该身份集合应由 D 的源解析/审阅 fixture 或协调者认可的等价证据提供，并绑定 A 的源摘要。替身测试里的 `SELF-TEST-FIXTURE`/`TEST-*` 是测试值，不是真实身份接受记录。当前要求明确声明一个 OB1；额外 OB 可以存在，但没有显式 OB1 的描述不会自动保留或创建默认 Main。

## 创建、导入和失败处理

1. 源、输出、自有 scratch 必须互不覆盖；输出不存在且父目录为 plain directory。自有 scratch 必须由调用者预先分配并创建。V21 内部工程目录限制 143 字符，超长则拒绝，不改用共享短目录。
2. 全部源和已声明资源先写到 `g-<GUID>/sources/<GUID>/` 的私有目录并复核 SHA-256，然后才创建新 `WithoutUserInterface` TIA 实例。输入只读，保留原始字节；拒绝路径逃逸、ADS、重解析点及摘要变化。
3. `Projects.Create` 和 `Devices.CreateWithItem` 使用准确 `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`，回读承载 PLC 软件的 DeviceItem TypeIdentifier；不扫描或 fallback 到其他 CPU。新实例只能有一个 PLC software。
4. 明确处理 fresh Main/OB1：只允许删除这次创建 CPU 的根 `Main`、编号 1、未保护的默认 OB；出现其他对象则拒绝。导入使用 `None`，不靠 Override 覆盖已有块。多变量表创建后实际回读名字、类型、地址及常量/表集合。
5. artifact 图和 source-unit 图均拒绝悬空、循环、自依赖和重复。显式依赖优先，独立 UDT 优先、OB 最后。同一 SCL 源的所有声明合成一个导入单元，只生成一次。
6. SCL：`ExternalSources.CreateFromFile` → `GenerateBlocksFromSource(GenerateBlockOption.None)`，保留返回的完整 `IEngineeringObject` 集合，删除临时 external source；不反射重试、不启用 KeepOnError。
7. SIMATIC SD：只有 `.s7dcl` 是程序文档；可选 `.s7res` 必须显式声明且同目录同 stem，独立资源不能作为程序源。块/UDT 分别使用 `Blocks/Types.ImportFromDocuments(..., ImportDocumentOptions.None)`，必须明确 Success；PartialSuccess、Failure、null 明报失败，保留消息和实际返回集合。
8. SimaticML：根 Blocks/Types.Import(..., ImportOptions.None)，回读集合；禁止混合块/类型的同一 XML/SD 单元。
9. 每次导入比对**返回集合**，并回读**累计实际用户块/类型集合**；额外隐式实例 DB、缺失对象、语言/编号/实例/分组不符、未知类型、保护对象或先前对象漂移都拒绝。不是按 XML/源文件数当块数。编译后再次比对集合；D 的完整内容/接口回读仍是最终关卡。
10. C 编译必须执行、Success/Warning、diagnosticsComplete、errorCount=0、warningCount 非负且非空、collectionFailures 为空、完整 report 可用，才允许保存及发布。任何部分导入失败或未知编译诊断都不发布工程。
11. 关闭拥有的 project/portal 后，把工程复制到输出同父目录的新 GUID publish 临时目录，再 Directory.Move 到全新输出，拒绝竞态覆盖。异常清理仅针对本次创建并记录归属的 stage/publish，调用现有 FileSystemSafety；遇到重解析点拒绝递归删除，并在 CleanupFailures 留证据。若工程已发布而 scratch 清理失败，分别报告 Published 和 CleanupFailures，不伪称回滚。

## 最小上游复用与许可证

没有整仓移植 MCP 宿主、联网/下载路径、反射 fallback、Siemens DLL 或许可证。保留的最小调用序列经过 net48 适配和失败封闭加固。

| 上游固定提交 | 实际核查源码 | 本模块适配内容 |
| --- | --- | --- |
| [bulaofen0036-coder/TIA_Portal_Openness_MCP@3e8f881](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP/tree/3e8f8810924657a1adc4b41c0166a0829ad1caf7) | `tools/tiaportal-mcp/src/TiaMcpServer/ModelContextProtocol/McpServer.ProjectSession.cs::ScaffoldProject`，`Siemens/Portal.Software.cs::ImportPlcExternalSource/GenerateBlocksFromExternalSource` | 按类型/依赖分组、创建后导入 SCL/SD 的顺序；CreateFromFile(name,path) 调用语义。未继承 CPU fallback、per-item 失败后继续成功保存、nullable ErrorCount 默认为零。 |
| [heilingbrunner/tiaportal-mcp@6824e4d](https://github.com/heilingbrunner/tiaportal-mcp/tree/6824e4d671b403993c35d98dddf104686a950f4e) | `src/TiaMcpServer/Siemens/Portal.Sources.cs::ImportOneSource/ImportExternalSource`，`Portal.Documents.cs::ImportOneTypeSet/ImportFromDocuments` | 临时 external source 生命周期、真实生成对象分类，Types/Blocks SD 文档导入与结果集合。这里还拒绝 PartialSuccess，且不静默吞掉 unknown 对象或清理失败。 |

完整 MIT 文本在 `third-party/project-generation-imports/LICENSE.bulaofen.txt`（Copyright (c) 2026 bulaofen0036-coder）及 `LICENSE.heilingbrunner.txt`（Copyright (c) 2025 J.Heilingbrunner）。新 native 导入模块有来源版权头。项目创建、已有文件安全工具和标签回读适配本仓库实现，不取消旧拒绝条件。

API 名称/签名已对本机 **V21 PublicAPI/net48** 的 Base/Step7 DLL 及官方 XML 文档核查，并实际编译：CreateFromFile、返回 `IList<IEngineeringObject>` 的 GenerateBlocksFromSource、Blocks/Types ImportFromDocuments、ImportedPlcBlocks/Types、DocumentResultState.PartialSuccess、ImportDocumentOptions.None、InstanceDB.InstanceOfName。编译引用不会复制 Siemens DLL。

本次引用身份 SHA-256：Step7 `DFBBE3863005FA2FBC8CB553A3E7E4612ACBACA19124939EB8D29DE24C814EBA`；Base `8C12B0FA70C298F1CD1221105880752AB204ED63C9F6D9DCB68833D977BCB5D0`。这是编译 SDK 身份，不是运行工程/编译接受回执。

## 能力证据表（准确 1212C/V4.7）

| 对象 | 格式 | A 输入校验 | B V21 API 类型/Release 编译 | 实际 V21 创建/导入/编译/回读 |
| --- | --- | --- | --- | --- |
| OB | scl | 有 | 有 | 未运行 |
| FB | scl | 有 | 有 | 未运行 |
| FC | scl | 有 | 有 | 未运行 |
| DB（含显式实例绑定） | scl | 声明 envelope 有；实例身份需额外证据 | 有 | 未运行 |
| UDT | scl | 有 | 有 | 未运行 |
| OB | simatic-sd | 当前明确拒绝 | 有 | 未运行 |
| FB | simatic-sd | 当前明确拒绝 | 有 | 未运行 |
| FC | simatic-sd | 当前明确拒绝 | 有 | 未运行 |
| DB | simatic-sd | 当前明确拒绝 | 有 | 未运行 |
| UDT | simatic-sd | 当前明确拒绝 | 有 | 未运行 |
| OB | simaticml | 当前明确拒绝 | 有 | 未运行 |
| FB | simaticml | 当前明确拒绝 | 有 | 未运行 |
| FC | simaticml | 当前明确拒绝 | 有 | 未运行 |
| DB | simaticml | 当前明确拒绝 | 有 | 未运行 |
| UDT | simaticml | 当前明确拒绝 | 有 | 未运行 |

B 的底层 SD/XML 组件为后续格式校验/真实证据准备；不会绕过 A 的 capability 拒绝把它们接到公开入口。没有任意 LAD/FBD 指令、硬件模块、HMI、PLCSIM、下载/启停/force 的能力声明。

## 验证方式与 integration request

独立新项目不改 solution/共享 csproj/CI/原 Smoke Program。`GenerationSpecRoot`、`GenerationCompilationRoot` 指向上述提交的独立依赖源码树；不是默认读取本机其他工作者的可变工作目录。

```powershell
dotnet test tests/TiaGuard.ProjectGeneration.Engine.Tests/TiaGuard.ProjectGeneration.Engine.Tests.csproj -c Release -p:GenerationSpecRoot=<A树> -p:GenerationCompilationRoot=<C树>
dotnet build tests/TiaGuard.ProjectGeneration.Smoke/TiaGuard.ProjectGeneration.Smoke.csproj -c Release -p:GenerationSpecRoot=<A树> -p:GenerationCompilationRoot=<C树> -p:TiaPortalPublicApi=<V21 API>
# 仅离线 harness 检查，不启动 TIA。
tests/TiaGuard.ProjectGeneration.Smoke/bin/Release/net48/TiaGuard.ProjectGeneration.Smoke.exe --self-test
```

已执行 75/75 组合纯测试（含 A 7 个/C 7 个适配检查、61 个核心检查）；既有 Contracts 152/152、Bridge 45/45、schema 检查通过，独立组合 Smoke Release 编译通过，self-test 通过。mock 工程及编译结果不算实际 V21 证据。旧 net48 fixture 默认深输出触发路径限制时，使用本次独占短 OutputPath；Bridge 测试只在测试进程设置 DOTNET_ROOT/DOTNET_ROOT_X64 为已安装 .NET8 根，未修改系统环境。

协调者需要串行处理：

1. 将 A、C 的指定候选与 B 组合，复核新 provider 修订的兼容性；共享 `TiaGuard.Openness.csproj` 开启 `PROJECT_GENERATION_SPEC_V1;PROJECT_GENERATION_COMPILATION_V1`，纳入上述来源模块，再接 Worker/Gateway。B 未编辑共享入口、项目、CI 或 #9/#43 字段。
2. 提供绑定 A SourceSha256 的完整块语言/编号/实例身份，以及 D 的内容/接口/变量表/硬件完整回读服务。不能将 B 的 inventory 检查替代 D 的最终验证。需扩展 schema/capabilities 到 SD/XML 时由 A/协调者实施，B 不改 contracts。
3. 向本次会话明确分配独占 TIA 实例、短 scratch、全新 output 及诊断证据目录，再执行真实 V21 创建→导入→C 编译→D 回读；无此分配不启动或关闭共享 TIA/GUI/Gateway，不接触用户 canonical .ap21 或真实 PLC。
4. 对 exact 组合候选做独立只读技术审查；#41 保留现有 GUI/Gateway P0 接入资格关卡。本 PR 不代表 #43 PASS、merge 或 release。
