# 编译诊断复用来源

本目录保留 `CompilationMessageCollector.cs` 和 `CompilationService.Openness.cs` 改编代码的 MIT 许可证。

- 上游：[Czarnak/tia-portal-mcp](https://github.com/Czarnak/tia-portal-mcp)。
- 固定提交：`7301fb809bba338719dd1d3724da055f27945653`。
- `TiaMcpServer.OpennessWorker/Openness/Block/CompileReportProjection.cs`，blob `df926ea7edda0aaafcfd85c0296300c802fb82c6`：复用父先于子遍历、单次展开、消息/深度/字段/总文本限额、空集合恰好达到限额仍完整、UTF-16 截断不拆代理对的逻辑。
- `TiaMcpServer.OpennessWorker/Openness/Block/CompileChecker.cs`，blob `7b7f54c4119f0b69ea6c760aee024c347c456786`：复用 `IEngineeringServiceProvider.GetService<ICompilable>()` 能力获取和消息 State/Path 投影；不继承反射 Compile 后备、不导入项目选择/MCP 宿主。
- `LICENSE`，blob `bdeb90ee1dba0ae72c045ecbbb0df3d4b9c4a043`；版权 `Copyright (c) 2026 Łukasz Czarnacki`。原文保留在本目录 [LICENSE](LICENSE)。

适配变更：使用 net48 兼容命名空间/语法；以纯反射读取已返回结果，保留逐字段读取失败与迭代中断；返回可空计数；增加未知/矛盾结果拒绝、父子 id、循环检测、失败列表限额和无效节点预算；任何缩短/遗漏都使 `diagnosticsComplete=false`。不从诊断行重新累加错误总数。没有复用上游 JSON 宿主或多 PLC 聚合模型。

另核查了 `bulaofen0036-coder/TIA_Portal_Openness_MCP@3e8f8810924657a1adc4b41c0166a0829ad1caf7` 的 `tools/tiaportal-mcp/src/TiaMcpServer/ModelContextProtocol/McpServer.CompilerMessages.cs` 和 MIT `LICENSE`。该路径会吞掉部分属性/子集合读取失败，且用字符串启发式分类汇总。为避免两套采集器叠加，本包未复制它的实现。它的 `McpServer.ProjectSession.cs:362` 的 `(c.ErrorCount ?? 0) == 0` 是未知计数假成功的最小回归依据。

依赖只使用项目已有 BCL/System.Web.Extensions 和 Siemens 本地 PublicAPI；测试使用现有版本的 xUnit/Test SDK。未复制 DLL、许可证二进制、TIA 工程或上游运行宿主。
