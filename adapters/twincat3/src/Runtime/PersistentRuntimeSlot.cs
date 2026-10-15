using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using TwinCat3.Engineering;

namespace TwinCat3.Runtime;

public sealed record RuntimeSlotSnapshot(string Fingerprint, string State, string? InstanceId,
    string? NetId, LicenseFileObservation License);
public sealed record LicenseFileObservation(int Files, DateTimeOffset? EarliestExpiryUtc,
    string Advisory, bool ValidatedByRuntime = false);

/// <summary>A trusted host-selected configuration, never a public attach/PID API.</summary>
public sealed class PersistentRuntimeSlot
{
    private const string RecordName = "tia-guard-instance.json";
    private readonly string executable;
    public string Root { get; }
    private sealed record Checkpoint(string Version, string InstanceId, string NetId,
        string ExecutableHash, string ConfigurationHash, string State, string Revision);

    public PersistentRuntimeSlot(string root, string workspace, string scratch, string executable)
    {
        Root = EngineeringPaths.RequireUnder(root, workspace);
        EngineeringPaths.RequireDistinctTrees(Root, scratch);
        this.executable = Path.GetFullPath(executable);
        if (!File.Exists(this.executable)) throw new FileNotFoundException("Runtime executable is absent.");
        if (!Directory.Exists(Path.GetDirectoryName(Root))) throw new DirectoryNotFoundException("Instance parent must exist.");
    }

    public RuntimeSlotSnapshot Inspect()
    {
        // The lock is outside Root so that inspection never creates the instance.
        EngineeringPaths.RequireUnder(Root, Path.GetDirectoryName(Root)!);
        if (!Directory.Exists(Root))
        {
            if (File.Exists(Root)) throw new IOException("Instance root is a file.");
            return new("new:" + HashFile(executable), "new", null, null, new(0, null, "missing"));
        }
        EngineeringPaths.CheckTree(Root);
        var file = Path.Combine(Root, RecordName);
        if (!File.Exists(file) || new FileInfo(file).Length > 16384) throw new InvalidDataException("Not a managed runtime instance.");
        var bytes = File.ReadAllBytes(file);
        var record = JsonSerializer.Deserialize<Checkpoint>(bytes) ?? throw new InvalidDataException("Invalid instance checkpoint.");
        if (record.Version != "1" || !Guid.TryParseExact(record.InstanceId, "N", out _) ||
            !Guid.TryParseExact(record.Revision, "N", out _) || record.State != "stopped")
            throw new InvalidDataException("Instance is busy or its prior shutdown is unqualified.");
        SimulationPolicy.RequireLoopbackAllocation(record.NetId);
        RequireRegistry(record.NetId);
        if (record.ExecutableHash != HashFile(executable) || record.ConfigurationHash != ConfigurationHash())
            throw new InvalidDataException("Runtime executable or stopped configuration changed.");
        return new(Convert.ToHexString(SHA256.HashData(bytes)), "stopped", record.InstanceId, record.NetId, ObserveLicenses());
    }

    public Lease Acquire(string expectedFingerprint)
    {
        EngineeringPaths.RequireUnder(Root + ".lease", Path.GetDirectoryName(Root)!);
        var file = new FileStream(Root + ".lease", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var snapshot = Inspect();
            if (snapshot.Fingerprint != expectedFingerprint) throw new InvalidOperationException("Instance changed after preview.");
            return new Lease(this, file, snapshot);
        }
        catch { file.Dispose(); throw; }
    }

