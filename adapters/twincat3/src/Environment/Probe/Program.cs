using System.Text.Json;
using TwinCat3.Contracts;
using TwinCat3.EnvironmentHost;

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
var environment = new WindowsEngineeringEnvironment(Option("--interop"));
try
{
    var report = await environment.InspectAsync();
    object result = report;
    if (args.Contains("--probe-dte") || args.Contains("--probe-standalone"))
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Functional COM probes require Windows.");
        var workspace = Option("--workspace") ?? throw new ArgumentException("An allocated --workspace is required for a functional probe.");
        var selection = new EnvironmentSelection { Provider = args.Contains("--probe-standalone") ? "standalone" : "dte", RequestedBuild = Option("--build") };
        await using var session = await environment.CreateOwnedSessionAsync(selection, workspace);
        var observation = await session.InvokeAsync(context => new
        {
            session.SessionId,
            staThreadId = System.Environment.CurrentManagedThreadId,
            apartment = Thread.CurrentThread.GetApartmentState().ToString(),
            hasDte = context.Dte is not null,
            hasSystemManager = context.SystemManager is not null,
            dteName = context.Dte is null ? null : (string)((dynamic)context.Dte).Name
        });
        result = new { environment = report, functionalProbe = observation, generationQualified = false };
    }
    var json = JsonSerializer.Serialize(result, ContractJson.Options);
    if (Option("--report") is { } output) await File.WriteAllTextAsync(output, json);
    Console.WriteLine(json);
    return 0;
}
catch (Exception ex)
{
    var json = JsonSerializer.Serialize(new { success = false, phase = "environment", error = ex.GetType().Name, message = ex.Message }, ContractJson.Options);
    if (Option("--report") is { } output) await File.WriteAllTextAsync(output, json);
    Console.WriteLine(json);
    return 5;
}
