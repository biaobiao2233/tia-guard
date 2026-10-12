namespace TwinCat3.Engineering;

/// <summary>Configured by the trusted worker, not supplied as authority by an MCP request.</summary>
public sealed class EngineeringAllocation
{
    public string WorkspaceRoot { get; }
    public string ScratchRoot { get; }
    public EngineeringAllocation(string workspaceRoot, string scratchRoot)
    {
        WorkspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        if (!Directory.Exists(WorkspaceRoot)) throw new DirectoryNotFoundException("The owned workspace must exist.");
        ScratchRoot = EngineeringPaths.RequireUnder(scratchRoot, WorkspaceRoot, true);
        if (!Directory.Exists(ScratchRoot)) throw new DirectoryNotFoundException("The owned scratch must exist.");
    }

    public string CheckInput(string input)
    {
        string full = EngineeringPaths.RequireUnder(input, WorkspaceRoot, true);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Input must be a directory.");
        EngineeringPaths.RequireDistinctTrees(full, ScratchRoot);
        return full;
    }
    public string CheckOutput(string output, string input)
    {
        string full = EngineeringPaths.RequireNewOutput(output, WorkspaceRoot);
        EngineeringPaths.RequireDistinctTrees(full, input, ScratchRoot);
        return full;
    }
    public string CheckStage(string stage)
    {
        string full = EngineeringPaths.RequireUnder(stage, ScratchRoot, true);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException("The own stage must exist.");
        return full;
    }
    internal void CheckStageParent()
    {
        EngineeringPaths.RequireUnder(ScratchRoot, WorkspaceRoot, true);
    }
}
