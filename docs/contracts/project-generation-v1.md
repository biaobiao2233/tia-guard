# 从零工程创建：并行实现边界 v1

本文件是 #43 第一批并行任务的最小接口约定，基于 main `542db2f33770d145ac351e891019501bf5335b1f`。
它定义开发目标，不表示功能已经实现或通过 V21 验收。四个任务从包含本文件的同一协调提交创建独立分支。

## 第一批目标

让 Agent 以工具生成的描述和手写程序源，在全新目录创建 V21 / S7-1200 工程，支持多个程序块、变量表、UDT/DB，取得完整编译诊断，并核对已声明的工程内容。
已有 round-trip-source-v1、单块 Build/Verify 和 AI patch 的协议与接受范围保持独立，不能通过删除原有校验把新工程判为通过。
CPU 首批只承诺原有准确身份 `OrderNumber:6ES7 212-1AE40-0XB0/V4.7`。增加型号必须有独立证据；不能自动换成目录里的另一型号。
本批不包含 IO 扩展模块、网络参数写入、HMI、PLCSIM、PLC 下载/启停/强制、TwinCAT、Git 功能重做。

## 工程描述 v1 的语义边界

A 负责补 JSON Schema、纯 C# 模型与校验器。公开 JSON 字段如下，名称变化由协调者更新本约定，不能由单个消费者私下改名：

- `schemaVersion`：字符串 `1.0`。
- `tiaVersion`：字符串 `V21`。
- `projectName`：新工程名称。
- `plc`：包含 `name` 和 `cpuTypeIdentifier`。
- `tagTables`：表数组，每项 `name`、`tags`；变量为 `name`、`dataType`、`logicalAddress`。首批复用原有 12 种基本类型及地址校验。
- `artifacts`：源对象数组，每项 `id`、`kind`、`name`、`format`、`sourcePath`、`dependsOn`。
  - `kind` 是 `OB|FB|FC|DB|UDT`，实际可用对象/格式组合由显式能力表决定。
  - `format` 是 `scl|simatic-sd|simaticml`；同样不能因为格式枚举存在就宣称所有组合已支持。
  - `sourcePath` 是描述目录内的相对路径；输入只读，不可路径逃逸、重解析点逃逸或注入未声明文件。
  - `dependsOn` 引用对象 id，必须拒绝悬空依赖、循环、重复 id/同范围重名。
  - 文件中的对象身份、语言、编号/接口等必须与声明及能力边界一致；SCL 一个文件含多个对象时必须明确声明/核对生成集合，不能漏报自动生成的实例对象。
- 输出路径是命令/调用参数，不放进可移植描述；输出必须是任务拥有的全新目录，不能覆盖现有工程。

A 在本任务评论发布 `CONTRACT_READY`，列出 Schema / 模型的 commit SHA、入口方法及样例。B/D 在绑定该版本后才能声称接口实现完成；此前允许阅读上游、准备独立组件、样例和测试设计。C 的诊断模型可独立实现，由 A 在冻结时核对集成类型，不形成 A/C 互相等待。

## 编译诊断

C 提供独立的纯结果模型、消息收集器与 Openness 服务。外部结果至少包含：

- `state`、可空整数 `errorCount`、`warningCount`、`diagnosticsComplete`。
- `messages` 数组：`severity`、`description`、可空 `engineeringPath`；保留嵌套消息的来源关系或明确展开。
- `collectionFailures`：无法读取结果、消息或枚举中断的证据。

读不到编译结果不是零错误。仅在执行成功、状态有效且读取结果完整时，才可给出编译成功结论；截断或读取失败须明确报告。警告与错误分开。编译通过不等于控制行为正确。

## 构建及验证

B 提供基于已验证描述的创建/导入实现：在拥有的 staging 工程操作，按实际依赖生成对象，核对创建结果，调用 C 的编译服务；失败清理仅限自己的 staging。C 尚未交付时使用明确的接口替身，不把替身算作真实 V21 证据。

D 提供独立回读/对照与测试输入：核对准确 CPU、对象集合/所属范围、变量表及类型/地址、源内容和可核对的接口/实例关系；覆盖不完整不能 PASS。核对依据是新建工程的声明与实际回读，不要求一份已有原工程。规范化必须有证据，禁止粗暴删去未知 XML/文本或把源码变化当易变元数据。

协调者最后接入 Gateway/Worker 的 preview/apply、GUI 单次确认、新离线工程输出与失败处理，并整合各分支；各工作者不并行改桥接总入口、共享项目文件或 CI。新的验证不冒充旧 RoundTripVerifier 的接受结果。

## 源码复用

先查实际固定源码、许可证、调用依赖，再选取最小模块；保留版权、许可与来源 SHA，不整仓复制上游 MCP 宿主。

- bulaofen0036-coder/TIA_Portal_Openness_MCP：`3e8f8810924657a1adc4b41c0166a0829ad1caf7`，MIT；ScaffoldProject、SCL/SD 导入及程序对象构建。
- Czarnak/tia-portal-mcp：`7301fb809bba338719dd1d3724da055f27945653`，MIT；工程生命周期、块导入与编译诊断。空块模板不能冒充完整程序逻辑。
- heilingbrunner/tiaportal-mcp：`6824e4d671b403993c35d98dddf104686a950f4e`，MIT；Portal.Documents.cs / Portal.Sources.cs。
- siemens/tia-portal-openness-code-snippets：`4a8cc79d0666633e524e52f3335d99ff993f8830`；LICENSE.md 第 2 节对源码采用 MIT，二进制有独立条件。

已知审计点：上游 ScaffoldProject 中的 `(ErrorCount ?? 0) == 0` 可能把未读到的结果算成零错误，不能原样继承。上游一般 LAD FlgNet XML 构建器覆盖有限；优先复用官方 SIMATIC SD 文本通道，并逐项用 V21 实证，不宣称覆盖任意梯形图。

## 共享资源与接受关卡

本机 TIA/V21 测试实例、GUI、Gateway 端口、输出目录分别隔离。独立 Git worktree 不意味着已隔离这些资源。
#41 保持现有集成链路的 P0：可以并行准备实现和纯测试，但新 Gateway 正式接入/验收须先完成该关卡。#41 和 #43 的真实 TIA 执行按协调记录串行分配；没有明确资源分配时只做离线代码/纯测试，不关闭用户或其他会话的实例。
每项交付提交候选 SHA/PR、上游来源、实际执行的检查和未验证项；合并/发布不在工作者授权内。最后需要独立只读技术审查与绑定组合候选的真实 V21 证据。
