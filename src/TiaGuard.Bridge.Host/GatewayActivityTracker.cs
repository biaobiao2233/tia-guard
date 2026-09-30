namespace TiaGuard.Bridge.Host;

public sealed class GatewayActivityTracker
{
    private readonly object _gate = new();
    private readonly TimeSpan _connectedWindow;
    private readonly Func<DateTimeOffset> _now;
    private int _active;
    private DateTimeOffset? _lastSeen;
    private string? _client;
    private string _category = "idle";

    public GatewayActivityTracker(TimeSpan? connectedWindow = null, Func<DateTimeOffset>? now = null)
    {
        _connectedWindow = connectedWindow ?? TimeSpan.FromSeconds(20);
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public static bool IsOperatorPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return true;
        if (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase)) return true;
        return path.StartsWith("/api/v1/gateway", StringComparison.OrdinalIgnoreCase);
    }

    public IDisposable Begin(string path, string? userAgent)
    {
        if (IsOperatorPath(path)) return EmptyScope.Instance;
        Interlocked.Increment(ref _active);
        lock (_gate)
        {
            _lastSeen = _now();
            _client = Trim(userAgent);
            _category = Category(path);
        }
        return new Scope(this);
    }

    public GatewayActivity Read()
    {
        var active = Volatile.Read(ref _active);
        DateTimeOffset? last;
        string? client;
        string category;
        lock (_gate)
        {
            last = _lastSeen;
            client = _client;
            category = _category;
        }
        var recent = last.HasValue && _now() - last.Value <= _connectedWindow;
        var connected = active > 0 || recent;
        var state = active > 0 ? "working" : connected ? "connected" : "waiting";
        return new GatewayActivity(connected, active, last, state, client, "http", active > 0 || recent ? category : "idle");
    }

    private static string Category(string path)
    {
        if (path.Contains("/patches", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/tags/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/publish", StringComparison.OrdinalIgnoreCase))
            return "write";
        if (path.Contains("/open-offline", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/disconnect", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/connect", StringComparison.OrdinalIgnoreCase))
            return "project";
        if (string.Equals(path, "/capabilities", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "/openapi.json", StringComparison.OrdinalIgnoreCase))
            return "discovery";
        return "read";
    }

    private static string? Trim(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;
        var value = userAgent.Trim();
        return value.Length <= 80 ? value : value.Substring(0, 80);
    }

    private void End() => Interlocked.Decrement(ref _active);

    private sealed class Scope : IDisposable
    {
        private GatewayActivityTracker? _owner;
        public Scope(GatewayActivityTracker owner) => _owner = owner;
        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.End();
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();
        public void Dispose() { }
    }
}

public sealed record GatewayActivity(
    bool Connected,
    int ActiveRequests,
    DateTimeOffset? LastSeenUtc,
    string State,
    string? Client,
    string Transport,
    string Category);
