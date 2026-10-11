using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TiaGuard.Openness;
using Xunit;

namespace TiaGuard.ProjectGeneration.Engine.Tests
{
    public sealed class EngineTests
    {
        [Theory]
        [InlineData("FAIL")]
        [InlineData("UNKNOWN")]
        [InlineData(null)]
        public void CompleteCompilationCannotPublishWithoutFullVerificationPass(string verdict)
        {
            using (var f = new Fixture())
            {
                var result = f.Build(verify: s => verdict == null ? null : new ProjectGenerationVerificationResult { Verdict = verdict });
                Assert.False(result.Published); Assert.False(Directory.Exists(f.Output));
                Assert.Equal("verify", result.Phase); Assert.True(f.Session.Disposed);
                Assert.NotEmpty(result.Failures);
            }
        }
        [Fact]
        public void FullVerificationRunsWhileOwnedSessionIsOpenAndPrecedesPublication()
        {
            using (var f = new Fixture())
            {
                var result = f.Build(verify: s =>
                {
                    Assert.False(f.Session.Disposed); Assert.False(Directory.Exists(f.Output));
                    return new ProjectGenerationVerificationResult { Verdict = "PASS" };
                });
                Assert.True(result.Published); Assert.Equal("complete", result.Phase); Assert.Equal("PASS", result.Verification.Verdict);
            }
        }
        [Fact]
        public void OrdersTypesThenExplicitDependenciesThenObAndImportsOneMultiObjectSourceOnce()
        {
            using (var f = new Fixture())
            {
                f.Plan.Artifacts = new[] { f.Add("ob", "Main", "OB", 1, "main.scl", "fb", "db"),
                    f.Add("db", "Inst", "DB", 1, "program.scl", "fb"),
                    f.Add("fb", "Controller", "FB", 1, "program.scl", "udt"),
                    f.Add("udt", "Settings", "UDT", null, "types.scl") };
                f.Plan.Artifacts[1].Object.InstanceOfName = "Controller";
                var units = ProjectGenerationImporter.Plan(f.Plan);
                Assert.Equal(new[] { "types.scl", "program.scl", "main.scl" }, units.Select(u => u.SourcePath));
                Assert.Equal(2, units[1].Expected.Count);
                var r = f.Build();
                Assert.True(r.Published, string.Join(";", r.Failures));
                Assert.Equal(3, f.Session.Calls.Count);
                Assert.Empty(r.CleanupFailures);
                Assert.Empty(Directory.GetDirectories(f.Scratch));
                Assert.Equal("fixture bytes", File.ReadAllText(Path.Combine(f.Source, "program.scl")));
            }
        }

        [Theory]
        [InlineData("cycle")]
        [InlineData("self")]
        [InlineData("dangling")]
        [InlineData("duplicate-id")]
        [InlineData("duplicate-name")]
        [InlineData("duplicate-number")]
        [InlineData("source-unit-cycle")]
        [InlineData("shared-hash")]
        [InlineData("undeclared-instance-fb")]
        [InlineData("cpu-fallback")]
        [InlineData("no-ob1")]
        [InlineData("s7res-primary")]
        [InlineData("nested-group")]
        [InlineData("protected")]
        [InlineData("scl-lad")]
        [InlineData("null-language")]
        public void InvalidPlansFailBeforeFactory(string scenario)
        {
            using (var f = new Fixture())
            {
                var a = f.Plan.Artifacts[0];
                var b = f.Add("fb", "Controller", "FB", 1, "program.scl");
                f.Plan.Artifacts = new[] { a, b };
                switch (scenario)
                {
                    case "cycle": a.DependsOn = new[] { "fb" }; b.DependsOn = new[] { "ob" }; break;
                    case "self": a.DependsOn = new[] { "ob" }; break;
                    case "dangling": a.DependsOn = new[] { "missing" }; break;
                    case "duplicate-id": b.Id = "ob"; break;
                    case "duplicate-name": b.Object.Name = "Main"; break;
                    case "duplicate-number": b.Object.Kind = "OB"; break;
                    case "source-unit-cycle":
                        var c = f.Add("fc", "Helper", "FC", 1, "main.scl", "fb");
                        b.DependsOn = new[] { "ob" }; f.Plan.Artifacts = new[] { a, b, c }; break;
                    case "shared-hash": b.SourcePath = "main.scl"; b.Sha256 = new string('0', 64); break;
                    case "undeclared-instance-fb": b.Object.Kind = "DB"; b.Object.Language = "DB"; b.Object.InstanceOfName = "Missing"; break;
                    case "cpu-fallback": f.Plan.CpuTypeIdentifier = "OrderNumber:6ES7 214-1AG40-0XB0/V4.7"; break;
                    case "no-ob1": a.Object.Number = 2; break;
                    case "s7res-primary": a.Format = "simatic-sd"; a.SourcePath = "main.s7res"; break;
                    case "nested-group": a.Object.Group = "Hidden"; break;
                    case "protected": a.Object.Protected = true; break;
                    case "scl-lad": a.Object.Language = "LAD"; break;
                    case "null-language": a.Object.Language = null; break;
                }
                var r = f.Build();
                Assert.False(r.Published); Assert.NotEmpty(r.Failures); Assert.False(f.FactoryCalled);
            }
        }

