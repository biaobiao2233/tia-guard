using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TiaGuard.Bridge.Host;

[McpServerToolType]
public class BridgeReadTools
{
    [McpServerTool(Name = "bridge_doctor", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Check the local TIA Portal V21 and Openness prerequisites without modifying a project.")]
    public static Task<string> Doctor(
        BridgeWorkerClient worker,
        CancellationToken cancellationToken)
        => worker.CallAsync("doctor", cancellationToken: cancellationToken);

    [McpServerTool(Name = "list_open_projects", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("List currently open TIA Portal V21 projects and their process IDs. Does not attach or modify them.")]
    public static Task<string> ListOpenProjects(
        BridgeWorkerClient worker,
        CancellationToken cancellationToken)
        => worker.CallAsync("list_open_projects", cancellationToken: cancellationToken);

    [McpServerTool(Name = "connect_project", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Bind the bridge to exactly one open TIA Portal V21 project. When multiple projects are open, provide the exact TIA process ID.")]
    public static Task<string> ConnectProject(
        BridgeWorkerClient worker,
        [Description("Optional exact TIA Portal process ID. Omit only when exactly one V21 project is open.")] int? processId = null,
        CancellationToken cancellationToken = default)
        => worker.CallAsync("connect",
            processId: processId,
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "open_offline_project", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Open a disposable copy of an offline .ap21 project for read-only AI inspection. The supplied original project is never opened or modified.")]
    public static Task<string> OpenOfflineProject(
        BridgeWorkerClient worker,
        [Description("Absolute path to an existing TIA Portal V21 .ap21 project.")] string projectPath,
        CancellationToken cancellationToken = default)
        => worker.CallAsync("open_offline_project",
            projectPath: projectPath,
            cancellationToken: cancellationToken);

    [McpServerTool(Name = "disconnect_project", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Release the current TIA Portal project binding. Does not close or save the user's project.")]
    public static Task<string> DisconnectProject(
        BridgeWorkerClient worker,
        CancellationToken cancellationToken)
        => worker.CallAsync("disconnect", cancellationToken: cancellationToken);

    [McpServerTool(Name = "get_bridge_state", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Get the current bridge binding state and bound project identity.")]
    public static Task<string> GetBridgeState(
        BridgeWorkerClient worker,
        CancellationToken cancellationToken)
        => worker.CallAsync("get_state", cancellationToken: cancellationToken);

    [McpServerTool(Name = "get_project", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read metadata for the currently bound TIA Portal project.")]
    public static Task<string> GetProject(
        BridgeWorkerClient worker,
        CancellationToken cancellationToken)
        => worker.CallAsync("get_project", cancellationToken: cancellationToken);

    [McpServerTool(Name = "get_project_snapshot", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read a bounded engineering snapshot of the currently bound project: devices, PLC software, blocks, tags, and explicit diagnostics. This is read-only live context, not the authoritative round-trip source.")]
    public static Task<string> GetProjectSnapshot(
        BridgeWorkerClient worker,
        CancellationToken cancellationToken)
        => worker.CallAsync("get_project_snapshot", cancellationToken: cancellationToken);
}
