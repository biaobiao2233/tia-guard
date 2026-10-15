PROGRAM MAIN
VAR
    power : MC_Power;
    home : MC_Home;
    moveAbsolute : MC_MoveAbsolute;
    stopAxis : MC_Stop;
    resetAxis : MC_Reset;
    homeEdge : R_TRIG;
    moveEdge : R_TRIG;
    resetEdge : R_TRIG;
    homeExecute : BOOL;
    moveExecute : BOOL;
    stopExecute : BOOL;
    resetExecute : BOOL;
    Step : INT;
    AcceptedPosition : LREAL;
    AcceptedVelocity : LREAL;
    ActualPosition : LREAL;
    ActualVelocity : LREAL;
    Enabled : BOOL;
    Homed : BOOL;
    Busy : BOOL;
    Done : BOOL;
    StopHeld : BOOL;
    Error : BOOL;
    ErrorID : UDINT;
    CompletedMoves : UDINT;
    RejectedCommands : UDINT;
    CycleCount : UDINT;
END_VAR