        [Theory]
        [InlineData("../outside.scl")]
        [InlineData("folder/../main.scl")]
        [InlineData("C:\\escaped.scl")]
        [InlineData("main.scl:ads")]
        [InlineData("./main.scl")]
        public void PathEscapeRejected(string path)
        {
            using (var f = new Fixture())
            {
                f.Plan.Artifacts[0].SourcePath = path;
                Assert.False(f.Build().Published); Assert.False(f.FactoryCalled);
            }
        }

        [Theory]
        [InlineData("null")]
        [InlineData("unknown-state")]
        [InlineData("not-executed")]
        [InlineData("missing-error-count")]
        [InlineData("missing-warning-count")]
        [InlineData("negative-warning-count")]
        [InlineData("errors")]
        [InlineData("incomplete")]
        [InlineData("collection-failure")]
        [InlineData("missing-failures")]
        [InlineData("missing-provider")]
        [InlineData("missing-full-result")]
        [InlineData("exception")]
        public void CompileGatesNeverPublishUnknownOrPartialDiagnostics(string scenario)
        {
            using (var f = new Fixture())
            {
                var c = Fixture.CleanCompile();
                switch (scenario)
                {
                    case "null": c = null; break;
                    case "unknown-state": c.State = "Unknown"; break;
                    case "not-executed": c.Executed = false; break;
                    case "missing-error-count": c.ErrorCount = null; break;
                    case "missing-warning-count": c.WarningCount = null; break;
                    case "negative-warning-count": c.WarningCount = -1; break;
                    case "errors": c.ErrorCount = 1; break;
                    case "incomplete": c.DiagnosticsComplete = false; break;
                    case "collection-failure": c.CollectionFailures = new[] { "message enumeration failed" }; break;
                    case "missing-failures": c.CollectionFailures = null; break;
                    case "missing-provider": c.ProviderRevision = null; break;
                    case "missing-full-result": c.FullResult = null; break;
                }
                var r = f.Build(compile: s => scenario == "exception" ? throw new IOException("compile fault") : c);
                Assert.False(r.Published); Assert.NotEmpty(r.Failures);
                Assert.False(Directory.Exists(f.Output)); Assert.True(f.Session.Disposed);
                Assert.Empty(Directory.GetDirectories(f.Scratch));
            }
        }

