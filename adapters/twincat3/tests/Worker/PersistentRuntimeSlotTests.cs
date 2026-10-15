using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using TwinCat3.Runtime;
using Xunit;

namespace TwinCat3.WorkerTests;

public sealed class PersistentRuntimeSlotTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), "tg-instance-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(workspace, "instance");
    private string Scratch => Path.Combine(workspace, "scratch");
    private string Executable => Path.Combine(workspace, "fixture.exe");
    private const string NetId = "127.0.0.1.110.20";
    public PersistentRuntimeSlotTests()
    {
        Directory.CreateDirectory(Scratch); File.WriteAllText(Executable, "fixture, never executed");
    }
    public void Dispose() => Directory.Delete(workspace, true);
    private PersistentRuntimeSlot Slot() => new(Root, workspace, Scratch, Executable);
    private void Configuration(string? expiry = null)
    {
        Directory.CreateDirectory(Path.Combine(Root, "3.1"));
        File.WriteAllText(Path.Combine(Root, "3.1", "TcRegistry.xml"),
            "<TcRegistry><Key Name='System'><Value Name='AmsNetId'>7F0000016E14</Value></Key></TcRegistry>");
        if (expiry is not null)
        {
            Directory.CreateDirectory(Path.Combine(Root, "3.1", "Target", "License"));
            File.WriteAllText(Path.Combine(Root, "3.1", "Target", "License", "fixture.tclrs"), "<License><ExpireTime>" + expiry + "</ExpireTime></License>");
        }
    }
    private void Managed(string? expiry = null)
    {
        var slot = Slot(); using var lease = slot.Acquire(slot.Inspect().Fingerprint);
        Configuration(expiry); lease.MarkActive(NetId); lease.CompleteStopped();
    }

    [Fact] public void PreviewNeverAllocatesOrLocksANewInstance()
    {
        var observed = Slot().Inspect(); Assert.Equal("new", observed.State);
        Assert.False(Directory.Exists(Root)); Assert.False(File.Exists(Root + ".lease"));
    }
    [Fact] public void TwoCleanLeasesRetainInstanceIdentityAndLicenseBytes()
    {
        Managed("2099-01-01T00:00:00Z"); var slot = Slot(); var first = slot.Inspect();
        var license = File.ReadAllBytes(Path.Combine(Root, "3.1", "Target", "License", "fixture.tclrs"));
        using (var lease = slot.Acquire(first.Fingerprint)) { lease.MarkActive(NetId); lease.CompleteStopped(); }
        var second = Slot().Inspect(); Assert.Equal(first.InstanceId, second.InstanceId); Assert.Equal(first.NetId, second.NetId);
        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => slot.Acquire(first.Fingerprint));
        Assert.Equal(license, File.ReadAllBytes(Path.Combine(Root, "3.1", "Target", "License", "fixture.tclrs")));
        Assert.Equal("file-expiry-future", second.License.Advisory); Assert.False(second.License.ValidatedByRuntime);
    }
    [Fact] public void ConcurrentWorkerCannotAcquireTheSameSlot()
    {
        Managed(); var slot = Slot(); var snapshot = slot.Inspect(); using var lease = slot.Acquire(snapshot.Fingerprint);
        Assert.Throws<IOException>(() => Slot().Acquire(snapshot.Fingerprint));
    }
    [Fact] public void CrashedOrUnqualifiedShutdownIsNotReusable()
    {
        Managed(); var slot = Slot();
        using (var lease = slot.Acquire(slot.Inspect().Fingerprint)) lease.MarkActive(NetId);
        Assert.Throws<InvalidDataException>(() => Slot().Inspect());
    }
    [Theory] [InlineData("configuration")] [InlineData("executable")] [InlineData("license")]
    public void ChangedResourcesInvalidateTheReviewedInstance(string target)
    {
        Managed("2099-01-01T00:00:00Z"); var slot = Slot(); var before = slot.Inspect();
        var path = target == "executable" ? Executable : target == "license"
            ? Path.Combine(Root, "3.1", "Target", "License", "fixture.tclrs") : Path.Combine(Root, "3.1", "changed.txt");
        File.AppendAllText(path, "changed"); Assert.Throws<InvalidDataException>(() => slot.Acquire(before.Fingerprint));
    }
    [Theory] [InlineData("2001-01-01T00:00:00Z", "file-expiry-past")] [InlineData("unknown", "unknown")]
    public void FileExpiryIsAdvisoryAndNeverProvesRuntimeValidity(string expiry, string expected)
    {
        Managed(expiry); var observed = Slot().Inspect().License;
        Assert.Equal(expected, observed.Advisory); Assert.False(observed.ValidatedByRuntime);
    }
    [Fact] public void MaliciousLicenseXmlCannotResolveExternalEntities()
    {
        var slot = Slot(); using (var lease = slot.Acquire(slot.Inspect().Fingerprint))
        {
            Configuration("x");
            File.WriteAllText(Path.Combine(Root, "3.1", "Target", "License", "fixture.tclrs"),
                "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///not-a-resource'>]><License><ExpireTime>&e;</ExpireTime></License>");
            lease.MarkActive(NetId); lease.CompleteStopped();
        }
        Assert.Equal("unknown", slot.Inspect().License.Advisory);
    }
    [Fact] public void OutsideWorkspaceAndScratchOverlapAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new PersistentRuntimeSlot(Path.GetTempPath(), workspace, Scratch, Executable));
        Assert.Throws<ArgumentException>(() => new PersistentRuntimeSlot(Scratch, workspace, Scratch, Executable));
    }
    [Fact] public void MigrationRejectsLivePriorOwnerAndDoesNotCopyLicenses()
    {
        Configuration("2099-01-01T00:00:00Z"); using var process = Process.GetCurrentProcess();
        Receipt(process.Id, process.StartTime);
        Assert.Throws<InvalidOperationException>(() => Slot().AdoptStoppedOwned());
        Assert.False(File.Exists(Path.Combine(Root, "tia-guard-instance.json")));
        Receipt(int.MaxValue, DateTime.Now);
        Slot().AdoptStoppedOwned(); Assert.Equal("stopped", Slot().Inspect().State);
        Assert.Single(Directory.GetFiles(Path.Combine(Root, "3.1", "Target", "License")));
    }
    private void Receipt(int pid, DateTime started) => File.WriteAllText(Path.Combine(Root, "ownership.json"), JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0", NetId, ProcessId = pid, executable = Executable,
        ConfigurationRoot = Path.Combine(Root, "3.1"),
        executableSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Executable))), startedAt = new DateTimeOffset(started)
    }));
}
