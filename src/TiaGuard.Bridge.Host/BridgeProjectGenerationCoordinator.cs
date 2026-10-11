using System.Text.Json;
using System.Text.Json.Nodes;

namespace TiaGuard.Bridge.Host;

public sealed class ProjectGenerationRequest
{
    public string SourceDirectory { get; init; } = string.Empty;
    public string OutputDirectory { get; init; } = string.Empty;
    public string OwnedScratchRoot { get; init; } = string.Empty;
    public string? PublicApiDirectory { get; init; }

    public ProjectGenerationRequest Normalize()
    {
        var source = AbsolutePath(SourceDirectory, nameof(SourceDirectory));
        var output = AbsolutePath(OutputDirectory, nameof(OutputDirectory));
        var scratch = AbsolutePath(OwnedScratchRoot, nameof(OwnedScratchRoot));
        var api = PublicApiDirectory == null ? null : AbsolutePath(PublicApiDirectory, nameof(PublicApiDirectory));
        if (!Directory.Exists(source))
            throw new ArgumentException("SourceDirectory does not exist.");
        if (Directory.Exists(output) || File.Exists(output))
            throw new InvalidOperationException("OutputDirectory must be a new, nonexistent directory.");
        if (!Directory.Exists(scratch))
            throw new ArgumentException("OwnedScratchRoot must be an existing directory for task-owned temporary projects.");
        if (api != null && !Directory.Exists(api))
            throw new ArgumentException("PublicApiDirectory does not exist.");
        if (Overlaps(source, output) || Overlaps(source, scratch) || Overlaps(output, scratch))
            throw new ArgumentException("Source, output and scratch directories must not contain one another.");
        return new ProjectGenerationRequest
        {
            SourceDirectory = source, OutputDirectory = output,
            OwnedScratchRoot = scratch, PublicApiDirectory = api
        };
    }

    private static string AbsolutePath(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
            throw new ArgumentException(name + " must be an absolute path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private static bool Overlaps(string left, string right)
        => Contains(left, right) || Contains(right, left);

    internal static bool Contains(string parent, string child)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(parent, child, comparison) ||
            child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, comparison);
    }
}

public interface IBridgeProjectGenerationGateway
{
    int Generation { get; }
    Task<string> StateAsync(CancellationToken cancellationToken);
    Task<string> InspectAsync(string sourceDirectory, CancellationToken cancellationToken);
    Task<string> BuildAsync(ProjectGenerationRequest request, string expectedInputHash, CancellationToken cancellationToken);
}

public sealed class BridgeWorkerProjectGenerationGateway : IBridgeProjectGenerationGateway
{
    private readonly BridgeWorkerClient _worker;
    public BridgeWorkerProjectGenerationGateway(BridgeWorkerClient worker) => _worker = worker;
    public int Generation => _worker.Generation;
    public Task<string> StateAsync(CancellationToken cancellationToken)
        => _worker.CallAsync("get_state", cancellationToken: cancellationToken);
    public Task<string> InspectAsync(string sourceDirectory, CancellationToken cancellationToken)
        => _worker.CallAsync("preview_project_generation", payloadJson: JsonSerializer.Serialize(new { SourceDirectory = sourceDirectory }),
            cancellationToken: cancellationToken);
    public Task<string> BuildAsync(ProjectGenerationRequest request, string expectedInputHash, CancellationToken cancellationToken)
        => _worker.CallAsync("build_project_generation", payloadJson: JsonSerializer.Serialize(new
        {
            request.SourceDirectory, request.OutputDirectory, request.OwnedScratchRoot,
            request.PublicApiDirectory, ExpectedInputHash = expectedInputHash
        }), cancellationToken: cancellationToken);
}

