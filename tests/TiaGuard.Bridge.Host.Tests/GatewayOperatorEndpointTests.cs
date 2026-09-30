using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TiaGuard.Bridge.Host;
using Xunit;

namespace TiaGuard.Bridge.Host.Tests;

public sealed class GatewayOperatorEndpointTests
{
    [Theory]
    [InlineData(null, "allow")]
    [InlineData(null, "reject")]
    [InlineData(null, "shutdown")]
    [InlineData("", "allow")]
    [InlineData("", "reject")]
    [InlineData("", "shutdown")]
    [InlineData(" ", "allow")]
    [InlineData(" ", "reject")]
    [InlineData(" ", "shutdown")]
    public async Task No_operator_key_fails_closed(string? expectedKey, string operation)
    {
        await using var host = await OperatorHost.StartAsync(expectedKey);
        var approval = host.Register("token");
        // A supplied arbitrary header must not create authority when no key exists.
        using var response = await host.PostAsync(operation, approval.Id, "client-invented-key");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(host.Approvals.Pending());
        Assert.False(host.Shutdown.Task.IsCompleted);
    }

    [Theory]
    [InlineData("allow")]
    [InlineData("reject")]
    [InlineData("shutdown")]
    public async Task Wrong_operator_key_is_forbidden(string operation)
    {
        await using var host = await OperatorHost.StartAsync("gui-key");
        var approval = host.Register("token");
        using var response = await host.PostAsync(operation, approval.Id, "wrong-key");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(host.Approvals.Pending());
        Assert.False(host.Shutdown.Task.IsCompleted);
    }

    [Theory]
    [InlineData("allow")]
    [InlineData("reject")]
    [InlineData("shutdown")]
    public async Task Missing_header_is_forbidden_even_with_a_configured_key(string operation)
    {
        await using var host = await OperatorHost.StartAsync("gui-key");
        using var response = await host.PostAsync(operation, host.Register("token").Id);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(host.Shutdown.Task.IsCompleted);
    }

