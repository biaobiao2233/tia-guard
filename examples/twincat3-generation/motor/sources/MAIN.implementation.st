Motor(
    Start := GVL_Signals.Start,
    Stop := GVL_Signals.Stop,
    Fault := GVL_Signals.Fault,
    Reset := GVL_Signals.Reset);
GVL_Signals.Status := Motor.Status;