public sealed class BridgeProjectGenerationCoordinator
{
    public const string Operation = "build_project_generation";
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web);
    private readonly IBridgeProjectGenerationGateway _gateway;
    private readonly BridgeWriteSafetyService _safety;
    private readonly GatewayApprovalService? _approvals;

    public BridgeProjectGenerationCoordinator(IBridgeProjectGenerationGateway gateway,
        BridgeWriteSafetyService safety, GatewayApprovalService? approvals = null)
    {
        _gateway = gateway;
        _safety = safety;
        _approvals = approvals;
    }

    public async Task<string> PreviewAsync(ProjectGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = request.Normalize();
        await RequireUnboundAsync(cancellationToken).ConfigureAwait(false);
        var inspection = await _gateway.InspectAsync(normalized.SourceDirectory, cancellationToken).ConfigureAwait(false);
        var inputHash = ReadInputHash(inspection);
        var identity = Identity();
        var ticket = _safety.CreatePreview(Operation,
            "创建新的离线 PLC 工程到 '" + normalized.OutputDirectory + "'，编译并独立回读验证通过后保存。",
            identity, JsonSerializer.Serialize(normalized, RequestJson), inputHash, savesProject: true);
        var node = JsonNode.Parse(inspection) as JsonObject
            ?? throw new InvalidOperationException("Project generation preview was not a JSON object.");
        node["status"] = "previewed";
        node["request"] = JsonSerializer.SerializeToNode(normalized, RequestJson);
        node["safetyToken"] = ticket.SafetyToken;
        node["expiresAt"] = ticket.ExpiresAt.ToString("O");
        node["mutatesProject"] = false;
        node["savesOriginalProject"] = false;
        var approval = _approvals?.Register(ticket.SafetyToken, ticket.Summary, ticket.ExpiresAt,
            ticket.TargetBindingHash, ticket.RequestHash, ticket.CurrentStateHash);
        if (approval != null)
        {
            node["approvalId"] = approval.Id;
            node["approvalSummary"] = approval.Summary;
        }
        return node.ToJsonString();
    }

    public async Task<string> BuildAsync(ProjectGenerationRequest request, string safetyToken, CancellationToken cancellationToken = default)
    {
        if (_approvals != null)
            await _approvals.WaitAsync(safetyToken, cancellationToken).ConfigureAwait(false);
        var normalized = request.Normalize();
        await RequireUnboundAsync(cancellationToken).ConfigureAwait(false);
        var inspection = await _gateway.InspectAsync(normalized.SourceDirectory, cancellationToken).ConfigureAwait(false);
        var inputHash = ReadInputHash(inspection);
        _safety.ValidateAndConsume(safetyToken, Operation, Identity(), JsonSerializer.Serialize(normalized, RequestJson), inputHash);
        // The worker repeats the session guard atomically within its serialized dispatch.
        // The core freezes the input and compares this hash again before opening TIA.
        var raw = await _gateway.BuildAsync(normalized, inputHash, cancellationToken).ConfigureAwait(false);
        var node = JsonNode.Parse(raw) as JsonObject
            ?? throw new InvalidOperationException("Project generation build was not a JSON object.");
        var failure = CheckPublishedResult(raw, normalized.OutputDirectory);
        node["status"] = failure == null ? "published-verified" : "generation_failed";
        node["success"] = failure == null;
        node["savesOriginalProject"] = false;
        if (failure != null) node["gatewayFailure"] = failure;
        return node.ToJsonString();
    }

    public static bool IsSuccess(string result)
    {
        using var document = JsonDocument.Parse(result);
        return document.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;
    }

    private string Identity() => "new-offline-project|worker-generation=" + _gateway.Generation;

    private async Task RequireUnboundAsync(CancellationToken cancellationToken)
    {
        using var state = JsonDocument.Parse(await _gateway.StateAsync(cancellationToken).ConfigureAwait(false));
        if (!TryProperty(state.RootElement, "connected", out var connected) ||
            connected.ValueKind != JsonValueKind.False)
            throw new InvalidOperationException("Disconnect the bound TIA project explicitly before creating a new offline project.");
    }

    private static string ReadInputHash(string inspection)
    {
        using var document = JsonDocument.Parse(inspection);
        if (!TryProperty(document.RootElement, "InputHash", out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Project generation inspection did not include an input hash.");
        var hash = value.GetString()!;
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Project generation inspection returned an invalid SHA-256 hash.");
        return hash.ToLowerInvariant();
    }

    private static string? CheckPublishedResult(string json, string outputDirectory)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!TryProperty(root, "Published", out var published) || published.ValueKind != JsonValueKind.True)
            return "The creation workflow did not publish a verified project; inspect its phase, compilation and verification reports.";
        if (!TryProperty(root, "Failures", out var failures) || failures.ValueKind != JsonValueKind.Array || failures.GetArrayLength() != 0 ||
            !TryProperty(root, "CleanupFailures", out var cleanup) || cleanup.ValueKind != JsonValueKind.Array || cleanup.GetArrayLength() != 0)
            return "The workflow reported failures or incomplete cleanup evidence.";
        if (!TryProperty(root, "Compile", out var compile) ||
            !TryProperty(compile, "AllowsPublication", out var compiled) || compiled.ValueKind != JsonValueKind.True)
            return "The workflow did not report complete successful compilation.";
        if (!TryProperty(root, "Verification", out var verification) ||
            !TryProperty(verification, "Verdict", out var verdict) || verdict.ValueKind != JsonValueKind.String ||
            !string.Equals(verdict.GetString(), "PASS", StringComparison.Ordinal))
            return "The workflow did not report independent verification PASS.";
        if (!TryProperty(root, "ProjectFile", out var file) || file.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(file.GetString()) || !Path.IsPathFullyQualified(file.GetString()!) ||
            !string.Equals(Path.GetExtension(file.GetString()), ".ap21", StringComparison.OrdinalIgnoreCase) ||
            !ProjectGenerationRequest.Contains(outputDirectory, Path.GetFullPath(file.GetString()!)) || !File.Exists(file.GetString()))
            return "The workflow did not return an existing V21 project file inside the previewed output directory.";
        return null;
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                { value = property.Value; return true; }
        value = default;
        return false;
    }
}
