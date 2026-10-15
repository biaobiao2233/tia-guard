using System.Collections.Generic;

namespace TiaGuard.Analysis;

public sealed class SnapshotDocument
{
    private readonly Dictionary<string, int> _objectLines;

    internal SnapshotDocument(SnapshotV1 snapshot, string artifactUri, Dictionary<string, int> objectLines)
    {
        Snapshot = snapshot;
        ArtifactUri = artifactUri;
        _objectLines = objectLines;
    }

    public SnapshotV1 Snapshot { get; }

    /// <summary>Repository-relative URI of the Snapshot JSON artifact.</summary>
    public string ArtifactUri { get; }

    internal FindingLocation GetTagLocation(int plcIndex, int tagIndex) =>
        GetLocation("/plcs/" + plcIndex + "/tags/" + tagIndex);

    internal FindingLocation GetBlockLocation(int plcIndex, int blockIndex) =>
        GetLocation("/plcs/" + plcIndex + "/blocks/" + blockIndex);

    internal FindingLocation GetCompileLocation(int plcIndex) =>
        GetLocation("/plcs/" + plcIndex + "/compile");

    internal FindingLocation GetCaptureLocation() => GetLocation("/capture");

    internal FindingLocation GetDiagnosticLocation(int diagnosticIndex) =>
        GetLocation("/diagnostics/" + diagnosticIndex);

    private FindingLocation GetLocation(string pointer)
    {
        if (!_objectLines.TryGetValue(pointer, out var line))
        {
            throw new System.IO.InvalidDataException("Could not locate Snapshot object " + pointer + " in the source JSON.");
        }

        return new FindingLocation(ArtifactUri, line);
    }
}
