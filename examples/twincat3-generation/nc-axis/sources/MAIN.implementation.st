// 仅软件 NC/PTP；不代表真实 EtherCAT、机械回零或安全急停。
GVL_Axis.Axis.ReadStatus();
CycleCount := CycleCount + 1;
homeEdge(CLK := GVL_Command.HomeRequest);
moveEdge(CLK := GVL_Command.MoveRequest);
resetEdge(CLK := GVL_Command.ResetRequest);

// 停止优先。MC_Stop 的 Execute 保持 TRUE 时轴保持锁定，需显式释放。
IF GVL_Command.StopRequest THEN
    stopExecute := TRUE;
    homeExecute := FALSE;
    moveExecute := FALSE;
    Step := 60;
ELSIF GVL_Command.ReleaseStop AND stopAxis.Done THEN
    stopExecute := FALSE;
END_IF;

IF resetEdge.Q AND NOT stopExecute THEN
    homeExecute := FALSE;
    moveExecute := FALSE;
    resetExecute := TRUE;
    Step := 90;
END_IF;

CASE Step OF
0:
    IF power.Status THEN Step := 20; END_IF;
20:
    IF NOT power.Status THEN
        Step := 0;
    ELSIF homeEdge.Q THEN
        homeExecute := TRUE;
        Done := FALSE;
        Step := 30;
    ELSIF moveEdge.Q AND Homed THEN
        // 在发送 NC 命令之前拒绝越界/非法速度；不会静默裁剪用户输入。
        IF (GVL_Command.TargetPosition >= 0.0) AND (GVL_Command.TargetPosition <= 100.0)
            AND (GVL_Command.Velocity > 0.0) AND (GVL_Command.Velocity <= 100.0) THEN
            AcceptedPosition := GVL_Command.TargetPosition;
            AcceptedVelocity := GVL_Command.Velocity;
            moveExecute := TRUE;
            Done := FALSE;
            Step := 50;
        ELSE
            RejectedCommands := RejectedCommands + 1;
        END_IF;
    END_IF;
30:
    IF home.Done THEN
        homeExecute := FALSE;
        Homed := TRUE;
        Step := 20;
    ELSIF home.Error OR home.CommandAborted THEN
        homeExecute := FALSE;
        Step := 80;
    END_IF;
50:
    IF moveAbsolute.Done THEN
        moveExecute := FALSE;
        Done := TRUE;
        CompletedMoves := CompletedMoves + 1;
        Step := 20;
    ELSIF moveAbsolute.Error OR moveAbsolute.CommandAborted THEN
        moveExecute := FALSE;
        Step := 80;
    END_IF;
60:
    IF NOT stopExecute AND NOT stopAxis.Busy THEN Step := 20; END_IF;
80:
    // 错误/中断停留，等待显式复位，不以延时假装命令完成。
90:
    IF resetAxis.Done THEN
        resetExecute := FALSE;
        Done := FALSE;
        Step := 0;
    ELSIF resetAxis.Error THEN
        resetExecute := FALSE;
        Step := 80;
    END_IF;
END_CASE;

// 每个 PLC 周期调用全部功能块；Execute 仅在受理时上升。
power(Axis := GVL_Axis.Axis, Enable := GVL_Command.Enable, Enable_Positive := TRUE, Enable_Negative := TRUE, Override := 100.0);
home(Axis := GVL_Axis.Axis, Execute := homeExecute, Position := 0.0, HomingMode := MC_Direct);
moveAbsolute(Axis := GVL_Axis.Axis, Execute := moveExecute, Position := AcceptedPosition, Velocity := AcceptedVelocity,
    Acceleration := 240.0, Deceleration := 240.0, Jerk := 1200.0);
stopAxis(Axis := GVL_Axis.Axis, Execute := stopExecute, Deceleration := 240.0, Jerk := 1200.0);
resetAxis(Axis := GVL_Axis.Axis, Execute := resetExecute);
Enabled := power.Status;
Busy := home.Busy OR moveAbsolute.Busy OR stopAxis.Busy OR resetAxis.Busy;
StopHeld := stopExecute OR stopAxis.Busy;
ActualPosition := GVL_Axis.Axis.NcToPlc.ActPos;
ActualVelocity := GVL_Axis.Axis.NcToPlc.ActVelo;
Error := power.Error OR home.Error OR moveAbsolute.Error OR stopAxis.Error OR resetAxis.Error OR GVL_Axis.Axis.Status.Error;
ErrorID := 0;
IF power.Error THEN ErrorID := power.ErrorID;
ELSIF home.Error THEN ErrorID := home.ErrorID;
ELSIF moveAbsolute.Error THEN ErrorID := moveAbsolute.ErrorID;
ELSIF stopAxis.Error THEN ErrorID := stopAxis.ErrorID;
ELSIF resetAxis.Error THEN ErrorID := resetAxis.ErrorID;
ELSIF GVL_Axis.Axis.Status.Error THEN ErrorID := GVL_Axis.Axis.NcToPlc.ErrorCode;
END_IF;
