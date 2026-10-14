using System.Text.RegularExpressions;
using TwinCat3.Engineering;

namespace TwinCat3.Worker;

public sealed record WorkerConfiguration(string PipeName, string Identity, string WorkspaceRoot,
    string ScratchRoot, string? InteropPath, string? Build, bool OfflineWrites)
{
    public bool RuntimeWrites { get; init; }
    public static WorkerConfiguration FromEnvironment()
    {
        string Read(string name) => System.Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidDataException("Missing trusted worker configuration: " + name);
        string endpoint = Read("TC3_PIPE");
        const string prefix = @"\\.\pipe\";
        if (!endpoint.StartsWith(prefix, StringComparison.Ordinal) ||
            !Regex.IsMatch(endpoint[prefix.Length..], @"\A[A-Za-z0-9_-]{1,180}\z"))
            throw new InvalidDataException("A concrete local Windows named pipe is required.");
        string identity = Read("TC3_IDENTITY");
        if (identity.Length is < 16 or > 128 || identity.Any(char.IsControl))
            throw new InvalidDataException("Invalid worker identity.");
        var allocation = new EngineeringAllocation(Read("TC3_WORKSPACE"), Read("TC3_SCRATCH"));
        return new(endpoint[prefix.Length..], identity, allocation.WorkspaceRoot, allocation.ScratchRoot,
            System.Environment.GetEnvironmentVariable("TC3_INTEROP"), System.Environment.GetEnvironmentVariable("TC3_BUILD"),
            System.Environment.GetEnvironmentVariable("TC3_OFFLINE_WRITES") == "1")
            { RuntimeWrites = System.Environment.GetEnvironmentVariable("TC3_RUNTIME_WRITES") == "1" };
    }
}
