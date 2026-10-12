using TiaGuard.TwinCat.Validation;
using Xunit;

namespace TiaGuard.TwinCat.Validation.Tests;

public class ComparisonTests
{
    private static ProjectFacts Expected() => new("NewSolution", "Config", "PLC", "3.1.4026.0", "TwinCAT RT (x64)",
        new[] { new SourceObject("MAIN", "Program", "PROGRAM MAIN\nVAR\nx:INT;\nEND_VAR", "x := 42;"),
            new SourceObject("ST_Status", "Struct", "TYPE ST_Status: STRUCT ready: BOOL; END_STRUCT END_TYPE", "") },
        new[] { new TaskFact("PlcTask", 10000, 20, "PlcTask", new[] { "MAIN", "OtherProgram" }) },
        new[] { new LibraryFact("Tc2_Standard", "Beckhoff Automation GmbH", "3.3.3.0", "3.3.3.0") });
    private static ReadbackObservation Actual(ProjectFacts? facts = null) => new(facts ?? Expected(), true, true, true, true, true, true, Array.Empty<string>(), Array.Empty<string>());

    [Fact] public void ExactReadbackPasses() => Assert.True(IndependentComparison.Compare(Expected(), Actual()).Success);
    [Theory] [InlineData("literal")] [InlineData("type")] [InlineData("kind")] [InlineData("cycle")]
    [InlineData("priority")] [InlineData("link")] [InlineData("callorder")] [InlineData("library")] [InlineData("tool")]
    [InlineData("platform")] [InlineData("missingobject")] [InlineData("extraobject")] [InlineData("extralibrary")]
    public void SemanticDriftFails(string mutation)
    {
        var baseline = Expected();
        var actual = mutation switch
        {
            "literal" => baseline with { Objects = new[] { baseline.Objects[0] with { Implementation = "x:=43;" }, baseline.Objects[1] } },
            "type" => baseline with { Objects = new[] { baseline.Objects[0], baseline.Objects[1] with { Declaration = "TYPE ST_Status: STRUCT ready: INT; END_STRUCT END_TYPE" } } },
            "kind" => baseline with { Objects = new[] { baseline.Objects[0] with { Kind = "Function" }, baseline.Objects[1] } },
            "cycle" => baseline with { Tasks = new[] { baseline.Tasks[0] with { CycleTimeUs = 20000 } } },
            "priority" => baseline with { Tasks = new[] { baseline.Tasks[0] with { Priority = 21 } } },
            "link" => baseline with { Tasks = new[] { baseline.Tasks[0] with { LinkedSystemTask = "OtherTask" } } },
            "callorder" => baseline with { Tasks = new[] { baseline.Tasks[0] with { Calls = new[] { "OtherProgram", "MAIN" } } } },
            "library" => baseline with { Libraries = new[] { baseline.Libraries[0] with { ResolvedVersion = "9.0.0.0" } } },
            "tool" => baseline with { ToolBuild = "3.1.4024.0" },
            "platform" => baseline with { Platform = "TwinCAT RT (x86)" },
            "missingobject" => baseline with { Objects = new[] { baseline.Objects[0] } },
            "extraobject" => baseline with { Objects = baseline.Objects.Append(new("Foreign", "Program", "PROGRAM Foreign", "")).ToArray() },
            _ => baseline with { Libraries = baseline.Libraries.Append(new("Extra", "Foreign", "1", "1")).ToArray() }
        };
        Assert.Equal(VerificationVerdict.Fail, IndependentComparison.Compare(baseline, Actual(actual)).Verdict);
    }
    [Theory] [InlineData("save")] [InlineData("close")] [InlineData("reopen")] [InlineData("objects")]
    [InlineData("tasks")] [InlineData("libraries")] [InlineData("opaque")] [InlineData("capture")]
    public void MissingCaptureCannotPass(string fault)
    {
        var baseline = Actual();
        var actual = fault switch
        {
            "save" => baseline with { Saved = false }, "close" => baseline with { Closed = false },
            "reopen" => baseline with { Reopened = false }, "objects" => baseline with { ObjectsComplete = false },
            "tasks" => baseline with { TasksComplete = false }, "libraries" => baseline with { LibrariesComplete = false },
            "opaque" => baseline with { Unsupported = new[] { "UnknownMember" } },
            _ => baseline with { CaptureProblems = new[] { "IOException" } }
        };
        Assert.Equal(VerificationVerdict.Unknown, IndependentComparison.Compare(Expected(), actual).Verdict);
    }
    [Fact] public void DuplicateInventoryIsUnknown()
    {
        var baseline = Expected();
        Assert.Equal(VerificationVerdict.Unknown, IndependentComparison.Compare(baseline,
            Actual(baseline with { Objects = baseline.Objects.Append(baseline.Objects[0]).ToArray() })).Verdict);
    }
    [Fact] public void LayoutAndCommentsAreTheOnlyNormalizations()
    {
        var baseline = Expected();
        var actual = baseline with { Objects = new[] { baseline.Objects[0] with { Implementation = "//comment\nX:= (* comment (* nested *) *)42 ;" }, baseline.Objects[1] } };
        Assert.True(IndependentComparison.Compare(baseline, Actual(actual)).Success);
    }
    [Theory]
    [InlineData("s:='(* x *)';", "s:='(* y *)';")]
    [InlineData("s:='a//b';", "s:='a//c';")]
    [InlineData("s:='upper';", "s:='UPPER';")]
    [InlineData("x:=16#FF;", "x:=16#FE;")]
    [InlineData("{attribute 'hide'} x:=1;", "{attribute 'visible'} x:=1;")]
    [InlineData("s:='a$'b';", "s:='a$'c';")]
    public void LiteralsAndPragmasAreSignificant(string left, string right) => Assert.NotEqual(StTokens.Canonicalize(left), StTokens.Canonicalize(right));
    [Theory] [InlineData("(* broken")] [InlineData("s:='broken")] [InlineData("s:=\"broken$")]
    public void BrokenSourceProjectionIsUnknown(string broken)
    {
        var baseline = Expected();
        var actual = baseline with { Objects = new[] { baseline.Objects[0] with { Implementation = broken }, baseline.Objects[1] } };
        Assert.Equal(VerificationVerdict.Unknown, IndependentComparison.Compare(baseline, Actual(actual)).Verdict);
    }
}
