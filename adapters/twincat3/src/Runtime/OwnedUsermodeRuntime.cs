using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using TwinCAT.Ads;
using TwinCat3.Contracts;
using TwinCat3.Engineering;

namespace TwinCat3.Runtime;

// Own only the process created here. No attach, shared/default instance, route
// installation, global registry write or license generation is available.
public sealed class OwnedUsermodeRuntime : IAsyncDisposable
{
    private readonly Process process;
    private readonly DateTime started;
    private readonly Task stdout;
    private readonly Task stderr;
    private readonly TaskCompletionSource<string> announcedNetId = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;
    public string SimulationId { get; } = Guid.NewGuid().ToString("N");
    public string NetId { get; }
    public string ConfigurationRoot { get; }
    public string InstanceRoot { get; }
    public string ExecutablePath { get; }
    public int ProcessId => process.Id;
    private OwnedUsermodeRuntime(Process process, string netId, string root, string executable)
    {
        this.process = process; NetId = netId; InstanceRoot = root; ExecutablePath = executable;
        ConfigurationRoot = Path.Combine(root, "3.1"); started = process.StartTime;
        stdout = DrainAsync(process.StandardOutput, Path.Combine(root, "stdout.log"), line =>
        {
            if (line.StartsWith("AmsNetId: ", StringComparison.Ordinal)) announcedNetId.TrySetResult(line[10..].Trim());
        });
        stderr = DrainAsync(process.StandardError, Path.Combine(root, "stderr.log"));
    }
    public static bool Installed(string? installation) => installation is not null &&
        File.Exists(Path.Combine(installation, "Runtimes", "bin", "TcSystemServiceUm.exe")) &&
        File.Exists(Path.Combine(installation, "Runtimes", "UmRT_Template", "3.1", "TcRegistry.xml"));

