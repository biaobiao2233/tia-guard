using System;
using System.Collections.Generic;

namespace TiaGuard.Openness
{
    // Pure immutable declarations. Loading is NOT a V21 syntax/compile verdict.
    public sealed class ProjectGenerationSpec
    {
        public string SchemaVersion => "1.0";
        public string TiaVersion => "V21";
        public string ProjectName { get; }
        public ProjectGenerationPlc Plc { get; }
        public IReadOnlyList<ProjectGenerationTagTable> TagTables { get; }
        public IReadOnlyList<ProjectGenerationArtifact> Artifacts { get; }
        internal ProjectGenerationSpec(string name, ProjectGenerationPlc plc,
            List<ProjectGenerationTagTable> tables, List<ProjectGenerationArtifact> artifacts)
        { ProjectName = name; Plc = plc; TagTables = tables.AsReadOnly(); Artifacts = artifacts.AsReadOnly(); }
    }
    public sealed class ProjectGenerationPlc
    {
        public string Name { get; }
        public string CpuTypeIdentifier { get; }
        internal ProjectGenerationPlc(string name, string cpu) { Name = name; CpuTypeIdentifier = cpu; }
    }
    public sealed class ProjectGenerationTagTable
    {
        public string Name { get; }
        public IReadOnlyList<ProjectGenerationTag> Tags { get; }
        internal ProjectGenerationTagTable(string name, List<ProjectGenerationTag> tags)
        { Name = name; Tags = tags.AsReadOnly(); }
    }
    public sealed class ProjectGenerationTag
    {
        public string Name { get; }
        public string DataType { get; }
        public string LogicalAddress { get; }
        internal ProjectGenerationTag(string name, string type, string address)
        { Name = name; DataType = type; LogicalAddress = address; }
    }
    public sealed class ProjectGenerationArtifact
    {
        public string Id { get; }
        public string Kind { get; }
        public string Name { get; }
        public string Format { get; }
        public string SourcePath { get; }
        public IReadOnlyList<string> DependsOn { get; }
        internal ProjectGenerationArtifact(string id, string kind, string name, string format,
            string path, List<string> dependencies)
        { Id = id; Kind = kind; Name = name; Format = format; SourcePath = path; DependsOn = dependencies.AsReadOnly(); }
    }
    public static class ProjectGenerationCapabilities
    {
        // Accepted as offline generation INPUT, not a claim of V21 runtime support.
        // SD/XML stay closed until B/D establish format-specific identity/coverage evidence.
        public static bool Supports(string tiaVersion, string cpu, string kind, string format) =>
            tiaVersion == "V21" && RoundTripProfile.SupportsCpu(cpu) && format == "scl" &&
            (kind == "OB" || kind == "FB" || kind == "FC" || kind == "DB" || kind == "UDT");
    }
}
