using System.Text.Json;
using TiaGuard.Bridge.Host;
using Xunit;

namespace TiaGuard.Bridge.Host.Tests;

public sealed class BridgeAiContextServiceTests
{
    [Fact]
    public async Task Second_read_hits_cache_until_content_or_binding_changes()
    {
        var gateway = new FakeGateway();
        var service = new BridgeAiContextService(gateway);

        var first = await service.GetProjectContextAsync();
        var second = await service.GetProjectContextAsync();
        Assert.Equal(1, gateway.Queries);
        Assert.Contains("\"cacheHit\":true", second);
        Assert.Contains("context-a", first);

        gateway.ContentId = "sha256:bbb";
        gateway.Body = "context-b";
        var third = await service.GetProjectContextAsync();
        Assert.Equal(2, gateway.Queries);
        Assert.Contains("context-b", third);

        service.Invalidate();
        gateway.ContentId = "sha256:ccc";
        gateway.Binding = "project-b|C:/other.ap21|offline-copy";
        gateway.Body = "context-c";
        var rebound = await service.GetNetworkAsync("OB1", 1);
        Assert.Equal(3, gateway.Queries);
        Assert.Contains("context-c", rebound);
        Assert.DoesNotContain("context-a", rebound);
    }

    [Fact]
    public async Task Refresh_bypasses_cache_without_a_write_flag()
    {
        var gateway = new FakeGateway();
        var service = new BridgeAiContextService(gateway);
        await service.GetProjectContextAsync();
        await service.RefreshAsync();
        Assert.Equal(2, gateway.Queries);
        Assert.True(gateway.LastForce);
    }

    [Fact]
    public void Read_only_surface_includes_ai_context_and_write_surface_adds_patch()
    {
        var root = FindRepoRoot();
        var tools = File.ReadAllText(Path.Combine(root, "src", "TiaGuard.Bridge.Host", "BridgeAiTools.cs"));
        var program = File.ReadAllText(Path.Combine(root, "src", "TiaGuard.Bridge.Host", "Program.cs"));
        foreach (var name in new[]
                 {
                     "get_ai_project_context", "get_program_graph", "get_network",
                     "where_used", "refresh_ai_context"
                 })
        {
            Assert.Contains("Name = \"" + name + "\"", tools);
            Assert.Contains("ReadOnly = true", tools);
        }
        Assert.Contains("WithTools<BridgeAiTools>()", program);
        Assert.Contains("if (options.AllowWrite)", program);
        Assert.DoesNotContain("WithTools<BridgePatchTools>()", program);
        Assert.Contains("/api/v1/ai/context", program);
        Assert.Contains("/api/v1/ai/program-graph", program);
        Assert.Contains("/api/v1/ai/network", program);
        Assert.Contains("/api/v1/ai/where-used", program);
        Assert.Contains("/api/v1/ai/refresh", program);
        Assert.DoesNotContain("/api/v1/ai/patches/preview", program);
        Assert.Contains("GetProjectContextAsync", program);
        Assert.DoesNotContain("download", tools, StringComparison.OrdinalIgnoreCase);
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
        throw new DirectoryNotFoundException("Repository root was not found from the test output.");
    }

    private sealed class FakeGateway : IBridgeEngineeringGateway
    {
        public string ContentId { get; set; } = "sha256:aaa";
        public string Binding { get; set; } = "project-a|C:/demo.ap21|offline-copy";
        public string Body { get; set; } = "context-a";
        public int Queries { get; private set; }
        public bool LastForce { get; private set; }
        public int Generation => 1;

        public Task<string> IdentityAsync(CancellationToken cancellationToken)
            => Task.FromResult(Envelope(false));

        public Task<string> QueryAsync(
            string kind, string? block, int? network, string? symbol, bool force,
            CancellationToken cancellationToken)
        {
            Queries++;
            LastForce = force;
            return Task.FromResult(Envelope(false));
        }

        public Task<string> PreviewPatchAsync(string patchJson, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<string> ApplyPatchAsync(
            string patchJson, string expectedContentId, string expectedFingerprint, int expectedEpoch,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        private string Envelope(bool cacheHit)
        {
            return JsonSerializer.Serialize(new
            {
                cacheHit,
                targetBindingHash = Binding,
                bindingIdentity = Binding,
                contentId = ContentId,
                epoch = 0,
                result = Body
            });
        }
    }
}
