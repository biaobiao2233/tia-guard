using System.ComponentModel;
using ModelContextProtocol.Server;

namespace TiaGuard.Bridge.Host;

[McpServerToolType]
public class BridgeAiTools
{
    [McpServerTool(Name = "get_ai_project_context", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read the derived AI Engineering v2 project context for the bound offline project: identity, PLC summary, blocks, tags, coverage and content identity. Does not return raw Siemens XML.")]
    public static Task<string> GetAiProjectContext(
        BridgeAiContextService context,
        CancellationToken cancellationToken)
        => context.GetProjectContextAsync(cancellationToken);

    [McpServerTool(Name = "get_program_graph", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read one block's AI Engineering v2 program graph. Evidence graph and derived analysis stay separate.")]
    public static Task<string> GetProgramGraph(
        BridgeAiContextService context,
        [Description("Block label such as OB1, or the block's engineering name.")] string block,
        CancellationToken cancellationToken)
        => context.GetProgramGraphAsync(block, cancellationToken);

    [McpServerTool(Name = "get_network", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Read one LAD network from AI Engineering v2, including source evidence and derived semantics as separate objects.")]
    public static Task<string> GetNetwork(
        BridgeAiContextService context,
        [Description("Block label such as OB1, or the block's engineering name.")] string block,
        [Description("1-based network ordinal.")] int network,
        CancellationToken cancellationToken)
        => context.GetNetworkAsync(block, network, cancellationToken);

    [McpServerTool(Name = "where_used", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Find read, negated-read and write references for a symbol in the AI Engineering v2 analysis. Unsupported networks are reported unsearched instead of guessed.")]
    public static Task<string> WhereUsed(
        BridgeAiContextService context,
        [Description("Exact PLC tag symbol, for example ForwardOut.")] string symbol,
        CancellationToken cancellationToken)
        => context.WhereUsedAsync(symbol, cancellationToken);

    [McpServerTool(Name = "refresh_ai_context", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Rebuild the temporary canonical source and derived AI context. Does not modify or save the TIA project.")]
    public static Task<string> RefreshAiContext(
        BridgeAiContextService context,
        CancellationToken cancellationToken)
        => context.RefreshAsync(cancellationToken);
}
