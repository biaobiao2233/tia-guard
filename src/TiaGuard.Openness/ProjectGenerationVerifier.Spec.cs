// Enable only when the coordinator integrates A's exact CONTRACT_READY sources.
// Pinned provider: #44 at 072c14f24fff1dbce712827886c1ba23e17cf5d5.
#if PROJECT_GENERATION_SPEC_V1
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TiaGuard.Openness
{
    public static class ProjectGenerationVerificationSpecAdapter
    {
        public const string ProviderCommit = "072c14f24fff1dbce712827886c1ba23e17cf5d5";

        // Consume immutable captured bytes; NEVER re-read original paths or fabricate
        // expected InterfaceXml from the generated project's observation.
        public static ProjectGenerationVerificationExpected FromSpec(ProjectGenerationSpecInput input,
            string independentEvidenceReference,
            IReadOnlyDictionary<string, ProjectGenerationVerificationObject> interfaceEvidence = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var spec = input.Spec;
            var expected = new ProjectGenerationVerificationExpected
            {
                ProjectName = spec.ProjectName, PlcName = spec.Plc.Name,
                CpuTypeIdentifier = spec.Plc.CpuTypeIdentifier,
                EvidenceReference = independentEvidenceReference,
                SourceSha256 = input.SourceSha256.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
            };
            if (interfaceEvidence != null && interfaceEvidence.Keys.Any(k => !spec.Artifacts.Any(a => a.Id == k)))
                throw new ArgumentException("Interface evidence contains an undeclared artifact.");
            foreach (var artifact in spec.Artifacts)
            {
                var obj = new ProjectGenerationVerificationObject
                {
                    Name = artifact.Name, Kind = artifact.Kind, Scope = artifact.Kind == "UDT" ? "types" : "blocks",
                    Language = artifact.Kind == "UDT" ? "UDT" : artifact.Kind == "DB" ? "DB" : "SCL",
                    ContentFormat = artifact.Format,
                    InstanceOfName = artifact.Kind == "DB" ? null : "",
                    InstanceComplete = artifact.Kind != "DB"
                };
                // Per-object source generation cannot compare a grouped input file
                // without an independently proven split/projection. Keep it UNKNOWN.
                if (artifact.Format == "scl" && spec.Artifacts.Count(a => a.SourcePath == artifact.SourcePath) == 1)
                {
                    var extension = artifact.Kind == "UDT" ? ".udt" : artifact.Kind == "DB" ? ".db" : ".scl";
                    obj.Documents.Add(extension, new UTF8Encoding(false, true).GetString(input.GetSourceBytes(artifact.SourcePath)));
                    obj.ContentComplete = true;
                }
                ProjectGenerationVerificationObject evidence;
                if (interfaceEvidence != null && interfaceEvidence.TryGetValue(artifact.Id, out evidence))
                {
                    if (evidence == null || evidence.Name != obj.Name || evidence.Kind != obj.Kind || evidence.Scope != obj.Scope)
                        throw new ArgumentException("Independent interface evidence identity does not match the declaration.");
                    obj.Number = evidence.Number;
                    obj.InterfaceXml = evidence.InterfaceXml;
                    obj.InterfaceComplete = evidence.InterfaceComplete;
                    obj.InstanceOfName = evidence.InstanceOfName;
                    obj.InstanceComplete = evidence.InstanceComplete;
                }
                expected.Objects.Add(obj);
            }
            foreach (var table in spec.TagTables)
                expected.TagTables.Add(new ProjectGenerationVerificationTagTable
                {
                    Name = table.Name, Complete = true,
                    Tags = table.Tags.Select(t => new ProjectGenerationVerificationTag
                        { Name = t.Name, DataType = t.DataType, LogicalAddress = t.LogicalAddress }).ToList()
                });
            return expected;
        }
    }
}
#endif
