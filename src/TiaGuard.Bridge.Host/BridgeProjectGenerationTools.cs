using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace TiaGuard.Bridge.Host;

[McpServerToolType]
public class BridgeProjectGenerationReadTools
{
    [McpServerTool(Name = "preview_project_generation", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Inspect an editable project-generation source directory without opening TIA. Preview creation of a new offline PLC project, binding its full input hash and paths to a single-use token. No existing project connection is needed.")]
    public static Task<string> PreviewProjectGeneration(BridgeProjectGenerationCoordinator coordinator,
        [Description("Absolute directory containing project-generation.json and its declared editable sources.")] string sourceDirectory,
        [Description("Absolute new output directory; must not exist.")] string outputDirectory,
        [Description("Absolute existing directory used only for this operation's owned temporary projects.")] string ownedScratchRoot,
        [Description("Optional absolute installed V21 PublicAPI directory.")] string? publicApiDirectory = null,
        CancellationToken cancellationToken = default)
        => coordinator.PreviewAsync(new ProjectGenerationRequest
        {
            SourceDirectory = sourceDirectory, OutputDirectory = outputDirectory,
            OwnedScratchRoot = ownedScratchRoot, PublicApiDirectory = publicApiDirectory
        }, cancellationToken);
}

[McpServerToolType]
public class BridgeProjectGenerationWriteTools
{
    [McpServerTool(Name = "build_project_generation", ReadOnly = false, Destructive = true, OpenWorld = false)]
    [Description("Create a new offline V21 PLC project from the exact previewed sources after single-use token approval, then compile and independently read back/verify before publishing. Refuses a bound old project. Failed compilation or verification is returned as a tool error with structured reports.")]
    public static async Task<CallToolResult> BuildProjectGeneration(BridgeProjectGenerationCoordinator coordinator,
        [Description("Absolute source directory, identical to preview.")] string sourceDirectory,
        [Description("Absolute new output directory, identical to preview.")] string outputDirectory,
        [Description("Absolute scratch directory, identical to preview.")] string ownedScratchRoot,
        [Description("Single-use safetyToken returned by preview_project_generation.")] string safetyToken,
        [Description("Optional installed PublicAPI directory, identical to preview.")] string? publicApiDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var json = await coordinator.BuildAsync(new ProjectGenerationRequest
        {
            SourceDirectory = sourceDirectory, OutputDirectory = outputDirectory,
            OwnedScratchRoot = ownedScratchRoot, PublicApiDirectory = publicApiDirectory
        }, safetyToken, cancellationToken).ConfigureAwait(false);
        return new CallToolResult
        {
            IsError = !BridgeProjectGenerationCoordinator.IsSuccess(json),
            StructuredContent = JsonSerializer.Deserialize<JsonElement>(json),
            Content = [new TextContentBlock { Text = json }]
        };
    }
}
