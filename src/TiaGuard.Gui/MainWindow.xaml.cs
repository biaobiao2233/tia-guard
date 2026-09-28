using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TiaGuard.Openness;
using Forms = System.Windows.Forms;

namespace TiaGuard.Gui
{
    public partial class MainWindow : Window
    {
        private bool _busy;
        private string _lastOutputDirectory;

        public MainWindow()
        {
            InitializeComponent();
            VersionText.Text = "v" + ProductVersion;
            FullWorkspaceTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TIA-Guard");
            AppendLog("TIA-Guard GUI started.");
            RefreshDoctor();
        }

        private static string ProductVersion
        {
            get
            {
                var attribute = typeof(MainWindow).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var version = attribute?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    var metadata = version.IndexOf('+');
                    return metadata >= 0 ? version.Substring(0, metadata) : version;
                }

                var assemblyVersion = typeof(MainWindow).Assembly.GetName().Version;
                return assemblyVersion == null
                    ? "0.1.0"
                    : assemblyVersion.Major + "." + assemblyVersion.Minor + "." + assemblyVersion.Build;
            }
        }

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (MaximizeButton == null) return;
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void OnRefreshDoctorClick(object sender, RoutedEventArgs e)
        {
            RefreshDoctor();
        }

        private OpennessEnvironmentReport RefreshDoctor()
        {
            try
            {
                var report = OpennessEnvironmentProbe.Inspect();
                if (report.Ready)
                {
                    DoctorStatusBorder.Background = ResourceBrush("GreenBrush");
                    DoctorStatusText.Text = "READY · 环境就绪";
                    DoctorIssueText.Text =
                        "TIA Portal V21 ✓   PublicAPI ✓   x64 ✓   Openness 权限 ✓";
                    AppendLog("doctor: ready");
                }
                else
                {
                    DoctorStatusBorder.Background = ResourceBrush("RedBrush");
                    DoctorStatusText.Text = "BLOCKED · 环境未就绪";
                    DoctorIssueText.Text = report.Issues.Count == 0
                        ? "环境检查未通过。"
                        : string.Join(Environment.NewLine, report.Issues);
                    AppendLog("doctor: blocked");
                    foreach (var issue in report.Issues)
                        AppendLog("doctor issue: " + issue);
                }

                return report;
            }
            catch (Exception error)
            {
                DoctorStatusBorder.Background = ResourceBrush("RedBrush");
                DoctorStatusText.Text = "ERROR · 检测失败";
                DoctorIssueText.Text = error.Message;
                AppendLog("doctor error: " + error);
                return null;
            }
        }

        private bool RequireEnvironment()
        {
            var report = RefreshDoctor();
            if (report != null && report.Ready) return true;

            SetBlocked(
                "环境未就绪",
                "请先解决左侧运行环境中的阻塞项，再执行工程操作。");
            return false;
        }

