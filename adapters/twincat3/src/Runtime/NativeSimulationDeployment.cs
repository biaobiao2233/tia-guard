using TwinCat3.Contracts;
#if TWINCAT_RUNTIME_NATIVE
using EnvDTE80;
using TCatSysManagerLib;
#endif

namespace TwinCat3.Runtime;

public static class NativeSimulationDeployment
{
    public static Task ShowOwnedLicensePageAsync(NativeProjectHandle project, IOwnedEngineeringSession session,
        OwnedUsermodeRuntime runtime, CancellationToken cancellationToken)
        => RecordOwnedIdeAsync(project, session, runtime, cancellationToken, showLicensePage: true);

    public static Task RecordOwnedIdeAsync(NativeProjectHandle project, IOwnedEngineeringSession session,
        OwnedUsermodeRuntime runtime, CancellationToken cancellationToken, bool showLicensePage = false)
        => session.InvokeAsync<bool>(context =>
        {
#if TWINCAT_RUNTIME_NATIVE
            runtime.RequireLive(); RequireBinding(context, project);
            var manager = (ITcSysManager18)context.SystemManager!;
            if (manager.GetTargetNetId() != runtime.NetId) throw new InvalidOperationException("Refusing a foreign licensing target.");
            var dte = (DTE2)context.Dte!;
            GetWindowThreadProcessId(new IntPtr(dte.MainWindow.HWnd), out var processId);
            if (processId == 0) throw new InvalidOperationException("Owned licensing IDE identity is unavailable.");
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            File.WriteAllText(Path.Combine(runtime.InstanceRoot, "ide-ownership.json"), System.Text.Json.JsonSerializer.Serialize(new
                { OwnedProcessId = processId, session.SessionId, Started = process.StartTime, runtime.SimulationId }));
            if (showLicensePage)
            {
                dte.SuppressUI = false; dte.MainWindow.Visible = true;
                manager.SelectTreeItem("TIRC^License");
            }
            return true; // Selection only. No license request/generation/copy.
#else
            throw new NotSupportedException("Native licensing page requires the local XAE interop.");
#endif
        }, cancellationToken);
    public static bool NativeBuilt
    {
        get
        {
#if TWINCAT_RUNTIME_NATIVE
            return true;
#else
            return false;
#endif
        }
    }
    public static Task SelectOwnedTargetAsync(NativeProjectHandle project, IOwnedEngineeringSession session,
        OwnedUsermodeRuntime runtime, CancellationToken cancellationToken, MotionSpec? motion = null)
        => session.InvokeAsync<bool>(context =>
        {
#if TWINCAT_RUNTIME_NATIVE
            runtime.RequireLive(); RequireBinding(context, project);
            SimulationPolicy.RequireNoHardware(project.TwinCatProjectPath, motion, project.PlcProjectName);
            var manager = (ITcSysManager2)context.SystemManager!;
            if (manager.LookupTreeItem("TIID").ChildCount != 0 || manager.LookupTreeItem("TINC").ChildCount != (motion is null ? 0 : 1))
                throw new NotSupportedException("Native hardware/motion inventory is not empty.");
            var config = (ITcConfigManager)((ITcSysManager7)manager).ConfigurationManager;
            if (config.ActiveTargetPlatform != "TwinCAT OS (x64)") throw new InvalidOperationException("Simulation requires the qualified OS x64 platform.");
            manager.SetTargetNetId(runtime.NetId);
            if (manager.GetTargetNetId() != runtime.NetId) throw new InvalidOperationException("Native target did not match the new owned Runtime.");
            ((ITcPlcProject)manager.LookupTreeItem("TIPC^" + project.PlcProjectName)).BootProjectAutostart = true;
            var dte = (DTE2)context.Dte!;
            dte.ExecuteCommand("File.SaveAll", ""); dte.Solution.SaveAs(project.SolutionPath);
            return true;
#else
            throw new NotSupportedException("Native Runtime deployment was built without the local XAE interop.");
#endif
        }, cancellationToken);

    public static Task ActivateAsync(NativeProjectHandle project, IOwnedEngineeringSession session,
        OwnedUsermodeRuntime runtime, CompilationReport compiled, VerificationReport verified,
        CancellationToken cancellationToken, MotionSpec? motion = null)
        => session.InvokeAsync<bool>(context =>
        {
#if TWINCAT_RUNTIME_NATIVE
            runtime.RequireLive(); RequireBinding(context, project);
            if (!compiled.Passed || !verified.Passed || compiled.InputHash != project.InputHash || compiled.ProjectIdentity != project.ProjectIdentity)
                throw new InvalidOperationException("Actual compilation and independent verification must precede activation.");
            SimulationPolicy.RequireNoHardware(project.TwinCatProjectPath, motion, project.PlcProjectName);
            var manager = (ITcSysManager2)context.SystemManager!;
            if (manager.GetTargetNetId() != runtime.NetId) throw new InvalidOperationException("Refusing a foreign target.");
            var plc = (ITcPlcProject)manager.LookupTreeItem("TIPC^" + project.PlcProjectName);
            if (!plc.BootProjectAutostart) throw new InvalidOperationException("Owned PLC boot autostart is not enabled.");
            plc.GenerateBootProject(true);
            runtime.RequireLive();
            if (manager.GetTargetNetId() != runtime.NetId) throw new InvalidOperationException("Target changed before activation.");
            manager.ActivateConfiguration();
            return true;
#else
            throw new NotSupportedException("Native Runtime deployment was built without the local XAE interop.");
#endif
        }, cancellationToken);
#if TWINCAT_RUNTIME_NATIVE
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    private static void RequireBinding(NativeEngineeringContext context, NativeProjectHandle project)
    {
        if (context.SystemManager is null || context.Dte is null || context.BoundProjectIdentity != project.ProjectIdentity ||
            !string.Equals(context.BoundProjectPath, project.TwinCatProjectPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native simulation deployment must use its exact owned copy.");
    }
#endif
}
