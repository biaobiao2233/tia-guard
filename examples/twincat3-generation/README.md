# TwinCAT offline generation fixtures

`motor/` is a self-authored nonempty offline input: MAIN, FB, GVL, Struct DUT,
an explicit 10 ms / priority 20 task and a fixed Tc2_Standard reference.
Build 4026.27 and Tc2_Standard 3.4.7.0 were observed installed on the development
machine; this is an explicit example selection, not a compatibility claim for
other machines. Run Doctor and select installed identities before previewing.

Declarations and implementations stay separate because those are the native
TwinCAT PLC code interfaces. Each change to a source or manifest byte requires a
new preview and a new single-use write grant. Outputs must be new directories
inside the configured owned workspace; intermediate projects stay in its separate
owned scratch. Only successful native compilation plus independent close/reopen
readback can publish a verified project.

The motor code has not been runtime tested. Offline creation and compilation do
not prove the PLC scan behavior, real-time performance or physical safety.
There are no hardware scan, activation, ADS, boot generation, library installation
or runtime commands in this fixture.
