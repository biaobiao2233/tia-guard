#if PROJECT_GENERATION_COMPILATION_V1
using System;

namespace TiaGuard.Openness
{
    public static partial class ProjectGenerationBuilder
    {
        public static ProjectGenerationBuildResult BuildWithCompilation(ProjectGenerationBuilderPlan plan,
            string outputDirectory, string ownedScratchRoot, string publicApiDirectory,
            Func<IProjectGenerationSession, ProjectGenerationVerificationResult> verify) =>
            BuildOffline(plan, outputDirectory, ownedScratchRoot, publicApiDirectory,
                plc => FromCompilationReport(CompilationService.Compile(plc)), verify);
    }
}
#endif
