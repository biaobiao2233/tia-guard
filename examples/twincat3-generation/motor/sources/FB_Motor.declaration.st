FUNCTION_BLOCK FB_Motor
VAR_INPUT
    Start : BOOL;
    Stop : BOOL;
    Fault : BOOL;
    Reset : BOOL;
END_VAR
VAR_OUTPUT
    Status : ST_MotorStatus;
END_VAR
