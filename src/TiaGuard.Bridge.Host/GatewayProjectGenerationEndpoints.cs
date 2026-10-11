using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace TiaGuard.Bridge.Host;

public static class GatewayProjectGenerationEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/project-generation/preview", async (ProjectGenerationHttpRequest request,
            BridgeProjectGenerationCoordinator coordinator, CancellationToken cancellationToken) =>
            Results.Text(await coordinator.PreviewAsync(request.ToRequest(), cancellationToken).ConfigureAwait(false), "application/json"));
        app.MapPost("/api/project-generation/build", async (ProjectGenerationBuildHttpRequest request,
            BridgeProjectGenerationCoordinator coordinator, CancellationToken cancellationToken) =>
        {
            var result = await coordinator.BuildAsync(request.ToRequest(), request.SafetyToken, cancellationToken).ConfigureAwait(false);
            return Results.Text(result, "application/json", statusCode:
                BridgeProjectGenerationCoordinator.IsSuccess(result) ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);
        });
    }
}

public class ProjectGenerationHttpRequest
{
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public string OwnedScratchRoot { get; set; } = string.Empty;
    public string? PublicApiDirectory { get; set; }
    public ProjectGenerationRequest ToRequest() => new()
    {
        SourceDirectory = SourceDirectory, OutputDirectory = OutputDirectory,
        OwnedScratchRoot = OwnedScratchRoot, PublicApiDirectory = PublicApiDirectory
    };
}

public sealed class ProjectGenerationBuildHttpRequest : ProjectGenerationHttpRequest
{
    public string SafetyToken { get; set; } = string.Empty;
}
