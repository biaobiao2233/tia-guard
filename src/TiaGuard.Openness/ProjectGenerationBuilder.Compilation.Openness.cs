#if PROJECT_GENERATION_COMPILATION_V1
namespace TiaGuard.Openness
{
    public static partial class ProjectGenerationBuilder
    {
        public static ProjectGenerationBuildResult BuildWithCompilation(ProjectGenerationBuilderPlan plan,
            string outputDirectory, string ownedScratchRoot, string publicApiDirectory) =>
            BuildOffline(plan, outputDirectory, ownedScratchRoot, publicApiDirectory,
                plc => FromCompilationReport(CompilationService.Compile(plc)));
    }
}
#endif