        [Theory]
        [InlineData("extra-instance")]
        [InlineData("partial")]
        [InlineData("null-return")]
        [InlineData("missing-object")]
        [InlineData("protected")]
        [InlineData("unknown")]
        [InlineData("wrong-number")]
        [InlineData("wrong-language")]
        [InlineData("wrong-group")]
        [InlineData("post-import-mutation")]
        [InlineData("post-compile-mutation")]
        [InlineData("import-exception")]
        public void ActualObjectReadbackRejectsFalseImportSuccess(string scenario)
        {
            using (var f = new Fixture())
            {
                f.Session.ImportHook = (unit, imported) =>
                {
                    if (scenario == "import-exception") throw new IOException("partially created then threw");
                    if (scenario == "null-return") return null;
                    if (scenario == "partial") imported.State = "PartialSuccess";
                    if (scenario == "missing-object") imported.Objects = new ProjectGenerationObject[0];
                    if (scenario == "extra-instance") f.Session.Objects.Add(new ProjectGenerationObject
                        { Kind = "DB", Name = "AutoInstance", Number = 3, Language = "DB", InstanceOfName = "Controller" });
                    var o = imported.Objects.FirstOrDefault();
                    if (o != null)
                    {
                        if (scenario == "protected") o.Protected = true;
                        if (scenario == "unknown") o.Kind = "Unknown";
                        if (scenario == "wrong-number") o.Number = 8;
                        if (scenario == "wrong-language") o.Language = "LAD";
                        if (scenario == "wrong-group") o.Group = "Elsewhere";
                    }
                    if (scenario == "post-import-mutation") f.Session.Objects.Clear();
                    return imported;
                };
                var r = f.Build(compile: s =>
                {
                    if (scenario == "post-compile-mutation") f.Session.Objects.Clear();
                    return Fixture.CleanCompile();
                });
                Assert.False(r.Published); Assert.NotEmpty(r.Failures);
                Assert.False(Directory.Exists(f.Output)); Assert.Empty(Directory.GetDirectories(f.Scratch));
            }
        }

        [Fact]
        public void ChangedSourceFailsBeforeTiaAndLeavesInputUntouched()
        {
            using (var f = new Fixture())
            {
                File.WriteAllText(Path.Combine(f.Source, "main.scl"), "modified since A validation");
                var r = f.Build();
                Assert.False(r.Published); Assert.False(f.FactoryCalled);
                Assert.Contains(r.Failures, m => m.Contains("Source changed"));
                Assert.Equal("modified since A validation", File.ReadAllText(Path.Combine(f.Source, "main.scl")));
            }
        }

        [Fact]
        public void MissingCompilerDoesNotCreateStageOrTia()
        {
            using (var f = new Fixture())
            {
                var r = ProjectGenerationBuilder.Build(f.Plan, f.Output, f.Scratch, (p, plan) =>
                    throw new Exception("must not be called"), null);
                Assert.False(r.Published); Assert.Empty(Directory.GetDirectories(f.Scratch));
            }
        }

        [Fact]
        public void SdCopiesOnlyDeclaredDclAndResource()
        {
            using (var f = new Fixture())
            {
                var a = f.Add("ob", "Main", "OB", 1, "main.s7dcl");
                a.Format = "simatic-sd"; a.Object.Language = "LAD";
                File.WriteAllText(Path.Combine(f.Source, "main.s7res"), "language resources");
                File.WriteAllText(Path.Combine(f.Source, "undeclared.s7res"), "not imported");
                a.ResourcePath = "main.s7res"; a.ResourceSha256 = Fixture.Hash(Path.Combine(f.Source, a.ResourcePath));
                f.Plan.Artifacts = new[] { a };
                f.Session.SourceHook = p =>
                {
                    Assert.NotEqual(Path.Combine(f.Source, a.SourcePath), p);
                    Assert.Equal(new[] { "main.s7dcl", "main.s7res" }, Directory.GetFiles(Path.GetDirectoryName(p))
                        .Select(Path.GetFileName).OrderBy(n => n));
                };
                var r = f.Build(); Assert.True(r.Published, string.Join(";", r.Failures));
                Assert.True(File.Exists(Path.Combine(f.Source, "undeclared.s7res")));
            }
        }

