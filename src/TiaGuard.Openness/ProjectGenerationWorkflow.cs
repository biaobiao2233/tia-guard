using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace TiaGuard.Openness
{
    public sealed class ProjectGenerationPreview
    {
        public string InputHash { get; internal set; }
        public string ProjectName { get; internal set; }
        public string PlcName { get; internal set; }
        public int ArtifactCount { get; internal set; }
        public int TagCount { get; internal set; }
    }

    public static partial class ProjectGenerationWorkflow
    {
        private sealed class Prepared
        {
            internal ProjectGenerationSpecInput Input;
            internal ProjectGenerationBuilderPlan Plan;
            internal ProjectGenerationVerificationExpected Expected;
        }
        public static ProjectGenerationPreview Inspect(string sourceDirectory)
        {
            var input = Prepare(sourceDirectory).Input;
            return new ProjectGenerationPreview { InputHash = input.InputHash, ProjectName = input.Spec.ProjectName,
                PlcName = input.Spec.Plc.Name, ArtifactCount = input.Spec.Artifacts.Count,
                TagCount = input.Spec.TagTables.Sum(t => t.Tags.Count) };
        }

        private static Prepared Prepare(string sourceDirectory)
        {
            var input = ProjectGenerationSpecLoader.Load(Path.Combine(sourceDirectory, ProjectGenerationSpecLoader.DefaultSpecFileName));
            var parsed = new Dictionary<string, ProjectGenerationSclProjection.SourceObject>(StringComparer.Ordinal);
            foreach (var path in input.OrderedSourcePaths)
            {
                var objects = ProjectGenerationSclProjection.Parse(new UTF8Encoding(false, true).GetString(input.GetSourceBytes(path)));
                foreach (var artifact in input.Spec.Artifacts.Where(a => a.SourcePath == path))
                    parsed.Add(artifact.Id, objects.Single(o => o.Kind == artifact.Kind && o.Name == artifact.Name));
            }
            var identities = new Dictionary<string, ProjectGenerationObject>(StringComparer.Ordinal);
            var counters = new Dictionary<string, int>();
            foreach (var artifact in input.Spec.Artifacts.OrderBy(a => a.Id, StringComparer.Ordinal))
            {
                var source = parsed[artifact.Id];
                if (source.Kind == "OB" && source.Name != "Main") throw new NotSupportedException("Generation currently supports the cyclic Main/OB1 only; other OB classes need explicit profiles.");
                if (!counters.ContainsKey(source.Kind)) counters[source.Kind] = 100;
                identities.Add(artifact.Id, new ProjectGenerationObject { Kind = source.Kind, Name = source.Name,
                    Number = source.Kind == "UDT" ? (int?)null : source.Kind == "OB" ? 1 : counters[source.Kind]++,
                    Language = source.Kind == "UDT" ? null : source.Kind == "DB" ? "DB" : "SCL", InstanceOfName = source.InstanceOfName });
            }
            var expected = new ProjectGenerationVerificationExpected { ProjectName = input.Spec.ProjectName,
                PlcName = input.Spec.Plc.Name, CpuTypeIdentifier = input.Spec.Plc.CpuTypeIdentifier,
                Normalization = ProjectGenerationSclProjection.Version,
                EvidenceReference = "frozen-source:" + input.InputHash + ";numbering-v1;" + ProjectGenerationSclProjection.Version,
                SourceSha256 = input.SourceSha256.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) };
            foreach (var artifact in input.Spec.Artifacts)
            {
                var source = parsed[artifact.Id]; var identity = identities[artifact.Id];
                var iface = source.Interface;
                if (source.Kind == "OB") iface = ProjectGenerationSclProjection.CyclicObInterface(iface);
                if (source.InstanceOfName != null)
                {
                    var fb = input.Spec.Artifacts.Single(a => a.Kind == "FB" && a.Name == source.InstanceOfName);
                    if (!artifact.DependsOn.Contains(fb.Id)) throw new InvalidDataException("Instance DB must declare its FB dependency.");
                    iface = parsed[fb.Id].Interface;
                }
                iface = ProjectGenerationSclProjection.ExpandTypes(iface, parsed.Values.Where(o => o.Kind == "UDT").ToDictionary(o => o.Name, o => o.Interface, StringComparer.Ordinal));
                var obj = new ProjectGenerationVerificationObject { Scope = source.Kind == "UDT" ? "types" : "blocks",
                    Kind = source.Kind, Name = source.Name, Number = identity.Number,
                    Language = source.Kind == "UDT" ? "UDT" : identity.Language, ContentFormat = "scl",
                    ContentComplete = true, InterfaceXml = iface, InterfaceComplete = true,
                    InstanceOfName = source.InstanceOfName ?? "", InstanceComplete = true };
                var extension = source.Kind == "UDT" ? ".udt" : source.Kind == "DB" ? ".db" : ".scl";
                obj.Documents.Add(extension, source.Text);
                expected.Objects.Add(obj);
            }
            foreach (var table in input.Spec.TagTables)
                expected.TagTables.Add(new ProjectGenerationVerificationTagTable { Name = table.Name, Complete = true,
                    Tags = table.Tags.Select(t => new ProjectGenerationVerificationTag { Name = t.Name, DataType = t.DataType, LogicalAddress = t.LogicalAddress }).ToList() });
            var plan = ProjectGenerationBuilder.FromValidatedSpec(input, identities, "numbering-v1;" + input.InputHash);
            if (plan.TagTables.Count == 0)
            {
                // An undeletable default table exists on a new V21 CPU. This tool-defined
                // empty table is declared before creation and compared like any user table.
                plan.TagTables = new[] { new ProjectGenerationEngineTagTable { Name = "_TiaGuard_Default" } };
                expected.TagTables.Add(new ProjectGenerationVerificationTagTable { Name = "_TiaGuard_Default", Complete = true });
            }
            return new Prepared { Input = input, Expected = expected, Plan = plan };
        }
    }
}
