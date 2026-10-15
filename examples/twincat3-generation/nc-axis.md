# 单轴 NC/PTP 生成示例

`nc-axis/` 是可冻结的工程输入目录。新增可选 `motion` 声明通过官方 Automation Interface 创建一对 NC SAF/SVB 任务、一根连续仿真轴和 `AXIS_REF` 双向过程映像绑定，不加载已有伺服演示工程。

固定 XAE 4026.27、Tc2_MC2 3.3.75.0，PLC/NC 周期均为 2 ms。行程 0–100 mm，最高速度 100 mm/s，加减速度 240 mm/s²，Jerk 1200 mm/s³。使用前确认本机存在这些固定版本；未知版本不能静默替换。

通过现有 MCP preview → authorize → build 生成并真实编译，再 verify 保存、关闭、重开，独立核对 NC 参数及两条映射。`motion` 不提供原始 XML、I/O、远程地址、任意绑定路径。没有完整映射或参数不一致时拒绝发布/激活。默认无 `motion` 的纯 PLC 行为保留。

运行仍使用适配器自己的独立 Usermode Runtime，需要安装官方 NC/PTP 用户态组件和对应合法许可。此前纯 PLC 的成功验收不代表 NC 已运行，也不意味着已有 PLC 许可覆盖 NC；仅工程生成、编译或云端测试成功不能记运动 PASS。

命令通过 `GVL_Command` 的顶层标量输入，反馈只读 `MAIN`。默认不使能、不运动：

1. Enable=TRUE，等待 MAIN.Enabled；HomeRequest 发 FALSE→TRUE→FALSE，等待 MAIN.Homed、Step=20。这里 MC_Home/MC_Direct 是坐标置零，不找机械原点。
2. 设置 TargetPosition、Velocity；MoveRequest 发上升沿。等待真实 MC_MoveAbsolute 的 Done、CompletedMoves 递增及 ActualPosition 到达目标。可依次 30→80→0 mm。
3. 运动中 StopRequest=TRUE；等待 StopHeld 且实际速度归零；撤销 StopRequest 后 ReleaseStop=TRUE 释放 MC_Stop 锁定。
4. 目标 101 mm 或速度超出 (0,100] 在 PLC 发送 NC 指令之前拒绝，RejectedCommands 递增。复位命令由 ResetRequest 上升沿触发。

ActualPosition/ActualVelocity 来自 NC 的过程映像，没有手写积分或延时模拟完成。MC 功能块每周期调用，Execute 按反馈释放。软件运动停止不是安全急停；本例没有 EtherCAT/真实电机/驱动器，不验证机械动力学或实时性。
