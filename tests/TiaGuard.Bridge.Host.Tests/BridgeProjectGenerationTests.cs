using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using TiaGuard.Bridge.Host;
using Xunit;

namespace TiaGuard.Bridge.Host.Tests;

public sealed class BridgeProjectGenerationTests
{
    [Fact]
    public async Task Preview_is_pure_and_exposes_editable_input_identity_without_a_project_connection()
    {
        using var fixture = new Fixture();
        using var document = JsonDocument.Parse(await fixture.Coordinator.PreviewAsync(fixture.Request));
        var preview = document.RootElement;
        Assert.Equal("previewed", preview.GetProperty("status").GetString());
        Assert.Equal(fixture.Gateway.InputHash, preview.GetProperty("InputHash").GetString());
        Assert.Equal(5, preview.GetProperty("ArtifactCount").GetInt32());
        Assert.Equal(0, fixture.Gateway.Builds);
        Assert.False(preview.GetProperty("mutatesProject").GetBoolean());
        Assert.False(preview.GetProperty("savesOriginalProject").GetBoolean());
        Assert.False(Directory.Exists(fixture.Request.OutputDirectory));
        Assert.False(preview.TryGetProperty("approvalId", out _));
    }

    [Fact]
    public async Task Headless_build_requires_token_preserves_full_reports_and_consumes_failed_build_token()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.BuildAsync(fixture.Request, "invented"));
        Assert.Equal(0, fixture.Gateway.Builds);
        var token = await fixture.PreviewToken();
        var result = await fixture.Coordinator.BuildAsync(fixture.Request, token);
        using var json = JsonDocument.Parse(result);
        Assert.False(BridgeProjectGenerationCoordinator.IsSuccess(result));
        Assert.Equal("generation_failed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("import:sources/Main.scl", json.RootElement.GetProperty("Phase").GetString());
        Assert.Equal("compile probe", json.RootElement.GetProperty("Compile").GetProperty("FullResult").GetProperty("Messages")[0].GetProperty("Description").GetString());
        Assert.Equal("UNKNOWN", json.RootElement.GetProperty("Verification").GetProperty("Verdict").GetString());
        Assert.Single(json.RootElement.GetProperty("Failures").EnumerateArray());
        Assert.Equal(fixture.Gateway.InputHash, fixture.Gateway.ExpectedInputHash);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.BuildAsync(fixture.Request, token));
        Assert.Equal(1, fixture.Gateway.Builds);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("output")]
    [InlineData("scratch")]
    [InlineData("api")]
    public async Task Token_binds_every_source_destination_scratch_and_api_path(string change)
    {
        using var fixture = new Fixture();
        var token = await fixture.PreviewToken();
        var other = Path.Combine(fixture.Root, "other");
        if (change != "output") Directory.CreateDirectory(other);
        var altered = new ProjectGenerationRequest
        {
            SourceDirectory = change == "source" ? other : fixture.Request.SourceDirectory,
            OutputDirectory = change == "output" ? other : fixture.Request.OutputDirectory,
            OwnedScratchRoot = change == "scratch" ? other : fixture.Request.OwnedScratchRoot,
            PublicApiDirectory = change == "api" ? other : fixture.Request.PublicApiDirectory
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.BuildAsync(altered, token));
        Assert.Equal(0, fixture.Gateway.Builds);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.BuildAsync(fixture.Request, token));
    }

    [Fact]
    public async Task Edited_source_hash_requires_new_preview_even_when_paths_are_identical()
    {
        using var fixture = new Fixture();
        var token = await fixture.PreviewToken();
        fixture.Gateway.InputHash = new string('b', 64);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.BuildAsync(fixture.Request, token));
        Assert.Equal(0, fixture.Gateway.Builds);
    }

    [Fact]
    public async Task Worker_restart_invalidates_old_creation_tokens()
    {
        using var fixture = new Fixture();
        var token = await fixture.PreviewToken();
        fixture.Gateway.Generation++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.BuildAsync(fixture.Request, token));
        Assert.Equal(0, fixture.Gateway.Builds);
    }

    [Theory]
    [InlineData("{\"connected\":true}")]
    [InlineData("{}")]
    [InlineData("{\"connected\":null}")]
    public async Task Preview_refuses_bound_or_unknown_worker_session_state(string state)
    {
        using var fixture = new Fixture();
        fixture.Gateway.State = state;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.PreviewAsync(fixture.Request));
        Assert.Equal(0, fixture.Gateway.Inspects);
        Assert.Equal(0, fixture.Gateway.Builds);
    }

    [Fact]
    public async Task Connecting_an_old_project_after_preview_prevents_generation()
    {
        using var fixture = new Fixture();
        var token = await fixture.PreviewToken();
        fixture.Gateway.State = "{\"connected\":true}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.BuildAsync(fixture.Request, token));
        Assert.Equal(0, fixture.Gateway.Builds);
    }

    [Fact]
    public async Task Default_gateway_waits_for_one_gui_approval_then_rechecks_sources()
    {
        using var fixture = new Fixture(requireApproval: true);
        using var preview = JsonDocument.Parse(await fixture.Coordinator.PreviewAsync(fixture.Request));
        var id = preview.RootElement.GetProperty("approvalId").GetString()!;
        var token = preview.RootElement.GetProperty("safetyToken").GetString()!;
        var task = fixture.Coordinator.BuildAsync(fixture.Request, token);
        Assert.False(task.IsCompleted);
        Assert.Equal(0, fixture.Gateway.Builds);
        fixture.Gateway.InputHash = new string('b', 64);
        fixture.Approvals.Allow(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal(0, fixture.Gateway.Builds);
    }

    [Fact]
    public async Task Rejecting_gui_approval_prevents_worker_build()
    {
        using var fixture = new Fixture(requireApproval: true);
        using var preview = JsonDocument.Parse(await fixture.Coordinator.PreviewAsync(fixture.Request));
        var task = fixture.Coordinator.BuildAsync(fixture.Request, preview.RootElement.GetProperty("safetyToken").GetString()!);
        fixture.Approvals.Reject(preview.RootElement.GetProperty("approvalId").GetString()!);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Contains("APPROVAL_REJECTED", error.Message);
        Assert.Equal(0, fixture.Gateway.Builds);
    }

    [Fact]
    public async Task Successful_gui_approval_builds_only_once()
    {
        using var fixture = new Fixture(requireApproval: true);
        var token = await fixture.PreviewToken();
        fixture.Approvals.Allow(Assert.Single(fixture.Approvals.Pending()).Id);
        await fixture.Coordinator.BuildAsync(fixture.Request, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.BuildAsync(fixture.Request, token));
        Assert.Equal(1, fixture.Gateway.Builds);
    }

    [Theory]
    [InlineData("relative-source")]
    [InlineData("existing-output")]
    [InlineData("nested-output")]
    [InlineData("nested-scratch")]
    public void Request_rejects_unsafe_or_existing_destinations(string invalid)
    {
        using var fixture = new Fixture();
        if (invalid == "existing-output") Directory.CreateDirectory(fixture.Request.OutputDirectory);
        var request = new ProjectGenerationRequest
        {
            SourceDirectory = invalid == "relative-source" ? "sources" : fixture.Request.SourceDirectory,
            OutputDirectory = invalid == "nested-output" ? Path.Combine(fixture.Request.SourceDirectory, "output") : fixture.Request.OutputDirectory,
            OwnedScratchRoot = invalid == "nested-scratch" ? fixture.Root : fixture.Request.OwnedScratchRoot
        };
        Assert.ThrowsAny<Exception>(() => request.Normalize());
    }

    [Theory]
    [InlineData("compile")]
    [InlineData("verification")]
    [InlineData("failures")]
    [InlineData("cleanup")]
    [InlineData("outside-output")]
    [InlineData("missing-project")]
    public async Task Published_true_cannot_hide_failed_postconditions_or_a_wrong_artifact(string broken)
    {
        using var fixture = new Fixture();
        fixture.Gateway.Result = request =>
        {
            var node = Success(request);
            if (broken == "compile") node["Compile"]!["AllowsPublication"] = false;
            if (broken == "verification") node["Verification"]!["Verdict"] = "UNKNOWN";
            if (broken == "failures") node["Failures"] = new JsonArray("failure");
            if (broken == "cleanup") node["CleanupFailures"] = new JsonArray("dispose_failed");
            if (broken == "outside-output") node["ProjectFile"] = Path.Combine(request.SourceDirectory, "unrelated.ap21");
            if (broken == "missing-project") node["ProjectFile"] = Path.Combine(request.OutputDirectory, "missing.ap21");
            return node.ToJsonString();
        };
        var result = await fixture.Coordinator.BuildAsync(fixture.Request, await fixture.PreviewToken());
        Assert.False(BridgeProjectGenerationCoordinator.IsSuccess(result));
        using var json = JsonDocument.Parse(result);
        Assert.True(json.RootElement.GetProperty("Published").GetBoolean());
        Assert.Equal("generation_failed", json.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("gatewayFailure").GetString()));
    }

    [Fact]
    public async Task Only_a_new_existing_project_with_compile_and_independent_PASS_is_success()
    {
        using var fixture = new Fixture();
        fixture.Gateway.Result = request => Success(request).ToJsonString();
        var result = await fixture.Coordinator.BuildAsync(fixture.Request, await fixture.PreviewToken());
        Assert.True(BridgeProjectGenerationCoordinator.IsSuccess(result));
        using var json = JsonDocument.Parse(result);
        Assert.Equal("published-verified", json.RootElement.GetProperty("status").GetString());
        Assert.True(File.Exists(json.RootElement.GetProperty("ProjectFile").GetString()));
    }

    [Fact]
    public async Task Mcp_marks_unsuccessful_creation_as_tool_error_and_keeps_diagnostics()
    {
        using var fixture = new Fixture();
        var result = await BridgeProjectGenerationWriteTools.BuildProjectGeneration(fixture.Coordinator,
            fixture.Request.SourceDirectory, fixture.Request.OutputDirectory, fixture.Request.OwnedScratchRoot,
            await fixture.PreviewToken());
        Assert.True(result.IsError);
        Assert.False(result.StructuredContent!.Value.GetProperty("success").GetBoolean());
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Contains("compile probe", content.Text);
        Assert.Contains("generation_failed", content.Text);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.UnprocessableEntity)]
    [InlineData(true, HttpStatusCode.OK)]
    public async Task Actual_http_routes_expose_preview_and_use_failure_status_with_full_reports(bool passes, HttpStatusCode expected)
    {
        using var fixture = new Fixture();
        if (passes) fixture.Gateway.Result = request => Success(request).ToJsonString();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(fixture.Coordinator);
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try { await next(); }
            catch (InvalidOperationException error)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = error.Message });
            }
        });
        GatewayProjectGenerationEndpoints.Map(app);
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var previewResponse = await client.PostAsJsonAsync("/api/project-generation/preview", fixture.Request);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        var request = new ProjectGenerationBuildHttpRequest
        {
            SourceDirectory = fixture.Request.SourceDirectory,
            OutputDirectory = fixture.Request.OutputDirectory,
            OwnedScratchRoot = fixture.Request.OwnedScratchRoot,
            SafetyToken = preview.RootElement.GetProperty("safetyToken").GetString()!
        };
        using var response = await client.PostAsJsonAsync("/api/project-generation/build", request);
        Assert.Equal(expected, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(passes, payload.RootElement.GetProperty("success").GetBoolean());
        Assert.True(payload.RootElement.TryGetProperty("Compile", out _));
        Assert.True(payload.RootElement.TryGetProperty("Verification", out _));
    }

    [Fact]
    public void Discovery_documents_the_new_offline_generation_surface()
    {
        using var catalog = JsonDocument.Parse(GatewayCatalog.CapabilitiesJson(false));
        var generation = catalog.RootElement.GetProperty("projectGeneration");
        Assert.False(generation.GetProperty("existingProjectRequired").GetBoolean());
        Assert.True(generation.GetProperty("inputHashBound").GetBoolean());
        Assert.Contains("build_project_generation", catalog.RootElement.GetProperty("write").GetProperty("operations").EnumerateArray().Select(x => x.GetString()));
        using var api = JsonDocument.Parse(GatewayCatalog.OpenApiJson());
        var paths = api.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/project-generation/preview", out _));
        Assert.True(paths.GetProperty("/api/project-generation/build").GetProperty("post").GetProperty("responses").TryGetProperty("422", out _));
    }

    [Fact]
    public void Gateway_activity_identifies_generation_build_as_a_write()
    {
        var activity = new GatewayActivityTracker();
        using (activity.Begin("/api/project-generation/preview", "agent"))
            Assert.Equal("read", activity.Read().Category);
        using (activity.Begin("/api/project-generation/build", "agent"))
            Assert.Equal("write", activity.Read().Category);
    }

    private static JsonObject Success(ProjectGenerationRequest request)
    {
        Directory.CreateDirectory(request.OutputDirectory);
        var file = Path.Combine(request.OutputDirectory, "Generated.ap21");
        File.WriteAllText(file, "synthetic fixture; never opened by TIA");
        return new JsonObject
        {
            ["Published"] = true, ["ProjectFile"] = file, ["Phase"] = "published",
            ["Compile"] = new JsonObject { ["AllowsPublication"] = true, ["FullResult"] = new JsonObject { ["ErrorCount"] = 0 } },
            ["Verification"] = new JsonObject { ["Verdict"] = "PASS" },
            ["Failures"] = new JsonArray(), ["CleanupFailures"] = new JsonArray()
        };
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "tia-generation-gateway-tests-" + Guid.NewGuid().ToString("N"));
        public FakeGateway Gateway { get; } = new();
        public GatewayApprovalService Approvals { get; }
        public ProjectGenerationRequest Request { get; }
        public BridgeProjectGenerationCoordinator Coordinator { get; }

        public Fixture(bool requireApproval = false)
        {
            Request = new ProjectGenerationRequest
            {
                SourceDirectory = Path.Combine(Root, "input"), OutputDirectory = Path.Combine(Root, "output"),
                OwnedScratchRoot = Path.Combine(Root, "scratch")
            };
            Directory.CreateDirectory(Request.SourceDirectory);
            Directory.CreateDirectory(Request.OwnedScratchRoot);
            Approvals = new GatewayApprovalService(requireApproval);
            Coordinator = new BridgeProjectGenerationCoordinator(Gateway, new BridgeWriteSafetyService(), Approvals);
        }

        public async Task<string> PreviewToken()
        {
            using var json = JsonDocument.Parse(await Coordinator.PreviewAsync(Request));
            return json.RootElement.GetProperty("safetyToken").GetString()!;
        }

        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed class FakeGateway : IBridgeProjectGenerationGateway
    {
        public int Generation { get; set; } = 1;
        public int Builds { get; private set; }
        public int Inspects { get; private set; }
        public string State { get; set; } = "{\"connected\":false}";
        public string InputHash { get; set; } = new string('a', 64);
        public string? ExpectedInputHash { get; private set; }
        public Func<ProjectGenerationRequest, string> Result { get; set; } = _ => """
            {"Published":false,"ProjectFile":null,"Phase":"import:sources/Main.scl",
            "Compile":{"AllowsPublication":false,"FullResult":{"Messages":[{"Description":"compile probe"}]}},
            "Verification":{"Verdict":"UNKNOWN","Findings":[{"Code":"unknown"}]},
            "Failures":["import_failed"],"CleanupFailures":[]}
            """;
        public Task<string> StateAsync(CancellationToken cancellationToken) => Task.FromResult(State);
        public Task<string> InspectAsync(string sourceDirectory, CancellationToken cancellationToken)
        {
            Inspects++;
            return Task.FromResult(JsonSerializer.Serialize(new { InputHash, ProjectName = "Demo", PlcName = "PLC", ArtifactCount = 5, TagCount = 5 }));
        }
        public Task<string> BuildAsync(ProjectGenerationRequest request, string expectedInputHash, CancellationToken cancellationToken)
        {
            Builds++;
            ExpectedInputHash = expectedInputHash;
            return Task.FromResult(Result(request));
        }
    }
}
