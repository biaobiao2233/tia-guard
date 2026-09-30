using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TiaGuard.Bridge.Host;

// Private GUI control routes: never register these in the AI/OpenAPI surface.
public static class GatewayOperatorEndpoints
{
    public static void Map(
        WebApplication app,
        GatewayApprovalService approvals,
        string? expectedKey,
        Func<Task> shutdown)
    {
        app.MapPost("/api/v1/gateway/approvals/{id}/allow", (string id, HttpRequest request) =>
            DecideApproval(id, request, expectedKey, approvals.Allow));
        app.MapPost("/api/v1/gateway/approvals/{id}/reject", (string id, HttpRequest request) =>
            DecideApproval(id, request, expectedKey, approvals.Reject));
        app.MapPost("/api/v1/gateway/shutdown", (HttpRequest request) =>
        {
            if (!AuthorizeOperator(request, expectedKey))
                return Forbidden();
            _ = Task.Run(async () =>
            {
                await Task.Delay(150);
                await shutdown();
            });
            return Results.Json(new { status = "stopping" });
        });
    }

    private static IResult DecideApproval(
        string id, HttpRequest request, string? expectedKey, Action<string> decide)
    {
        if (!AuthorizeOperator(request, expectedKey))
            return Forbidden();
        decide(id);
        return Results.Json(new { status = "recorded" });
    }

    private static bool AuthorizeOperator(HttpRequest request, string? expectedKey)
    {
        // A manually started gateway without a GUI key has no operator authority.
        if (string.IsNullOrWhiteSpace(expectedKey))
            return false;
        var presented = request.Headers["X-TiaGuard-Approval-Key"];
        if (presented.Count != 1 || string.IsNullOrEmpty(presented[0]))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedKey), Encoding.UTF8.GetBytes(presented[0]!));
    }

    private static IResult Forbidden() => Results.Json(
        new { error = "APPROVAL_FORBIDDEN" }, statusCode: StatusCodes.Status403Forbidden);
}
