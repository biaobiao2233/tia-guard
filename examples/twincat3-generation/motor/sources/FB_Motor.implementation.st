Status.CycleCount := Status.CycleCount + 1;
IF Fault THEN
    Status.Faulted := TRUE;
ELSIF Reset THEN
    Status.Faulted := FALSE;
END_IF;
IF Stop OR Status.Faulted THEN
    Status.Running := FALSE;
ELSIF Start THEN
    Status.Running := TRUE;
END_IF;
