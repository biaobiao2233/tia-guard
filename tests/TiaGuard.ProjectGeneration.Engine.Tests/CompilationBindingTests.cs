#if PROJECT_GENERATION_COMPILATION_V1
using System;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.ProjectGeneration.Engine.Tests
{
    public sealed class CompilationBindingTests
    {
        [Fact]
        public void ExactCCompleteReportIsKeptInFullAndPermitsPublication()
        {
            var report = CompilationMessageCollector.Collect(new CompileResult());
            var evidence = ProjectGenerationBuilder.FromCompilationReport(report);
            Assert.True(evidence.AllowsPublication);
            Assert.Same(report, evidence.FullResult);
            Assert.Equal(ProjectGenerationBuilder.BoundCompilationRevision, evidence.ProviderRevision);
        }
        [Theory]
        [InlineData("null")]
        [InlineData("unknown")]
        [InlineData("negative")]
        [InlineData("errors")]
        [InlineData("missing-count")]
        [InlineData("no-messages")]
        public void CIncompleteOrFailedResultsCannotBeCollapsedToZero(string scenario)
        {
            var result = new CompileResult();
            if (scenario == "unknown") result.State = "Unknown";
            if (scenario == "negative") result.WarningCount = -1;
            if (scenario == "errors") result.ErrorCount = 1;
            if (scenario == "missing-count") result.ErrorCount = null;
            if (scenario == "no-messages") result.Messages = null;
            var report = CompilationMessageCollector.Collect(scenario == "null" ? null : result);
            var evidence = ProjectGenerationBuilder.FromCompilationReport(report);
            Assert.False(evidence.AllowsPublication);
            Assert.Same(report, evidence.FullResult);
        }
        public sealed class CompileResult
        {
            public string State { get; set; } = "Success";
            public int? ErrorCount { get; set; } = 0;
            public int? WarningCount { get; set; } = 0;
            public object[] Messages { get; set; } = new object[0];
        }
    }
}
#endif
