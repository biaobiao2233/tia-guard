using System.Security.Cryptography;
using System.Text.Json;

namespace TiaGuard.Bridge.Host;

public sealed class BridgeProjectPublishCoordinator
{
    private const string Operation = "publish_offline_copy";
    private readonly BridgeWorkerClient _worker;
    private readonly BridgeWriteSafetyService _safety;

    public BridgeProjectPublishCoordinator(
        BridgeWorkerClient worker,
        BridgeWriteSafetyService safety)
    {
        _worker = worker;
        _safety = safety;
    }

    public async Task<BridgeWritePreview> PreviewAsync(
        ProjectPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        var target = await ReadOfflineTargetAsync(cancellationToken)
            .ConfigureAwait(false);
        request.ValidateProjectName(target.ProjectName);
        var contentId = await ReadContentIdAsync(cancellationToken)
            .ConfigureAwait(false);
        var requestJson = BridgeWriteSafetyService.SerializeRequest(request);

        return _safety.CreatePreview(
            Operation,
            $"Compile the disposable offline copy and publish it under " +
            $"'{Path.Combine(request.OutputDirectory, request.OutputName)}'. " +
            "The original project is never overwritten.",
            target.Identity,
            requestJson,
            contentId,
            savesProject: true);
    }

    public async Task<BridgePublishApplyResult> ApplyAsync(
        ProjectPublishRequest request,
        string safetyToken,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        var target = await ReadOfflineTargetAsync(cancellationToken)
            .ConfigureAwait(false);
        request.ValidateProjectName(target.ProjectName);
        var contentIdBefore = await ReadContentIdAsync(cancellationToken)
            .ConfigureAwait(false);
        var requestJson = BridgeWriteSafetyService.SerializeRequest(request);

        _safety.ValidateAndConsume(
            safetyToken,
            Operation,
            target.Identity,
            requestJson,
            contentIdBefore);

        var resultJson = await _worker.CallAsync(
            "publish_offline_copy",
            outputDirectory: Path.GetFullPath(request.OutputDirectory),
            outputName: request.OutputName,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        using var resultDocument = JsonDocument.Parse(resultJson);
        var result = resultDocument.RootElement;
        var projectFile = GetString(result, "ProjectFile");
        var compileErrors = GetInt(result, "CompileErrors");
        var compileWarnings = GetInt(result, "CompileWarnings");

        if (compileErrors != 0)
            throw new InvalidOperationException(
                "Bridge publish returned a project with compile errors.");
        if (string.IsNullOrWhiteSpace(projectFile) || !File.Exists(projectFile))
            throw new InvalidOperationException(
                "Bridge publish did not return an existing project file.");
        if (!string.Equals(Path.GetExtension(projectFile), ".ap21",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Bridge publish did not return a TIA Portal V21 .ap21 file.");

        // The worker closes the TIA project in a finally block before this
        // response arrives, so hashing here avoids racing TIA Portal's SaveAs file lock.
        string actualSha;
        using (var stream = File.OpenRead(projectFile))
            actualSha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        var openedForVerification = false;
        try
        {
            await _worker.CallAsync(
                "open_offline_project",
                projectPath: projectFile,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            openedForVerification = true;

            var contentIdAfter = await ReadContentIdAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    contentIdBefore, contentIdAfter, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Published .ap21 reopened successfully but its deterministic engineering " +
                    "contentId differs from the pre-publish disposable project state.");

            return new BridgePublishApplyResult
            {
                Status = "published-verified",
                Operation = Operation,
                SavedProject = true,
                ProjectFile = Path.GetFullPath(projectFile),
                Sha256 = actualSha,
                VerifiedContentId = contentIdAfter,
                CompileErrors = compileErrors,
                CompileWarnings = compileWarnings
            };
        }
        finally
        {
            if (openedForVerification)
            {
                try
                {
                    await _worker.CallAsync(
                        "disconnect",
                        cancellationToken: CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // The verification result/failure is more important than cleanup text.
                    // The host still owns the worker process and disposes it at shutdown.
                }
            }
        }
    }

    private async Task<OfflineTarget> ReadOfflineTargetAsync(
        CancellationToken cancellationToken)
    {
        var stateJson = await _worker.CallAsync(
            "get_state",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(stateJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("connected", out var connected) ||
            connected.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("project", out var project) ||
            project.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                "No TIA project is bound. Open an offline .ap21 copy before publishing.");

        var sourceKind = GetString(project, "SourceKind");
        if (!string.Equals(sourceKind, "offline-copy", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Bridge publishing is restricted to disposable offline project copies.");

        var projectName = GetString(project, "Name");
        if (string.IsNullOrWhiteSpace(projectName))
            throw new InvalidOperationException(
                "The bound TIA project name is unavailable.");

        return new OfflineTarget
        {
            ProjectName = projectName,
            Identity = string.Join("|",
                projectName,
                GetString(project, "Path"),
                GetString(project, "TiaVersion"),
                GetString(project, "TiaBuild"),
                sourceKind,
                GetNullableInt(project, "ProcessId"))
        };
    }

    private async Task<string> ReadContentIdAsync(
        CancellationToken cancellationToken)
    {
        var snapshotJson = await _worker.CallAsync(
            "get_project_snapshot",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(snapshotJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("capture", out var capture) ||
            !string.Equals(GetString(capture, "status"), "complete",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Bridge publish requires a complete engineering snapshot.");

        if (!root.TryGetProperty("project", out var project))
            throw new InvalidOperationException(
                "Bridge publish snapshot has no project identity.");

        var contentId = GetString(project, "contentId");
        if (string.IsNullOrWhiteSpace(contentId))
            throw new InvalidOperationException(
                "Bridge publish requires a deterministic snapshot contentId.");
        return contentId;
    }

    private sealed class OfflineTarget
    {
        public string ProjectName { get; init; } = string.Empty;
        public string Identity { get; init; } = string.Empty;
    }

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var number)
            ? number
            : -1;

    private static string GetNullableInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var number)
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
}
