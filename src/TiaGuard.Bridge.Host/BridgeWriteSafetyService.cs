using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TiaGuard.Bridge.Host;

public sealed class TagUpsertRequest
{
    public string TableName { get; init; } = string.Empty;
    public string TagName { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string LogicalAddress { get; init; } = string.Empty;

    public void Validate()
    {
        RequireName(TableName, nameof(TableName));
        RequireName(TagName, nameof(TagName));
        RequireName(DataType, nameof(DataType));
        if (LogicalAddress is null)
            throw new ArgumentException("LogicalAddress is required.", nameof(LogicalAddress));
    }

    private static void RequireName(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(field + " is required.", field);
        if (value.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException(
                "Bridge v0 accepts root tag-table and tag names, not paths.", field);
    }
}

public sealed class ProjectPublishRequest
{
    public string OutputDirectory { get; init; } = string.Empty;
    public string OutputName { get; init; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(OutputDirectory))
            throw new ArgumentException(
                "OutputDirectory is required.", nameof(OutputDirectory));
        if (!Path.IsPathFullyQualified(OutputDirectory))
            throw new ArgumentException(
                "OutputDirectory must be an absolute path.", nameof(OutputDirectory));
        if (!Directory.Exists(OutputDirectory))
            throw new DirectoryNotFoundException(
                "OutputDirectory does not exist: " + OutputDirectory);

        if (string.IsNullOrWhiteSpace(OutputName) ||
            OutputName == "." || OutputName == ".." ||
            OutputName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            OutputName.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
            OutputName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            throw new ArgumentException(
                "OutputName must be one safe new directory name.", nameof(OutputName));

        var destination = Path.GetFullPath(
            Path.Combine(Path.GetFullPath(OutputDirectory), OutputName));
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new InvalidOperationException(
                "Publish destination already exists: " + destination);
    }
    public void ValidateProjectName(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            throw new InvalidOperationException(
                "The bound TIA project name is unavailable.");
        if (!string.Equals(OutputName, projectName, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Bridge publish preserves the TIA project identity. OutputName must exactly " +
                "match the currently bound project name: " + projectName);
    }
}

public sealed class BridgeWritePreview
{
    public string Operation { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string TargetBindingHash { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public string CurrentStateHash { get; init; } = string.Empty;
    public string SafetyToken { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; init; }
    public bool SavesProject { get; init; }
}

public sealed class BridgeWriteApplyResult
{
    public string Status { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public bool SavedProject { get; init; }
    public JsonElement VerifiedTagState { get; init; }
}

public sealed class BridgePublishApplyResult
{
    public string Status { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public bool SavedProject { get; init; }
    public string ProjectFile { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string VerifiedContentId { get; init; } = string.Empty;
    public int CompileErrors { get; init; }
    public int CompileWarnings { get; init; }
}

public sealed class BridgeWriteSafetyService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Ticket> _tickets = new(StringComparer.Ordinal);
    private readonly TimeSpan _ttl;

    public BridgeWriteSafetyService(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromMinutes(10);
    }

    public BridgeWritePreview CreatePreview(
        string operation,
        string summary,
        string targetIdentity,
        string requestJson,
        string currentStateJson,
        bool savesProject = false)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var ticket = new Ticket
        {
            Operation = operation,
            TargetBindingHash = Hash(targetIdentity),
            RequestHash = Hash(requestJson),
            CurrentStateHash = Hash(currentStateJson),
            ExpiresAt = now + _ttl
        };

        lock (_gate)
        {
            PruneExpired(now);
            _tickets[token] = ticket;
        }

        return new BridgeWritePreview
        {
            Operation = operation,
            Summary = summary,
            TargetBindingHash = ticket.TargetBindingHash,
            RequestHash = ticket.RequestHash,
            CurrentStateHash = ticket.CurrentStateHash,
            SafetyToken = token,
            ExpiresAt = ticket.ExpiresAt,
            SavesProject = savesProject
        };
    }

    public void ValidateAndConsume(
        string token,
        string operation,
        string targetIdentity,
        string requestJson,
        string currentStateJson)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("A safetyToken from preview is required.");

        Ticket ticket;
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            PruneExpired(now);
            if (!_tickets.Remove(token, out ticket!))
                throw new InvalidOperationException(
                    "The safetyToken is missing, expired, unknown, or already used.");
        }

        if (!string.Equals(ticket.Operation, operation, StringComparison.Ordinal))
            throw new InvalidOperationException("The safetyToken is bound to a different operation.");
        if (!FixedEquals(ticket.TargetBindingHash, Hash(targetIdentity)))
            throw new InvalidOperationException(
                "The bound TIA project changed after preview. Request a new preview.");
        if (!FixedEquals(ticket.RequestHash, Hash(requestJson)))
            throw new InvalidOperationException(
                "The requested write differs from the preview. Request a new preview.");
        if (!FixedEquals(ticket.CurrentStateHash, Hash(currentStateJson)))
            throw new InvalidOperationException(
                "The target tag state changed after preview. Request a new preview.");
        if (DateTimeOffset.UtcNow > ticket.ExpiresAt)
            throw new InvalidOperationException("The safetyToken expired. Request a new preview.");
    }

    public static string SerializeRequest(TagUpsertRequest request)
        => JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

    public static string SerializeRequest(ProjectPublishRequest request)
        => JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool FixedEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var token in _tickets
                     .Where(pair => pair.Value.ExpiresAt < now)
                     .Select(pair => pair.Key)
                     .ToArray())
            _tickets.Remove(token);
    }

    private sealed class Ticket
    {
        public string Operation { get; init; } = string.Empty;
        public string TargetBindingHash { get; init; } = string.Empty;
        public string RequestHash { get; init; } = string.Empty;
        public string CurrentStateHash { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; init; }
    }
}

public sealed class BridgeTagWriteCoordinator
{
    private const string Operation = "upsert_tag";
    private readonly BridgeWorkerClient _worker;
    private readonly BridgeWriteSafetyService _safety;

    public BridgeTagWriteCoordinator(
        BridgeWorkerClient worker,
        BridgeWriteSafetyService safety)
    {
        _worker = worker;
        _safety = safety;
    }

    public async Task<BridgeWritePreview> PreviewAsync(
        TagUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        var targetIdentity = await ReadOfflineTargetIdentityAsync(cancellationToken)
            .ConfigureAwait(false);
        var currentState = await ReadTagStateAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var requestJson = BridgeWriteSafetyService.SerializeRequest(request);

        return _safety.CreatePreview(
            Operation,
            $"Upsert tag '{request.TagName}' in root table '{request.TableName}' "
            + $"as {request.DataType} at '{request.LogicalAddress}'. "
            + "The change affects only the disposable offline copy and is not saved.",
            targetIdentity,
            requestJson,
            currentState);
    }

    public async Task<BridgeWriteApplyResult> ApplyAsync(
        TagUpsertRequest request,
        string safetyToken,
        CancellationToken cancellationToken = default)
    {
        request.Validate();
        var targetIdentity = await ReadOfflineTargetIdentityAsync(cancellationToken)
            .ConfigureAwait(false);
        var currentState = await ReadTagStateAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var requestJson = BridgeWriteSafetyService.SerializeRequest(request);

        _safety.ValidateAndConsume(
            safetyToken,
            Operation,
            targetIdentity,
            requestJson,
            currentState);

        await _worker.CallAsync(
            "upsert_tag",
            tableName: request.TableName,
            tagName: request.TagName,
            dataType: request.DataType,
            logicalAddress: request.LogicalAddress,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var verifiedJson = await ReadTagStateAsync(request, cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(verifiedJson);
        VerifyAppliedState(document.RootElement, request);

        return new BridgeWriteApplyResult
        {
            Status = "applied-in-memory",
            Operation = Operation,
            SavedProject = false,
            VerifiedTagState = document.RootElement.Clone()
        };
    }

    private async Task<string> ReadOfflineTargetIdentityAsync(
        CancellationToken cancellationToken)
    {
        var stateJson = await _worker.CallAsync(
            "get_state",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(stateJson);
        var root = document.RootElement;
        if (!root.TryGetProperty("connected", out var connected) ||
            connected.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException(
                "No TIA project is bound. Open an offline .ap21 copy before previewing a write.");

        if (!root.TryGetProperty("project", out var project) ||
            project.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("The bound project identity is unavailable.");

        var sourceKind = GetString(project, "SourceKind");
        if (!string.Equals(sourceKind, "offline-copy", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Bridge v0 engineering writes are restricted to disposable offline project copies.");

        return string.Join("|",
            GetString(project, "Name"),
            GetString(project, "Path"),
            GetString(project, "TiaVersion"),
            GetString(project, "TiaBuild"),
            sourceKind,
            GetNullableInt(project, "ProcessId"));
    }

    private Task<string> ReadTagStateAsync(
        TagUpsertRequest request,
        CancellationToken cancellationToken)
        => _worker.CallAsync(
            "get_tag_state",
            tableName: request.TableName,
            tagName: request.TagName,
            cancellationToken: cancellationToken);

    private static void VerifyAppliedState(
        JsonElement state,
        TagUpsertRequest request)
    {
        if (!state.TryGetProperty("Exists", out var exists) ||
            exists.ValueKind != JsonValueKind.True ||
            !string.Equals(GetString(state, "TableName"), request.TableName,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(state, "Name"), request.TagName,
                StringComparison.Ordinal) ||
            !string.Equals(GetString(state, "DataType"), request.DataType,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(state, "LogicalAddress"), request.LogicalAddress,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Tag apply returned without the requested state. The write is not accepted as verified.");
    }

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string GetNullableInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var number)
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
}