        private async void OnFullRoundTripClick(object sender, RoutedEventArgs e)
        {
            if (!RequireIdle() || !RequireEnvironment()) return;

            var project = FullProjectTextBox.Text.Trim();
            var workspace = FullWorkspaceTextBox.Text.Trim();
            if (!RequireAp21(project, "请选择原始 .ap21 工程。") ||
                !RequireFolderValue(workspace, "请选择工作目录。"))
                return;

            var runRoot = Path.Combine(
                Path.GetFullPath(workspace),
                "roundtrip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
            var sourceRoot = Path.Combine(runRoot, "source");
            var rebuiltRoot = Path.Combine(runRoot, "rebuilt");

            BeginOperation("完整往返验证", "准备 Export → Build → Verify。");
            ResetSteps();

            try
            {
                Directory.CreateDirectory(runRoot);
                SetOutput(runRoot);

                SetStep(ExportStepBorder, ExportStepText, "EXPORT", "运行中", "YellowBrush");
                var manifest = await RunStaAsync(() =>
                {
                    using (var session = TiaProjectSession.OpenOfflineCopy(project))
                        return session.ExportRoundTripSource(sourceRoot, ReportStage);
                });
                AppendLog("export manifest:");
                AppendLog(RoundTripJson.Serialize(manifest).Trim());

                if (!manifest.RoundTripReady)
                {
                    SetStep(ExportStepBorder, ExportStepText, "EXPORT", "BLOCKED", "RedBrush");
                    SetBlocked(
                        "导出完成，但当前工程不能安全往返",
                        "TIA-Guard 已停止后续重建，避免生成不完整工程。技术日志里保留了 manifest。");
                    return;
                }
                SetStep(ExportStepBorder, ExportStepText, "EXPORT", "完成", "GreenBrush");

                SetStep(BuildStepBorder, BuildStepText, "BUILD", "运行中", "PurpleBrush");
                var build = await RunStaAsync(() =>
                    RoundTripBuilder.Build(sourceRoot, rebuiltRoot, ReportStage));
                SetStep(BuildStepBorder, BuildStepText, "BUILD", "完成", "GreenBrush");
                AppendLog("rebuilt project: " + build.ProjectFile);
                AppendLog("compileErrors=" + build.CompileErrors);
                AppendLog("compileWarnings=" + build.CompileWarnings);

                SetStep(VerifyStepBorder, VerifyStepText, "VERIFY", "运行中", "CyanBrush");
                var verify = await RunStaAsync(() =>
                    RoundTripVerifier.VerifyProjects(project, build.ProjectFile, ReportStage));

                ExportProjectTextBox.Text = project;
                ExportTargetTextBox.Text = sourceRoot;
                BuildSourceTextBox.Text = sourceRoot;
                BuildTargetTextBox.Text = rebuiltRoot;
                VerifyOriginalTextBox.Text = project;
                VerifyRebuiltTextBox.Text = build.ProjectFile;

                ApplyVerifyResult(verify);
            }
            catch (OpennessAccessException error)
            {
                SetBlocked("Openness 权限不可用", error.Message);
            }
            catch (Exception error)
            {
                SetError("完整往返失败", error);
            }
            finally
            {
                EndOperation();
            }
        }

        private async void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (!RequireIdle() || !RequireEnvironment()) return;

            var project = ExportProjectTextBox.Text.Trim();
            var target = ExportTargetTextBox.Text.Trim();
            if (!RequireAp21(project, "请选择要导出的 .ap21 工程。") ||
                !RequireFolderValue(target, "请输入 Git Source 输出目录。"))
                return;

            BeginOperation("导出到 Git Source", "正在从离线工程副本提取 canonical source。");
            ResetSteps();
            SetStep(ExportStepBorder, ExportStepText, "EXPORT", "运行中", "YellowBrush");

            try
            {
                var manifest = await RunStaAsync(() =>
                {
                    using (var session = TiaProjectSession.OpenOfflineCopy(project))
                        return session.ExportRoundTripSource(target, ReportStage);
                });

                AppendLog(RoundTripJson.Serialize(manifest).Trim());
                SetOutput(Path.GetFullPath(target));

                if (manifest.RoundTripReady)
                {
                    SetStep(ExportStepBorder, ExportStepText, "EXPORT", "完成", "GreenBrush");
                    SetSuccess(
                        "导出完成",
                        "Git Source 已生成，并通过当前 bounded round-trip readiness 检查。");
                }
                else
                {
                    SetStep(ExportStepBorder, ExportStepText, "EXPORT", "BLOCKED", "RedBrush");
                    SetBlocked(
                        "导出已生成，但不能安全往返",
                        "为避免丢失工程内容，TIA-Guard 不会把此结果当作可重建工程。");
                }
            }
            catch (OpennessAccessException error)
            {
                SetBlocked("Openness 权限不可用", error.Message);
            }
            catch (Exception error)
            {
                SetError("导出失败", error);
            }
            finally
            {
                EndOperation();
            }
        }

