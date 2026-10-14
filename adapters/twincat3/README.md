# TIA-Guard 的 TwinCAT 3 适配器

目前提供独立的 ST 工程生成、真实编译诊断和保存关闭重开后的源码核验。通过 Node stdio MCP 调用自有 Windows Worker；使用 Beckhoff Automation Interface，沿用仓库中的上游许可证声明。

已在 TwinCAT XAE 3.1.4026.27 / `TwinCAT OS (x64)` 上实际创建并编译电机逻辑示例，核对 MAIN、FB、GVL、结构体、任务调用及固定版本库。真实 MCP 测试已观察到“故意错误被拒绝 → 修改 ST → 新预览和授权 → 编译通过 → 独立核验通过”。这不等于伺服硬件或 Runtime 验收。

功能入口见 [MCP 文档](docs/MCP.md)，具体本机绑定与编译方法见 [Worker 文档](docs/WORKER.md)。输入示例位于 [motor](../../examples/twincat3-generation/motor/twincat-generation.json)。

当前限制：仅支持明确的 ST 对象、一个配置与一个 PLC 工程；未知对象、任务选项、无法完整读取的诊断都会阻止成功交付。Error List 可能异步刷新；这时仍返回 `unknown`，并从本次原生编译输出补充实际错误码和位置，供 Agent 修复。

现有用户工程只允许读取或复制到自有目录后检查；不会附着用户 IDE。当前 MCP 工具没有下装、运行、ADS 写入、硬件扫描或许可操作。运行能力将使用独立的本地 Usermode 仿真实例单独实现、验证。

编译优先级：GitHub 云端检查 → 新加坡服务器（平台支持时）→ 必须有本机 TwinCAT 的原生验证。GitHub 工作流是纯逻辑/协议测试，未安装 TwinCAT，不能作为实际 PLC 编译或运行的证明。本机回退时缓存与临时文件放 E 盘，验证结束后清理可重建产物。
