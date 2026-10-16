using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TiaGuard.Bridge.Host;

[McpServerToolType]
public class BridgePlcsimDesktopReadTools
{
    [McpServerTool(Name = "get_plcsim_desktop_status", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Report availability and this Bridge's desktop session binding without launching Siemens software.")]
    public static object Status(BridgePlcsimDesktopCoordinator c) => c.Status();
    [McpServerTool(Name = "inspect_plcsim_desktop", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read the current owned PLCSIM control tree: names, stable ancestor identifiers, enabled state, input values and supported UI patterns. Dialogs not yet opened require another inspection. Requires an owned session.")]
    public static Task<CallToolResult> Inspect(BridgePlcsimDesktopCoordinator c, string sessionId, CancellationToken ct) => BridgeSimulationReadTools.Wrap(c.InspectAsync(sessionId, ct));
}
[McpServerToolType]
public class BridgePlcsimDesktopWriteTools
{
    [McpServerTool(Name = "open_plcsim_desktop", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Launch an owned Siemens V21 Standard PLCSIM GUI and keep a persistent FlaUI connection. Requires --allow-write --enable-simulation; protects existing applications and user instances. Does not power a CPU or open TIA.")]
    public static Task<CallToolResult> Open(BridgePlcsimDesktopCoordinator c, CancellationToken ct) => BridgeSimulationReadTools.Wrap(c.OpenAsync(ct));
    [McpServerTool(Name = "act_plcsim_desktop", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Operate only this Bridge's owned desktop: create, open-details, rename, power-on, power-off, prepare-io or delete. prepare-io takes inputAddresses/outputAddresses and creates a new bound SIM view/table with canonical Bool I/Q addresses; it does not write values or qualify monitoring. Names start TiaGuard_. Power uses owned signed runtime process/readback. Unresolved toggles/creation are never repeated. No arbitrary clicks/scripts, download or physical PLC controls.")]
    public static Task<CallToolResult> Act(BridgePlcsimDesktopCoordinator c, PlcsimDesktopActionRequest request, CancellationToken ct) => BridgeSimulationReadTools.Wrap(c.ActAsync(request, ct));
    [McpServerTool(Name = "close_plcsim_desktop", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Delete only the confirmed owned instance and normally close its owned GUI. Refuses to remove additional user instances or blindly terminate software.")]
    public static Task<CallToolResult> Close(BridgePlcsimDesktopCoordinator c, string sessionId, CancellationToken ct) => BridgeSimulationReadTools.Wrap(c.CloseAsync(sessionId, ct));
}
