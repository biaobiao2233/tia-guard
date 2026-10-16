using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TiaGuard.Bridge.Host;
[McpServerToolType]
public class BridgeSimulationReadTools
{
    [McpServerTool(Name = "get_simulation_status", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Report simulator capability and this Bridge's owned session state. Does not launch or operate a PLC.")]
    public static object Status(BridgeSimulationCoordinator c, BridgeWorkerClient worker) => c.Status(worker.ReadRequiredInteraction());
    [McpServerTool(Name = "read_simulation_outputs", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read actual Bool Q outputs from this Bridge's loaded, owned Standard PLCSIM session.")]
    public static Task<CallToolResult> Outputs(BridgeSimulationCoordinator c, string sessionId, CancellationToken ct) => Wrap(c.OutputsAsync(sessionId, ct));
    internal static async Task<CallToolResult> Wrap(Task<object> result)
    {
        var json = JsonSerializer.SerializeToElement(await result);
        return new CallToolResult { IsError = !json.GetProperty("success").GetBoolean(), StructuredContent = json,
            Content = [new TextContentBlock { Text = json.GetRawText() }] };
    }
}
[McpServerToolType]
public class BridgeSimulationWriteTools
{
    [McpServerTool(Name = "create_simulation_session", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Copy and compile a V21 project for simulation, create an owned Standard S7-1200 instance and load through PLCSIM only. Requires --enable-simulation. Refuses existing instances and attached projects.")]
    public static Task<CallToolResult> Create(BridgeSimulationCoordinator c, SimulationRequest request, CancellationToken ct) => BridgeSimulationReadTools.Wrap(c.CreateAsync(request, ct));
    [McpServerTool(Name = "load_simulation_session", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Retry a failed download into this Bridge's already prepared, owned simulator. Rechecks current instance identity and PLCSIM discovery. Does not adopt another instance or recreate a project.")]
    public static Task<CallToolResult> Load(BridgeSimulationCoordinator c, string sessionId, CancellationToken ct) => BridgeSimulationReadTools.Wrap(c.LoadAsync(sessionId, ct));
    [McpServerTool(Name = "run_simulation_test", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Create/load an isolated simulator session and execute input/scan/output assertions. Returns actual observations; failed load, missing outputs or mismatches cannot qualify. Scope: CPU1212C/V4.7 Bool I/Q. Leaves a handle for inspection and close.")]
    public static Task<CallToolResult> Test(BridgeSimulationCoordinator c, SimulationRequest request, CancellationToken ct) => BridgeSimulationReadTools.Wrap(c.TestAsync(request, ct));
    [McpServerTool(Name = "set_simulation_run", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Put this Bridge's loaded simulated CPU into RUN or STOP; never selects a physical PLC.")]
    public static Task<CallToolResult> Run(BridgeSimulationCoordinator c, string sessionId, bool run, CancellationToken ct) => BridgeSimulationReadTools.Wrap(c.SetRunAsync(sessionId, run, ct));
    [McpServerTool(Name = "set_simulation_inputs", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Write exactly the declared Bool I inputs of the owned simulator while scanning is paused. Q writes and force operations are unavailable.")]
    public static Task<CallToolResult> Inputs(BridgeSimulationCoordinator c, string sessionId, Dictionary<string, bool> inputs, CancellationToken ct) => BridgeSimulationReadTools.Wrap(c.InputsAsync(sessionId, inputs, ct));
    [McpServerTool(Name = "step_simulation", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Execute 1–100 scans using the owned Standard PLCSIM instance's scan control.")]
    public static Task<CallToolResult> Scan(BridgeSimulationCoordinator c, string sessionId, int scans, CancellationToken ct) => BridgeSimulationReadTools.Wrap(c.ScanAsync(sessionId, scans, ct));
    [McpServerTool(Name = "close_simulation_session", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Stop and remove only this Bridge's owned simulator instance, normally close its native application and dispose the copied project.")]
    public static Task<CallToolResult> Close(BridgeSimulationCoordinator c, string sessionId, CancellationToken ct) => BridgeSimulationReadTools.Wrap(c.CloseAsync(sessionId, ct));
}