        private async void OnBuildClick(object sender, RoutedEventArgs e)
        {
            if (!RequireIdle() || !RequireEnvironment()) return;

            var source = BuildSourceTextBox.Text.Trim();
            var target = BuildTargetTextBox.Text.Trim();
            if (!RequireExistingDirectory(source, "请选择已有 Git Source 目录。") ||
                !RequireFolderValue(target, "请输入新工程输出目录。"))
                return;

            BeginOperation("从 Git Source 重建", "正在验证 source 并创建 fresh TIA V21 工程。");
            ResetSteps();
            SetStep(BuildStepBorder, BuildStepText, "BUILD", "运行中", "PurpleBrush");

            try
            {
                var result = await RunStaAsync(() =>
                    RoundTripBuilder.Build(source, target, ReportStage));

                SetStep(BuildStepBorder, BuildStepText, "BUILD", "完成", "GreenBrush");
                SetOutput(Path.GetDirectoryName(result.ProjectFile));
                VerifyRebuiltTextBox.Text = result.ProjectFile;
                AppendLog("projectFile=" + result.ProjectFile);
                AppendLog("compileErrors=" + result.CompileErrors);
                AppendLog("compileWarnings=" + result.CompileWarnings);
                SetSuccess(
                    "重建完成",
                    "Fresh .ap21 已生成并完成编译，Compile errors = 0。");
            }
            catch (OpennessAccessException error)
            {
                SetBlocked("Openness 权限不可用", error.Message);
            }
            catch (Exception error)
            {
                SetError("重建失败", error);
            }
            finally
            {
                EndOperation();
            }
        }

        private async void OnVerifyClick(object sender, RoutedEventArgs e)
        {
            if (!RequireIdle() || !RequireEnvironment()) return;

            var original = VerifyOriginalTextBox.Text.Trim();
            var rebuilt = VerifyRebuiltTextBox.Text.Trim();
            if (!RequireAp21(original, "请选择原始 .ap21 工程。") ||
                !RequireAp21(rebuilt, "请选择重建后的 .ap21 工程。"))
                return;

            BeginOperation("语义验证", "正在比较受支持的工程语义。");
            ResetSteps();
            SetStep(VerifyStepBorder, VerifyStepText, "VERIFY", "运行中", "CyanBrush");

            try
            {
                var result = await RunStaAsync(() =>
                    RoundTripVerifier.VerifyProjects(original, rebuilt, ReportStage));
                ApplyVerifyResult(result);
            }
            catch (Exception error)
            {
                SetError("验证失败", error);
            }
            finally
            {
                EndOperation();
            }
        }

        private void ApplyVerifyResult(RoundTripVerifyResult result)
        {
            AppendLog(RoundTripJson.Serialize(result).Trim());

            if (string.Equals(result.Verdict, "pass", StringComparison.OrdinalIgnoreCase))
            {
                SetStep(VerifyStepBorder, VerifyStepText, "VERIFY", "PASS", "GreenBrush");
                SetSuccess(
                    "VERIFY PASS",
                    "原工程与重建工程在当前支持的 S7-1200 / V21 工程语义范围内一致。");
                return;
            }

            if (string.Equals(result.Verdict, "mismatch", StringComparison.OrdinalIgnoreCase))
            {
                SetStep(VerifyStepBorder, VerifyStepText, "VERIFY", "MISMATCH", "RedBrush");
                SetBlocked(
                    "VERIFY MISMATCH",
                    result.Differences.Count == 0
                        ? "检测到语义差异。"
                        : "检测到 " + result.Differences.Count + " 项语义差异，详见技术日志。");
                return;
            }

            SetStep(VerifyStepBorder, VerifyStepText, "VERIFY", "BLOCKED", "RedBrush");
            SetBlocked("VERIFY BLOCKED", HumanizeBlockedCode(result.BlockedCode));
        }

