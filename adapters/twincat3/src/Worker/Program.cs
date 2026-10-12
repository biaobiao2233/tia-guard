using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace TwinCat3.Worker;

public static class Program
{
    public static async Task<int> Main()
    {
        WorkerBackend? backend = null;
        int exit = 0;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var configuration = WorkerConfiguration.FromEnvironment();
            backend = await WorkerBackend.CreateAsync(configuration).ConfigureAwait(false);
            using var connection = new NamedPipeServerStream(configuration.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 16384, 16384);
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, args) => { args.Cancel = true; cancellation.Cancel(); };
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            startup.CancelAfter(TimeSpan.FromSeconds(30));
            await connection.WaitForConnectionAsync(startup.Token).ConfigureAwait(false);
            var requests = Channel.CreateBounded<WorkerRequest>(new BoundedChannelOptions(2)
                { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
            var ids = new HashSet<string>(StringComparer.Ordinal);
            bool disposedNormally = false;
            Task reader = ReadRequestsAsync();
            try
            {
                bool hello = false;
                await foreach (var request in requests.Reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
                {
                    JsonObject result;
                    if (!hello)
                    {
                        if (request.Action != "hello" || request.Payload.EnumerateObject().Any())
                            throw new InvalidDataException("Hello is required before operations.");
                        result = backend.Hello(); hello = true;
                    }
                    else
                    {
                        if (request.Action == "hello") throw new InvalidDataException("Hello cannot rebind a session.");
                        result = await backend.DispatchAsync(request.Action, request.Payload, cancellation.Token).ConfigureAwait(false);
                    }
                    var response = new JsonObject { ["id"] = request.Id, ["ok"] = true, ["result"] = result };
                    byte[] bytes = Encoding.UTF8.GetBytes(response.ToJsonString() + "\n");
                    if (bytes.Length > WorkerProtocol.MaxBytes) throw new InvalidDataException("Response frame limit exceeded.");
                    await connection.WriteAsync(bytes, cancellation.Token).ConfigureAwait(false);
                    await connection.FlushAsync(cancellation.Token).ConfigureAwait(false);
                    if (request.Action == "dispose") { disposedNormally = true; break; }
                }
            }
            finally
            {
                cancellation.Cancel();
                try { await reader.ConfigureAwait(false); }
                catch (Exception) when (disposedNormally) { }
            }

            async Task ReadRequestsAsync()
            {
                try
                {
                    await foreach (var frame in WorkerProtocol.ReadFramesAsync(connection, cancellation.Token).ConfigureAwait(false))
                    {
                        var request = WorkerProtocol.Parse(frame, configuration.Identity);
                        if (ids.Count >= 65536 || !ids.Add(request.Id)) throw new InvalidDataException("Duplicate request or session limit.");
                        await requests.Writer.WriteAsync(request, cancellation.Token).ConfigureAwait(false);
                    }
                    // EOF cancels a pending native operation and prevents publication after disconnect.
                    cancellation.Cancel(); requests.Writer.TryComplete();
                }
                catch (Exception exception)
                {
                    requests.Writer.TryComplete(exception); cancellation.Cancel(); throw;
                }
            }
        }
        catch (OperationCanceledException) { exit = 1; WriteError("connection-ended-indeterminate"); }
        catch { exit = 1; WriteError("worker-configuration-or-protocol-failed"); }
        finally
        {
            if (backend is not null)
                try { await backend.DisposeAsync().ConfigureAwait(false); }
                catch { exit = 2; WriteError("owned-session-cleanup-failed"); }
        }
        return exit;
    }

    private static void WriteError(string code) => Console.Error.WriteLine(JsonSerializer.Serialize(new
        { schemaVersion = "1.0", operation = "dispose", phase = "cleanup", success = false, failures = new[] { code } }));
}
