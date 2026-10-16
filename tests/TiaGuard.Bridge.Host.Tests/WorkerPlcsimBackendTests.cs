using System.Text.Json;
using TiaGuard.Bridge.Host;
using TiaGuard.Bridge.Worker;
using Xunit;

public sealed class WorkerPlcsimBackendTests
{
    [Fact] public void Worker_observation_timestamp_is_a_wire_string_readable_by_the_net8_proof_reader()
    {
        var now = DateTime.UtcNow;
        var json = JsonSerializer.SerializeToElement(new { observedAtUtc = PlcsimUiIdentity.ObservationTimestamp(now) });
        Assert.Equal(JsonValueKind.String, json.GetProperty("observedAtUtc").ValueKind);
        Assert.Equal(now, json.GetProperty("observedAtUtc").GetDateTime());
        Assert.Throws<ArgumentException>(() => PlcsimUiIdentity.ObservationTimestamp(DateTime.SpecifyKind(now, DateTimeKind.Unspecified)));
    }
    [Fact] public void Legacy_framework_date_encoding_reports_a_contract_error_instead_of_a_generic_format_exception()
    {
        var legacy = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Observation().GetRawText())!;
        legacy["observedAtUtc"] = JsonSerializer.SerializeToElement("/Date(1792137600000)/");
        var error = Assert.Throws<InvalidOperationException>(() => WorkerPlcsimSimulationBackend.ReadProof(JsonSerializer.SerializeToElement(legacy), Instance, Name));
        Assert.Contains("ISO 8601", error.Message);
    }
    private static readonly string Instance = Guid.NewGuid().ToString("D");
    private const string Name = "TiaGuard_native_test";
    private static JsonElement Observation(string? id = null, string? name = null, string address = "192.168.0.1",
        bool? powered = true, int count = 1, int cpus = 1, DateTime? at = null, string? addressOwner = null) => JsonSerializer.SerializeToElement(new {
        success = true, instanceId = id ?? Instance, instanceName = name ?? Name, instanceCount = count, powered,
        virtualCpuProcessIds = Enumerable.Range(1, cpus).ToArray(), observedAtUtc = at ?? DateTime.UtcNow,
        controls = new[] {
            new { name = "S7-1200", ancestorIds = Array.Empty<string>() },
            new { name = "STANDARD ", ancestorIds = Array.Empty<string>() },
            new { name = address, ancestorIds = new[] { "x1-interface-" + (addressOwner ?? Instance) } }
        }
    });
    [Fact] public void Fresh_owned_virtual_cpu_produces_an_engineering_proof()
    {
        var proof = WorkerPlcsimSimulationBackend.ReadProof(Observation(), Instance, Name);
        Assert.Equal(Instance, proof.InstanceId); Assert.True(proof.Powered); Assert.Equal("STANDARD S7-1200", proof.Kind);
    }
    [Fact] public void Correct_address_under_another_instance_is_not_a_target_proof() =>
        Assert.Throws<InvalidOperationException>(() => WorkerPlcsimSimulationBackend.ReadProof(Observation(addressOwner: Guid.NewGuid().ToString()), Instance, Name));
    [Fact] public void Manual_rename_invalidates_the_engineering_proof() =>
        Assert.Throws<InvalidOperationException>(() => WorkerPlcsimSimulationBackend.ReadProof(Observation(name: "User_name"), Instance, Name));
    [Fact] public void A_second_instance_invalidates_the_engineering_proof() =>
        Assert.Throws<InvalidOperationException>(() => WorkerPlcsimSimulationBackend.ReadProof(Observation(count: 2), Instance, Name));
    [Theory] [InlineData(false)] [InlineData(null)]
    public void Unknown_or_off_power_cannot_authorize_a_download(bool? power) =>
        Assert.Throws<InvalidOperationException>(() => WorkerPlcsimSimulationBackend.ReadProof(Observation(powered: power), Instance, Name));
    [Theory] [InlineData(0)] [InlineData(2)]
    public void Missing_or_ambiguous_virtual_cpu_rejects_download(int cpus) =>
        Assert.Throws<InvalidOperationException>(() => WorkerPlcsimSimulationBackend.ReadProof(Observation(cpus: cpus), Instance, Name));
    [Fact] public void Expired_observation_rejects_download() =>
        Assert.Throws<InvalidOperationException>(() => WorkerPlcsimSimulationBackend.ReadProof(Observation(at: DateTime.UtcNow.AddMinutes(-1)), Instance, Name));
    [Fact] public void Future_observation_rejects_download() =>
        Assert.Throws<InvalidOperationException>(() => WorkerPlcsimSimulationBackend.ReadProof(Observation(at: DateTime.UtcNow.AddMinutes(1)), Instance, Name));
    private sealed class BusyGateway : IPlcsimDesktopGateway
    {
        public int Generation => 1;
        public List<string> Calls { get; } = [];
        public Task<JsonElement> CallAsync(string method, object payload, CancellationToken ct)
        {
            Calls.Add(method);
            return Task.FromResult(JsonSerializer.SerializeToElement(new { success = false, error = "Another operation owns the desktop." }));
        }
    }
    [Fact] public async Task A_failed_busy_open_cannot_adopt_or_close_an_existing_desktop()
    {
        var gateway = new BusyGateway(); var backend = new WorkerPlcsimSimulationBackend(new() { Enabled = true }, gateway);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.CreateAsync(Name, default));
        await backend.CloseAsync(default);
        Assert.Equal(new[] { "open_plcsim_desktop" }, gateway.Calls);
    }
}
