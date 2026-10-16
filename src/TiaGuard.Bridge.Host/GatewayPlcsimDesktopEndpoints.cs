using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Text.Json;

namespace TiaGuard.Bridge.Host;

public static class GatewayPlcsimDesktopEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/simulation/desktop/status", (BridgePlcsimDesktopCoordinator c) => Results.Json(c.Status()));
        app.MapPost("/api/simulation/desktop/open", async (BridgePlcsimDesktopCoordinator c, CancellationToken ct) => Reply(await c.OpenAsync(ct)));
        app.MapGet("/api/simulation/desktop/{sessionId}", async (string sessionId, BridgePlcsimDesktopCoordinator c, CancellationToken ct) => Reply(await c.InspectAsync(sessionId, ct)));
        app.MapPost("/api/simulation/desktop/act", async (PlcsimDesktopActionRequest r, BridgePlcsimDesktopCoordinator c, CancellationToken ct) => Reply(await c.ActAsync(r, ct)));
        app.MapPost("/api/simulation/desktop/close", async (SimulationSessionRequest r, BridgePlcsimDesktopCoordinator c, CancellationToken ct) => Reply(await c.CloseAsync(r.SessionId, ct)));
    }
    private static IResult Reply(object result)
    {
        var json = JsonSerializer.SerializeToElement(result);
        return Results.Json(json, statusCode: json.GetProperty("success").GetBoolean() ? 200 : 422);
    }
}
