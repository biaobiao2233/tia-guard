using System.Text.Json;
using TiaGuard.Bridge.Host;
using Xunit;

namespace TiaGuard.Bridge.Host.Tests;

public sealed class GatewayProductTests
{
    [Fact]
    public void Capabilities_describe_the_skill_boundary()
    {
        using var document = JsonDocument.Parse(GatewayCatalog.CapabilitiesJson(false));
        var root = document.RootElement;
        Assert.Equal("TIA-Guard AI Gateway", root.GetProperty("product").GetString());
        Assert.Equal("1", root.GetProperty("gatewaySchemaVersion").GetString());
        Assert.Equal("127.0.0.1", root.GetProperty("bind").GetString());
        Assert.Contains("network", root.GetProperty("read").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("one-shot-gui-approval", root.GetProperty("write").GetProperty("authorization").GetString());
        Assert.Contains("upsert_tag", root.GetProperty("write").GetProperty("operations").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("or", root.GetProperty("unsupported").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("download", root.GetProperty("unsupported").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("authoritative", root.GetProperty("authority").GetProperty("tiaSource").GetString());
        Assert.Equal("derived", root.GetProperty("authority").GetProperty("aiEngineering").GetString());
        Assert.Equal("mutation-intent", root.GetProperty("authority").GetProperty("patch").GetString());
        var safety = root.GetProperty("safety");
        Assert.False(safety.GetProperty("originalProjectWrite").GetBoolean());
        Assert.True(safety.GetProperty("offlineDisposableOnly").GetBoolean());
        Assert.True(safety.GetProperty("previewRequired").GetBoolean());
        Assert.True(safety.GetProperty("singleUseToken").GetBoolean());
        Assert.True(safety.GetProperty("compileRequired").GetBoolean());
        Assert.True(safety.GetProperty("roundTripVerifyRequired").GetBoolean());
        Assert.True(safety.GetProperty("semanticPostconditionRequired").GetBoolean());
        Assert.True(safety.GetProperty("localhostOnly").GetBoolean());
        Assert.Equal("single-use-preview-token",
            JsonDocument.Parse(GatewayCatalog.CapabilitiesJson(true)).RootElement
                .GetProperty("write").GetProperty("authorization").GetString());
    }

    [Fact]
    public void OpenApi_covers_the_formal_http_surface()
    {
        using var document = JsonDocument.Parse(GatewayCatalog.OpenApiJson());
        var root = document.RootElement;
        Assert.Equal("3.0.3", root.GetProperty("openapi").GetString());
        var paths = root.GetProperty("paths");
        foreach (var path in new[]
                 {
                     "/health", "/capabilities", "/openapi.json", "/api/v1/gateway/status",
                     "/api/v1/ai/context", "/api/v1/ai/network", "/api/v1/ai/patches/preview",
                     "/api/v1/ai/patches/apply"
                 })
            Assert.True(paths.TryGetProperty(path, out _), path);
        Assert.False(paths.TryGetProperty("/api/v1/gateway/approvals/{id}/allow", out _));
        var patch = root.GetProperty("components").GetProperty("schemas").GetProperty("EngineeringPatch");
        Assert.Contains("upsert_tag", patch.GetProperty("properties").GetProperty("operation").GetProperty("enum").EnumerateArray().Select(item => item.GetString()));
        Assert.True(root.GetProperty("components").GetProperty("schemas").TryGetProperty("ErrorBody", out _));
    }

    [Fact]
    public void Gateway_listener_stays_on_localhost()
    {
        var program = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TiaGuard.Bridge.Host", "Program.cs"));
        Assert.Contains("http://127.0.0.1:", program);
        Assert.DoesNotContain("0.0.0.0", program);
        Assert.Contains("UseUrls", program);
    }

    [Fact]
    public void Ai_activity_is_recent_and_ignores_the_status_poll()
    {
        var now = DateTimeOffset.Parse("2026-09-30T03:00:00Z");
        var tracker = new GatewayActivityTracker(TimeSpan.FromSeconds(20), () => now);
        Assert.Equal("waiting", tracker.Read().State);
        using (tracker.Begin("/api/v1/gateway/status", "gui"))
            Assert.Equal(0, tracker.Read().ActiveRequests);
        using (tracker.Begin("/api/v1/ai/network", "Cursor"))
        {
            var active = tracker.Read();
            Assert.Equal("working", active.State);
            Assert.Equal(1, active.ActiveRequests);
            Assert.Equal("read", active.Category);
            Assert.Equal("Cursor", active.Client);
        }
        Assert.Equal("connected", tracker.Read().State);
        now = now.AddSeconds(21);
        Assert.Equal("waiting", tracker.Read().State);
        Assert.NotNull(tracker.Read().LastSeenUtc);
    }

    [Fact]
    public async Task One_shot_approval_is_required_single_use_and_state_bound()
    {
        var gateway = new RecordingGateway();
        var approvals = new GatewayApprovalService(required: true);
        var coordinator = new BridgePatchCoordinator(
            gateway, new BridgeWriteSafetyService(TimeSpan.FromMinutes(5)), new BridgeAiContextService(gateway), approvals);
        var preview = await coordinator.PreviewAsync(gateway.Patch);
        using var previewJson = JsonDocument.Parse(preview);
        var token = previewJson.RootElement.GetProperty("safetyToken").GetString()!;
        var approvalId = previewJson.RootElement.GetProperty("approvalId").GetString()!;
        Assert.Contains("修改", previewJson.RootElement.GetProperty("approvalSummary").GetString());
        Assert.NotEmpty(approvals.Pending());

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApplyAsync(gateway.Patch, "missing-token"));
        Assert.Equal(0, gateway.Applies);

        approvals.Reject(approvalId);
        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApplyAsync(gateway.Patch, token));
        Assert.Contains("APPROVAL_REJECTED", rejected.Message);
        Assert.Equal(0, gateway.Applies);

        var second = await coordinator.PreviewAsync(gateway.Patch);
        using var secondJson = JsonDocument.Parse(second);
        var secondToken = secondJson.RootElement.GetProperty("safetyToken").GetString()!;
        var secondId = secondJson.RootElement.GetProperty("approvalId").GetString()!;
        approvals.Allow(secondId);
        var used = Assert.Throws<InvalidOperationException>(() => approvals.Allow(secondId));
        Assert.Contains("APPROVAL_USED", used.Message);
        gateway.ContentId = "sha256:changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApplyAsync(gateway.Patch, secondToken));
        Assert.Equal(0, gateway.Applies);

        gateway.ContentId = "sha256:stable";
        var third = await coordinator.PreviewAsync(gateway.Patch);
        using var thirdJson = JsonDocument.Parse(third);
        var thirdToken = thirdJson.RootElement.GetProperty("safetyToken").GetString()!;
        approvals.Allow(thirdJson.RootElement.GetProperty("approvalId").GetString()!);
        var applied = await coordinator.ApplyAsync(gateway.Patch, thirdToken);
        Assert.Contains("\"status\":\"applied\"", applied);
        Assert.Equal(1, gateway.Applies);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ApplyAsync(gateway.Patch, thirdToken));
        Assert.Equal(1, gateway.Applies);
    }

    [Fact]
    public async Task Expired_approval_is_rejected()
    {
        var approvals = new GatewayApprovalService(required: true);
        var created = approvals.Register("token", "修改标签", DateTimeOffset.UtcNow.AddMilliseconds(-1), "b", "r", "s");
        Assert.NotNull(created);
        var error = Assert.Throws<InvalidOperationException>(() => approvals.Allow(created!.Id));
        Assert.Contains("APPROVAL_EXPIRED", error.Message);
        var wait = await Assert.ThrowsAsync<InvalidOperationException>(() => approvals.WaitAsync("token", CancellationToken.None));
        Assert.Contains("APPROVAL_EXPIRED", wait.Message);
    }

    [Fact]
    public void Gui_starts_the_gateway_and_confirms_one_change()
    {
        var gui = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TiaGuard.Gui", "MainWindow.Bridge.cs"));
        var csproj = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TiaGuard.Gui", "TiaGuard.Gui.csproj"));
        Assert.Contains("AI GATEWAY", gui);
        Assert.Contains("允许本次修改", gui);
        Assert.Contains("拒绝", gui);
        Assert.Contains("ProbeHealth", gui);
        Assert.Contains("taskkill.exe", gui);
        Assert.Contains("/api/v1/gateway/shutdown", gui);
        Assert.Contains("TIA_GUARD_APPROVAL_KEY", gui);
        Assert.DoesNotContain("Copy MCP config", gui);
        Assert.DoesNotContain("启动 Bridge", gui);
        Assert.Contains("ApplicationIcon", csproj);
        Assert.True(File.Exists(Path.Combine(FindRepoRoot(), "src", "TiaGuard.Gui", "Assets", "TiaGuard.ico")));
        var programIndex = gui.IndexOf("ProbeHealth()", StringComparison.Ordinal);
        var startIndex = gui.IndexOf("Process.Start(start)", StringComparison.Ordinal);
        Assert.True(programIndex >= 0 && programIndex < startIndex);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "TiaGuard.Bridge.Host", "Program.cs")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("repo root");
    }

