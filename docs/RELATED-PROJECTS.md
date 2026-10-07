# 相关项目、设计来源与后续改进

**中文** | [English](RELATED-PROJECTS.en.md)

[首页](../README.md) · [AI Gateway](TIA-AI-BRIDGE.md)

核对日期：2026-10-07。本页承认已有工具的价值，说明本项目的定位和可吸收的做法。比较依据是下列固定版本公开文档及本仓库记录，不是对上游的完整功能审计，也不将所有项目统称为生产级成熟工具。

## 参考关系的依据

- **已记录的 MCP 设计来源**：[Bridge 文档](TIA-AI-BRIDGE.md#设计与架构)明确列出两个 TIA MCP，并说明未整仓复制，保留自己的契约与范围。
- **Gateway 设计来源**：项目作者于 2026-10-07 明确确认参考嘉立创 EDA 专业版 Run API Gateway 扩展。配套 Skill 是该扩展公开架构的一部分；这不表示复用了上游代码、WebSocket 协议或 EDA API。
- **生态研究对象**：[#8 的 2026-09-27 研究记录](https://github.com/biaobiao2233/tia-guard/issues/8#issuecomment-5855568296)包含 CODESCRIBE、iec-checker、RuSTy、IronPLC、radevgit/plc 和 CODESYS File-Based Storage。研究对象不自动成为实现依赖或直接设计来源；这些记录不足以恢复最初讨论的完整名单。
- “范围差别”说明平台与任务不同，不根据 README 未提某功能就断言上游没有该功能。上游能力不能作为本项目已实现或验收的证据。

## 已有工具覆盖什么，如何互补

| 项目 / 固定版本来源 | 上游公开覆盖与优势 | 范围差别与可吸收做法 |
| --- | --- | --- |
| [CODESCRIBE](https://github.com/greenforge-labs/codescribe/blob/ee404ce9cf4a1d13bf3599518114fa39d77f58ab/README.md) | CODESYS ScriptEngine 的 ST/原生 XML 导出与导入、Git 审查、图形程序派生阅读视图。 | 面向 CODESYS，部分配置依赖工程模板；派生文本不是导入源。学习对象级文本、源与视图分离、export-only 和 staging 后替换。不能据此宣称已适配 CODESYS。 |
| [iec-checker](https://github.com/iec-checker/iec-checker/blob/d3e5dae2c9b5096a197e4134d7d0549201f3a953/README.md) | IEC 61131-3 静态分析、PLCopen 规则和结构化诊断。 | 可选分析层候选；不能替代 Siemens 编译与重建验证。当前未接入。 |
| [RuSTy](https://github.com/PLC-lang/rusty/blob/8032704d1c454be34a13daa54dfa51c959c09333/README.md) | Rust/LLVM ST 编译器与语言测试生态。 | 学习语言一致性与测试语料；其编译结果不是 TIA 工程或 PLC 运行证明。当前不是本项目编译器。 |
| [IronPLC](https://github.com/ironplc/ironplc/blob/0f4c100ad8440488e118c12732d915b3dfe7b230/README.md) | IEC 61131-3 工具链，含编译器、运行时、编辑器与 MCP 入口。 | 学习格式/方言边界、诊断和测试；其工具链验收不覆盖本项目 Openness 闭环。当前未接入。 |
| [radevgit/plc](https://github.com/radevgit/plc/blob/8d940638e85d4e4d721e4a94c6aa85fbf04e9455/README.md) | L5X/ST 解析、代码异味检测与可视化。 | 学习来源定位及解析/检查/视图分层；解析文件不等于厂商软件重建成功。当前未接入。 |
| [CODESYS File-Based Storage](https://www.codesys.com/ecosystem/release-lifecycle/releases-updates/file-based-storage/) | #8 记录的官方文件化存储方向。 | 本次未安装或运行核验。未来适配先评估官方入口；商业产品不能当作可复制的开源库。 |
| [Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp/blob/b11cbf230f8fa3a96f0c84c49a202a9e25b57d78/README.md) | TIA V21 MCP、持久 net48 worker、精确绑定、受保护读写及结构化结果。 | 已记录设计来源。学习分页、完整性/警告、身份变化与恢复；保留本项目默认只读、离线副本和 preview/apply。上游写策略已演进，旧研究记录不表示当前协议相同。 |
| [TIA_Portal_Openness_MCP](https://github.com/bulaofen0036-coder/TIA_Portal_Openness_MCP/blob/59b2ffaa88dcf494b2242b5b4c7d4f43f46b0cf8/README.md) | TIA V20/V21、MCP/CLI、声明式 PLC/HMI 操作、编译诊断与 VCI/Git。 | 已记录能力研究来源。学习能力矩阵、模板和工具说明，按 fixture 逐项增加；VCI 对象导出与本项目 fresh Build/Verify 的验收范围不同。 |
| [嘉立创 EDA Run API Gateway](https://github.com/easyeda/eext-run-api-gateway/blob/e55348027d1008a3d448b8b228fbca7b82dc8381/README.md) 与 [配套 Skill](https://github.com/easyeda/easyeda-api-skill/blob/b9bf568239b235592bb0c12c6ac233502b9801c1/README.md) | 外部 Agent 经本机 Bridge 访问 EDA 扩展；WebSocket 握手、连接/重连反馈、API 文档及调用流程。 | Gateway 设计参考。学习本机桥接、服务身份、连接反馈和配套文档。TIA-Guard 采用 HTTP/MCP、net48 Openness worker 及有限离线 patch；不是 EDA API 或任意代码执行接口。 |

## 本项目当前解决的具体问题

已有工具在文本化、分析、编译和工程自动化上有重要成果。TIA-Guard 聚焦受支持 Siemens 工程的可核对闭环：

`TIA V21 工程 → 确定性 canonical 源 → Git 审查 → 全新工程重建 → 编译与受支持工程语义 Verify`

canonical 源保存构建事实；`ai/`、图与摘要从它派生，不能替代构建源。GUI、CLI 和 Gateway 复用该事实来源；不支持、读取不完整或验证失败不能得到假 PASS。当前实现包含精确绑定、单次 preview token、HTTP 的 GUI 单次确认和离线副本回滚，但不能据此声称每条真实路径已验收。

- [历史 LAD 验证](verification/lad-graph.md)包含自建非空 fixture 的 Build/Compile/Verify、有限逻辑真值表与来源引用。
- [集成记录](verification/publication-20261001.md)区分纯测试、打包/启动与真实 TIA 证据；最新集成 GUI/Gateway 修改闭环仍待复验。
- [源契约](contracts/roundtrip-source-v1.md)与 [LAD 边界](LAD-GRAPH.md)规定当前范围。旧证据只属于注明的版本；工程语义一致不是 PLC 实际运行证明，也不是任意 TIA 工程支持。

## 后续吸收顺序与验收条件

以下为计划，不是新增功能或完成声明。先补证据，再扩展；不承诺固定日期。

| 顺序 | 借鉴与现有基础 | 下一步与验收要求 |
| --- | --- | --- |
| **P0：验证现有集成** | MCP 身份/故障契约与嘉立创连接反馈；已有绑定、preview/apply、回滚及 Host 测试。 | [#41](https://github.com/biaobiao2233/tia-guard/issues/41)：固定候选与非空 fixture，真实执行最新 GUI/Gateway；patch 后 Compile、再导出、fresh Build、Verify；拒绝/回滚证据分层，原工程哈希不变。 |
| **P1：改善 Git 审查** | CODESCRIBE 对象级文本与派生视图；已有 canonical、来源引用、有限 LAD 图和工程槽位。 | 先审计现有输出，再补确定性 PR 变更摘要，显示变量/网络变化与支持状态，复用 parser。同源反复生成字节一致；布局不产生逻辑误报；逻辑变更可定位原源；未知内容显示不完整；生成失败保留先前有效输出。 |
| **P2：逐项扩展逻辑与诊断** | 编译器语言/方言测试及静态诊断。 | 首个候选为自建 V21 OR 重汇合 fixture：原生导出、拓扑与小规模完整真值表、正反例和未知指令拒绝、新能力真实 Compile/Build/Verify。ST 检查等到 ST 输入契约可验证后评估；定时器、计数器和扫描顺序另立语义范围。 |
| **P3：一个只读平台入口** | 脚本/官方文件化入口、明确能力与版本矩阵。 | 环境及非空工程齐备后，只选 TwinCAT Automation Interface 或 CODESYS ScriptEngine 中一个平台/版本评估读取/导出。记录工具/设备/库版本、支持状态、重复导出及失败保留输出；无重建证据时不宣称 round-trip，也不假定现有 TIA 契约通用。 |

P1–P3 尚未开始实现。每项动手前核对现有代码与任务归属，选择一个小范围改动和验收 fixture，使用短分支。上游任务 #10/#11/#12 不自动并入核心项目。

## 致谢、复用与贡献

本次只增加比较、致谢和计划，没有引入上游代码或运行依赖。未来复用具体代码、文档或 fixture 时，记录仓库、exact commit、文件和改动，核对许可并保留所需版权/许可/NOTICE；设计参考与代码复用分别说明。第三方工程需有可再分发授权且完成脱敏后才可入库。

贡献按[指南](../CONTRIBUTING.md#plc-platform-adapters)附可复现正反例与实际环境证据，测试和维护者审查通过后考虑合并。API 连通、纯测试、工程编译、往返语义和实际运行分别记录，不能互相替代。
