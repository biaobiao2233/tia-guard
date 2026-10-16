namespace TiaGuard.Bridge.Host;

public static class SimulationProcessEnvironment
{
    // Standard S7-1200 starts an x86 .NET apphost, even from an x64 Bridge.
    // A Bridge-specific DOTNET_ROOT can make that apphost load an x64 hostfxr.
    // Use the installed application's registered runtimes; change only its child
    // environment, leaving the Bridge and the user's environment untouched.
    public static void UseInstalledRuntimes(IDictionary<string, string?> childEnvironment)
    {
        foreach (var key in childEnvironment.Keys.Where(k =>
            k.Equals("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("DOTNET_ROOT(x86)", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("DOTNET_ROOT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            childEnvironment.Remove(key);
    }
}
