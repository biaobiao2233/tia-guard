using System.Text;
using System.Text.Json;
using TwinCat3.Contracts;
using Xunit;

namespace TwinCat3.EnvironmentTests;

public sealed class ContractTests
{
    private static GenerationManifest Valid() => new()
    {
        SchemaVersion = "1.0", SolutionName = "Motor", ConfigurationName = "MotorConfig", PlcProjectName = "MotorPlc",
        Environment = new(),
        Artifacts = new[] { new ArtifactSpec { Id = "main", Name = "MAIN", Kind = ArtifactKind.Program,
            DeclarationFile = "MAIN.decl.st", ImplementationFile = "MAIN.impl.st" } },
        Tasks = new[] { new TaskSpec { Name = "PlcTask", CycleTimeUs = 10000, Priority = 20, Programs = new[] { "MAIN" } } },
        Libraries = Array.Empty<LibrarySpec>()
    };

    [Fact] public void FrozenHashIncludesManifestWhitespaceAndImplementationComments()
    {
        using var fixture = new Fixture();
        var first = GenerationInputLoader.Load(fixture.Path);
        File.AppendAllText(System.IO.Path.Combine(fixture.Path, "MAIN.impl.st"), "\n// edit after preview");
        var second = GenerationInputLoader.Load(fixture.Path);
        Assert.NotEqual(first.InputHash, second.InputHash);
        File.AppendAllText(System.IO.Path.Combine(fixture.Path, GenerationInputLoader.ManifestFileName), "\n ");
        Assert.NotEqual(second.InputHash, GenerationInputLoader.Load(fixture.Path).InputHash);
        Assert.Equal("", first.Files["MAIN.impl.st"].Text);
    }

