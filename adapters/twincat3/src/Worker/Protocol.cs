using System.Buffers;
using System.Text;
using System.Text.Json;

namespace TwinCat3.Worker;

public sealed record WorkerRequest(string Id, string Action, JsonElement Payload);

public static class WorkerProtocol
{
    public const int MaxBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal)
        { "hello", "doctor", "inspect", "preview", "authorize", "build-generation", "compile", "verify", "dispose" };

    public static WorkerRequest Parse(byte[] frame, string identity)
    {
        if (frame.Length == 0 || frame.Length > MaxBytes) throw new InvalidDataException("Invalid frame size.");
        using JsonDocument doc = JsonDocument.Parse(StrictUtf8.GetString(frame), new JsonDocumentOptions { MaxDepth = 64 });
        RequireUniqueProperties(doc.RootElement);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4 ||
            !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("identity", out var bind) || bind.ValueKind != JsonValueKind.String || bind.GetString() != identity ||
            !root.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid request envelope.");
        string requestId = id.GetString()!;
        if (requestId.Length is < 1 or > 128 || requestId.Any(char.IsControl) || !Actions.Contains(action.GetString()!))
            throw new InvalidDataException("Invalid action or request identity.");
        return new(requestId, action.GetString()!, payload.Clone());
    }

    private static void RequireUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON property.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RequireUniqueProperties(child);
    }

    public static async IAsyncEnumerable<byte[]> ReadFramesAsync(Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16384);
        using var frame = new MemoryStream();
        try
        {
            int count;
            while ((count = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) != 0)
            {
                int start = 0;
                for (int i = 0; i < count; i++)
                    if (buffer[i] == 10)
                    {
                        if (frame.Length + i - start > MaxBytes) throw new InvalidDataException("Frame limit exceeded.");
                        frame.Write(buffer, start, i - start);
                        yield return frame.ToArray(); frame.SetLength(0); start = i + 1;
                    }
                if (frame.Length + count - start > MaxBytes) throw new InvalidDataException("Frame limit exceeded.");
                frame.Write(buffer, start, count - start);
            }
            if (frame.Length != 0) throw new InvalidDataException("Truncated frame.");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
