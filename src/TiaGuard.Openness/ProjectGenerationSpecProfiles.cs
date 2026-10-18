using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaGuard.Openness
{
    // Generation-only allowlist. This does not broaden RoundTripProfile, LAD
    // patching, simulator access, or qualification to an entire CPU family.
    public sealed class ProjectGenerationCpuProfile
    {
        public string Id { get; }
        public string CpuTypeIdentifier { get; }
        public string Family { get; }
        public bool RequiresSoftwareUnitReadback { get; }
        internal ProjectGenerationCpuProfile(string id, string cpu, string family, bool units)
        { Id = id; CpuTypeIdentifier = cpu; Family = family; RequiresSoftwareUnitReadback = units; }
    }

    public static class ProjectGenerationCpuProfiles
    {
        public const string DefaultProfileId = "s7-1200-1212c-v4.7";
        public const string DefaultCpuTypeIdentifier = "OrderNumber:6ES7 212-1AE40-0XB0/V4.7";
        public const string S71500ProfileId = "s7-1500-1511-1pn-v2.9";
        public const string S71500CpuTypeIdentifier = "OrderNumber:6ES7 511-1AK02-0AB0/V2.9";
        private static readonly IReadOnlyList<ProjectGenerationCpuProfile> profiles = Array.AsReadOnly(new[]
        {
            new ProjectGenerationCpuProfile(DefaultProfileId, DefaultCpuTypeIdentifier, "S7-1200", false),
            // Exact CPU/firmware only; catalog availability and native qualification
            // are separate from accepting a generation input declaration.
            new ProjectGenerationCpuProfile(S71500ProfileId, S71500CpuTypeIdentifier, "S7-1500", true)
        });
        public static IReadOnlyList<ProjectGenerationCpuProfile> All => profiles;
        public static ProjectGenerationCpuProfile FindByCpu(string cpu) =>
            profiles.SingleOrDefault(p => string.Equals(p.CpuTypeIdentifier, cpu, StringComparison.Ordinal));
        public static bool SupportsCpu(string cpu) => FindByCpu(cpu) != null;
        public static ProjectGenerationCpuProfile RequireById(string id) =>
            profiles.SingleOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException("Unknown generation CPU profile; use an exact documented profile ID.");
    }
}