        private static string HumanizeBlockedCode(string code)
        {
            switch (code)
            {
                case "OPENNESS_ACCESS_BLOCKED":
                    return "当前 Windows 登录令牌没有有效的 Siemens TIA Openness 权限。";
                case "PROJECT_INPUT_INVALID":
                    return "工程路径无效，或指定文件不是可用的 .ap21。";
                case "PROJECT_INPUTS_IDENTICAL":
                    return "原工程和重建工程不能指向同一个文件。";
                case "REBUILT_COMPILE_ERRORS":
                    return "重建工程编译存在错误，验证已停止。";
                case "ORIGINAL_SOURCE_INVALID":
                    return "原工程无法生成满足当前契约的 Git Source。";
                case "REBUILT_SOURCE_INVALID":
                    return "重建工程无法生成满足当前契约的 Git Source。";
                case "ORIGINAL_EXPORT_FAILED":
                    return "原工程导出失败。";
                case "REBUILT_EXPORT_OR_COMPILE_FAILED":
                    return "重建工程导出或编译失败。";
                case "VERIFY_CLEANUP_FAILED":
                    return "验证已执行，但临时验证目录清理失败。";
                default:
                    return string.IsNullOrWhiteSpace(code)
                        ? "验证被安全边界阻止，详见技术日志。"
                        : "验证被安全边界阻止：" + code;
            }
        }

        private void BeginOperation(string title, string detail)
        {
            _busy = true;
            ActionsPanel.IsEnabled = false;
            BusyProgress.Visibility = Visibility.Visible;
            ResultBadgeBorder.Background = ResourceBrush("YellowBrush");
            ResultBadgeText.Text = "RUNNING";
            ResultTitleText.Text = title;
            ResultDetailText.Text = detail;
            AppendLog("---- " + title + " ----");
        }

        private void EndOperation()
        {
            _busy = false;
            ActionsPanel.IsEnabled = true;
            BusyProgress.Visibility = Visibility.Collapsed;
        }

