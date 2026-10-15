# TIA-Guard 的 TwinCAT 3 适配器

目前提供独立的 ST 工程生成、真实编译诊断和保存关闭重开后的源码核验。通过 Node stdio MCP 调用自有 Windows Worker；使用 Beckhoff Automation Interface，沿用仓库中的上游许可证声明。

已在 TwinCAT XAE 3.1.4026.27 / `TwinCAT OS (x64)` 上实际创建并编译电机逻辑示例，核对 MAIN、FB、GVL、结构体、任务调用及固定版本库。真实 MCP 测试已观察到“故意错误被拒绝 → 修改 ST → 新预览和授权 → 编译通过 → 独立核验通过”。这不等于伺服硬件或 Runtime 验收。

功能入口见 [MCP 文档](docs/MCP.md)，具体本机绑定与编译方法见 [Worker 文档](docs/WORKER.md)。输入示例位于 [motor](../../examples/twincat3-generation/motor/twincat-generation.json)。

当前限制：仅支持明确的 ST 对象、一个配置与一个 PLC 工程；未知对象、任务选项、无法完整读取的诊断都会阻止成功交付。Error List 可能异步刷新；这时仍返回 `unknown`，并从本次原生编译输出补充实际错误码和位置，供 Agent 修复。

现有用户工程只允许读取或复制到自有目录后检查；不会附着用户 IDE。新增 MCP 运行接口使用 Beckhoff 官方 Usermode Runtime 和 ADS SDK，只能分配新的自有 PLC 软件仿真实例，提供 RUN/STOP、标量读写及退出。原生运行验收与云端纯测试分别记录；工具注册不等于已通过运行验收。不会操作既有 Runtime、EtherCAT 硬件、全局路由或许可证。

2026-10-15，源版本 `ba5e635` 的实际 MCP 软件 PLC 闭环已通过：从零生成、编译、独立重开核验，人工完成官方试用许可后恢复编译与实际 Run，观察循环计数变化、Start/Stop/Fault/Reset 反馈、PLC Stop 时计数冻结，以及自有实例与 IDE 退出。精确来源、原始证据哈希和验证边界见[运行验收记录](evidence/runtime-20261015.json)。这不涵盖 NC/PTP、EtherCAT 或真实电机。

后续源版本 `96397df` 已通过两个独立 MCP/Worker 会话复用同一专用实例配置和原有有效许可的原生验证：每次启动新的自有进程，保持实例身份与许可文件字节不变，两轮均真实编译、核验、Run 和反馈通过；Stop 后计数冻结、恢复 Run 后计数再次变化，最终进程退出并保存新的停止凭据。见[持久化复用证据](evidence/persistent-runtime-20261015.json)。通过可信 host 的 `TC3_RUNTIME_INSTANCE` 显式启用，默认仍是临时实例；试用到期仍需本人处理官方许可，文件到期提示不代表签名或功能授权验证。

编译优先级：GitHub 云端检查 → 新加坡服务器（平台支持时）→ 必须有本机 TwinCAT 的原生验证。GitHub 工作流是纯逻辑/协议测试，未安装 TwinCAT，不能作为实际 PLC 编译或运行的证明。本机回退时缓存与临时文件放 E 盘，验证结束后清理可重建产物。

新增可选 `motion` 输入，可从零创建一对 NC SAF/SVB 任务、一根连续仿真轴，配置动态参数、软件行程限位，并在 PLC 编译后建立 `AXIS_REF` 的两条映射。保存关闭重开后从原生工程独立读取参数和映射，再与冻结输入比较；未显式声明的 NC、真实 I/O、缺失/偏移/外部映射会被拒绝。参见[单轴示例与操作](../../examples/twincat3-generation/nc-axis.md)。原有纯 PLC 验收和许可不能自动延伸为 NC/PTP 运动验收，实际结果以对应证据为准。

2026-10-15，源版本 `9ece75f` 已实际通过 NC 从零生成、真实 PLC 编译、发布前关闭重开、独立副本重开以及运行副本重编译和核验。首次运行因缺少 NC 许可阻断，原始失败与清理异常保留在[分层验证记录](evidence/nc-axis-20261015.json)。用户本人完成同一专用实例的官方试用后，实际 MCP 恢复编译/核验和系统/PLC Run，完成 30→80→0 mm 三次真实 NC 定位、运动中停止并释放、101 mm 越界拒绝、复位及受保护输出写入拒绝，最终自有进程退出且实例干净停止；见[NC 运动 PASS 证据](evidence/nc-motion-20261015.json)。坐标置零使用 MC_Home/MC_Direct，反馈来自 NC 过程映像，仍不代表真实硬件、机械回零或安全验证。
