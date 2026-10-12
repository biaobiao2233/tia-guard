using TiaGuard.TwinCat.Validation;
using Xunit;

namespace TiaGuard.TwinCat.Validation.Tests;

public class CompilationTests
{
    private static CapturedDiagnostic Diagnostic(DiagnosticSeverity severity = DiagnosticSeverity.Error,
        DiagnosticCorrelation correlation = DiagnosticCorrelation.CurrentBuild) => new(severity, "Missing symbol", "PLC",
            "MAIN.TcPOU", 3, 7, null, correlation);

    private static BuildObservation Valid(params CapturedDiagnostic[] diagnostics) => new()
    {
        BuildInvoked = true, BuildStarted = true, BuildCompleted = true,
        IntendedSolution = true, IntendedConfiguration = true, IntendedPlatform = true,
        FreshDiagnosticBaseline = true, FailedProjectCount = 0,
        ErrorList = new(diagnostics.Length, diagnostics.Length, diagnostics, Array.Empty<string>()),
        BuildOutputComplete = true, BuildOutput = "Build started; build completed."
    };

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(15)]
    public void InclusiveComCollectionCapturesEveryIndex(int count)
    {
        var visited = new List<int>();
        var collected = OneBasedErrorCollector.Read(() => count, i => { visited.Add(i); return Diagnostic(); });
        Assert.Equal(Enumerable.Range(1, count), visited);
        Assert.Equal(count, collected.Items.Count);
        Assert.True(collected.Complete);
    }

    [Fact] public void SingleErrorDoesNotPass() => Assert.Equal(CompilationVerdict.Error, CompilationGate.Evaluate(Valid(Diagnostic())).Verdict);
    [Fact] public void LastErrorDoesNotPass() => Assert.Equal(CompilationVerdict.Error, CompilationGate.Evaluate(Valid(Diagnostic(DiagnosticSeverity.Warning), Diagnostic())).Verdict);
    [Fact] public void CompleteZeroErrorsPass() => Assert.True(CompilationGate.Evaluate(Valid()).Success);
    [Fact] public void WarningCountRemainsDistinct()
    {
        var result = CompilationGate.Evaluate(Valid(Diagnostic(DiagnosticSeverity.Warning), Diagnostic(DiagnosticSeverity.Information)));
        Assert.True(result.Success); Assert.Equal(1, result.WarningCount); Assert.Equal(0, result.ErrorCount);
    }

    [Theory]
    [InlineData("invoked")] [InlineData("start")] [InlineData("done")] [InlineData("timeout")]
    [InlineData("solution")] [InlineData("configuration")] [InlineData("platform")]
    [InlineData("baseline")] [InlineData("lastbuildinfo")] [InlineData("output")]
    [InlineData("emptyoutput")] [InlineData("errorlist")] [InlineData("collector")]
    public void MissingRequiredEvidenceCannotPass(string fault)
    {
        var baseline = Valid();
        var invalid = fault switch
        {
            "invoked" => baseline with { BuildInvoked = false },
            "start" => baseline with { BuildStarted = false },
            "done" => baseline with { BuildCompleted = false },
            "timeout" => baseline with { TimedOut = true },
            "solution" => baseline with { IntendedSolution = false },
            "configuration" => baseline with { IntendedConfiguration = false },
            "platform" => baseline with { IntendedPlatform = false },
            "baseline" => baseline with { FreshDiagnosticBaseline = false },
            "lastbuildinfo" => baseline with { FailedProjectCount = null },
            "output" => baseline with { BuildOutputComplete = false },
            "emptyoutput" => baseline with { BuildOutput = "" },
            "errorlist" => baseline with { ErrorList = null },
            _ => baseline with { CollectorProblems = new[] { "capture failed" } }
        };
        var result = CompilationGate.Evaluate(invalid);
        Assert.False(result.Success); Assert.False(result.DiagnosticsComplete); Assert.Equal(CompilationVerdict.Unknown, result.Verdict);
    }

    [Fact] public void LastBuildInfoFailureCannotPass()
    {
        var result = CompilationGate.Evaluate(Valid() with { FailedProjectCount = 1 });
        Assert.Equal(CompilationVerdict.Error, result.Verdict); Assert.False(result.Success);
    }
    [Theory] [InlineData(DiagnosticCorrelation.Stale)] [InlineData(DiagnosticCorrelation.Foreign)] [InlineData(DiagnosticCorrelation.Unknown)]
    public void UncorrelatedErrorCannotBecomeComplete(DiagnosticCorrelation correlation)
        => Assert.False(CompilationGate.Evaluate(Valid(Diagnostic(DiagnosticSeverity.Warning, correlation))).DiagnosticsComplete);
    [Fact] public void UnknownSeverityPreventsPass() => Assert.False(CompilationGate.Evaluate(Valid(Diagnostic(DiagnosticSeverity.Unknown))).Success);
    [Fact] public void TruncationIsExplicit()
    {
        var captured = OneBasedErrorCollector.Read(() => 2, _ => Diagnostic(DiagnosticSeverity.Warning), 1);
        Assert.False(captured.Complete); Assert.Single(captured.Items); Assert.Contains(captured.Problems, x => x.Contains("limit"));
    }
    [Fact] public void ItemFailureRetainsFollowingLastError()
    {
        var captured = OneBasedErrorCollector.Read(() => 3, i => i == 2 ? throw new InvalidOperationException("broken") : Diagnostic());
        Assert.False(captured.Complete); Assert.Equal(2, captured.Items.Count); Assert.Contains(captured.Problems, x => x.Contains("Diagnostic 2"));
    }
    [Fact] public void CountFailureCannotBecomeEmptySuccess()
    {
        var captured = OneBasedErrorCollector.Read(() => throw new InvalidOperationException("no collection"), _ => Diagnostic());
        Assert.False(captured.Complete); Assert.Null(captured.DeclaredCount);
    }
    [Fact] public void CountChangingDuringReadIsIncomplete()
    {
        var calls = 0;
        var captured = OneBasedErrorCollector.Read(() => ++calls, _ => Diagnostic());
        Assert.False(captured.Complete); Assert.Contains(captured.Problems, x => x.Contains("changed"));
    }
    [Fact] public void UnknownLocationIsNotInvented()
    {
        var captured = OneBasedErrorCollector.Read(() => 1, _ => Diagnostic() with { File = null, Line = null, Column = null });
        Assert.True(captured.Complete); Assert.Null(captured.Items[0].Line);
    }
    [Theory] [InlineData("MAIN(3,7): error C0001: missing symbol")][InlineData("MAIN(3,7): warning C0001: truncation")]
    public void CompilerOutputDiagnosticMissingFromErrorListCannotPass(string output)
        => Assert.False(CompilationGate.Evaluate(Valid() with { BuildOutput = output }).Success);
    [Fact] public void SummaryCountsAreNotMisreadAsCompilerDiagnostics()
        => Assert.True(CompilationGate.Evaluate(Valid() with { BuildOutput = "Build: 1 succeeded, 0 failed. 0 errors, 0 warnings" }).Success);
    [Fact] public void LastDistinctOutputDiagnosticCannotBeDropped()
    {
        var observation = Valid(Diagnostic(DiagnosticSeverity.Warning)) with { BuildOutput = "warning C0001: first\nwarning C0002: last" };
        Assert.False(CompilationGate.Evaluate(observation).Success);
    }
}
