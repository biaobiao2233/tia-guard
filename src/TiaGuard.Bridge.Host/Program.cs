using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using TiaGuard.Bridge.Host;

BridgeOptions options;
try
{
    options = BridgeOptions.Parse(args);
}
catch (ArgumentException error)
{
    Console.Error.WriteLine("error=" + error.Message);
    Console.Error.WriteLine(BridgeOptions.Usage);
    Environment.ExitCode = 2;
    return;
}

if (options.ShowHelp)
{
    Console.WriteLine(BridgeOptions.Usage);
    return;
}

if (!File.Exists(options.WorkerPath))
{
    Console.Error.WriteLine("error=Bridge worker not found: " + options.WorkerPath);
    Environment.ExitCode = 2;
    return;
}

if (options.Transport == "stdio")
{
    var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
    builder.Logging.AddConsole(log => log.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddSingleton(new BridgeWorkerClient(options.WorkerPath, options.AllowWrite));
    builder.Services.AddSingleton<IBridgeEngineeringGateway>(services =>
        new BridgeWorkerEngineeringGateway(services.GetRequiredService<BridgeWorkerClient>()));
    builder.Services.AddSingleton<BridgeAiContextService>();
    if (options.AllowWrite)
    {
        builder.Services.AddSingleton<BridgeWriteSafetyService>();
        builder.Services.AddSingleton<BridgeTagWriteCoordinator>();
        builder.Services.AddSingleton<BridgeProjectPublishCoordinator>();
        builder.Services.AddSingleton<BridgePatchCoordinator>();
    }

    var mcp = builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<BridgeReadTools>()
        .WithTools<BridgeAiTools>();
    if (options.AllowWrite)
        mcp.WithTools<BridgeWriteTools>().WithTools<BridgePatchTools>();

    using var host = builder.Build();
    await host.RunAsync();
    return;
}

var webBuilder = WebApplication.CreateBuilder(Array.Empty<string>());
webBuilder.Logging.AddConsole();
webBuilder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
webBuilder.Services.AddSingleton(new BridgeWorkerClient(options.WorkerPath, options.AllowWrite));
webBuilder.Services.AddSingleton<IBridgeEngineeringGateway>(services =>
    new BridgeWorkerEngineeringGateway(services.GetRequiredService<BridgeWorkerClient>()));
webBuilder.Services.AddSingleton<BridgeAiContextService>();
if (options.AllowWrite)
{
    webBuilder.Services.AddSingleton<BridgeWriteSafetyService>();
    webBuilder.Services.AddSingleton<BridgeTagWriteCoordinator>();
    webBuilder.Services.AddSingleton<BridgeProjectPublishCoordinator>();
    webBuilder.Services.AddSingleton<BridgePatchCoordinator>();
}

var httpMcp = webBuilder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<BridgeReadTools>()
    .WithTools<BridgeAiTools>();
if (options.AllowWrite)
    httpMcp.WithTools<BridgeWriteTools>().WithTools<BridgePatchTools>();

var app = webBuilder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception error) when (error is InvalidOperationException or ArgumentException &&
        context.Request.Path.StartsWithSegments("/api"))
    {
        if (context.Response.HasStarted)
            throw;
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new { error = error.Message }));
    }
});

app.MapMcp("/mcp");

app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    product = "TIA-Guard Bridge",
    version = "0.1.0",
    transport = "http",
    accessMode = options.AllowWrite ? "read-write" : "read-only",
    bind = "127.0.0.1",
    port = options.Port
}));

app.MapGet("/api/v1/projects", async (
    BridgeWorkerClient worker,
    CancellationToken cancellationToken) =>
    Json(await worker.CallAsync("list_open_projects", cancellationToken: cancellationToken)));

app.MapPost("/api/v1/connect", async (
    ConnectRequest request,
    BridgeWorkerClient worker,
    CancellationToken cancellationToken) =>
    Json(await worker.CallAsync("connect",
        processId: request.ProcessId,
        cancellationToken: cancellationToken)));

app.MapPost("/api/v1/open-offline", async (
    OpenOfflineRequest request,
    BridgeWorkerClient worker,
    CancellationToken cancellationToken) =>
    Json(await worker.CallAsync(
        "open_offline_project",
        projectPath: request.ProjectPath,
        cancellationToken: cancellationToken)));

