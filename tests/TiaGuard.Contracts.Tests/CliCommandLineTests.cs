using System.IO;
using TiaGuard.Cli;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.Contracts.Tests
{
    public sealed class CliCommandLineTests
    {
        [Fact]
        public void ParsesDoctorExportBuildAndVerify()
        {
            Assert.True(CliCommandLine.TryParse(
                new[] { "doctor" }, out var doctor));
            Assert.Equal("doctor", doctor.Command);

            Assert.True(CliCommandLine.TryParse(
                new[] { "export", "demo.ap21", "repo" }, out var export));
            Assert.Equal("export", export.Command);
            Assert.Equal("demo.ap21", export.Source);
            Assert.Equal("repo", export.Target);

            Assert.True(CliCommandLine.TryParse(
                new[] { "build", "repo", "--output", "rebuilt" }, out var build));
            Assert.Equal("build", build.Command);
            Assert.Equal("repo", build.Source);
            Assert.Equal("rebuilt", build.Target);

            Assert.True(CliCommandLine.TryParse(
                new[] { "verify", "original.ap21", "rebuilt.ap21" }, out var verify));
            Assert.Equal("verify", verify.Command);
            Assert.Equal("original.ap21", verify.Source);
            Assert.Equal("rebuilt.ap21", verify.Target);
        }

        [Fact]
        public void DoctorReportIsReadyOnlyWhenAllPrerequisitesArePresent()
        {
            var api = @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";
            var report = OpennessEnvironmentProbe.Evaluate(
                true,
                api,
                path => path.EndsWith("Siemens.Engineering.Base.dll") ||
                        path.EndsWith("Siemens.Engineering.Step7.dll"),
                path => path.EndsWith("Portal V21"),
                true);

            Assert.True(report.Ready);
            Assert.Equal("x64", report.ProcessArchitecture);
            Assert.True(report.TiaPortalV21);
            Assert.True(report.OpennessAssemblies);
            Assert.True(report.EffectiveOpennessGroupMembership);
            Assert.Empty(report.Issues);
        }

        [Fact]
        public void DoctorReportListsBlockingPrerequisites()
        {
            var api = @"C:\missing\PublicAPI\V21\net48";
            var report = OpennessEnvironmentProbe.Evaluate(
                false,
                api,
                _ => false,
                _ => false,
                false);

            Assert.False(report.Ready);
            Assert.Equal("x86", report.ProcessArchitecture);
            Assert.False(report.TiaPortalV21);
            Assert.False(report.OpennessAssemblies);
            Assert.False(report.EffectiveOpennessGroupMembership);
            Assert.Equal(4, report.Issues.Count);
        }

        [Theory]
        [InlineData()]
        [InlineData("doctor", "extra")]
        [InlineData("export")]
        [InlineData("export", "demo.ap21", "repo", "extra")]
        [InlineData("build", "repo", "-o", "rebuilt")]
        [InlineData("build", "repo", "--output", " ")]
        [InlineData("verify", "original.ap21")]
        [InlineData("VERIFY", "original.ap21", "rebuilt.ap21")]
        public void RejectsMalformedArguments(params string[] args)
        {
            Assert.False(CliCommandLine.TryParse(args, out var invocation));
            Assert.Null(invocation);
        }
    }
}
