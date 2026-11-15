# 上游 MCP：工程副本读取/导出真实验证

2026-11-15，Windows 11 / TIA V21。固定原版上游 v2.7.11，未修改其源码、二进制或程序文件。此次验证通过真实 MCP 客户端 → TIA-Guard MCP 适配层 → 原版引擎 → Siemens Openness 完成；没有鼠标或坐标操作，没有连接实体设备。

| 验证项 | 实际结果 |
|---|---|
| 源工程保护 | 71 个文件逐 SHA-256 复制到新目录；结束再次核对源工程完全未变 |
| 实例与目标 | ConnectIsolated 新建无界面博途；先核对空实例，再从 GetProject 的实际 Path 属性核对精确副本路径 |
| PLC 结构化读取 | 从设备树解析软件名；完整读取 14 个根目录块、67 个变量 |
| 大响应 | GetBlocks、ExportBlocks 及重开后的 GetBlocks 实际触发 GetExport；连续拼接字符页后才解析，未把 20,000 字符头部当作完整结果 |
| 原生导出 | 14 份 XML；独立 XML 解析得到的块名称集合与实际 MCP 块清单完全一致，逐文件 SHA-256 一致 |
| 关闭重开 | 关闭副本、在同一专属实例重新打开、再次核对路径与块身份；源工程未打开 |
| 断连与清理 | CloseProject、Disconnect、GetState=false；MCP 清理确认引擎退出，现场无遗留博途、上游引擎、PLCSIM 或 Factory 进程 |
| 云端保护测试 | 27 个 Node 测试、4 个 Python 测试通过；语法检查通过；测试源码与本地规范化指纹一致 |

准确工具 schema、源码与原始回执指纹见[脱敏机器摘要](upstream-mcp-owned-read-20261115.json)。原生回执、工程副本、XML 与路径只留本机，不收入公开源码。

失败与修正全部保留：[首次连接](upstream-mcp-p1-start-20261115.md)曾在 Openness 确认处超时；用户选择长期允许后，同一固定 EXE 的后续实例直接连接。早期读块探测得到截断结果，读取判定保持 false；接入上游公开 GetExport 后才完成真实回读。云端一次通过 npm 运行 Python 时因该 Runner 没有 python 别名退出 127，随后以 Runner 的实际 Python 3.12.3 执行同一 4 个测试通过，未改全局别名或掩盖失败。

已验证的是 **P1 的读取/导出子集**。没有调用 SaveProject，没有验证工程改写保存、原生编译、HMI 制作/控制、PLC 下载/RUN 或整线运行；不将此结果记为完整 P1/P2/P3 通过。版本清单的 accepted / rollback 仍为空，候选等待评审，不替换发行包。

操作入口与配置见[适配器说明](../../adapters/tia-openness-mcp/README.md#自有工程副本读取)。下一步可在新工程副本上单独接入修改/编译与错误源码反例，原生保存重开回读后，再考虑与已有仿真后端衔接；原产线的暂停状态保持。
