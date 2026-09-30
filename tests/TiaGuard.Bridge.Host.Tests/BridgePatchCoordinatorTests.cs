using TiaGuard.Bridge.Host;
using Xunit;

namespace TiaGuard.Bridge.Host.Tests;

public sealed class BridgePatchCoordinatorTests
{
    [Fact]
    public async Task Replay_stale_content_and_request_mismatch_are_rejected()
    {
        var gateway = new FakePatchGateway();
        var service = new BridgeAiContextService(gateway);
        var coordinator = new BridgePatchCoordinator(
            gateway, new BridgeWriteSafetyService(), service);
        var preview = await coordinator.PreviewAsync(gateway.Patch);

        var token = Token(preview);
        var applied = await coordinator.ApplyAsync(gateway.Patch, token);
        Assert.Contains("\"status\":\"applied\"", applied);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ApplyAsync(gateway.Patch, token));

        var second = await coordinator.PreviewAsync(gateway.Patch);
        gateway.ContentId = "sha256:changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ApplyAsync(gateway.Patch, Token(second)));

        gateway.ContentId = "sha256:stable";
        var third = await coordinator.PreviewAsync(gateway.Patch);
        gateway.Patch = gateway.Patch.Replace("ForwardOut", "ReverseOut");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ApplyAsync(gateway.Patch, Token(third)));
    }

    private static string Token(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty("safetyToken").GetString()
            ?? throw new InvalidOperationException("missing token");
    }

    private sealed class FakePatchGateway : IBridgeEngineeringGateway
    {
        public string ContentId { get; set; } = "sha256:stable";
        public string Patch { get; set; } = """
            {"schemaVersion":"tia-guard.engineering-patch/v1","operation":"replace_output_condition","output":"ForwardOut"}
            """;
        public int Generation => 2;
        public int Applies { get; private set; }

        public Task<string> IdentityAsync(CancellationToken cancellationToken)
            => Task.FromResult(PreviewJson());

        public Task<string> QueryAsync(
            string kind, string? block, int? network, string? symbol, bool force,
            CancellationToken cancellationToken)
            => Task.FromResult(PreviewJson());

        public Task<string> PreviewPatchAsync(string patchJson, CancellationToken cancellationToken)
        {
            Patch = patchJson;
            return Task.FromResult(PreviewJson());
        }

        public Task<string> ApplyPatchAsync(
            string patchJson, string expectedContentId, string expectedFingerprint, int expectedEpoch,
            string? injectFailure, CancellationToken cancellationToken)
        {
            Applies++;
            Assert.Equal(ContentId, expectedContentId);
            Assert.Equal(0, expectedEpoch);
            return Task.FromResult("""{"status":"applied"}""");
        }

        private string PreviewJson()
        {
            var canonical = Patch.Trim();
            var fingerprint = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            return "{" +
                "\"bindingIdentity\":\"demo|C:/demo.ap21|offline-copy\"," +
                "\"contentId\":\"" + ContentId + "\"," +
                "\"epoch\":0," +
                "\"result\":{" +
                    "\"canonicalPatch\":" + System.Text.Json.JsonSerializer.Serialize(canonical) + "," +
                    "\"fingerprint\":\"" + fingerprint + "\"" +
                "}}";
        }
    }
}
