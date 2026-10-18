using System;
using System.IO;
using System.Runtime.CompilerServices;
using Siemens.Engineering.SW;

namespace TiaGuard.Openness
{
    public static partial class ProjectGenerationWorkflow
    {
        public static ProjectGenerationBuildResult Build(string sourceDirectory, string outputDirectory,
            string ownedScratchRoot, string publicApiDirectory = null, string expectedInputHash = null)
        {
            var failure = new ProjectGenerationBuildResult();
            try
            {
                var prepared = Prepare(sourceDirectory);
                failure.InputHash = prepared.Input.InputHash;
                if (expectedInputHash != null && expectedInputHash != failure.InputHash)
                    throw new InvalidDataException("Generation input changed after preview; request a new preview.");
                OpennessAccess.RequireAccess();
                OpennessRuntime.Initialize(publicApiDirectory);
                return RunOffline(prepared, outputDirectory, ownedScratchRoot);
            }
            catch (Exception ex) { failure.Failures.Add(ex.GetType().Name + ": " + ex.Message); return failure; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ProjectGenerationBuildResult RunOffline(Prepared prepared, string outputDirectory, string scratchRoot)
        {
            ProjectGenerationVerificationObservation observation = null;
            var result = ProjectGenerationBuilder.Build(prepared.Plan, outputDirectory, scratchRoot,
                (stage, plan) => new ProjectGenerationOpennessSession(stage, plan),
                session => ProjectGenerationBuilder.FromCompilationReport(CompilationService.Compile((PlcSoftware)session.NativePlc)),
                session =>
                {
                    var native = (ProjectGenerationOpennessSession)session;
                    var reopen = ProjectGenerationCpuProfiles.FindByCpu(prepared.Plan.CpuTypeIdentifier).Family == "S7-1500";
                    if (reopen) native.ReopenOwnedProjectForReadback();
                    var evidence = Path.Combine(Path.GetFullPath(scratchRoot), "readback-" + Guid.NewGuid().ToString("N"));
                    observation = ProjectGenerationReadback.Capture(native.NativeProject,
                        evidence, (scope, name) => "scl", reopen);
                    return ProjectGenerationVerifier.Compare(prepared.Expected, observation);
                });
            result.InputHash = prepared.Input.InputHash;
            result.Observation = observation;
            return result;
        }
    }
}
