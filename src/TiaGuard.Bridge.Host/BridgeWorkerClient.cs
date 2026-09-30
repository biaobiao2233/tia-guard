using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TiaGuard.Bridge.Host;

public sealed class BridgeWorkerClient : IDisposable
{
    private const string ProtocolVersion = "tia-guard.bridge.worker/v1";

    private readonly string _workerPath;
    private readonly bool _allowWrite;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private Process? _process;
    private Task? _stderrPump;
    private bool _handshakeComplete;
    private bool _disposed;
    private int _generation;

    public int Generation => _generation;

    public BridgeWorkerClient(
        string workerPath,
        bool allowWrite = false,
        TimeSpan? timeout = null)
    {
        _workerPath = Path.GetFullPath(workerPath);
        _allowWrite = allowWrite;
        _timeout = timeout ?? TimeSpan.FromMinutes(30);
    }

    public bool IsIdle => _gate.CurrentCount > 0;

    public async Task ShutdownAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_process is not { HasExited: false })
                return;
            if (_handshakeComplete)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    await ExchangeAsync(
                        "shutdown", null, null, null, null, null, null, null, null, null, timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
            try { _process.StandardInput.Close(); } catch (Exception) { }
            if (!_process.WaitForExit(8000))
                KillWorker();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> CallAsync(
        string method,
        int? processId = null,
        string? projectPath = null,
        string? tableName = null,
        string? tagName = null,
        string? dataType = null,
        string? logicalAddress = null,
        string? outputDirectory = null,
        string? outputName = null,
        string? payloadJson = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureProcess();

            if (!_handshakeComplete)
            {
                var hello = await ExchangeAsync(
                    "hello", null, null, null, null, null, null, null, null, null, cancellationToken)
                    .ConfigureAwait(false);
                if (!hello.Success)
                    throw BridgeWorkerException.From(hello);
                _handshakeComplete = true;
            }

            var response = await ExchangeAsync(
                method, processId, projectPath, tableName, tagName, dataType, logicalAddress,
                outputDirectory, outputName, payloadJson, cancellationToken).ConfigureAwait(false);
            if (!response.Success)
                throw BridgeWorkerException.From(response);

            return string.IsNullOrWhiteSpace(response.PayloadJson)
                ? "{}"
                : response.PayloadJson;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WorkerResponse> ExchangeAsync(
        string method,
        int? processId,
        string? projectPath,
        string? tableName,
        string? tagName,
        string? dataType,
        string? logicalAddress,
        string? outputDirectory,
        string? outputName,
        string? payloadJson,
        CancellationToken cancellationToken)
    {
        var process = _process ?? throw new InvalidOperationException("Worker process is unavailable.");
        var request = new WorkerRequest
        {
            ProtocolVersion = ProtocolVersion,
            Id = Guid.NewGuid().ToString("N"),
            Method = method,
            ProcessId = processId,
            ProjectPath = projectPath,
            TableName = tableName,
            TagName = tagName,
            DataType = dataType,
            LogicalAddress = logicalAddress,
            OutputDirectory = outputDirectory,
            OutputName = outputName,
            PayloadJson = payloadJson
        };

        var line = JsonSerializer.Serialize(request);
        try
        {
            await process.StandardInput.WriteLineAsync(line).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            KillWorker();
            throw;
        }

        var readTask = process.StandardOutput.ReadLineAsync();
        var timeoutTask = Task.Delay(_timeout, cancellationToken);
        var completed = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);
        if (completed != readTask)
        {
            KillWorker();
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"TIA-Guard Bridge worker did not respond within {_timeout.TotalSeconds:N0} seconds.");
        }

        var responseLine = await readTask.ConfigureAwait(false);
        if (responseLine is null)
        {
            KillWorker();
            throw new InvalidOperationException(
                "TIA-Guard Bridge worker exited without returning a response.");
        }

        var response = JsonSerializer.Deserialize<WorkerResponse>(responseLine, _json);
        if (response is null ||
            !string.Equals(response.ProtocolVersion, ProtocolVersion, StringComparison.Ordinal))
        {
            KillWorker();
            throw new InvalidOperationException(
                "TIA-Guard Bridge worker returned an invalid protocol response.");
        }

        return response;
    }

    private void EnsureProcess()
    {
        if (_process is { HasExited: false })
            return;

        KillWorker();

        if (!File.Exists(_workerPath))
            throw new FileNotFoundException(
                "TIA-Guard Bridge Openness worker was not found.", _workerPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = _workerPath,
            Arguments = _allowWrite ? "--allow-write" : string.Empty,
            WorkingDirectory = Path.GetDirectoryName(_workerPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the TIA-Guard Bridge worker.");
        _stderrPump = PumpStderrAsync(_process);
    }

    private static async Task PumpStderrAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    Console.Error.WriteLine("[tia-guard-worker] " + line);
            }
        }
        catch (Exception error) when (
            error is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private void KillWorker()
    {
        _handshakeComplete = false;
        _generation++;
        var process = _process;
        _process = null;
        _stderrPump = null;
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BridgeWorkerClient));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        KillWorker();
        _gate.Dispose();
    }

    private sealed class WorkerRequest
    {
        public string? ProtocolVersion { get; set; }
        public string? Id { get; set; }
        public string? Method { get; set; }
        public int? ProcessId { get; set; }
        public string? ProjectPath { get; set; }
        public string? TableName { get; set; }
        public string? TagName { get; set; }
        public string? DataType { get; set; }
        public string? LogicalAddress { get; set; }
        public string? OutputDirectory { get; set; }
        public string? OutputName { get; set; }
        public string? PayloadJson { get; set; }
    }

    internal sealed class WorkerResponse
    {
        public string? ProtocolVersion { get; set; }
        public string? Id { get; set; }
        public bool Success { get; set; }
        public string? PayloadJson { get; set; }
        public string? ErrorCategory { get; set; }
        public string? Error { get; set; }
    }

    public sealed class BridgeWorkerException : InvalidOperationException
    {
        public string Category { get; }

        private BridgeWorkerException(string category, string message)
            : base(message)
        {
            Category = category;
        }

        internal static BridgeWorkerException From(WorkerResponse response)
        {
            return new BridgeWorkerException(
                response.ErrorCategory ?? "worker_error",
                response.Error ?? "TIA-Guard Bridge worker operation failed.");
        }
    }
}
