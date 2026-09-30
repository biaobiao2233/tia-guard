namespace TiaGuard.Bridge.Host;

public sealed record GatewayApproval(string Id, string Summary, DateTimeOffset ExpiresAt);

public sealed class GatewayApprovalService
{
    private readonly bool _required;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _byToken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);

    public GatewayApprovalService(bool required)
    {
        _required = required;
    }

    public bool Required => _required;

    public GatewayApproval? Register(
        string token,
        string summary,
        DateTimeOffset expiresAt,
        string bindingHash,
        string requestHash,
        string stateHash)
    {
        if (!_required) return null;
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("A safety token is required.", nameof(token));
        var entry = new Entry
        {
            Id = Guid.NewGuid().ToString("N"),
            Token = token,
            Summary = string.IsNullOrWhiteSpace(summary) ? "修改当前工程的离线副本" : summary.Trim(),
            ExpiresAt = expiresAt,
            BindingHash = bindingHash,
            RequestHash = requestHash,
            StateHash = stateHash
        };
        lock (_gate)
        {
            _byToken[token] = entry;
            _byId[entry.Id] = entry;
        }
        return new GatewayApproval(entry.Id, entry.Summary, entry.ExpiresAt);
    }

    public async Task WaitAsync(string token, CancellationToken cancellationToken)
    {
        if (!_required) return;
        Entry entry;
        lock (_gate)
        {
            if (!_byToken.TryGetValue(token ?? string.Empty, out entry!))
                throw new InvalidOperationException("APPROVAL_REQUIRED");
        }
        if (DateTimeOffset.UtcNow > entry.ExpiresAt)
            throw new InvalidOperationException("APPROVAL_EXPIRED");
        var remaining = entry.ExpiresAt - DateTimeOffset.UtcNow;
        using var timeout = new CancellationTokenSource(remaining <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : remaining);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        bool allowed;
        try
        {
            allowed = await entry.Decision.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("APPROVAL_EXPIRED");
        }
        if (!allowed)
            throw new InvalidOperationException("APPROVAL_REJECTED");
        lock (_gate)
        {
            if (entry.Consumed)
                throw new InvalidOperationException("APPROVAL_USED");
            entry.Consumed = true;
        }
    }

    public void Allow(string id) => Decide(id, true);

    public void Reject(string id) => Decide(id, false);

    public bool Captures(string token, string bindingHash, string requestHash, string stateHash)
    {
        lock (_gate)
        {
            if (!_byToken.TryGetValue(token ?? string.Empty, out var entry)) return false;
            return string.Equals(entry.BindingHash, bindingHash, StringComparison.Ordinal) &&
                string.Equals(entry.RequestHash, requestHash, StringComparison.Ordinal) &&
                string.Equals(entry.StateHash, stateHash, StringComparison.Ordinal);
        }
    }

    public IReadOnlyList<GatewayApproval> Pending()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            return _byId.Values
                .Where(entry => !entry.Decided && entry.ExpiresAt >= now)
                .Select(entry => new GatewayApproval(entry.Id, entry.Summary, entry.ExpiresAt))
                .ToArray();
        }
    }

    private void Decide(string id, bool allow)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("APPROVAL_UNKNOWN");
        lock (_gate)
        {
            if (!_byId.TryGetValue(id, out var entry))
                throw new InvalidOperationException("APPROVAL_UNKNOWN");
            if (DateTimeOffset.UtcNow > entry.ExpiresAt)
                throw new InvalidOperationException("APPROVAL_EXPIRED");
            if (entry.Decided)
                throw new InvalidOperationException("APPROVAL_USED");
            entry.Decided = true;
            entry.Decision.TrySetResult(allow);
        }
    }

    private sealed class Entry
    {
        public string Id { get; init; } = string.Empty;
        public string Token { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; init; }
        public string BindingHash { get; init; } = string.Empty;
        public string RequestHash { get; init; } = string.Empty;
        public string StateHash { get; init; } = string.Empty;
        public bool Decided { get; set; }
        public bool Consumed { get; set; }
        public TaskCompletionSource<bool> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
