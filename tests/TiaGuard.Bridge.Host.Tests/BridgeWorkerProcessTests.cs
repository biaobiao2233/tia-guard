using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TiaGuard.Bridge.Host;
using Xunit;

public sealed class BridgeWorkerProcessTests
{
    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("space here", "\"space here\"")]
    [InlineData("C:\\path with spaces\\", "\"C:\\path with spaces\\\\\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    public void Windows_arguments_preserve_empty_spaces_quotes_and_trailing_backslashes(string value, string expected) =>
        Assert.Equal(expected, BridgeWorkerProcess.QuoteArgument(value));

    [Fact]
    public async Task Redirected_worker_preserves_unicode_environment_and_parent_identity_and_exits_on_eof()
    {
        if (!OperatingSystem.IsWindows()) return;
        var token = "中文-" + Guid.NewGuid().ToString("N");
        var directory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            WorkingDirectory = directory,
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "[Console]::InputEncoding = New-Object Text.UTF8Encoding($false); [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false); " +
            "$line = [Console]::ReadLine(); [Console]::Error.WriteLine('worker-stderr'); " +
            "[Console]::WriteLine((@{ line=$line; inherited=$env:TiaGuard_Launch_Test; cwd=[Environment]::CurrentDirectory; pid=$PID } | ConvertTo-Json -Compress)); " +
            "while ($null -ne [Console]::ReadLine()) {}" }) info.ArgumentList.Add(argument);
        info.Environment["TiaGuard_Launch_Test"] = token;
        using var worker = BridgeWorkerProcess.Start(info);
        try
        {
            var started = worker.Process.StartTime.ToUniversalTime();
            await worker.StandardInput.WriteLineAsync(token);
            var line = await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            using var json = JsonDocument.Parse(line!);
            Assert.Equal(token, json.RootElement.GetProperty("line").GetString());
            Assert.Equal(token, json.RootElement.GetProperty("inherited").GetString());
            Assert.Equal(directory, json.RootElement.GetProperty("cwd").GetString(), ignoreCase: true);
            Assert.Equal(worker.Process.Id, json.RootElement.GetProperty("pid").GetInt32());
            Assert.Equal("worker-stderr", await worker.StandardError.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.False(worker.HasExited);
            Assert.Equal(started, worker.Process.StartTime.ToUniversalTime());
            worker.StandardInput.Close();
            Assert.True(worker.WaitForExit(10000));
        }
        finally { if (!worker.HasExited) worker.Kill(entireProcessTree: true); }
    }
}