    // One-time trusted host migration of an older adapter-created, stopped
    // instance. Not exposed through MCP; no licence/configuration is copied.
    public void AdoptStoppedOwned()
    {
        EngineeringPaths.RequireUnder(Root + ".lease", Path.GetDirectoryName(Root)!);
        using var file = new FileStream(Root + ".lease", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        EngineeringPaths.CheckTree(Root);
        if (File.Exists(Path.Combine(Root, RecordName))) throw new InvalidOperationException("Instance is already managed.");
        var receiptPath = Path.Combine(Root, "ownership.json");
        if (new FileInfo(receiptPath).Length > 16384) throw new InvalidDataException("Ownership receipt too large.");
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(receiptPath));
        var r = receipt.RootElement;
        var netId = r.GetProperty("NetId").GetString()!;
        if (r.GetProperty("schemaVersion").GetString() != "1.0" ||
            !Path.GetFullPath(r.GetProperty("executable").GetString()!).Equals(executable, StringComparison.OrdinalIgnoreCase) ||
            r.GetProperty("executableSha256").GetString() != HashFile(executable) ||
            !Path.GetFullPath(r.GetProperty("ConfigurationRoot").GetString()!).Equals(Path.Combine(Root, "3.1"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prior owned runtime identity differs.");
        SimulationPolicy.RequireLoopbackAllocation(netId); RequireRegistry(netId);
        try
        {
            using var prior = Process.GetProcessById(r.GetProperty("ProcessId").GetInt32());
            if (!prior.HasExited && prior.StartTime.ToUniversalTime() == r.GetProperty("startedAt").GetDateTimeOffset().UtcDateTime)
                throw new InvalidOperationException("Prior owned runtime is still alive.");
        }
        catch (ArgumentException) { /* Original process no longer exists. */ }
        Write(new("1", Guid.NewGuid().ToString("N"), netId, HashFile(executable), ConfigurationHash(), "stopped", Guid.NewGuid().ToString("N")));
    }

    private void RequireRegistry(string netId)
    {
        using var reader = XmlReader.Create(Path.Combine(Root, "3.1", "TcRegistry.xml"), XmlSettings());
        var xml = new XmlDocument { XmlResolver = null }; xml.Load(reader);
        if (xml.SelectSingleNode("//Key[@Name='System']/Value[@Name='AmsNetId']")?.InnerText !=
            string.Concat(netId.Split('.').Select(p => byte.Parse(p).ToString("X2"))))
            throw new InvalidDataException("Instance registry has a different AMS identity.");
    }

    private LicenseFileObservation ObserveLicenses()
    {
        var folder = Path.Combine(Root, "3.1", "Target", "License");
        if (!Directory.Exists(folder)) return new(0, null, "missing");
        var files = Directory.GetFiles(folder, "*.tclrs");
        var expiries = new List<DateTimeOffset>();
        bool unknown = false;
        foreach (var file in files)
        {
            try
            {
                using var reader = XmlReader.Create(file, XmlSettings());
                var xml = new XmlDocument { XmlResolver = null }; xml.Load(reader);
                var text = xml.SelectSingleNode("//ExpireTime")?.InnerText;
                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var expiry)) expiries.Add(expiry.ToUniversalTime());
                else unknown = true;
            }
            catch (XmlException) { unknown = true; }
        }
        DateTimeOffset? earliest = expiries.Count == 0 ? null : expiries.Min();
        return new(files.Length, earliest, files.Length == 0 ? "missing" : unknown ? "unknown" :
            earliest <= DateTimeOffset.UtcNow ? "file-expiry-past" : "file-expiry-future");
        // File dates are advisory only: no signature/feature/platform validity is inferred.
    }
    private static XmlReaderSettings XmlSettings() => new()
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 };

    private string ConfigurationHash()
    {
        var folder = Path.Combine(Root, "3.1"); EngineeringPaths.CheckTree(folder);
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        if (files.Length > 10000 || files.Sum(p => new FileInfo(p).Length) > 192L * 1024 * 1024)
            throw new InvalidDataException("Stopped configuration exceeds the instance limit.");
        return EngineeringPipeline.HashTree(folder);
    }
    private static string HashFile(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private void Write(Checkpoint record)
    {
        var path = Path.Combine(Root, RecordName);
        var temporary = path + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(record));
        File.Move(temporary, path, overwrite: true);
    }

    public sealed class Lease : IDisposable
    {
        private readonly PersistentRuntimeSlot slot;
        private readonly FileStream file;
        private Checkpoint? active;
        public RuntimeSlotSnapshot Snapshot { get; }
        internal Lease(PersistentRuntimeSlot slot, FileStream file, RuntimeSlotSnapshot snapshot)
            { this.slot = slot; this.file = file; Snapshot = snapshot; }
        public void MarkActive(string netId)
        {
            if (Snapshot.NetId is { } prior && prior != netId) throw new InvalidDataException("Stored instance identity changed.");
            slot.RequireRegistry(netId);
            active = new("1", Snapshot.InstanceId ?? Guid.NewGuid().ToString("N"), netId,
                HashFile(slot.executable), "", "active", Guid.NewGuid().ToString("N"));
            slot.Write(active);
        }
        public void CompleteStopped()
        {
            if (active is null) throw new InvalidOperationException("Instance was not started under this lease.");
            slot.RequireRegistry(active.NetId);
            slot.Write(active with { State = "stopped", ConfigurationHash = slot.ConfigurationHash() });
        }
        public void Dispose() => file.Dispose();
    }
}
