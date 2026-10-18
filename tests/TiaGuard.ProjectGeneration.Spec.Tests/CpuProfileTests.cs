using System;
using System.IO;
using System.Linq;
using System.Text;
using TiaGuard.Cli;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.ProjectGeneration.Spec.Tests
{
    public sealed class CpuProfileTests : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "cpu-" + Guid.NewGuid().ToString("N"));
        public CpuProfileTests() { Directory.CreateDirectory(root); }
        public void Dispose() { FileSystemSafety.DeleteOwnedTree(root); }

        [Fact]
        public void DefaultRemains1200AndProfileSelectionOnlyChangesCpuInput()
        {
            var legacy = ProjectGenerationInitializer.Initialize(Path.Combine(root, "legacy"));
            var selected = ProjectGenerationInitializer.Initialize(Path.Combine(root, "selected"), ProjectGenerationCpuProfiles.S71500ProfileId);
            Assert.Equal(ProjectGenerationCpuProfiles.DefaultCpuTypeIdentifier, legacy.Spec.Plc.CpuTypeIdentifier);
            Assert.Equal(ProjectGenerationCpuProfiles.S71500CpuTypeIdentifier, selected.Spec.Plc.CpuTypeIdentifier);
            Assert.Equal(legacy.GetSourceBytes("sources/Main.scl"), selected.GetSourceBytes("sources/Main.scl"));
            Assert.NotEqual(legacy.InputHash, selected.InputHash);
            Assert.Equal(legacy.Spec.ProjectName, selected.Spec.ProjectName);
            Assert.Equal(legacy.Spec.Plc.Name, selected.Spec.Plc.Name);
            Assert.Equal(legacy.SourceSha256, selected.SourceSha256);
            Assert.True(ProjectGenerationCapabilities.Supports("V21", selected.Spec.Plc.CpuTypeIdentifier, "FB", "scl"));
            Assert.False(RoundTripProfile.SupportsCpu(selected.Spec.Plc.CpuTypeIdentifier));
            Assert.True(RoundTripProfile.SupportsCpu(legacy.Spec.Plc.CpuTypeIdentifier));
        }

        [Theory]
        [InlineData(null)][InlineData("")][InlineData("s7-1500")]
        [InlineData("S7-1500-1511-1pn-v2.9")][InlineData("s7-1500-1511-1pn-v3.0")]
        public void UnknownProfileRefusesBeforeAnyFilesAreCreated(string profile)
        {
            var destination = Path.Combine(root, "input");
            Assert.Throws<ArgumentException>(() => ProjectGenerationInitializer.Initialize(destination, profile));
            Assert.False(Directory.Exists(destination));
            Assert.Empty(Directory.GetFileSystemEntries(root));
        }

        [Theory]
        [InlineData("OrderNumber:6ES7 511-1AK02-0AB0/V2.8")]
        [InlineData("OrderNumber:6ES7 511-1AK02-0AB0/V3.0")]
        [InlineData("OrderNumber:6ES7 511-1AL03-0AB0/V2.9")]
        [InlineData("OrderNumber:6ES7511-1AK02-0AB0/V2.9")]
        public void OtherModelFirmwareOrAliasDoesNotGainGenerationSupport(string cpu)
        {
            var input = ProjectGenerationInitializer.Initialize(Path.Combine(root, "input"));
            var file = Path.Combine(input.SourceRoot, ProjectGenerationSpecLoader.DefaultSpecFileName);
            File.WriteAllText(file, File.ReadAllText(file).Replace(ProjectGenerationCpuProfiles.DefaultCpuTypeIdentifier, cpu), new UTF8Encoding(false));
            Assert.Throws<InvalidDataException>(() => ProjectGenerationSpecLoader.Load(file));
            Assert.False(ProjectGenerationCapabilities.Supports("V21", cpu, "OB", "scl"));
        }

        [Fact]
        public void ChangingOnlyCpuInvalidatesFrozenInput()
        {
            var input = ProjectGenerationInitializer.Initialize(Path.Combine(root, "input"));
            var file = Path.Combine(input.SourceRoot, ProjectGenerationSpecLoader.DefaultSpecFileName);
            File.WriteAllText(file, File.ReadAllText(file).Replace(ProjectGenerationCpuProfiles.DefaultCpuTypeIdentifier,
                ProjectGenerationCpuProfiles.S71500CpuTypeIdentifier), new UTF8Encoding(false));
            Assert.Throws<InvalidDataException>(() => input.ValidateUnchanged());
            Assert.NotEqual(input.InputHash, ProjectGenerationSpecLoader.Load(file).InputHash);
        }

        [Fact]
        public void ExplicitInitProfileParsesWithoutChangingLegacyCommands()
        {
            Assert.True(CliCommandLine.TryParse(new[] { "init", "new", "--profile", ProjectGenerationCpuProfiles.S71500ProfileId }, out var parsed));
            Assert.Equal(ProjectGenerationCpuProfiles.S71500ProfileId, parsed.CpuProfile);
            Assert.True(CliCommandLine.TryParse(new[] { "init", "new" }, out var legacy));
            Assert.Null(legacy.CpuProfile);
            foreach (var args in new[] {
                new[] { "init", "new", "--profile" }, new[] { "init", "new", "--profile", " " },
                new[] { "init", "new", "--cpu", "1500" }, new[] { "init", "new", "--profile", "1500", "extra" } })
                Assert.False(CliCommandLine.TryParse(args, out _));
        }
    }
}