    public static async Task<OwnedUsermodeRuntime> StartAsync(string installation, string root, string scratch,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("Windows x64 is required.");
        if (!Installed(installation)) throw new InvalidOperationException("Official Usermode Runtime is not installed.");
        EngineeringPaths.RequireNewOutput(root, scratch);
        var executable = Path.Combine(installation, "Runtimes", "bin", "TcSystemServiceUm.exe");
        var template = Path.Combine(installation, "Runtimes", "UmRT_Template");
        EngineeringPaths.CheckTree(template);
        var netId = "127.0.0.1." + RandomNumberGenerator.GetInt32(64, 255) + "." + RandomNumberGenerator.GetInt32(0, 256);
        SimulationPolicy.RequireLoopbackAllocation(netId);
        Directory.CreateDirectory(root);
        foreach (var file in Directory.EnumerateFiles(template, "*", SearchOption.AllDirectories))
        {
            var destination = EngineeringPaths.RequireUnder(Path.Combine(root, Path.GetRelativePath(template, file)), root);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination, false);
        }
        var registryPath = Path.Combine(root, "3.1", "TcRegistry.xml");
        var registry = ReadRegistry(registryPath);
        var system = registry.SelectSingleNode("//Key[@Name='TwinCAT3']/Key[@Name='System']") ?? throw new InvalidDataException("Template system registry is absent.");
        Set("HeapMemSizeMB", "DW", "128");
        Set("AmsNetId", "BIN", string.Concat(netId.Split('.').Select(p => byte.Parse(p).ToString("X2"))));
        registry.Save(registryPath);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        // Keep the instance name within the native router's short-name range.
        foreach (var arg in new[] { "-t", "bin", "-n", "TiaGuard" + Guid.NewGuid().ToString("N")[..12], "-i", netId, "-c", Path.Combine(root, "3.1") }) start.ArgumentList.Add(arg);
        var child = Process.Start(start) ?? throw new InvalidOperationException("Owned Runtime did not start.");
        var owned = new OwnedUsermodeRuntime(child, netId, root, executable);
        try
        {
            // The official Runtime changes its announced AMS identity on collision.
            // Require the address from our own child's output before opening ADS,
            // so an existing Runtime can never be mistaken for the allocation.
            var announced = await owned.announcedNetId.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            if (announced != netId) throw new InvalidOperationException("Owned Runtime announced a different AMS identity; allocation collided.");
            await File.WriteAllTextAsync(Path.Combine(root, "ownership.json"), JsonSerializer.Serialize(new
                { schemaVersion = "1.0", owned.SimulationId, owned.NetId, owned.ProcessId, owned.ConfigurationRoot, executable,
                    executableSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))), startedAt = owned.started }), cancellationToken);
            var timer = Stopwatch.StartNew();
            string lastAdsError = "No reply";
            while (timer.Elapsed < TimeSpan.FromSeconds(20))
            {
                cancellationToken.ThrowIfCancellationRequested(); owned.RequireLive();
                try { using var ads = owned.Connect(10000); _ = ads.ReadState(); return owned; }
                catch (AdsErrorException ex) { lastAdsError = ex.ErrorCode + ": " + ex.Message; await Task.Delay(100, cancellationToken); }
            }
            throw new TimeoutException("Owned Runtime ADS service did not become available. Last ADS error: " + lastAdsError);
        }
        catch { await owned.DisposeAsync(); throw; }
        void Set(string name, string type, string value)
        {
            var item = system.SelectSingleNode("Value[@Name='" + name + "']") ?? registry.CreateElement("Value");
            if (item.ParentNode is null) system.AppendChild(item);
            ((XmlElement)item).SetAttribute("Name", name); ((XmlElement)item).SetAttribute("Type", type); item.InnerText = value;
        }
    }
    public void RequireLive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        process.Refresh();
        if (process.HasExited || process.StartTime != started) throw new InvalidOperationException("Owned Runtime process is no longer alive.");
        SimulationPolicy.RequireLoopbackAllocation(NetId);
        var registry = ReadRegistry(Path.Combine(ConfigurationRoot, "TcRegistry.xml"));
        var actual = registry.SelectSingleNode("//Key[@Name='System']/Value[@Name='AmsNetId']")?.InnerText;
        if (actual != string.Concat(NetId.Split('.').Select(p => byte.Parse(p).ToString("X2"))))
            throw new InvalidOperationException("Owned Runtime configuration identity changed.");
    }
    public AdsClient Connect(int port)
    {
        RequireLive(); if (port is not (851 or 10000)) throw new InvalidDataException("Unsupported simulation port.");
        var client = new AdsClient { Timeout = 1000 }; client.Connect(NetId, port); return client;
    }
    public object ReadSymbol(FrozenGenerationInput input, string symbol, string type)
    {
        SimulationPolicy.RequireSymbol(input, symbol, false);
        using var ads = Connect(851); ValidateSymbol(ads, symbol, type, false);
        return ads.ReadValue(symbol, SimulationPolicy.ScalarType(type)) ?? throw new InvalidDataException("No scalar feedback.");
    }
    public object WriteSymbol(FrozenGenerationInput input, string symbol, string type, JsonElement value)
    {
        SimulationPolicy.RequireSymbol(input, symbol, true);
        using var ads = Connect(851); ValidateSymbol(ads, symbol, type, true);
        var requested = SimulationPolicy.ScalarValue(type, value); ads.WriteValue(symbol, requested);
        return ads.ReadValue(symbol, SimulationPolicy.ScalarType(type)) ?? throw new InvalidDataException("No scalar readback.");
    }
    private static void ValidateSymbol(AdsClient ads, string name, string type, bool writing)
    {
        var symbol = ads.ReadSymbol(name); _ = SimulationPolicy.ScalarType(type);
        if (!symbol.IsPrimitiveType || symbol.IsPointer || symbol.IsReference || symbol.IsProperty ||
            !symbol.TypeName.Equals(type, StringComparison.OrdinalIgnoreCase) || (writing && symbol.IsReadOnly))
            throw new InvalidDataException("Actual PLC symbol type/access differs from the requested scalar.");
    }
    public object Status()
    {
        using var system = Connect(10000); using var plc = Connect(851);
        return new { SimulationId, NetId, ProcessId, SystemState = system.ReadState().AdsState.ToString(), PlcState = plc.ReadState().AdsState.ToString() };
    }
    public async Task SetPlcStateAsync(string state, CancellationToken cancellationToken)
    {
        if (state is not ("Run" or "Stop")) throw new InvalidDataException("Only Run/Stop are allowed.");
        using var ads = Connect(851); ads.WriteControl(new StateInfo(state == "Run" ? AdsState.Run : AdsState.Stop, 0));
        await WaitStateAsync(851, state == "Run" ? AdsState.Run : AdsState.Stop, cancellationToken);
    }
    public async Task EnterConfigAsync(CancellationToken cancellationToken)
    {
        using (var ads = Connect(10000)) ads.WriteControl(new StateInfo(AdsState.Reconfig, 0));
        await WaitStateAsync(10000, AdsState.Config, cancellationToken);
    }
    public async Task EnterRunAsync(CancellationToken cancellationToken)
    {
        using (var ads = Connect(10000)) ads.WriteControl(new StateInfo(AdsState.Reset, 0));
        await WaitStateAsync(10000, AdsState.Run, cancellationToken);
        await WaitStateAsync(851, AdsState.Run, cancellationToken);
    }
    private async Task WaitStateAsync(int port, AdsState wanted, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(20))
        {
            cancellationToken.ThrowIfCancellationRequested(); RequireLive();
            try { using var ads = Connect(port); if (ads.ReadState().AdsState == wanted) return; }
            catch (AdsErrorException) { }
            await Task.Delay(100, cancellationToken);
        }
        throw new TimeoutException("Owned Runtime did not reach " + wanted + " on port " + port + "; inspect local Runtime/licensing diagnostics.");
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        try
        {
            if (!process.HasExited)
            {
                try { await process.StandardInput.WriteLineAsync("x"); await process.StandardInput.FlushAsync(); }
                catch (IOException) { } // Closed input must not prevent owned-process cleanup.
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException) { process.Kill(entireProcessTree: false); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            }
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { if (process.HasExited) { disposed = true; process.Dispose(); } }
    }
    private static XmlDocument ReadRegistry(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings
            { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20000000 });
        var registry = new XmlDocument { XmlResolver = null }; registry.Load(reader); return registry;
    }
    private static async Task DrainAsync(StreamReader stream, string path, Action<string>? onLine = null)
    {
        using var writer = new StreamWriter(path, false);
        var bytes = 0;
        while (await stream.ReadLineAsync() is { } line)
        {
            onLine?.Invoke(line);
            if (bytes < 4 * 1024 * 1024) { await writer.WriteLineAsync(line); await writer.FlushAsync(); bytes += line.Length * 2; }
        }
    }
}
