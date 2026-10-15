using TiaGuard.TwinCat.Validation;
using Xunit;

namespace TiaGuard.TwinCat.Validation.Tests;

public class GeneratedLibraryTests
{
    private const string Reference = "<PlaceholderReference systemLibrary='true'><LibItemName>#Tc3_GlobalTypes</LibItemName>" +
        "<PlaceholderName>Tc3_GlobalTypes</PlaceholderName><Namespace>Tc3_GlobalTypes</Namespace><EffectiveResolution>" +
        "<LibraryName>Tc3_GlobalTypes</LibraryName><Version>1.0.0.0</Version><Distributor>Beckhoff Automation GmbH</Distributor>" +
        "</EffectiveResolution></PlaceholderReference>";
    private static string Wrap(string reference) => "<TreeItem><PlcLibDef><References>" + reference + "</References></PlcLibDef></TreeItem>";

    [Fact] public void CompilerInjectedResolvedSystemLibraryIsRecognized()
        => Assert.True(GeneratedLibraryProjection.IsSystemGlobalTypes(Wrap(Reference)));
    [Theory]
    [InlineData("systemLibrary='true'", "systemLibrary='false'")]
    [InlineData("1.0.0.0", "*")]
    [InlineData("Beckhoff Automation GmbH", "Other Vendor")]
    [InlineData("<LibraryName>Tc3_GlobalTypes</LibraryName>", "<LibraryName>UserLibrary</LibraryName>")]
    public void UnresolvedOrSubstitutedReferenceIsRejected(string before, string after)
        => Assert.False(GeneratedLibraryProjection.IsSystemGlobalTypes(Wrap(Reference.Replace(before, after))));
    [Fact] public void DuplicateGeneratedReferenceIsRejected()
        => Assert.False(GeneratedLibraryProjection.IsSystemGlobalTypes(Wrap(Reference + Reference)));
}
