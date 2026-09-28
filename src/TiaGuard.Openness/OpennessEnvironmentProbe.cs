using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace TiaGuard.Openness
{
    [DataContract]
    public sealed class OpennessEnvironmentReport
    {
        [DataMember(Name = "ready", Order = 1)]
        public bool Ready { get; set; }

        [DataMember(Name = "processArchitecture", Order = 2)]
        public string ProcessArchitecture { get; set; }

        [DataMember(Name = "tiaPortalV21", Order = 3)]
        public bool TiaPortalV21 { get; set; }

        [DataMember(Name = "publicApiDirectory", Order = 4)]
        public string PublicApiDirectory { get; set; }

        [DataMember(Name = "opennessAssemblies", Order = 5)]
        public bool OpennessAssemblies { get; set; }

        [DataMember(Name = "effectiveOpennessGroupMembership", Order = 6)]
        public bool EffectiveOpennessGroupMembership { get; set; }

        [DataMember(Name = "issues", Order = 7)]
        public List<string> Issues { get; set; } = new List<string>();
    }

    public static class OpennessEnvironmentProbe
    {
        public static OpennessEnvironmentReport Inspect()
        {
            var membership = false;
            string membershipFailure = null;
            try
            {
                membership = OpennessAccess.HasEffectiveGroupMembership();
            }
            catch (Exception error)
            {
                membershipFailure =
                    "Unable to evaluate effective Siemens TIA Openness group membership: " +
                    error.GetType().Name + ".";
            }

            var report = Evaluate(
                Environment.Is64BitProcess,
                OpennessRuntime.DefaultPublicApiDirectory,
                File.Exists,
                Directory.Exists,
                membership);

            if (membershipFailure != null)
            {
                report.Ready = false;
                report.Issues.Add(membershipFailure);
            }

            return report;
        }

        internal static OpennessEnvironmentReport Evaluate(
            bool is64BitProcess,
            string publicApiDirectory,
            Func<string, bool> fileExists,
            Func<string, bool> directoryExists,
            bool hasEffectiveGroupMembership)
        {
            if (string.IsNullOrWhiteSpace(publicApiDirectory))
                throw new ArgumentException("PublicAPI directory is required.", nameof(publicApiDirectory));
            if (fileExists == null) throw new ArgumentNullException(nameof(fileExists));
            if (directoryExists == null) throw new ArgumentNullException(nameof(directoryExists));

            var api = Path.GetFullPath(publicApiDirectory);
            var portalRoot = Path.GetFullPath(Path.Combine(api, @"..\..\.."));
            var baseDll = Path.Combine(api, "Siemens.Engineering.Base.dll");
            var step7Dll = Path.Combine(api, "Siemens.Engineering.Step7.dll");

            var report = new OpennessEnvironmentReport
            {
                ProcessArchitecture = is64BitProcess ? "x64" : "x86",
                TiaPortalV21 = directoryExists(portalRoot),
                PublicApiDirectory = api,
                OpennessAssemblies = fileExists(baseDll) && fileExists(step7Dll),
                EffectiveOpennessGroupMembership = hasEffectiveGroupMembership
            };

            if (!is64BitProcess)
                report.Issues.Add("TIA-Guard requires an x64 process.");
            if (!report.TiaPortalV21)
                report.Issues.Add("TIA Portal V21 installation directory was not found.");
            if (!report.OpennessAssemblies)
                report.Issues.Add("TIA Portal V21 PublicAPI Base/Step7 assemblies were not found.");
            if (!report.EffectiveOpennessGroupMembership)
                report.Issues.Add(
                    "The current Windows logon token does not include the Siemens TIA Openness group.");

            report.Ready =
                is64BitProcess &&
                report.TiaPortalV21 &&
                report.OpennessAssemblies &&
                report.EffectiveOpennessGroupMembership;

            return report;
        }
    }
}