        private bool RequireIdle()
        {
            if (!_busy) return true;
            MessageBox.Show(
                this,
                "当前已有任务正在运行。",
                "TIA-Guard",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        private bool RequireAp21(string path, string message)
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                File.Exists(path) &&
                string.Equals(Path.GetExtension(path), ".ap21", StringComparison.OrdinalIgnoreCase))
                return true;

            MessageBox.Show(this, message, "TIA-Guard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private bool RequireExistingDirectory(string path, string message)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                return true;

            MessageBox.Show(this, message, "TIA-Guard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private bool RequireFolderValue(string path, string message)
        {
            if (!string.IsNullOrWhiteSpace(path)) return true;

            MessageBox.Show(this, message, "TIA-Guard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void ReportStage(string stage)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ResultDetailText.Text = "当前阶段：" + stage;
                AppendLog("stage=" + stage);
            }));
        }

        private void SetSuccess(string title, string detail)
        {
            ResultBadgeBorder.Background = ResourceBrush("GreenBrush");
            ResultBadgeText.Text = "PASS";
            ResultTitleText.Text = title;
            ResultDetailText.Text = detail;
        }

        private void SetBlocked(string title, string detail)
        {
            ResultBadgeBorder.Background = ResourceBrush("RedBrush");
            ResultBadgeText.Text = "BLOCKED";
            ResultTitleText.Text = title;
            ResultDetailText.Text = detail;
            AppendLog("blocked: " + detail);
        }

        private void SetError(string title, Exception error)
        {
            ResultBadgeBorder.Background = ResourceBrush("RedBrush");
            ResultBadgeText.Text = "ERROR";
            ResultTitleText.Text = title;
            ResultDetailText.Text = FriendlyErrorMessage(error);
            AppendLog("error: " + error);
        }

        private static string FriendlyErrorMessage(Exception error)
        {
            if (error == null) return "发生未知错误，请查看技术日志。";

            if (error is IOException &&
                error.Message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
                return "目标路径已经存在。为防止覆盖工程，TIA-Guard 要求使用一个尚不存在的新目录。";

            if (error is DirectoryNotFoundException)
                return "找不到目标路径的上级目录，请先选择一个已经存在的父目录。";

            if (error.Message.IndexOf("too long", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.Message.IndexOf("143", StringComparison.OrdinalIgnoreCase) >= 0)
                return "输出路径过长。TIA Portal V21 的暂存工程路径有长度限制，请换到更短的目录。";

            if (error is UnauthorizedAccessException)
                return "Windows 拒绝访问该路径，请更换目录或检查文件权限。";

            return error.Message;
        }

        private void ResetSteps()
        {
            SetStep(ExportStepBorder, ExportStepText, "EXPORT", "待命", null);
            SetStep(BuildStepBorder, BuildStepText, "BUILD", "待命", null);
            SetStep(VerifyStepBorder, VerifyStepText, "VERIFY", "待命", null);
        }

        private void SetStep(Border border, TextBlock text, string name, string state, string brushKey)
        {
            border.Background = brushKey == null
                ? ResourceBrush("SoftBrush")
                : ResourceBrush(brushKey);
            text.Text = name + " · " + state;
        }

        private void SetOutput(string directory)
        {
            _lastOutputDirectory = directory;
            OutputPathText.Text = string.IsNullOrWhiteSpace(directory) ? "—" : directory;
            OpenOutputButton.IsEnabled =
                !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
        }

        private void AppendLog(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }

            var line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
            LogTextBox.AppendText(line + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        }

        private Brush ResourceBrush(string key)
        {
            return (Brush)FindResource(key);
        }

        private static Task<T> RunStaAsync<T>(Func<T> action)
        {
            var completion = new TaskCompletionSource<T>();
            var thread = new Thread(() =>
            {
                try
                {
                    completion.SetResult(action());
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return completion.Task;
        }

        private void OnOpenOutputClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_lastOutputDirectory) ||
                !Directory.Exists(_lastOutputDirectory))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = _lastOutputDirectory,
                UseShellExecute = true
            });
        }

        private void OnBrowseFullProject(object sender, RoutedEventArgs e)
        {
            BrowseProject(FullProjectTextBox);
        }

        private void OnBrowseFullWorkspace(object sender, RoutedEventArgs e)
        {
            BrowseFolder(FullWorkspaceTextBox, "选择完整往返工作目录");
        }

        private void OnBrowseExportProject(object sender, RoutedEventArgs e)
        {
            BrowseProject(ExportProjectTextBox);
        }

        private void OnBrowseExportTarget(object sender, RoutedEventArgs e)
        {
            BrowseNewFolderTarget(ExportTargetTextBox, "选择 Git Source 的父目录", "tia-guard-source");
        }

        private void OnBrowseBuildSource(object sender, RoutedEventArgs e)
        {
            BrowseFolder(BuildSourceTextBox, "选择 Git Source 目录");
        }

        private void OnBrowseBuildTarget(object sender, RoutedEventArgs e)
        {
            BrowseNewFolderTarget(BuildTargetTextBox, "选择重建工程的父目录", "tia-guard-rebuilt");
        }

        private void OnBrowseVerifyOriginal(object sender, RoutedEventArgs e)
        {
            BrowseProject(VerifyOriginalTextBox);
        }

        private void OnBrowseVerifyRebuilt(object sender, RoutedEventArgs e)
        {
            BrowseProject(VerifyRebuiltTextBox);
        }

        private void BrowseNewFolderTarget(TextBox target, string description, string suggestedName)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = description + "（TIA-Guard 会使用一个新的子目录）";
                dialog.ShowNewFolderButton = true;

                var current = target.Text;
                if (!string.IsNullOrWhiteSpace(current))
                {
                    try
                    {
                        var parent = Directory.Exists(current)
                            ? current
                            : Path.GetDirectoryName(Path.GetFullPath(current));
                        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                            dialog.SelectedPath = parent;
                    }
                    catch
                    {
                    }
                }

                if (dialog.ShowDialog() != Forms.DialogResult.OK) return;

                var candidate = Path.Combine(dialog.SelectedPath, suggestedName);
                if (Directory.Exists(candidate) || File.Exists(candidate))
                    candidate = Path.Combine(
                        dialog.SelectedPath,
                        suggestedName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                target.Text = candidate;
            }
        }

        private void BrowseProject(TextBox target)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 TIA Portal V21 工程",
                Filter = "TIA Portal V21 project (*.ap21)|*.ap21|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == true)
                target.Text = dialog.FileName;
        }

        private void BrowseFolder(TextBox target, string description)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.Description = description;
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(target.Text))
                    dialog.SelectedPath = target.Text;

                if (dialog.ShowDialog() == Forms.DialogResult.OK)
                    target.Text = dialog.SelectedPath;
            }
        }

    }
}