        [Theory]
        [InlineData("existing-output")]
        [InlineData("output-appears-during-compile")]
        [InlineData("source-as-output")]
        [InlineData("scratch-in-source")]
        [InlineData("wrong-cpu")]
        [InlineData("unknown-fresh-object")]
        [InlineData("wrong-main")]
        [InlineData("dispose-fails")]
        [InlineData("project-path-escape")]
        public void SafetyGatesPreserveUnknownResources(string scenario)
        {
            using (var f = new Fixture())
            {
                var output = scenario == "source-as-output" ? f.Source : f.Output;
                var scratch = scenario == "scratch-in-source" ? f.Source : f.Scratch;
                if (scenario == "existing-output") { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "sentinel"), "owned by someone else"); }
                if (scenario == "wrong-cpu") f.Session.Cpu = "fallback-cpu";
                if (scenario == "unknown-fresh-object") f.Session.Objects.Add(new ProjectGenerationObject { Kind = "FC", Name = "Unknown", Number = 1, Language = "SCL" });
                if (scenario == "wrong-main") f.Session.Objects[0].Number = 9;
                if (scenario == "dispose-fails") f.Session.ThrowOnDispose = true;
                if (scenario == "project-path-escape") f.Session.OverrideProjectFile = Path.Combine(f.Source, "original.ap21");
                var r = f.Build(output, scratch, s =>
                {
                    if (scenario == "output-appears-during-compile")
                    { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "sentinel"), "racing owner"); }
                    return Fixture.CleanCompile();
                });
                Assert.False(r.Published); Assert.NotEmpty(r.Failures);
                if (scenario == "existing-output" || scenario == "output-appears-during-compile")
                    Assert.True(File.Exists(Path.Combine(output, "sentinel")));
                Assert.True(File.Exists(Path.Combine(f.Source, "main.scl")));
            }
        }

        [Fact]
        public void ReparseSourceAndOutputParentAreRejectedWithoutDeletingTarget()
        {
            using (var f = new Fixture())
            {
                var outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside);
                File.WriteAllText(Path.Combine(outside, "main.scl"), "fixture bytes");
                var link = Path.Combine(f.Source, "link");
                CreateJunction(link, outside);
                try
                {
                    f.Plan.Artifacts[0].SourcePath = "link/main.scl";
                    Assert.False(f.Build().Published); Assert.False(f.FactoryCalled);
                    f.Plan.Artifacts[0].SourcePath = "main.scl";
                    Assert.False(f.Build(Path.Combine(link, "output")).Published); Assert.False(f.FactoryCalled);
                    Assert.True(File.Exists(Path.Combine(outside, "main.scl")));
                }
                finally { if (Directory.Exists(link)) Directory.Delete(link); }
            }
        }

        [Fact]
        public void CleanupRefusesInjectedReparseChildAndReportsRemainingOwnedStage()
        {
            using (var f = new Fixture())
            {
                string link = null;
                var outside = Path.Combine(f.Root, "outside"); Directory.CreateDirectory(outside);
                File.WriteAllText(Path.Combine(outside, "sentinel"), "never delete");
                f.Session.ImportHook = (unit, result) =>
                {
                    link = Path.Combine(f.Session.Stage, "injected");
                    CreateJunction(link, outside);
                    throw new IOException("fail with redirected cleanup");
                };
                try
                {
                    var r = f.Build();
                    Assert.False(r.Published); Assert.NotEmpty(r.CleanupFailures);
                    Assert.True(File.Exists(Path.Combine(outside, "sentinel")));
                    Assert.True(Directory.Exists(f.Session.Stage));
                }
                finally { if (link != null && Directory.Exists(link)) Directory.Delete(link); }
            }
        }

        private static void CreateJunction(string link, string target)
        {
            // Both paths are freshly allocated test-owned directories. Junctions need no
            // symlink privilege or system settings changes. Cleanup removes only the link.
            var start = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                process.WaitForExit(); Assert.True(process.ExitCode == 0, output);
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly string Root = Path.Combine(Path.GetTempPath(), "tg45-" + Guid.NewGuid().ToString("N"));
            internal string Source => Path.Combine(Root, "source");
            internal string Scratch => Path.Combine(Root, "scratch");
            internal string Output => Path.Combine(Root, "output");
            internal readonly ProjectGenerationBuilderPlan Plan;
            internal readonly FakeSession Session = new FakeSession();
            internal bool FactoryCalled;
            internal Fixture()
            {
                Directory.CreateDirectory(Source); Directory.CreateDirectory(Scratch);
                Plan = new ProjectGenerationBuilderPlan { ContractRevision = "TEST-A", SourceRoot = Source,
                    ProjectName = "Demo", PlcName = "PLC_1", CpuTypeIdentifier = RoundTripProfile.CpuTypeIdentifier };
                Plan.Artifacts = new[] { Add("ob", "Main", "OB", 1, "main.scl") };
            }
            internal ProjectGenerationEngineArtifact Add(string id, string name, string kind, int? number, string file, params string[] dependsOn)
            {
                var path = Path.Combine(Source, file);
                File.WriteAllText(path, "fixture bytes");
                return new ProjectGenerationEngineArtifact { Id = id, SourcePath = file, Format = "scl", Sha256 = Hash(path),
                    DependsOn = dependsOn, Object = new ProjectGenerationObject { Name = name, Kind = kind,
                        Number = number, Language = kind == "UDT" ? null : kind == "DB" ? "DB" : "SCL" } };
            }
            internal ProjectGenerationBuildResult Build(string output = null, string scratch = null,
                Func<IProjectGenerationSession, ProjectGenerationCompileEvidence> compile = null,
                Func<IProjectGenerationSession, ProjectGenerationVerificationResult> verify = null) =>
                ProjectGenerationBuilder.Build(Plan, output ?? Output, scratch ?? Scratch, (stage, plan) =>
                {
                    FactoryCalled = true; Session.Stage = stage;
                    Session.File = Path.Combine(stage, plan.ProjectName, plan.ProjectName + ".ap21");
                    Directory.CreateDirectory(Path.GetDirectoryName(Session.File)); File.WriteAllText(Session.File, "fake project, no V21 evidence");
                    return Session;
                }, compile ?? (s => CleanCompile()), verify);
            internal static ProjectGenerationCompileEvidence CleanCompile() => new ProjectGenerationCompileEvidence
            { ProviderRevision = "TEST-C", Executed = true, State = "Success", ErrorCount = 0, WarningCount = 0,
                DiagnosticsComplete = true, FullResult = "fake diagnostics, no V21 evidence" };
            internal static string Hash(string path)
            { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
            public void Dispose() { if (Directory.Exists(Root)) FileSystemSafety.DeleteOwnedTree(Root); }
        }
        private sealed class FakeSession : IProjectGenerationSession
        {
            internal string Stage, File, OverrideProjectFile;
            internal string Cpu = RoundTripProfile.CpuTypeIdentifier;
            internal bool Disposed, ThrowOnDispose;
            internal readonly List<ProjectGenerationObject> Objects = new List<ProjectGenerationObject>
                { new ProjectGenerationObject { Kind = "OB", Name = "Main", Number = 1, Language = "LAD" } };
            internal readonly List<string> Calls = new List<string>();
            internal Func<ProjectGenerationImportUnit, ProjectGenerationImportResult, ProjectGenerationImportResult> ImportHook;
            internal Action<string> SourceHook;
            public object NativePlc => null;
            public string ProjectFile => OverrideProjectFile ?? File;
            public string CpuTypeIdentifier => Cpu;
            public IReadOnlyList<ProjectGenerationObject> ReadObjects() => Objects.Select(Copy).ToArray();
            public void DeleteFreshMain() => Objects.Clear();
            public void CreateTagTables(IReadOnlyList<ProjectGenerationEngineTagTable> tables) { }
            public ProjectGenerationImportResult Import(ProjectGenerationImportUnit unit, string source)
            {
                Calls.Add(unit.SourcePath); SourceHook?.Invoke(source);
                var generated = unit.Expected.Select(Copy).ToArray(); Objects.AddRange(generated.Select(Copy));
                var r = new ProjectGenerationImportResult { State = "Success", Objects = generated };
                return ImportHook == null ? r : ImportHook(unit, r);
            }
            private static ProjectGenerationObject Copy(ProjectGenerationObject o) => new ProjectGenerationObject
                { Kind = o.Kind, Name = o.Name, Number = o.Number, Language = o.Language, Group = o.Group,
                    InstanceOfName = o.InstanceOfName, Protected = o.Protected };
            public void Save() { }
            public void Dispose() { Disposed = true; if (ThrowOnDispose) throw new IOException("owned session failed to close"); }
        }
    }
}
