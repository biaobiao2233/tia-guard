using TiaGuard.Bridge.Host;
using Xunit;

public sealed class SimulationProcessEnvironmentTests
{
    [Fact]
    public void Mixed_architecture_child_does_not_inherit_bridge_runtime_overrides()
    {
        var parent = new Dictionary<string, string?>
        {
            ["DOTNET_ROOT"] = @"C:\bridge-x64-runtime",
            ["DOTNET_ROOT_X64"] = @"C:\bridge-x64-runtime",
            ["DOTNET_ROOT_X86"] = @"C:\stale-x86-runtime",
            ["DOTNET_ROOT(x86)"] = @"C:\stale-x86-runtime",
            ["TEMP"] = @"E:\task-temp",
            ["NUGET_PACKAGES"] = @"E:\task-cache"
        };
        var child = new Dictionary<string, string?>(parent);
        SimulationProcessEnvironment.UseInstalledRuntimes(child);
        Assert.Equal(@"C:\bridge-x64-runtime", parent["DOTNET_ROOT"]);
        Assert.Equal(@"E:\task-temp", child["TEMP"]);
        Assert.Equal(@"E:\task-cache", child["NUGET_PACKAGES"]);
        Assert.DoesNotContain(child.Keys, k => k.StartsWith("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase));
    }
}
