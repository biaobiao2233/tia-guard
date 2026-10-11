using System;
using System.Collections.Generic;

namespace TiaGuard.Openness
{
    // Independent comparison DTOs. These are not A's project-generation input contract.
    // Populate expectations from validated declarations and independently justified source
    // projections, never by copying the project observation under test.
    public sealed class ProjectGenerationVerificationExpected
    {
        public string ProjectName { get; set; }
        public string PlcName { get; set; }
        public string CpuTypeIdentifier { get; set; }
        public string EvidenceReference { get; set; }
        public Dictionary<string, string> SourceSha256 { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<ProjectGenerationVerificationObject> Objects { get; set; } = new List<ProjectGenerationVerificationObject>();
        public List<ProjectGenerationVerificationTagTable> TagTables { get; set; } = new List<ProjectGenerationVerificationTagTable>();
    }

    public sealed class ProjectGenerationVerificationObservation
    {
        public string ProjectName { get; set; }
        public string PlcName { get; set; }
        public string CpuTypeIdentifier { get; set; }
        public string TiaVersion { get; set; }
        public string ApiVersion { get; set; }
        public string CapturedAtUtc { get; set; }
        public string EvidenceDirectory { get; set; }
        public bool HardwareComplete { get; set; }
        public bool ObjectsComplete { get; set; }
        public bool TagTablesComplete { get; set; }
        public List<ProjectGenerationVerificationObject> Objects { get; set; } = new List<ProjectGenerationVerificationObject>();
        public List<ProjectGenerationVerificationTagTable> TagTables { get; set; } = new List<ProjectGenerationVerificationTagTable>();
        public List<ProjectGenerationVerificationFinding> ReadFailures { get; set; } = new List<ProjectGenerationVerificationFinding>();
        public Dictionary<string, string> CoverageNotes { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ProjectGenerationVerificationObject
    {
        public string Scope { get; set; } // blocks, types, or system-blocks; escaped nested names
        public string Kind { get; set; } // OB, FB, FC, DB, UDT
        public string Name { get; set; }
        public string Language { get; set; }
        public int? Number { get; set; }
        public string ContentFormat { get; set; } // scl, simatic-sd, simaticml
        // Keys are extensions (.s7dcl/.s7res or .scl/.db/.udt or .xml).
        // A resource file is comparison-significant, not silently discarded.
        public Dictionary<string, string> Documents { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool ContentComplete { get; set; }
        public string InterfaceXml { get; set; }
        public bool InterfaceComplete { get; set; }
        // Empty means proven non-instance; null means unreadable/unknown.
        public string InstanceOfName { get; set; }
        public bool InstanceComplete { get; set; }
        public string ExportState { get; set; }
        public List<string> ExportMessages { get; set; } = new List<string>();
        public Dictionary<string, string> RawSha256 { get; set; } = new Dictionary<string, string>();
    }

    public sealed class ProjectGenerationVerificationTagTable
    {
        public string Scope { get; set; } = "tags";
        public string Name { get; set; }
        public bool Complete { get; set; }
        public List<ProjectGenerationVerificationTag> Tags { get; set; } = new List<ProjectGenerationVerificationTag>();
    }

    public sealed class ProjectGenerationVerificationTag
    {
        public string Name { get; set; }
        public string DataType { get; set; }
        public string LogicalAddress { get; set; }
    }

    public sealed class ProjectGenerationVerificationFinding
    {
        public string Code { get; set; }
        public string Path { get; set; }
        public string Detail { get; set; }
        public bool Unknown { get; set; }
    }

    public sealed class ProjectGenerationVerificationResult
    {
        // PASS is comparison-only. It says nothing about compilation or control behavior.
        public string Verdict { get; internal set; } = "UNKNOWN";
        public string Normalization { get; } = "transport-text-v1";
        public List<ProjectGenerationVerificationFinding> Findings { get; } = new List<ProjectGenerationVerificationFinding>();
        public List<string> Compared { get; } = new List<string>();
        public Dictionary<string, string> ExpectedSourceSha256 { get; internal set; } = new Dictionary<string, string>();
    }
}
