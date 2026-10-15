namespace TiaGuard.TwinCat.Validation;

public enum VerificationVerdict { Unknown, Fail, Pass }
public sealed record SourceObject(string Name, string Kind, string Declaration, string Implementation);
public sealed record TaskFact(string Name, long CycleTimeUs, int Priority, string LinkedSystemTask, IReadOnlyList<string> Calls);
public sealed record LibraryFact(string Name, string Distributor, string RequestedVersion, string ResolvedVersion);
public sealed record ProjectFacts(string SolutionName, string ConfigurationName, string PlcName, string ToolBuild, string Platform,
    IReadOnlyList<SourceObject> Objects, IReadOnlyList<TaskFact> Tasks, IReadOnlyList<LibraryFact> Libraries);
public sealed record ReadbackObservation(ProjectFacts Facts, bool Saved, bool Closed, bool Reopened,
    bool ObjectsComplete, bool TasksComplete, bool LibrariesComplete, IReadOnlyList<string> Unsupported,
    IReadOnlyList<string> CaptureProblems);
public sealed record ComparisonEvidence(VerificationVerdict Verdict, bool Complete, IReadOnlyList<string> Differences, IReadOnlyList<string> Unknowns)
{
    public bool Success => Verdict == VerificationVerdict.Pass && Complete;
}

public static class IndependentComparison
{
    // expected is constructed from immutable input BEFORE creation, never from actual.Facts.
    public static ComparisonEvidence Compare(ProjectFacts expected, ReadbackObservation actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        var differences = new List<string>();
        var unknown = actual.CaptureProblems.Concat(actual.Unsupported).ToList();
        if (!actual.Saved || !actual.Closed || !actual.Reopened) unknown.Add("Independent save/close/reopen was not established.");
        if (!actual.ObjectsComplete) unknown.Add("Object inventory/source capture is incomplete.");
        if (!actual.TasksComplete) unknown.Add("Task inventory/configuration/calls capture is incomplete.");
        if (!actual.LibrariesComplete) unknown.Add("Library inventory/resolution capture is incomplete.");
        void Equal(string label, object? left, object? right) { if (!Equals(left, right)) differences.Add($"{label}: expected '{left}', actual '{right}'."); }
        Equal("Solution", expected.SolutionName, actual.Facts.SolutionName);
        Equal("Configuration", expected.ConfigurationName, actual.Facts.ConfigurationName);
        Equal("PLC", expected.PlcName, actual.Facts.PlcName);
        Equal("ToolBuild", expected.ToolBuild, actual.Facts.ToolBuild);
        Equal("Platform", expected.Platform, actual.Facts.Platform);
        CompareSet("Objects", expected.Objects, actual.Facts.Objects, x => x.Name, (left, right) =>
        {
            Equal($"Object {left.Name} kind", left.Kind, right.Kind);
            try
            {
                Equal($"Object {left.Name} declaration", StTokens.Canonicalize(left.Declaration), StTokens.Canonicalize(right.Declaration));
                Equal($"Object {left.Name} implementation", StTokens.Canonicalize(left.Implementation), StTokens.Canonicalize(right.Implementation));
            }
            catch (Exception ex) { unknown.Add($"Object {left.Name} source projection failed: {ex.Message}"); }
        });
        CompareSet("Tasks", expected.Tasks, actual.Facts.Tasks, x => x.Name, (left, right) =>
        {
            Equal($"Task {left.Name} cycle", left.CycleTimeUs, right.CycleTimeUs);
            Equal($"Task {left.Name} priority", left.Priority, right.Priority);
            Equal($"Task {left.Name} system link", left.LinkedSystemTask, right.LinkedSystemTask);
            if (!left.Calls.SequenceEqual(right.Calls, StringComparer.Ordinal)) differences.Add($"Task {left.Name} call sequence changed.");
        });
        CompareSet("Libraries", expected.Libraries, actual.Facts.Libraries, x => x.Name, (left, right) =>
        {
            Equal($"Library {left.Name} distributor", left.Distributor, right.Distributor);
            Equal($"Library {left.Name} requested version", left.RequestedVersion, right.RequestedVersion);
            Equal($"Library {left.Name} resolved version", left.ResolvedVersion, right.ResolvedVersion);
            if (string.IsNullOrWhiteSpace(right.ResolvedVersion) || right.ResolvedVersion.Contains('*')) unknown.Add($"Library {left.Name} version is unresolved.");
        });
        return new(differences.Count > 0 ? VerificationVerdict.Fail : unknown.Count > 0 ? VerificationVerdict.Unknown : VerificationVerdict.Pass,
            unknown.Count == 0, differences, unknown.Distinct(StringComparer.Ordinal).ToArray());

        void CompareSet<T>(string label, IReadOnlyList<T> wanted, IReadOnlyList<T> found, Func<T, string> key, Action<T, T> compare)
        {
            if (wanted.GroupBy(key, StringComparer.Ordinal).Any(x => x.Count() != 1)) { unknown.Add($"Duplicate expected {label} identity."); return; }
            if (found.GroupBy(key, StringComparer.Ordinal).Any(x => x.Count() != 1)) { unknown.Add($"Duplicate captured {label} identity."); return; }
            var wantedMap = wanted.ToDictionary(key, StringComparer.Ordinal);
            var foundMap = found.ToDictionary(key, StringComparer.Ordinal);
            foreach (var name in wantedMap.Keys.Except(foundMap.Keys, StringComparer.Ordinal)) differences.Add($"Missing {label} item {name}.");
            foreach (var name in foundMap.Keys.Except(wantedMap.Keys, StringComparer.Ordinal)) differences.Add($"Unexpected {label} item {name}.");
            foreach (var name in wantedMap.Keys.Intersect(foundMap.Keys, StringComparer.Ordinal)) compare(wantedMap[name], foundMap[name]);
        }
    }
}