    [Fact] public void FrozenBytesAndCollectionsCannotRewriteExpectedSource()
    {
        using var fixture = new Fixture();
        var input = GenerationInputLoader.Load(fixture.Path);
        var source = input.Files["MAIN.decl.st"];
        var copy = source.Bytes; copy[0] = 0;
        Assert.StartsWith("PROGRAM MAIN", source.Text);
        Assert.NotEqual(0, source.Bytes[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<ArtifactSpec>)input.Manifest.Artifacts).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)input.Manifest.Tasks[0].Programs).Clear());
    }

    [Fact] public void ExtraFilesAndMissingSourcesAreRejected()
    {
        using var fixture = new Fixture();
        File.WriteAllText(System.IO.Path.Combine(fixture.Path, "untracked.st"), "unexpected");
        Assert.Throws<InvalidDataException>(() => GenerationInputLoader.Load(fixture.Path));
        File.Delete(System.IO.Path.Combine(fixture.Path, "untracked.st"));
        File.Delete(System.IO.Path.Combine(fixture.Path, "MAIN.impl.st"));
        Assert.Throws<InvalidDataException>(() => GenerationInputLoader.Load(fixture.Path));
    }

    [Theory]
    [InlineData("../escape.st")][InlineData("/absolute.st")][InlineData("C:/file.st")]
    [InlineData("folder\\file.st")][InlineData("a//b.st")][InlineData("folder/./b.st")]
    [InlineData("folder./b.st")][InlineData("a /b.st")]
    public void UnsafeSourcePathsFailBeforeReading(string path)
        => Assert.Throws<InvalidDataException>(() => GenerationInputLoader.ValidateRelativePath(path));

    [Theory]
    [InlineData("duplicate")][InlineData("unknown")][InlineData("null")][InlineData("numericKind")]
    public void StrictJsonRejectsAmbiguousInput(string failure)
    {
        using var fixture = new Fixture();
        var path = System.IO.Path.Combine(fixture.Path, GenerationInputLoader.ManifestFileName);
        var json = File.ReadAllText(path);
        json = failure switch
        {
            "duplicate" => json.Replace("\"schemaVersion\": \"1.0\",", "\"schemaVersion\": \"1.0\", \"schemaVersion\": \"2.0\","),
            "unknown" => json.Insert(json.IndexOf('{') + 1, "\"arbitraryComAction\": \"ActivateConfiguration\","),
            "null" => json.Replace("\"libraries\": []", "\"libraries\": null"),
            _ => json.Replace("\"kind\": \"program\"", "\"kind\": 0")
        };
        File.WriteAllText(path, json);
        Assert.Throws<InvalidDataException>(() => GenerationInputLoader.Load(fixture.Path));
    }

    [Fact] public void NullRequiredNamesAndMissingMainScheduleFail()
    {
        Assert.Throws<InvalidDataException>(() => GenerationInputLoader.ValidateManifest(Valid() with { SolutionName = null! }));
        Assert.Throws<InvalidDataException>(() => GenerationInputLoader.ValidateManifest(Valid() with { Tasks = Array.Empty<TaskSpec>() }));
        Assert.Throws<InvalidDataException>(() => GenerationInputLoader.ValidateManifest(Valid() with
        { Tasks = new[] { Valid().Tasks[0] with { Programs = new[] { "Missing" } } } }));
    }

    [Theory]
    [InlineData("duplicates")][InlineData("taskCalls")][InlineData("latestLibrary")][InlineData("standalone")]
    public void UnsupportedProfileDoesNotSilentlyExpand(string failure)
    {
        var valid = Valid();
        var invalid = failure switch
        {
            "duplicates" => valid with { Artifacts = new[] { valid.Artifacts[0], valid.Artifacts[0] with { Id = "other" } } },
            "taskCalls" => valid with { Tasks = new[] { valid.Tasks[0] with { Programs = new[] { "MAIN", "MAIN" } } } },
            "latestLibrary" => valid with { Libraries = new[] { new LibrarySpec { Name = "Tc2_Standard", Version = "*", Vendor = "Beckhoff" } } },
            _ => valid with { Environment = valid.Environment with { Provider = "standalone" } }
        };
        Assert.Throws<InvalidDataException>(() => GenerationInputLoader.ValidateManifest(invalid));
    }

    [Theory]
    [InlineData("notStarted")][InlineData("notCompleted")][InlineData("failedProject")][InlineData("partial")]
    [InlineData("outputMissing")][InlineData("lastError")][InlineData("warningCount")][InlineData("foreign")]
    public void CompilationCannotPassOnIncompleteOrContradictoryEvidence(string failure)
    {
        var good = new CompilationReport { State = CompilationState.Success, BuildStarted = true, BuildCompleted = true,
            FailedProjectCount = 0, ErrorCount = 0, WarningCount = 0, DiagnosticsComplete = true, OutputCaptureComplete = true,
            InputHash = "source", ProjectIdentity = "owned-project", Configuration = "Release", Platform = "TwinCAT RT (x64)" };
        Assert.True(good.Passed);
        var bad = failure switch
        {
            "notStarted" => good with { BuildStarted = false },
            "notCompleted" => good with { BuildCompleted = false },
            "failedProject" => good with { FailedProjectCount = 1 },
            "partial" => good with { DiagnosticsComplete = false },
            "outputMissing" => good with { OutputCaptureComplete = false },
            "warningCount" => good with { WarningCount = 1 },
            "foreign" => good with { Diagnostics = new[] { new CompilationDiagnostic { Message = "foreign", Severity = DiagnosticSeverity.Message } } },
            _ => good with { Diagnostics = new[] { new CompilationDiagnostic { Message = "last error", Severity = DiagnosticSeverity.Error, Correlated = true } } }
        };
        Assert.False(bad.Passed);
    }

    [Fact] public void VerificationRequiresCompleteIndependentEvidence()
    {
        Assert.False(new VerificationReport().Passed);
        Assert.False(new VerificationReport { Verdict = VerificationVerdict.Pass, Complete = true, Unknowns = new[] { "opaque" } }.Passed);
        Assert.False(new VerificationReport { Verdict = VerificationVerdict.Pass, Complete = true, Differences = new[] { "task drift" } }.Passed);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string ownerRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tc53-contract-tests");
        public string Path { get; }
        public Fixture()
        {
            Path = System.IO.Path.Combine(ownerRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, GenerationInputLoader.ManifestFileName), JsonSerializer.Serialize(Valid(), ContractJson.Options));
            File.WriteAllText(System.IO.Path.Combine(Path, "MAIN.decl.st"), "PROGRAM MAIN\nVAR\nEND_VAR", new UTF8Encoding(false));
            File.WriteAllText(System.IO.Path.Combine(Path, "MAIN.impl.st"), "", new UTF8Encoding(false));
        }
        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(Path);
            var boundary = System.IO.Path.GetFullPath(ownerRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup.");
            Directory.Delete(resolved, recursive: true);
        }
    }
}
