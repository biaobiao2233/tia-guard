using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwinCat3.Contracts;

public enum ArtifactKind { Program, Function, FunctionBlock, GlobalVariableList, Struct, Enum }
public enum CompilationState { Unknown, Error, Success }
public enum DiagnosticSeverity { Unknown, Error, Warning, Message }
public enum VerificationVerdict { Unknown, Fail, Pass }

public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}

public sealed record EnvironmentSelection
{
    public string? RequestedBuild { get; init; }
    public string DteProgId { get; init; } = "TcXaeShell.DTE.17.0";
    public string Provider { get; init; } = "dte";
    public string TargetPlatform { get; init; } = "TwinCAT RT (x64)";
    public string BuildConfiguration { get; init; } = "Release";
    public string PlcTemplate { get; init; } = "Empty PLC Template";
}

public sealed record GenerationManifest
{
    public required string SchemaVersion { get; init; }
    public required string SolutionName { get; init; }
    public required string ConfigurationName { get; init; }
    public required string PlcProjectName { get; init; }
    public required EnvironmentSelection Environment { get; init; }
    public required IReadOnlyList<ArtifactSpec> Artifacts { get; init; }
    public required IReadOnlyList<TaskSpec> Tasks { get; init; }
    public required IReadOnlyList<LibrarySpec> Libraries { get; init; }
    public MotionSpec? Motion { get; init; }
}

// One continuous, software-only NC/PTP axis. No arbitrary XML, paths or I/O endpoints.
public sealed record MotionSpec
{
    public required string TaskName { get; init; }
    public required string AxisName { get; init; }
    public required string AxisSymbol { get; init; }
    public required long CycleTimeUs { get; init; }
    public required double MinimumPosition { get; init; }
    public required double MaximumPosition { get; init; }
    public required double MaximumVelocity { get; init; }
    public required double Acceleration { get; init; }
    public required double Deceleration { get; init; }
    public required double Jerk { get; init; }
}

public sealed record ArtifactSpec
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ArtifactKind Kind { get; init; }
    public required string DeclarationFile { get; init; }
    public string? ImplementationFile { get; init; }
}

public sealed record TaskSpec
{
    public required string Name { get; init; }
    public required long CycleTimeUs { get; init; }
    public required int Priority { get; init; }
    public required IReadOnlyList<string> Programs { get; init; }
}

public sealed record LibrarySpec
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Vendor { get; init; }
}

public sealed class FrozenSourceFile
{
    private readonly byte[] bytes;
    public string RelativePath { get; }
    public byte[] Bytes => bytes.ToArray();
    public string Sha256 { get; }
    public string Text { get; }
    public FrozenSourceFile(string relativePath, byte[] content)
    {
        RelativePath = relativePath;
        bytes = content.ToArray();
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        if (Text.Contains('\0')) throw new InvalidDataException("Source contains a NUL character: " + relativePath);
    }
}

public sealed class FrozenGenerationInput
{
    public GenerationManifest Manifest { get; }
    public IReadOnlyDictionary<string, FrozenSourceFile> Files { get; }
    public string InputHash { get; }
    public string RootDirectory { get; }
    public FrozenGenerationInput(GenerationManifest manifest,
        IReadOnlyDictionary<string, FrozenSourceFile> files, string inputHash, string rootDirectory)
    {
        Manifest = manifest with
        {
            Artifacts = Array.AsReadOnly(manifest.Artifacts.ToArray()),
            Tasks = Array.AsReadOnly(manifest.Tasks.Select(t => t with
                { Programs = Array.AsReadOnly(t.Programs.ToArray()) }).ToArray()),
            Libraries = Array.AsReadOnly(manifest.Libraries.ToArray())
        };
        Files = new ReadOnlyDictionary<string, FrozenSourceFile>(
            new Dictionary<string, FrozenSourceFile>(files, StringComparer.OrdinalIgnoreCase));
        InputHash = inputHash;
        RootDirectory = rootDirectory;
    }
}

public sealed record CapabilityObservation
{
    public bool? Available { get; init; }
    public string State { get; init; } = "unknown";
    public string Reason { get; init; } = "Not probed";
}

public sealed record ComRegistration
{
    public required string ProgId { get; init; }
    public string? ClassId { get; init; }
    public string? ServerPath { get; init; }
    public bool Registered { get; init; }
}

public sealed record EnvironmentReport
{
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public bool IsWindows { get; init; }
    public string ProcessArchitecture { get; init; } = "unknown";
    public bool CurrentTokenAdministrator { get; init; }
    public string? InstallDirectory { get; init; }
    public IReadOnlyList<string> InstalledBuilds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ComRegistration> ComRegistrations { get; init; } = Array.Empty<ComRegistration>();
    public string? ProjectTemplatePath { get; init; }
    public string? ProjectTemplateHash { get; init; }
    public IReadOnlyList<string> PlcTemplateNames { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> InteropAssemblyPaths { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, CapabilityObservation> Capabilities { get; init; }
        = new Dictionary<string, CapabilityObservation>();
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}

public sealed record NativeProjectHandle
{
    public required string SolutionPath { get; init; }
    public required string TwinCatProjectPath { get; init; }
    public required string PlcProjectName { get; init; }
    public required string StageRoot { get; init; }
    public required string ProjectIdentity { get; init; }
    public required string InputHash { get; init; }
    public static string ComputeIdentity(string twinCatProjectPath, string configurationName, string plcProjectName)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Path.GetFullPath(twinCatProjectPath).ToUpperInvariant() + "\0" + configurationName + "\0" + plcProjectName)))
            .ToLowerInvariant();
}

