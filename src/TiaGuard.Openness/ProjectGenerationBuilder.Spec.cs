#if PROJECT_GENERATION_SPEC_V1
using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaGuard.Openness
{
    public static partial class ProjectGenerationBuilder
    {
        public const string BoundSpecRevision = "072c14f24fff1dbce712827886c1ba23e17cf5d5";

        // #44 deliberately has no block number/language/instance metadata. Consumers must
        // supply source-bound identities (D or a reviewed fixture), not infer them from names
        // or assign a guessed number. No speculative schema fields are added to A's model.
        public static ProjectGenerationBuilderPlan FromValidatedSpec(ProjectGenerationSpecInput input,
            IReadOnlyDictionary<string, ProjectGenerationObject> sourceIdentities, string identityRevision)
        {
            if (input == null || sourceIdentities == null || string.IsNullOrWhiteSpace(identityRevision))
                throw new ArgumentException("Validated spec and version-bound source identities are required.");
            input.ValidateUnchanged();
            if (sourceIdentities.Count != input.Spec.Artifacts.Count)
                throw new ArgumentException("Source identity coverage must match the complete artifact set.");
            var artifacts = new List<ProjectGenerationEngineArtifact>();
            foreach (var declared in input.OrderedArtifacts)
            {
                if (!sourceIdentities.TryGetValue(declared.Id, out var identity) || identity == null ||
                    identity.Kind != declared.Kind || identity.Name != declared.Name)
                    throw new ArgumentException("Source identity differs from declaration: " + declared.Id);
                artifacts.Add(new ProjectGenerationEngineArtifact
                {
                    Id = declared.Id, SourcePath = declared.SourcePath, Format = declared.Format,
                    Sha256 = input.SourceSha256[declared.SourcePath], DependsOn = declared.DependsOn.ToArray(),
                    Object = new ProjectGenerationObject { Kind = identity.Kind, Name = identity.Name,
                        Number = identity.Number, Language = identity.Language, InstanceOfName = identity.InstanceOfName,
                        Group = identity.Group, Protected = identity.Protected }
                });
            }
            var plan = new ProjectGenerationBuilderPlan
            {
                ContractRevision = BoundSpecRevision, IdentityRevision = identityRevision,
                SourceRoot = input.SourceRoot, ProjectName = input.Spec.ProjectName,
                PlcName = input.Spec.Plc.Name, CpuTypeIdentifier = input.Spec.Plc.CpuTypeIdentifier,
                TagTables = input.Spec.TagTables.Select(t => new ProjectGenerationEngineTagTable
                { Name = t.Name, Tags = t.Tags.Select(tag => new ProjectGenerationEngineTag
                    { Name = tag.Name, DataType = tag.DataType, LogicalAddress = tag.LogicalAddress }).ToArray() }).ToArray(),
                Artifacts = artifacts.ToArray(),
                ValidatedSourceBytes = input.OrderedSourcePaths.ToDictionary(p => p, p => input.GetSourceBytes(p), StringComparer.Ordinal)
            };
            ProjectGenerationImporter.Plan(plan);
            return plan;
        }
    }
}
#endif
