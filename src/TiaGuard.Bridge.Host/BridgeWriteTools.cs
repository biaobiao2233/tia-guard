using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TiaGuard.Bridge.Host;

[McpServerToolType]
public class BridgeWriteTools
{
    [McpServerTool(
        Name = "preview_tag_upsert",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Preview a tag create/update in the currently bound disposable offline project copy. Returns a single-use safety token and does not modify or save the project.")]
    public static Task<BridgeWritePreview> PreviewTagUpsert(
        BridgeTagWriteCoordinator coordinator,
        [Description("Root PLC tag table name, not a path.")] string tableName,
        [Description("PLC tag name.")] string tagName,
        [Description("PLC data type such as Bool, Int, or Word.")] string dataType,
        [Description("Logical PLC address such as %M10.0 or %I0.0.")] string logicalAddress,
        CancellationToken cancellationToken = default)
        => coordinator.PreviewAsync(
            new TagUpsertRequest
            {
                TableName = tableName,
                TagName = tagName,
                DataType = dataType,
                LogicalAddress = logicalAddress
            },
            cancellationToken);

    [McpServerTool(
        Name = "apply_tag_upsert",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false)]
    [Description("Apply exactly the previously previewed tag create/update. The token is single-use and bound to the project, request, and current tag state. Phase 2 writes only the disposable in-memory copy; it does not save the .ap21.")]
    public static Task<BridgeWriteApplyResult> ApplyTagUpsert(
        BridgeTagWriteCoordinator coordinator,
        [Description("Root PLC tag table name, identical to preview.")] string tableName,
        [Description("PLC tag name, identical to preview.")] string tagName,
        [Description("PLC data type, identical to preview.")] string dataType,
        [Description("Logical PLC address, identical to preview.")] string logicalAddress,
        [Description("Single-use token returned by preview_tag_upsert.")] string safetyToken,
        CancellationToken cancellationToken = default)
        => coordinator.ApplyAsync(
            new TagUpsertRequest
            {
                TableName = tableName,
                TagName = tagName,
                DataType = dataType,
                LogicalAddress = logicalAddress
            },
            safetyToken,
            cancellationToken);

    [McpServerTool(
        Name = "preview_publish_modified_copy",
        ReadOnly = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Preview compiling and publishing the currently bound disposable offline project to a new destination directory. Returns a single-use safety token and writes nothing.")]
    public static Task<BridgeWritePreview> PreviewPublishModifiedCopy(
        BridgeProjectPublishCoordinator coordinator,
        [Description("Existing absolute parent directory for the new project copy.")] string outputDirectory,
        [Description("TIA project directory/name. It must exactly match the currently bound TIA project name; choose a different parent directory for a distinct published copy.")] string outputName,
        CancellationToken cancellationToken = default)
        => coordinator.PreviewAsync(
            new ProjectPublishRequest
            {
                OutputDirectory = outputDirectory,
                OutputName = outputName
            },
            cancellationToken);

    [McpServerTool(
        Name = "apply_publish_modified_copy",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false)]
    [Description("Compile and SaveAs the exactly previewed disposable offline project into a new destination, reopen the published .ap21 and verify its deterministic engineering contentId. Never overwrites the original or an existing destination.")]
    public static Task<BridgePublishApplyResult> ApplyPublishModifiedCopy(
        BridgeProjectPublishCoordinator coordinator,
        [Description("Existing absolute parent directory, identical to preview.")] string outputDirectory,
        [Description("TIA project directory/name, identical to preview and to the currently bound project name.")] string outputName,
        [Description("Single-use token returned by preview_publish_modified_copy.")] string safetyToken,
        CancellationToken cancellationToken = default)
        => coordinator.ApplyAsync(
            new ProjectPublishRequest
            {
                OutputDirectory = outputDirectory,
                OutputName = outputName
            },
            safetyToken,
            cancellationToken);
}
