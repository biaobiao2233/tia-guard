using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace TiaGuard.Bridge.Host;
public static class GatewaySimulationEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/simulation/status", (BridgeSimulationCoordinator c, BridgeWorkerClient worker) => Results.Json(c.Status(worker.ReadRequiredInteraction())));
        app.MapPost("/api/simulation/create", async (SimulationRequest r, BridgeSimulationCoordinator c, CancellationToken ct) => Reply(await c.CreateAsync(r, ct)));
        app.MapPost("/api/simulation/load", async (SimulationSessionRequest r, BridgeSimulationCoordinator c, CancellationToken ct) => Reply(await c.LoadAsync(r.SessionId, ct)));
        app.MapPost("/api/simulation/test", async (SimulationRequest r, BridgeSimulationCoordinator c, CancellationToken ct) => Reply(await c.TestAsync(r, ct)));
        app.MapPost("/api/simulation/run", async (SimulationRunRequest r, BridgeSimulationCoordinator c, CancellationToken ct) => Reply(await c.SetRunAsync(r.SessionId, r.Run, ct)));
        app.MapPost("/api/simulation/inputs", async (SimulationInputsRequest r, BridgeSimulationCoordinator c, CancellationToken ct) => Reply(await c.InputsAsync(r.SessionId, r.Inputs, ct)));
        app.MapPost("/api/simulation/scan", async (SimulationScanRequest r, BridgeSimulationCoordinator c, CancellationToken ct) => Reply(await c.ScanAsync(r.SessionId, r.Scans, ct)));
        app.MapGet("/api/simulation/outputs/{sessionId}", async (string sessionId, BridgeSimulationCoordinator c, CancellationToken ct) => Reply(await c.OutputsAsync(sessionId, ct)));
        app.MapPost("/api/simulation/close", async (SimulationSessionRequest r, BridgeSimulationCoordinator c, CancellationToken ct) => Reply(await c.CloseAsync(r.SessionId, ct)));
    }
    private static IResult Reply(object result)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result);
        return Results.Json(json, statusCode: json.GetProperty("success").GetBoolean() ? 200 : 422);
    }
}
public class SimulationSessionRequest { public string SessionId { get; set; } = ""; }
public sealed class SimulationRunRequest : SimulationSessionRequest { public bool Run { get; set; } }
public sealed class SimulationScanRequest : SimulationSessionRequest { public int Scans { get; set; } = 1; }
public sealed class SimulationInputsRequest : SimulationSessionRequest { public Dictionary<string, bool> Inputs { get; set; } = []; }
