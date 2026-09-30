using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TiaGuard.Bridge.Host;

[McpServerToolType]
public class BridgePatchTools
{
    [McpServerTool(Name = "preview_patch", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Validate a structured engineering patch and return the current graph, expected graph, and a single-use safety token. Does not modify the project.")]
    public static Task<string> PreviewPatch(
        BridgePatchCoordinator patches,
        [Description("Structured patch JSON using tia-guard.engineering-patch/v1 and AI Engineering v2 expression shape.")] string patch,
        CancellationToken cancellationToken)
        => patches.PreviewAsync(patch, cancellationToken);

    [McpServerTool(Name = "apply_patch", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Apply a previously previewed structured patch to the disposable offline project, then compile, export, verify and regenerate AI Engineering v2. Refuses download, start, stop, force and online writes.")]
    public static Task<string> ApplyPatch(
        BridgePatchCoordinator patches,
        [Description("The same structured patch JSON that was previewed.")] string patch,
        [Description("Single-use safetyToken returned by preview_patch.")] string safetyToken,
        CancellationToken cancellationToken)
        => patches.ApplyAsync(patch, safetyToken, cancellationToken);
}