app.MapPost("/api/v1/disconnect", async (
    BridgeWorkerClient worker,
    BridgeAiContextService context,
    CancellationToken cancellationToken) =>
{
    context.Invalidate();
    return Json(await worker.CallAsync("disconnect", cancellationToken: cancellationToken));
});

app.MapGet("/api/v1/state", async (
    BridgeWorkerClient worker,
    CancellationToken cancellationToken) =>
    Json(await worker.CallAsync("get_state", cancellationToken: cancellationToken)));

app.MapGet("/api/v1/project", async (
    BridgeWorkerClient worker,
    CancellationToken cancellationToken) =>
    Json(await worker.CallAsync("get_project", cancellationToken: cancellationToken)));

app.MapGet("/api/v1/project/snapshot", async (
    BridgeWorkerClient worker,
    CancellationToken cancellationToken) =>
    Json(await worker.CallAsync("get_project_snapshot", cancellationToken: cancellationToken)));

app.MapGet("/api/v1/ai/context", async (
    BridgeAiContextService context,
    CancellationToken cancellationToken) =>
    Json(await context.GetProjectContextAsync(cancellationToken)));

app.MapGet("/api/v1/ai/program-graph", async (
    string block,
    BridgeAiContextService context,
    CancellationToken cancellationToken) =>
    Json(await context.GetProgramGraphAsync(block, cancellationToken)));

app.MapGet("/api/v1/ai/network", async (
    string block,
    int network,
    BridgeAiContextService context,
    CancellationToken cancellationToken) =>
    Json(await context.GetNetworkAsync(block, network, cancellationToken)));

app.MapGet("/api/v1/ai/where-used", async (
    string symbol,
    BridgeAiContextService context,
    CancellationToken cancellationToken) =>
    Json(await context.WhereUsedAsync(symbol, cancellationToken)));

app.MapPost("/api/v1/ai/refresh", async (
    BridgeAiContextService context,
    CancellationToken cancellationToken) =>
    Json(await context.RefreshAsync(cancellationToken)));

if (options.AllowWrite)
{
    app.MapPost("/api/v1/tags/preview-upsert", async (
        TagUpsertHttpRequest request,
        BridgeTagWriteCoordinator coordinator,
        CancellationToken cancellationToken) =>
        Results.Json(await coordinator.PreviewAsync(request.ToRequest(), cancellationToken)));

    app.MapPost("/api/v1/tags/apply-upsert", async (
        TagUpsertApplyHttpRequest request,
        BridgeTagWriteCoordinator coordinator,
        CancellationToken cancellationToken) =>
        Results.Json(await coordinator.ApplyAsync(
            request.ToRequest(), request.SafetyToken, cancellationToken)));

    app.MapPost("/api/v1/project/preview-publish", async (
        ProjectPublishHttpRequest request,
        BridgeProjectPublishCoordinator coordinator,
        CancellationToken cancellationToken) =>
        Results.Json(await coordinator.PreviewAsync(
            request.ToRequest(), cancellationToken)));

    app.MapPost("/api/v1/project/apply-publish", async (
        ProjectPublishApplyHttpRequest request,
        BridgeProjectPublishCoordinator coordinator,
        CancellationToken cancellationToken) =>
        Results.Json(await coordinator.ApplyAsync(
            request.ToRequest(), request.SafetyToken, cancellationToken)));

    app.MapPost("/api/v1/ai/patches/preview", async (
        PatchHttpRequest request,
        BridgePatchCoordinator coordinator,
        CancellationToken cancellationToken) =>
        Json(await coordinator.PreviewAsync(request.PatchJson(), cancellationToken)));

    app.MapPost("/api/v1/ai/patches/apply", async (
        PatchApplyHttpRequest request,
        BridgePatchCoordinator coordinator,
        CancellationToken cancellationToken) =>
        Json(await coordinator.ApplyAsync(
            request.PatchJson(), request.SafetyToken, cancellationToken)));

}

Console.Error.WriteLine(
    $"TIA-Guard Bridge HTTP listening on http://127.0.0.1:{options.Port} "
    + $"({(options.AllowWrite ? "read-write" : "read-only")})");
await app.RunAsync();