// These COM references are process-local and only used on the owned STA.
// This type must never be a transport request/response or grant payload.
public sealed class NativeEngineeringContext
{
    public object? Dte { get; set; }
    public object? SystemManager { get; set; }
    public object? TwinCatProject { get; set; }
    public string? BoundProjectPath { get; set; }
    public string? BoundProjectIdentity { get; set; }
    public required EnvironmentReport Environment { get; init; }
    public IReadOnlyDictionary<string, string> InteropAssemblyPaths => Environment.InteropAssemblyPaths;
}

public sealed record CompilationDiagnostic
{
    public DiagnosticSeverity Severity { get; init; }
    public string? Code { get; init; }
    public required string Message { get; init; }
    public string? File { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public string? Project { get; init; }
    public string? ObjectPath { get; init; }
    public string Origin { get; init; } = "unknown";
    public bool Correlated { get; init; }
}

public sealed record CompilationReport
{
    public CompilationState State { get; init; } = CompilationState.Unknown;
    public bool BuildStarted { get; init; }
    public bool BuildCompleted { get; init; }
    public int? FailedProjectCount { get; init; }
    public int? ErrorCount { get; init; }
    public int? WarningCount { get; init; }
    public bool DiagnosticsComplete { get; init; }
    public bool OutputCaptureComplete { get; init; }
    public IReadOnlyList<CompilationDiagnostic> Diagnostics { get; init; } = Array.Empty<CompilationDiagnostic>();
    public string BuildOutput { get; init; } = "";
    public string? Configuration { get; init; }
    public string? Platform { get; init; }
    public string? ProjectIdentity { get; init; }
    public string? InputHash { get; init; }
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();
    [JsonIgnore]
    public bool Passed => State == CompilationState.Success && BuildStarted && BuildCompleted
        && FailedProjectCount == 0 && ErrorCount == 0 && WarningCount >= 0
        && DiagnosticsComplete && OutputCaptureComplete && Failures.Count == 0
        && Diagnostics.All(d => d.Correlated && d.Severity != DiagnosticSeverity.Unknown
            && d.Severity != DiagnosticSeverity.Error)
        && WarningCount == Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning)
        && !string.IsNullOrEmpty(ProjectIdentity) && !string.IsNullOrEmpty(InputHash)
        && !string.IsNullOrEmpty(Configuration) && !string.IsNullOrEmpty(Platform);
}

public sealed record CapturedArtifact
{
    public required string Name { get; init; }
    public ArtifactKind Kind { get; init; }
    public required string DeclarationText { get; init; }
    public string? ImplementationText { get; init; }
    public required string TreePath { get; init; }
    public int? NativeItemType { get; init; }
    public bool Opaque { get; init; }
}

public sealed record CapturedTask
{
    public required string Name { get; init; }
    public long CycleTimeUs { get; init; }
    public int Priority { get; init; }
    public IReadOnlyList<string> Programs { get; init; } = Array.Empty<string>();
    public required string LinkedSystemTask { get; init; }
}

public sealed record CapturedLibrary
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Vendor { get; init; }
    public required string ResolvedVersion { get; init; }
    public bool IsPlaceholder { get; init; }
}

public sealed record ProjectCapture
{
    public string? ProjectIdentity { get; init; }
    public string? InputHash { get; init; }
    public string? SolutionName { get; init; }
    public string? ConfigurationName { get; init; }
    public string? PlcProjectName { get; init; }
    public string? ToolBuild { get; init; }
    public string? Platform { get; init; }
    public bool Saved { get; init; }
    public bool Closed { get; init; }
    public bool Reopened { get; init; }
    public bool ObjectsComplete { get; init; }
    public bool TasksComplete { get; init; }
    public bool LibrariesComplete { get; init; }
    public IReadOnlyList<CapturedArtifact> Artifacts { get; init; } = Array.Empty<CapturedArtifact>();
    public IReadOnlyList<CapturedTask> Tasks { get; init; } = Array.Empty<CapturedTask>();
    public IReadOnlyList<CapturedLibrary> Libraries { get; init; } = Array.Empty<CapturedLibrary>();
    public MotionSpec? Motion { get; init; }
    public bool MotionComplete { get; init; }
    public IReadOnlyList<string> Unsupported { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RawEvidencePaths { get; init; } = Array.Empty<string>();
}

public sealed record VerificationReport
{
    public VerificationVerdict Verdict { get; init; } = VerificationVerdict.Unknown;
    public bool Complete { get; init; }
    public IReadOnlyList<string> Differences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Unknowns { get; init; } = Array.Empty<string>();
    [JsonIgnore]
    public bool Passed => Verdict == VerificationVerdict.Pass && Complete && Differences.Count == 0 && Unknowns.Count == 0;
}
