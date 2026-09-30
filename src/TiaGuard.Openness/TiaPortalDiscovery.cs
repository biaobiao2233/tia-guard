using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Siemens.Engineering;

namespace TiaGuard.Openness
{
    public sealed class TiaPortalCandidate
    {
        public int ProcessId { get; set; }
        public string ProjectPath { get; set; }
        public string ProjectFileName { get; set; }
        public string ProcessPath { get; set; }
        public string TiaVersion { get; set; }
    }

    public static class TiaPortalDiscovery
    {
        public static IReadOnlyList<TiaPortalCandidate> ListOpenV21Projects()
        {
            OpennessAccess.RequireAccess();
            OpennessRuntime.Initialize();
            return ListOpenV21ProjectsCore();
        }

        // Keep Siemens-engineering type references out of the public entry method so
        // the runtime resolver is installed before the CLR needs those assemblies.
        private static IReadOnlyList<TiaPortalCandidate> ListOpenV21ProjectsCore()
        {
            return TiaPortal.GetProcesses()
                .Where(IsV21)
                .Where(process => process.ProjectPath != null)
                .Select(process => new TiaPortalCandidate
                {
                    ProcessId = process.Id,
                    ProjectPath = process.ProjectPath.FullName,
                    ProjectFileName = Path.GetFileName(process.ProjectPath.FullName),
                    ProcessPath = process.Path?.FullName,
                    TiaVersion = GetVersion(process)
                })
                .OrderBy(candidate => candidate.ProcessId)
                .ToList();
        }

        private static bool IsV21(TiaPortalProcess process)
        {
            return (process.Path?.FullName.IndexOf("Portal V21", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                || process.InstalledSoftware.Any(product => IsVersion21(product.Version));
        }

        private static string GetVersion(TiaPortalProcess process)
        {
            return process.InstalledSoftware
                .Select(product => product.Version)
                .FirstOrDefault(IsVersion21) ?? "V21";
        }

        private static bool IsVersion21(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return false;

            var normalized = version.Trim();
            return normalized.StartsWith("21", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("V21", StringComparison.OrdinalIgnoreCase);
        }
    }
}
