using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace TiaGuard.Gui
{
    public partial class MainWindow
    {
        private const int BridgeHttpPort = 18761;
        private static readonly string GatewayRoot = "http://127.0.0.1:" + BridgeHttpPort;
        private Process _bridgeProcess;
        private readonly DispatcherTimer _bridgeTimer = new DispatcherTimer();
        private readonly WebClient _bridgeHttp = CreateBridgeClient();
        private readonly string _approvalKey = CreateApprovalKey();
        private bool _ownsBridge;
        private bool _gatewayReady;
        private string _openApprovalId;
        private GatewayApprovalWindow _approvalWindow;
        private TextBlock _gatewayLine;
        private TextBlock _aiLine;
        private TextBlock _tiaLine;
        private TextBlock _projectLine;
        private TextBlock _gatewayHint;

        private void InstallBridgeSurface()
        {
            var card = CreateProductCard(
                "AI GATEWAY",
                "本地 AI 工程入口",
                "打开后即可让本机 AI 读取并申请修改当前工程。",
                "SoftBrush");
            var body = (StackPanel)card.Child;
            _gatewayLine = AddBridgeLine(body, "Gateway", "正在启动");
            _aiLine = AddBridgeLine(body, "AI", "等待连接");
            _tiaLine = AddBridgeLine(body, "TIA", "未连接");
            _projectLine = AddBridgeLine(body, "Project", "未绑定");
            _gatewayHint = new TextBlock
            {
                Text = "AI 可直接操作当前工程",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            body.Children.Add(_gatewayHint);
            ActionsPanel.Children.Add(card);

            _bridgeTimer.Interval = TimeSpan.FromSeconds(2);
            _bridgeTimer.Tick += async delegate { await RefreshBridgeStatusAsync(); };
            Closing += delegate { ShutdownOwnedGateway(); };
            Closed += delegate { _bridgeHttp.Dispose(); };
            Dispatcher.BeginInvoke(new Action(StartGateway));
        }

        private TextBlock AddBridgeLine(Panel parent, string label, string value)
        {
            var line = new TextBlock
            {
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            SetBridgeLine(line, label, value, "MutedBrush");
            parent.Children.Add(line);
            return line;
        }

        private void SetBridgeLine(TextBlock line, string label, string value, string brushKey)
        {
            if (line == null) return;
            line.Inlines.Clear();
            line.Inlines.Add(new Run("● ") { Foreground = ResourceBrush(brushKey), FontWeight = FontWeights.Bold });
            line.Inlines.Add(new Run(label + "   " + value));
        }

        private void StartGateway()
        {
            try
            {
                SetBridgeLine(_gatewayLine, "Gateway", "正在启动", "YellowBrush");
                if (ProbeHealth())
                {
                    _ownsBridge = false;
                    _gatewayReady = true;
                    _bridgeTimer.Start();
                    return;
                }
                var exe = FindBridgeExecutable();
                if (exe == null)
                {
                    SetBridgeLine(_gatewayLine, "Gateway", "未找到", "RedBrush");
                    _gatewayHint.Text = "安装包里没有 AI Gateway。";
                    return;
                }
                if (ProbeHealth())
                {
                    _ownsBridge = false;
                    _gatewayReady = true;
                    _bridgeTimer.Start();
                    return;
                }
                var start = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--transport http --port " + BridgeHttpPort,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                start.EnvironmentVariables["TIA_GUARD_APPROVAL_KEY"] = _approvalKey;
                _bridgeProcess = Process.Start(start);
                if (_bridgeProcess == null)
                    throw new InvalidOperationException("AI Gateway 没有启动。");
                _ownsBridge = true;
                _bridgeProcess.ErrorDataReceived += (ignored, data) =>
                {
                    if (!string.IsNullOrWhiteSpace(data.Data))
                        Dispatcher.BeginInvoke(new Action(() => AppendLog(data.Data)));
                };
                _bridgeProcess.BeginErrorReadLine();
                _bridgeTimer.Start();
            }
            catch (Exception error)
            {
                SetBridgeLine(_gatewayLine, "Gateway", "错误", "RedBrush");
                _gatewayHint.Text = error.Message;
                AppendLog("AI Gateway 启动失败: " + error.Message);
            }
        }

        private bool ProbeHealth()
        {
            try
            {
                var health = _bridgeHttp.DownloadString(GatewayRoot + "/health");
                return health != null && health.IndexOf("\"status\":\"ok\"", StringComparison.Ordinal) >= 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ShutdownOwnedGateway()
        {
            _bridgeTimer.Stop();
            if (_approvalWindow != null)
            {
                try { _approvalWindow.Close(); } catch (Exception) { }
                _approvalWindow = null;
            }
            if (!_ownsBridge) return;
            try
            {
                using (var shutdown = new WebClient())
                {
                    shutdown.Headers["X-TiaGuard-Approval-Key"] = _approvalKey;
                    shutdown.UploadString(GatewayRoot + "/api/v1/gateway/shutdown", "POST", "");
                }
            }
            catch (Exception)
            {
            }
            var process = _bridgeProcess;
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                    process.WaitForExit(15000);
                if (!process.HasExited)
                    TaskKillTree(process.Id);
            }
            catch (Exception)
            {
            }
        }

        private static void TaskKillTree(int processId)
        {
            using (var killer = Process.Start(new ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = "/PID " + processId + " /T /F",
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                if (killer != null) killer.WaitForExit(8000);
            }
        }

        private async System.Threading.Tasks.Task RefreshBridgeStatusAsync()
        {
            try
            {
                var body = await _bridgeHttp.DownloadStringTaskAsync(GatewayRoot + "/api/v1/gateway/status");
                _gatewayReady = true;
                ApplyGatewayStatus(body);
            }
            catch (Exception)
            {
                if (_ownsBridge && _bridgeProcess != null && _bridgeProcess.HasExited)
                {
                    SetBridgeLine(_gatewayLine, "Gateway", "错误", "RedBrush");
                    _gatewayHint.Text = "AI Gateway 已退出。";
                    _bridgeTimer.Stop();
                    return;
                }
                SetBridgeLine(_gatewayLine, "Gateway", _gatewayReady ? "错误" : "正在启动", _gatewayReady ? "RedBrush" : "YellowBrush");
            }
        }

        private void ApplyGatewayStatus(string json)
        {
            SetBridgeLine(_gatewayLine, "Gateway", "就绪", "GreenBrush");
            var ai = Slice(json, "state");
            var active = SliceNumber(json, "activeRequests");
            if (active > 0 || ai == "working")
                SetBridgeLine(_aiLine, "AI", "正在操作", "CyanBrush");
            else if (ai == "connected")
                SetBridgeLine(_aiLine, "AI", "已连接", "GreenBrush");
            else
            {
                var ago = IdleText(json);
                SetBridgeLine(_aiLine, "AI", ago ?? "等待连接", "MutedBrush");
            }
            var tia = System.Text.RegularExpressions.Regex.IsMatch(
                json, "\"tia\"\\s*:\\s*\\{[^}]*\"connected\"\\s*:\\s*true");
            SetBridgeLine(_tiaLine, "TIA", tia ? "已连接" : "未连接", tia ? "GreenBrush" : "MutedBrush");
            var project = ProjectName(json);
            SetBridgeLine(_projectLine, "Project", string.IsNullOrWhiteSpace(project) ? "未绑定" : project, tia ? "InkBrush" : "MutedBrush");
            var approvalId = ApprovalId(json);
            var summary = ApprovalSummary(json);
            if (!string.IsNullOrWhiteSpace(approvalId))
            {
                _gatewayHint.Text = "AI 想修改当前工程的离线副本";
                ShowApproval(approvalId, summary);
            }
            else
            {
                _openApprovalId = null;
                _gatewayHint.Text = "AI 可直接操作当前工程";
            }
        }

        private void ShowApproval(string id, string summary)
        {
            if (_approvalWindow != null && _openApprovalId == id) return;
            if (_approvalWindow != null)
            {
                try { _approvalWindow.Close(); } catch (Exception) { }
            }
            _openApprovalId = id;
            _approvalWindow = new GatewayApprovalWindow(summary, delegate { DecideApproval(id, "allow"); }, delegate { DecideApproval(id, "reject"); });
            _approvalWindow.Closed += delegate
            {
                if (_openApprovalId == id) _approvalWindow = null;
            };
            _approvalWindow.Show();
        }

        private void DecideApproval(string id, string action)
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers["X-TiaGuard-Approval-Key"] = _approvalKey;
                    client.UploadString(GatewayRoot + "/api/v1/gateway/approvals/" + id + "/" + action, "POST", "");
                }
            }
            catch (Exception error)
            {
                AppendLog("确认修改失败: " + error.Message);
            }
        }

        private static string Slice(string json, string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static int SliceNumber(string json, string name)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, "\"" + name + "\"\\s*:\\s*(\\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private static string ProjectName(string json)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, "\"project\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ApprovalId(string json)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, "\"pendingApprovals\"\\s*:\\s*\\[\\s*\\{[^}]*\"id\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ApprovalSummary(string json)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, "\"pendingApprovals\"\\s*:\\s*\\[\\s*\\{[^}]*\"summary\"\\s*:\\s*\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : "修改当前工程的离线副本";
        }

        private static string IdleText(string json)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json, "\"lastSeenUtc\"\\s*:\\s*\"([^\"]+)\"");
            if (!match.Success) return null;
            DateTimeOffset seen;
            if (!DateTimeOffset.TryParse(match.Groups[1].Value, out seen)) return null;
            var seconds = Math.Max(0, (int)(DateTimeOffset.UtcNow - seen).TotalSeconds);
            return "上次活动 " + seconds + " 秒前";
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

        private static WebClient CreateBridgeClient()
        {
            var client = new WebClient();
            client.Encoding = System.Text.Encoding.UTF8;
            return client;
        }

        private static string CreateApprovalKey()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create())
                random.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    internal sealed class GatewayApprovalWindow : Window
    {
        public GatewayApprovalWindow(string summary, Action allow, Action reject)
        {
            Title = "确认修改";
            Width = 440;
            Height = 260;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Background = BrushOf("PaperBrush", Colors.White);
            var root = new StackPanel { Margin = new Thickness(22) };
            root.Children.Add(new TextBlock
            {
                Text = "AI 想修改当前 TIA 工程的离线副本",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            });
            root.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(summary) ? "修改当前工程的离线副本" : summary,
                FontSize = 14,
                Margin = new Thickness(0, 14, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            root.Children.Add(new TextBlock
            {
                Text = "原始工程不会被覆盖。会自动 Compile 和 Verify。",
                FontSize = 13,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = BrushOf("MutedBrush", Colors.DimGray)
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 22, 0, 0) };
            var allowButton = new Button { Content = "允许本次修改", MinWidth = 140, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(12, 8, 12, 8) };
            var rejectButton = new Button { Content = "拒绝", MinWidth = 90, Padding = new Thickness(12, 8, 12, 8) };
            allowButton.Click += delegate { allow(); Close(); };
            rejectButton.Click += delegate { reject(); Close(); };
            buttons.Children.Add(allowButton);
            buttons.Children.Add(rejectButton);
            root.Children.Add(buttons);
            Content = root;
        }

        private static Brush BrushOf(string key, Color fallback)
        {
            var found = Application.Current == null ? null : Application.Current.TryFindResource(key) as Brush;
            return found ?? new SolidColorBrush(fallback);
        }
    }
}
