# #47 从零工程回读验证候选

本包基于派发 `8b5039fc80e57c543ccb69b8b304fbfde67f7995`。Status: **CANDIDATE**。
纯比较、输入快照绑定及实际 V21 SDK 构建证据，不等于真实工程创建/编译/回读验收。
具体候选提交、draft PR、检查回执由 [Issue #47](https://github.com/biaobiao2233/tia-guard/issues/47) RESULT 绑定。
没有原 `.ap21` 输入；未启动 TIA/GUI/Gateway，也未操作 PLC。

## 已交付入口

```csharp
ProjectGenerationVerificationResult ProjectGenerationVerifier.Compare(
    ProjectGenerationVerificationExpected expected,
    ProjectGenerationVerificationObservation actual);
ProjectGenerationVerificationObservation ProjectGenerationReadback.Capture(
    Siemens.Engineering.Project ownedOfflineProject,
    string newEvidenceDirectory,
    Func<string, string, string> projection = null);
// 在组合候选启用 PROJECT_GENERATION_SPEC_V1：
ProjectGenerationVerificationExpected ProjectGenerationVerificationSpecAdapter.FromSpec(
    ProjectGenerationSpecInput input,
    string independentEvidenceReference,
    IReadOnlyDictionary<string, ProjectGenerationVerificationObject> interfaceEvidence = null);
```

比较结果是 `PASS|FAIL|UNKNOWN`，只针对工程声明/回读一致性。编译结果和机器控制行为
分别报告，不能从此 verdict 推断编译成功、仿真成功或父任务 PASS。
发现确定差异优先 FAIL；任何不完整/未知字段或读取失败都禁止 PASS。
逐项 `Compared`、`Findings` 和 A 的 `ExpectedSourceSha256` 进入回执。
此 DTO 是独立对照模型，未取代 A 的 Project Generation 描述。

核对 CPU 精确身份、project/PLC 名称、V21/API 版本、完整程序/UDT/变量表集合与所属范围、
对象 kind/language/显式编号、变量类型地址、全部代码/资源文本、可读接口和实例目标。
对象身份使用长度前缀，名称含分隔符不能碰撞。缺失/新增、同范围重复、未知/部分读取
都写账本；空声明不能获 PASS。声明的自动编号可不设 Number，显式编号必须核对。

`transport-text-v1` **仅**去除开头 BOM 并统一 CRLF/CR 为 LF；不 trim 空白、不删除注释、
不删未知 XML/文本、不重写变量/指令、不删除时间戳或其他 metadata。未知内容在被比较
的文本和 InterfaceXml 中保留比较意义。SimaticML 模式比较全文；独立 XML 辅助器只投影
接口/实例，不声称校验 XML 的其他属性。所有原始 XML 都留在证据目录供完整审查。
未来若需要比较额外 XML 属性，必须增加独立期望及覆盖项，不能借接口 PASS 推断全文等价。

SCL 原输入与 GenerateSource 文本即便只因格式/注释变化，也会保守 FAIL；跨 SCL/SD
格式为 UNKNOWN。Grouped SCL 输入的逐对象拆分尚无证明，适配器明确 ContentComplete=false。
**不得复制待验工程 observation 生成 expected，也不得使用输入哈希代替回读正文。**
接口 XML 和实例关系无法从 A 的描述直接推导时，适配器保持 UNKNOWN；只有有独立来源的
接口期望才能补齐。严禁给空默认值然后判接口/实例通过。

## 回读边界

Capture 接收协调者已经拥有的 V21 离线 staging/copy；它不负责启动、附加、关闭、保存、
编译或删除工程。输出必须全新并拒绝重解析祖先；对象导出目录使用数字序号，工程对象名
不会成为 filesystem 路径。证据目录不自动清理。

遍历根/分组/未分组设备及 DeviceItems；要求一个设备和一个 PLC。扫描用户块、系统块、
UDT 和分组、变量表/分组；单对象失败仍保留其他对象，枚举中断有失败账本。
额外外部源/技术对象/常量及 unit-scoped 对象为未支持，不能忽略。
V21 `GetService<PlcUnitProvider>().UnitGroup.Units/SafetyUnits` 检查已按安装 SDK 编译。
provider 为 null 时，仅对准确 S7-1200 配置采用官方软件单元的 S7-1500 边界并写 CoverageNotes；
这是文档能力推断，API 异常仍 UNKNOWN，不代表本候选已经运行过。

默认 `simatic-sd` 调用 PlcBlock/PlcType.ExportAsDocuments；`scl` 调用 GenerateSource(None)，
DB/UDT 回读文件扩展名为 `.db/.udt`，代码块为 `.scl`。`simaticml` 保留完整原 XML。
文档模式保留 State、Messages、ReportedDocuments 与实际文件；仅 Success 且两套文件完全一致、
UTF-8 严格可读、非空才标记完整。缺失/新增 `.s7res`、未知扩展名、外逸路径、重复文件均拒绝。
正文不截断。源内容摘要取实际回读字节，原输出同时保留，不能拿生成器输入缓存冒充回读。

## 样例及已执行/未执行矩阵

`examples/project-generation-v1/verification/fixture-plan.json` 是 D 的核对/依赖计划，
**不是** A 的可执行 spec，不新增公共 JSON 契约。

| 场景/检查 | 当前证据 |
| --- | --- |
| SCL：MotorStatus UDT、Status 全局 DB、上游 FB_BasicLatch、LatchInstance DB、Main OB；IO 表 4 个 Bool 输入/1 个输出 | A@072c14f 实际 Loader 在任务自有测试目录加载全部 5 源和 5 tags；纯 envelope/path/type 检查通过，V21 语法/导入未执行 |
| LAD：LadContactCoil FB 的一段 Contact/Coil SIMATIC SD | 有实际候选源，完整性/hash 检查通过；无 `.s7res`、无资源引用。只声明这两个指令；V21/1212C V4.7。A 当前拒绝 SD，未导入/编译 |
| 比较与文件/XML正负例 | 独立 62 tests；绑定 A@072c14f 共 63 tests，通过 |
| Release Openness / Smoke | 注册表 V21 SDK `D:\Siemens\Portal V21\PublicAPI\V21\net48`，构建 0 warning/error |
| D 全部生产源码 + A@072c14f 适配器 | 隔离组合项目启用 PROJECT_GENERATION_SPEC_V1，真实 V21 SDK Release build 0 warning/error；不等于 B/C/D 组合运行 |
| 旧契约 / Smoke self-test / schemas | 152 tests、self-test、3 组 schemas 通过；self-test compile 是 unknown/not-observed |
| B 创建 + C 编译 + D 回读的真实组合 | 未执行；等待 B、组合审查、#41 和明确实例/目录分配 |
| 控制行为、仿真器、真实 PLC | 未执行、未授权，不宣称行为验收 |

样例 program transport-text SHA-256 在 `verification/source-hashes.json`；这是 fixture 完整性
证据，不是实际导入/生成工程的证据。上游固定 commit/Git blob SHA-256/许可和改编范围见
[来源目录](../../third-party/project-generation-verification/README.md)。SD 文件被全仓库
`.gitignore` 忽略，本包仅显式追踪自己的 `LadContactCoil.s7dcl`，没有修改共享 ignore 规则。

## 精确 integration request

1. 绑定 A/#44 `072c14f24fff1dbce712827886c1ba23e17cf5d5` 的 Spec/Loader 源码与接口；
   本包 `ProjectGenerationVerifier.Spec.cs` 按 `PROJECT_GENERATION_SPEC_V1` 编译。
   协调者在共享 csproj 定义该符号并在 CI 增加独立测试命令，本包不改共享文件。
   standalone 不需要 A；组合测试用 `GenerationSpecSourceDirectory=<pinned-A-src>`。
2. A 当前所有 `sourcePath` 必须 `.scl`，D 的 UDT/DB 输入也采用 `.scl`；回读时才使用 `.udt/.db`。
   把 fixture-plan 的 SCL objects/tags 在**新的拥有目录**投影成 A 的 `project-generation.json`，
   id 中 `-` 换成 `_`；仅复制 5 个声明源，不混入 plan/hash/README 等未声明文件。
   因 A root 必须恰好含声明文件，不应从父目录用 `../programs` 逃逸。
   具体静态投影已由 SpecAdapterTests 对真正 Loader 测试。A 的 specs 目录仍归 A。
3. B 绑定同一输入快照，使用 GetSourceBytes 私有副本导入、每个源路径只导入一次；
   C/#46 `be8453e79914457901b01eb62420d1699833e492` 在拥有 staging 上 Compile(plc)，
   保留 CompilationReportJson；RequireSuccess 后进入 D 回读。Capture 自身不主动编译。
   这个绑定是接入要求，尚无 B/C/D 整合运行证据。
4. 将 A 输入和有独立来源的接口/编号/实例期望交给 FromSpec。保留所有默认/自动生成对象，
   不通过过滤待验 inventory 或复制 actual 的接口补齐 expected。默认空表/初始 OB 等若存在，
   由 B 明确处理或作为声明差异，不能无声放行。
5. 对 SCL 选择 `projection=(scope,name)=>"scl"`；SD 仍为候选，需 A 新能力/输入身份验证
   和单独真实证据才能开放。不能只因为 `.s7dcl` 已追踪就改 A Supports 放行。
6. 独立只读会话审查 exact 组合候选；协调者分配专有 TIA/offline 目录后，执行创建→C 编译
   →D 回读，补全合法规范化和接口投影证据，再运行负例。不能扩大旧单 OB1 accepted Verify。

完整运行 receipt 至少记录 A/B/C/D+协调提交的精确 SHA、project spec/raw source hashes、
TIA build/API 21.0.0.0/CPU 身份、fixture 来源、创建/编译原始输出、D observation.json 和
全部回读文档的 raw SHA-256、逐项已执行/未执行矩阵、comparison verdict、独立审查结果。
生成工程资产只放拥有的新目录，不提交 `.ap21`/Siemens DLL/客户材料。合并、发布和真实 PLC
不在本包授权内。

## 重现检查

```powershell
dotnet test tests/TiaGuard.ProjectGeneration.Verify.Tests/TiaGuard.ProjectGeneration.Verify.Tests.csproj -c Release
dotnet test tests/TiaGuard.ProjectGeneration.Verify.Tests/TiaGuard.ProjectGeneration.Verify.Tests.csproj -c Release -p:GenerationSpecSourceDirectory=<A-source-at-072c14f>
dotnet build src/TiaGuard.Openness/TiaGuard.Openness.csproj -c Release -p:TiaPortalPublicApi="D:\Siemens\Portal V21\PublicAPI\V21\net48"
dotnet build tests/TiaGuard.Openness.Smoke/TiaGuard.Openness.Smoke.csproj -c Release -p:TiaPortalPublicApi="D:\Siemens\Portal V21\PublicAPI\V21\net48"
tests/TiaGuard.Openness.Smoke/bin/Release/net48/TiaGuard.Openness.Smoke.exe self-test
dotnet test tests/TiaGuard.Contracts.Tests/TiaGuard.Contracts.Tests.csproj -c Release -p:BaseOutputPath=<owned-short-output>
python scripts/test-contract-schemas.py
git diff --check
```

schemas 使用会话可读的 jsonschema 4.26.0，未改全局 Python。第一次 default C: SDK 路径构建
失败（程序集不存在）；从 V21 注册表恢复 D: 正确路径后通过。初次 A 集成测试对 SourceSha256
误计 descriptor：A 仅公开 5 个程序源 hash，断言已按实际契约修正并重测。

## 官方 API/格式依据

- [V21 Generate source from block](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-for-accessing-the-data-of-a-plc-device/blocks/generate-source-from-block)：文本回读通道与 None 依赖选项。
- [V21 Exporting program block as document](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-data-of-a-plc-device/blocks/exporting-program-block-as-document)：State/Messages/ExportedDocuments 结果。
- [V21 Exporting UDT as document](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-data-of-a-plc-device/blocks/exporting-udt-as-document)：UDT 文档回读入口。
- [V21 SIMATIC SD format](https://docs.tia.siemens.cloud/r/en-us/v21/creating-and-managing-blocks/exporting-and-importing-blocks-in-simatic-sd-format-s7-1200-s7-1500-s7-1200-g2/exporting-and-importing-blocks-in-simatic-sd-format-s7-1200-s7-1500-s7-1200-g2)：s7dcl/s7res、编号和格式能力边界。
- [V21 block/DB/UDT attributes](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/general-functions/setting-attributes-of-blocks-dbs-udts)：接口 XML、InstanceOfName。
- [V21 Programming a PLC](https://docs.tia.siemens.cloud/r/en-us/v21/programming-a-plc)：软件单元 S7-1500 范围。

文档和 SDK 编译仅证明通道/签名可用；不把它们当真实创建或回读结果。
