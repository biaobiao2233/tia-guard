using TiaGuard.TwinCat.Validation;
using Xunit;

namespace TiaGuard.TwinCat.Validation.Tests;

public class BuildOutputDiagnosticsTests
{
    [Fact] public void FreshOwnedOutputAddsActualCodeAndLocationWithoutDuplicatingMessage()
    {
        var root = Path.Combine(Path.GetTempPath(), "owned-native");
        var file = Path.Combine(root, "POUs", "MAIN.TcPOU");
        var original = new[] { new CapturedDiagnostic(DiagnosticSeverity.Error, "Identifier 'x' not defined", "PLC", null, null, null, null, DiagnosticCorrelation.Unknown) };
        var result = BuildOutputDiagnostics.Supplement(original, file + "(13) : error: C0046: Identifier 'x' not defined", root, "PLC");
        var item = Assert.Single(result);
        Assert.Equal("C0046", item.Code); Assert.Equal(13, item.Line); Assert.Equal(file, item.File);
        Assert.Equal("DTE.BuildOutput", item.Source);
    }
    [Fact] public void ForeignAndOpaqueOutputNeverSuppliesInventedLocation()
    {
        var result = BuildOutputDiagnostics.Supplement(Array.Empty<CapturedDiagnostic>(),
            Path.Combine(Path.GetTempPath(), "foreign", "MAIN.TcPOU") + "(13) : error: C0046: failure\nerror C0046: no source location",
            Path.Combine(Path.GetTempPath(), "owned"), "PLC");
        Assert.Empty(result);
    }
}
