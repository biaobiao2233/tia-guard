#if PROJECT_GENERATION_COMPILATION_V1
using System.Linq;

namespace TiaGuard.Openness
{
    public static partial class ProjectGenerationBuilder
    {
        public const string BoundCompilationRevision = "be8453e79914457901b01eb62420d1699833e492";

        public static ProjectGenerationCompileEvidence FromCompilationReport(CompilationReport report) =>
            report == null ? null : new ProjectGenerationCompileEvidence
            {
                ProviderRevision = BoundCompilationRevision, Executed = true,
                State = report.State, ErrorCount = report.ErrorCount, WarningCount = report.WarningCount,
                DiagnosticsComplete = report.DiagnosticsComplete, FullResult = report,
                CollectionFailures = report.CollectionFailures.Select(f => f.Code + " at " + f.Location).ToArray()
            };
    }
}
#endif