    private sealed class RecordingGateway : IBridgeEngineeringGateway
    {
        public string ContentId { get; set; } = "sha256:stable";
        public string Patch { get; set; } = """
            {"schemaVersion":"tia-guard.engineering-patch/v1","operation":"replace_output_condition","target":{"block":"OB1","network":1},"output":"ForwardOut"}
            """;
        public int Generation => 2;
        public int Applies { get; private set; }

        public Task<string> IdentityAsync(CancellationToken cancellationToken) => Task.FromResult(PreviewJson());
        public Task<string> QueryAsync(string kind, string? block, int? network, string? symbol, bool force, CancellationToken cancellationToken)
            => Task.FromResult(PreviewJson());
        public Task<string> PreviewPatchAsync(string patchJson, CancellationToken cancellationToken)
        {
            Patch = patchJson;
            return Task.FromResult(PreviewJson());
        }
        public Task<string> ApplyPatchAsync(string patchJson, string expectedContentId, string expectedFingerprint, int expectedEpoch, string? injectFailure, CancellationToken cancellationToken)
        {
            Applies++;
            return Task.FromResult("""{"status":"applied"}""");
        }
        private string PreviewJson()
        {
            var canonical = Patch.Trim();
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            return "{\"bindingIdentity\":\"demo|C:/demo.ap21|offline-copy\",\"contentId\":\"" + ContentId +
                "\",\"epoch\":0,\"result\":{\"canonicalPatch\":" + JsonSerializer.Serialize(canonical) +
                ",\"fingerprint\":\"" + fingerprint + "\"}}";
        }
    }
}