    [Fact]
    public async Task Correct_key_approves_only_the_matching_preview_once()
    {
        await using var host = await OperatorHost.StartAsync("gui-key");
        var first = host.Register("first-token");
        var second = host.Register("second-token");
        using var response = await host.PostAsync("allow", first.Id, "gui-key");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("recorded", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        await host.Approvals.WaitAsync("first-token", CancellationToken.None);
        Assert.Equal(second.Id, Assert.Single(host.Approvals.Pending()).Id);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            host.Approvals.WaitAsync("second-token", cancellation.Token));
        using var duplicate = await host.PostAsync("allow", first.Id, "gui-key");
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("APPROVAL_USED", await duplicate.Content.ReadAsStringAsync());
        var replay = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Approvals.WaitAsync("first-token", CancellationToken.None));
        Assert.Equal("APPROVAL_USED", replay.Message);
    }

    [Fact]
    public async Task Correct_key_rejects_the_preview()
    {
        await using var host = await OperatorHost.StartAsync("gui-key");
        using var response = await host.PostAsync("reject", host.Register("token").Id, "gui-key");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.Approvals.WaitAsync("token", CancellationToken.None));
        Assert.Equal("APPROVAL_REJECTED", error.Message);
    }

    [Fact]
    public async Task Correct_key_does_not_approve_unknown_or_expired_preview()
    {
        await using var host = await OperatorHost.StartAsync("gui-key");
        using var unknown = await host.PostAsync("allow", "unknown", "gui-key");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("APPROVAL_UNKNOWN", await unknown.Content.ReadAsStringAsync());
        var expired = host.Register("expired", DateTimeOffset.UtcNow.AddSeconds(-1));
        using var response = await host.PostAsync("allow", expired.Id, "gui-key");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("APPROVAL_EXPIRED", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Correct_key_can_shutdown()
    {
        await using var host = await OperatorHost.StartAsync("gui-key");
        using var response = await host.PostAsync("shutdown", "", "gui-key");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("stopping", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        await host.Shutdown.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Headless_write_skips_gui_but_requires_a_valid_single_use_preview_token()
    {
        var gateway = new PatchGateway();
        var approvals = new GatewayApprovalService(required: false);
        var coordinator = new BridgePatchCoordinator(
            gateway, new BridgeWriteSafetyService(), new BridgeAiContextService(gateway), approvals);
        var preview = await coordinator.PreviewAsync(PatchGateway.Patch);
        using var json = JsonDocument.Parse(preview);
        Assert.False(json.RootElement.TryGetProperty("approvalId", out _));
        Assert.Empty(approvals.Pending());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ApplyAsync(PatchGateway.Patch, "invented-token"));
        Assert.Equal(0, gateway.Applies);
        var token = json.RootElement.GetProperty("safetyToken").GetString()!;
        await coordinator.ApplyAsync(PatchGateway.Patch, token);
        Assert.Equal(1, gateway.Applies);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ApplyAsync(PatchGateway.Patch, token));
        Assert.Equal(1, gateway.Applies);
    }

    private sealed class OperatorHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        public GatewayApprovalService Approvals { get; } = new(required: true);
        public TaskCompletionSource<bool> Shutdown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private OperatorHost(WebApplication app)
        {
            _app = app;
            Client = new HttpClient();
        }

        public static async Task<OperatorHost> StartAsync(string? key)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var app = builder.Build();
            var host = new OperatorHost(app);
            app.Use(async (context, next) =>
            {
                try { await next(); }
                catch (InvalidOperationException error)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new { error = error.Message });
                }
            });
            // Exercise the exact production mapper over a real loopback HTTP listener.
            GatewayOperatorEndpoints.Map(app, host.Approvals, key, () =>
            {
                host.Shutdown.TrySetResult(true);
                return Task.CompletedTask;
            });
            await app.StartAsync();
            host.Client.BaseAddress = new Uri(app.Urls.Single());
            return host;
        }

        public GatewayApproval Register(string token, DateTimeOffset? expiresAt = null) =>
            Approvals.Register(token, "Modify a disposable copy", expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(1),
                "binding", "request", "state")!;

        public Task<HttpResponseMessage> PostAsync(string operation, string id, string? key = null)
        {
            var route = operation == "shutdown"
                ? "/api/v1/gateway/shutdown"
                : "/api/v1/gateway/approvals/" + id + "/" + operation;
            var request = new HttpRequestMessage(HttpMethod.Post, route);
            if (key != null) request.Headers.Add("X-TiaGuard-Approval-Key", key);
            return Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    private sealed class PatchGateway : IBridgeEngineeringGateway
    {
        public const string Patch = """
            {"schemaVersion":"tia-guard.engineering-patch/v1","operation":"upsert_tag","target":{"table":"tags","tag":"Probe"},"dataType":"Bool","logicalAddress":"%M1.0"}
            """;
        public int Generation => 1;
        public int Applies { get; private set; }
        public Task<string> IdentityAsync(CancellationToken cancellationToken) => PreviewPatchAsync(Patch, cancellationToken);
        public Task<string> QueryAsync(string kind, string? block, int? network, string? symbol, bool force, CancellationToken cancellationToken)
            => PreviewPatchAsync(Patch, cancellationToken);
        public Task<string> PreviewPatchAsync(string patchJson, CancellationToken cancellationToken) =>
            Task.FromResult(JsonSerializer.Serialize(new
            {
                bindingIdentity = "owned-offline-copy",
                contentId = "sha256:stable",
                epoch = 0,
                result = new { fingerprint = "sha256:preview", canonicalPatch = patchJson }
            }));
        public Task<string> ApplyPatchAsync(string patchJson, string expectedContentId, string expectedFingerprint,
            int expectedEpoch, string? injectFailure, CancellationToken cancellationToken)
        {
            Applies++;
            return Task.FromResult("""{"status":"applied"}""");
        }
    }
}