static IResult Json(string payload)
    => Results.Text(payload, "application/json");

internal sealed class ConnectRequest
{
    public int? ProcessId { get; set; }
}

internal sealed class OpenOfflineRequest
{
    public string ProjectPath { get; set; } = string.Empty;
}

internal class TagUpsertHttpRequest
{
    public string TableName { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string LogicalAddress { get; set; } = string.Empty;

    public TagUpsertRequest ToRequest() => new()
    {
        TableName = TableName,
        TagName = TagName,
        DataType = DataType,
        LogicalAddress = LogicalAddress
    };
}

internal sealed class TagUpsertApplyHttpRequest : TagUpsertHttpRequest
{
    public string SafetyToken { get; set; } = string.Empty;
}

internal class ProjectPublishHttpRequest
{
    public string OutputDirectory { get; set; } = string.Empty;
    public string OutputName { get; set; } = string.Empty;

    public ProjectPublishRequest ToRequest() => new()
    {
        OutputDirectory = OutputDirectory,
        OutputName = OutputName
    };
}

internal sealed class ProjectPublishApplyHttpRequest : ProjectPublishHttpRequest
{
    public string SafetyToken { get; set; } = string.Empty;
}

internal class PatchHttpRequest
{
    public JsonElement Patch { get; set; }

    public string PatchJson()
        => Patch.ValueKind == JsonValueKind.String
            ? Patch.GetString() ?? string.Empty
            : Patch.GetRawText();
}

internal sealed class PatchApplyHttpRequest : PatchHttpRequest
{
    public string SafetyToken { get; set; } = string.Empty;
}

internal sealed class BridgeOptions
{
    public const string Usage =
@"TIA-Guard Bridge

Usage:
  tia-guard-bridge --transport stdio [--worker <path>] [--allow-write]
  tia-guard-bridge --transport http [--port <1-65535>] [--worker <path>] [--allow-write]

Defaults:
  transport = stdio
  http bind = 127.0.0.1
  http port = 18761
  worker = worker\TiaGuard.Bridge.Worker.exe when packaged; flat next-to-host path remains the development fallback

The default is read-only. It includes AI Engineering v2 context tools.
--allow-write enables guarded tag edits, structured engineering patches, and
publication to a NEW output directory on a disposable offline copy.
It does not enable attached-project saves/overwrites, PLC download, start/stop, force,
online writes, or Safety operations.";

    public string Transport { get; private set; } = "stdio";
    public int Port { get; private set; } = 18761;
    public string WorkerPath { get; private set; } = GetDefaultWorkerPath();
    public bool ShowHelp { get; private set; }
    public bool AllowWrite { get; private set; }

    private static string GetDefaultWorkerPath()
    {
        var isolated = Path.Combine(
            AppContext.BaseDirectory,
            "worker",
            "TiaGuard.Bridge.Worker.exe");
        if (File.Exists(isolated))
            return isolated;

        return Path.Combine(
            AppContext.BaseDirectory,
            "TiaGuard.Bridge.Worker.exe");
    }

    public static BridgeOptions Parse(string[] args)
    {
        var result = new BridgeOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h")
            {
                result.ShowHelp = true;
                continue;
            }

            if (arg == "--allow-write")
            {
                result.AllowWrite = true;
                continue;
            }

            if (arg == "--transport")
            {
                RequireValue(args, ref i, arg, out var value);
                result.Transport = value.ToLowerInvariant();
                continue;
            }

            if (arg == "--port")
            {
                RequireValue(args, ref i, arg, out var value);
                if (!int.TryParse(value, out var port) || port < 1 || port > 65535)
                    throw new ArgumentException("--port must be between 1 and 65535.");
                result.Port = port;
                continue;
            }

            if (arg == "--worker")
            {
                RequireValue(args, ref i, arg, out var value);
                result.WorkerPath = Path.GetFullPath(value);
                continue;
            }

            throw new ArgumentException("Unknown Bridge argument: " + arg);
        }

        if (result.Transport != "stdio" && result.Transport != "http")
            throw new ArgumentException("--transport must be stdio or http.");

        return result;
    }

    private static void RequireValue(
        string[] args,
        ref int index,
        string name,
        out string value)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
            throw new ArgumentException(name + " requires a value.");

        value = args[++index];
    }
}
