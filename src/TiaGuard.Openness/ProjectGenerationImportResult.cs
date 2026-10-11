using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaGuard.Openness
{
    // Engine boundary, not the public JSON contract owned by #44.
    public sealed class ProjectGenerationObject
    {
        public string Kind { get; set; }
        public string Name { get; set; }
        public int? Number { get; set; }
        public string Language { get; set; }
        public string InstanceOfName { get; set; }
        public string Group { get; set; } = "";
        public bool Protected { get; set; }
        internal string Key => (Kind == "UDT" ? "type:" : "block:") + Group + "/" + Name;
        internal bool Same(ProjectGenerationObject other) => other != null &&
            Kind == other.Kind && Name == other.Name && Number == other.Number &&
            Language == other.Language && InstanceOfName == other.InstanceOfName &&
            Group == other.Group && !Protected && !other.Protected;
    }

    public sealed class ProjectGenerationImportResult
    {
        public string State { get; set; }
        public IReadOnlyList<ProjectGenerationObject> Objects { get; set; }
        public IReadOnlyList<string> Messages { get; set; } = new string[0];
    }

    // Adapter of C's result, never a replacement compile implementation.
    public sealed class ProjectGenerationCompileEvidence
    {
        public string ProviderRevision { get; set; }
        public bool Executed { get; set; }
        public string State { get; set; }
        public int? ErrorCount { get; set; }
        public int? WarningCount { get; set; }
        public bool DiagnosticsComplete { get; set; }
        public IReadOnlyList<string> CollectionFailures { get; set; } = new string[0];
        public object FullResult { get; set; }

        public bool AllowsPublication => !string.IsNullOrWhiteSpace(ProviderRevision) &&
            Executed && DiagnosticsComplete && ErrorCount == 0 && WarningCount.HasValue &&
            WarningCount.Value >= 0 && (State == "Success" || State == "Warning") &&
            CollectionFailures != null && CollectionFailures.Count == 0 && FullResult != null;
    }

    public sealed class ProjectGenerationBuildResult
    {
        public bool Published { get; internal set; }
        public string ProjectFile { get; internal set; }
        public string ContractRevision { get; internal set; }
        public string IdentityRevision { get; internal set; }
        public ProjectGenerationCompileEvidence Compile { get; internal set; }
        public List<ProjectGenerationImportResult> Imports { get; } = new List<ProjectGenerationImportResult>();
        public List<string> Failures { get; } = new List<string>();
        public List<string> CleanupFailures { get; } = new List<string>();
    }

    public interface IProjectGenerationSession : IDisposable
    {
        object NativePlc { get; }
        string ProjectFile { get; }
        string CpuTypeIdentifier { get; }
        IReadOnlyList<ProjectGenerationObject> ReadObjects();
        void DeleteFreshMain();
        void CreateTagTables(IReadOnlyList<ProjectGenerationEngineTagTable> tables);
        ProjectGenerationImportResult Import(ProjectGenerationImportUnit unit, string copiedSource);
        void Save();
    }

    internal static class ProjectGenerationInventory
    {
        internal static void RequireExact(IEnumerable<ProjectGenerationObject> expected,
            IReadOnlyList<ProjectGenerationObject> actual, string context)
        {
            if (actual == null) throw new InvalidOperationException(context + ": missing inventory.");
            var want = expected.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
            var got = new Dictionary<string, ProjectGenerationObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in actual)
            {
                if (item == null || item.Protected || !ProjectGenerationImporter.KnownKind(item.Kind) ||
                    got.ContainsKey(item.Key))
                    throw new InvalidOperationException(context + ": unknown/protected/duplicate object.");
                got.Add(item.Key, item);
            }
            if (want.Count != got.Count || want.Any(x => !got.TryGetValue(x.Key, out var v) || !x.Value.Same(v)))
                throw new InvalidOperationException(context + ": actual object set/identity differs from declarations.");
        }
    }
}
