using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TiaGuard.Gui
{
    public partial class MainWindow
    {
        private const int BridgeHttpPort = 18761;
        private Process _bridgeProcess;
        private readonly DispatcherTimer _bridgeTimer = new DispatcherTimer();
        private readonly HttpClient _bridgeHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        private CheckBox _bridgeGuarded;
        private TextBlock _bridgeStatus;
        private TextBlock _bridgeProject;
        private TextBlock _bridgeMode;
        private TextBlock _bridgeMcp;
        private TextBlock _bridgeHttpText;
        private TextBlock _bridgeAi;
        private Button _bridgeStartButton;

        private void InstallBridgeSurface()
        {
            var card = CreateProductCard(
                "TIA AI BRIDGE",
                "本地 AI 工程桥",
                "外部 AI 通过 MCP 或本机 HTTP 读取工程语义。默认只读。受保护编辑必须单独打开。",
                "SoftBrush");
            var body = (StackPanel)card.Child;
            _bridgeStatus = AddBridgeLine(body, "Status", "Stopped");
            _bridgeProject = AddBridgeLine(body, "Project", "Not bound");
            _bridgeMode = AddBridgeLine(body, "Mode", "Read-only");
            _bridgeMcp = AddBridgeLine(body, "MCP", "stdio");
            _bridgeHttpText = AddBridgeLine(body, "HTTP", "http://127.0.0.1:" + BridgeHttpPort);
            _bridgeAi = AddBridgeLine(body, "AI context", "Starts with the bridge");

            _bridgeGuarded = new CheckBox
            {
                Content = "Guarded Engineering Edit",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 8),
                IsChecked = false
            };
            _bridgeGuarded.Checked += delegate { UpdateBridgeModeLabel(); };
            _bridgeGuarded.Unchecked += delegate { UpdateBridgeModeLabel(); };
            body.Children.Add(_bridgeGuarded);
            body.Children.Add(new TextBlock
            {
                Text = "默认 Read-only。勾选后，下一次启动才允许 preview/apply 受保护工程补丁。不会下载、启停或强制 PLC。",
                FontSize = 11,
                Foreground = ResourceBrush("MutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var buttons = new WrapPanel();
            _bridgeStartButton = CreatePrimaryButton("启动 Bridge", "YellowBrush", OnStartBridgeClick);
            _bridgeStartButton.Margin = new Thickness(0, 0, 8, 8);
            var stop = CreatePrimaryButton("停止", "RedBrush", OnStopBridgeClick);
            stop.Margin = new Thickness(0, 0, 8, 8);
            var copyMcp = CreatePrimaryButton("Copy MCP config", "CyanBrush", OnCopyMcpConfigClick);
            copyMcp.Margin = new Thickness(0, 0, 8, 8);
            var copyHttp = CreatePrimaryButton("Copy HTTP endpoint", "CyanBrush", OnCopyHttpEndpointClick);
            copyHttp.Margin = new Thickness(0, 0, 8, 8);
            buttons.Children.Add(_bridgeStartButton);
            buttons.Children.Add(stop);
            buttons.Children.Add(copyMcp);
            buttons.Children.Add(copyHttp);
            body.Children.Add(buttons);
            ActionsPanel.Children.Add(card);

            _bridgeTimer.Interval = TimeSpan.FromSeconds(2);
            _bridgeTimer.Tick += async delegate { await RefreshBridgeStatusAsync(); };
            Closed += delegate
            {
                StopBridgeProcess();
                _bridgeHttp.Dispose();
            };
            UpdateBridgeModeLabel();
        }

        private TextBlock AddBridgeLine(Panel parent, string label, string value)
        {
            var line = new TextBlock
            {
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            line.Text = label + "  " + value;
            parent.Children.Add(line);
            return line;
        }

        private void SetBridgeLine(TextBlock line, string label, string value)
        {
            if (line != null) line.Text = label + "  " + value;
        }

        private bool BridgeGuarded { get { return _bridgeGuarded != null && _bridgeGuarded.IsChecked == true; } }

        private void UpdateBridgeModeLabel()
        {
            SetBridgeLine(_bridgeMode, "Mode", BridgeGuarded ? "Guarded Engineering Edit" : "Read-only");
        }

        private void OnStartBridgeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_bridgeProcess != null && !_bridgeProcess.HasExited)
                {
                    AppendLog("TIA AI Bridge is already running.");
                    return;
                }
                var exe = FindBridgeExecutable();
                if (exe == null)
                {
                    SetBridgeLine(_bridgeStatus, "Status", "Bridge package not found");
                    AppendLog("tia-guard-bridge.exe was not found. Package the bridge or set TIA_GUARD_BRIDGE.");
                    return;
                }
                var args = "--transport http --port " + BridgeHttpPort;
                if (BridgeGuarded) args += " --allow-write";
                var start = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                _bridgeProcess = Process.Start(start);
                if (_bridgeProcess == null)
                    throw new InvalidOperationException("The bridge process did not start.");
                _bridgeProcess.ErrorDataReceived += (ignored, data) =>
                {
                    if (!string.IsNullOrWhiteSpace(data.Data))
                        Dispatcher.BeginInvoke(new Action(() => AppendLog(data.Data)));
                };
                _bridgeProcess.BeginErrorReadLine();
                _bridgeGuarded.IsEnabled = false;
                SetBridgeLine(_bridgeStatus, "Status", "Running");
                SetBridgeLine(_bridgeHttpText, "HTTP", "http://127.0.0.1:" + BridgeHttpPort);
                SetBridgeLine(_bridgeAi, "AI context", "Waiting for a bound project");
                _bridgeTimer.Start();
                AppendLog("TIA AI Bridge started (" + (BridgeGuarded ? "guarded edit" : "read-only") + ").");
            }
            catch (Exception error)
            {
                SetBridgeLine(_bridgeStatus, "Status", "Failed to start");
                AppendLog("TIA AI Bridge start failed: " + error.Message);
            }
        }

        private void OnStopBridgeClick(object sender, RoutedEventArgs e)
        {
            StopBridgeProcess();
            SetBridgeLine(_bridgeStatus, "Status", "Stopped");
            SetBridgeLine(_bridgeProject, "Project", "Not bound");
            SetBridgeLine(_bridgeAi, "AI context", "Stopped");
            if (_bridgeGuarded != null) _bridgeGuarded.IsEnabled = true;
            AppendLog("TIA AI Bridge stopped.");
        }

        private void StopBridgeProcess()
        {
            _bridgeTimer.Stop();
            var process = _bridgeProcess;
            _bridgeProcess = null;
            if (process == null) return;
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        private void OnCopyMcpConfigClick(object sender, RoutedEventArgs e)
        {
            var exe = FindBridgeExecutable() ?? "tia-guard-bridge.exe";
            var guarded = BridgeGuarded ? ", \"--allow-write\"" : string.Empty;
            var json = "{\n  \"mcpServers\": {\n    \"tia-guard\": {\n      \"command\": " +
                JsonString(exe) + ",\n      \"args\": [\"--transport\", \"stdio\"" + guarded + "]\n    }\n  }\n}";
            Clipboard.SetText(json);
            AppendLog("Copied MCP config.");
        }

        private void OnCopyHttpEndpointClick(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText("http://127.0.0.1:" + BridgeHttpPort);
            AppendLog("Copied HTTP endpoint.");
        }

        private async System.Threading.Tasks.Task RefreshBridgeStatusAsync()
        {
            if (_bridgeProcess == null || _bridgeProcess.HasExited)
            {
                SetBridgeLine(_bridgeStatus, "Status", "Stopped");
                _bridgeTimer.Stop();
                if (_bridgeGuarded != null) _bridgeGuarded.IsEnabled = true;
                return;
            }
            try
            {
                var health = await _bridgeHttp.GetStringAsync("http://127.0.0.1:" + BridgeHttpPort + "/health");
                var mode = health.IndexOf("read-write", StringComparison.Ordinal) >= 0
                    ? "Guarded Engineering Edit" : "Read-only";
                SetBridgeLine(_bridgeStatus, "Status", "Running");
                SetBridgeLine(_bridgeMode, "Mode", mode);
                SetBridgeLine(_bridgeMcp, "MCP", "stdio config can be copied");
                SetBridgeLine(_bridgeHttpText, "HTTP", "http://127.0.0.1:" + BridgeHttpPort);
                var state = await _bridgeHttp.GetStringAsync("http://127.0.0.1:" + BridgeHttpPort + "/api/v1/state");
                if (state.IndexOf("\"connected\":true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    state.IndexOf("\"Connected\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SetBridgeLine(_bridgeProject, "Project", "Bound");
                    SetBridgeLine(_bridgeAi, "AI context", "Ready");
                }
                else
                {
                    SetBridgeLine(_bridgeProject, "Project", "Not bound");
                    SetBridgeLine(_bridgeAi, "AI context", "Waiting for a project");
                }
            }
            catch (Exception)
            {
                SetBridgeLine(_bridgeStatus, "Status", "Starting");
            }
        }

        private static string FindBridgeExecutable()
        {
            var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge", "tia-guard-bridge.exe");
            if (File.Exists(bundled)) return Path.GetFullPath(bundled);
            var direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tia-guard-bridge.exe");
            if (File.Exists(direct)) return direct;
            var overridePath = Environment.GetEnvironmentVariable("TIA_GUARD_BRIDGE");
            if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
                return Path.GetFullPath(overridePath);
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                var packaged = Path.Combine(
                    dir.FullName, "artifacts", "release",
                    "tia-guard-bridge-v0.1.0-windows-x64", "tia-guard-bridge.exe");
                if (File.Exists(packaged)) return packaged;
                var built = Path.Combine(
                    dir.FullName, "src", "TiaGuard.Bridge.Host", "bin", "Release",
                    "net8.0", "win-x64", "tia-guard-bridge.exe");
                if (File.Exists(built)) return built;
                dir = dir.Parent;
            }
            return null;
        }

        private static string JsonString(string value)
        {
            var builder = new StringBuilder("\"");
            foreach (var ch in value ?? string.Empty)
            {
                if (ch == '\\' || ch == '"') builder.Append('\\');
                builder.Append(ch);
            }
            builder.Append('"');
            return builder.ToString();
        }
    }
}
