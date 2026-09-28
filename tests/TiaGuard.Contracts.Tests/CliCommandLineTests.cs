using TiaGuard.Cli;
using Xunit;

namespace TiaGuard.Contracts.Tests
{
    public sealed class CliCommandLineTests
    {
        [Fact]
        public void ParsesExportBuildAndVerify()
        {
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

        [Theory]
        [InlineData()]
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
