using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TiaGuard.Openness;
using Forms = System.Windows.Forms;

namespace TiaGuard.Gui
{
    public partial class MainWindow : Window
    {
        private bool _busy;
        private string _lastOutputDirectory;
        private readonly GitRepositoryClient _git = new GitRepositoryClient();
        private TextBox RestoreGitUrlTextBox;
        private TextBox RestoreOutputTextBox;
        private TextBox PublishProjectTextBox;
        private TextBox PublishGitUrlTextBox;
        private TextBox PublishCommitTextBox;
        private Border GitStepBorder;
        private TextBlock GitStepText;
        private Border PushStepBorder;
        private TextBlock PushStepText;
        private readonly Stopwatch _operationStopwatch = new Stopwatch();
        private readonly Stopwatch _stageStopwatch = new Stopwatch();
        private readonly DispatcherTimer _elapsedTimer = new DispatcherTimer();
        private string _activeStage;
        private string _activeStageLabel;

        public MainWindow()
        {
            InitializeComponent();
            InstallProductSurface();
            _elapsedTimer.Interval = TimeSpan.FromSeconds(1);
            _elapsedTimer.Tick += (sender, args) => UpdateProgressStatus();
            VersionText.Text = "v" + ProductVersion;
            FullWorkspaceTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TIA-Guard");
            PublishCommitTextBox.Text = "Update TIA source " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm");
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

        private void InstallProductSurface()
        {
            if (ActionsPanel.Children.Count < 3)
                throw new InvalidOperationException("The GUI action surface is incomplete.");

            var environmentCard = ActionsPanel.Children[0];
            var oldRoundTripCard = ActionsPanel.Children[1];
            var oldManualCard = ActionsPanel.Children[2];
            ActionsPanel.Children.Clear();
            ActionsPanel.Children.Add(environmentCard);

            var restoreCard = CreateProductCard(
                "GITHUB → TIA",
                "从 GitHub 还原 TIA 工程",
                "粘贴仓库 URL；仓库可包含多个 TIA 工程。选择指定工程后，TIA-Guard 只重建、编译并验证这一份。",
                "YellowBrush");
            var restoreBody = (StackPanel)restoreCard.Child;
            RestoreGitUrlTextBox = AddTextField(
                restoreBody, "Git 仓库 URL", "使用系统 Git 凭据；支持 HTTPS / SSH");
            RestoreOutputTextBox = AddBrowseField(
                restoreBody, "Fresh .ap21 输出目录", OnBrowseRestoreOutput);
            restoreBody.Children.Add(new TextBlock
            {
                Text = "TIA 编译使用 TIA-Guard 自己控制的短内部 staging，最终工程再安全发布到你选择的位置。",
                FontSize = 11,
                Foreground = ResourceBrush("MutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            var restoreButton = CreatePrimaryButton(
                "还原工程 →", "YellowBrush", OnRestoreFromGitClick);
            restoreButton.Margin = new Thickness(0, 14, 0, 0);
            restoreBody.Children.Add(restoreButton);
            ActionsPanel.Children.Add(restoreCard);

            var publishCard = CreateProductCard(
                "TIA → GITHUB",
                "发布 TIA 工程到 GitHub",
                "选择自己的 .ap21 和 Git 仓库；可更新仓库里的指定工程，或把它作为新工程加入同一个仓库。",
                "CyanBrush");
            var publishBody = (StackPanel)publishCard.Child;
            PublishProjectTextBox = AddBrowseField(
                publishBody, "自己的 TIA 工程 (.ap21)", OnBrowsePublishProject);
            PublishGitUrlTextBox = AddTextField(
                publishBody, "Git 仓库 URL", "不保存 Token；使用 Git Credential Manager / SSH");
            PublishCommitTextBox = AddTextField(
                publishBody, "Commit message", null);
            publishBody.Children.Add(new TextBlock
            {
                Text = "新布局：repo/tia-projects/<工程槽位>/tia-source/；只修改所选工程。旧版 repo/tia-source/ 仍可读取和更新。",
                FontSize = 11,
                Foreground = ResourceBrush("MutedBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            var publishButton = CreatePrimaryButton(
                "发布到 GitHub →", "CyanBrush", OnPublishToGitClick);
            publishButton.Margin = new Thickness(0, 14, 0, 0);
            publishBody.Children.Add(publishButton);
            ActionsPanel.Children.Add(publishCard);

            var advancedBody = new StackPanel();
            advancedBody.Children.Add(oldRoundTripCard);
            advancedBody.Children.Add(oldManualCard);
            var advanced = new Expander
            {
                Header = "高级工具 / Export · Build · Verify",
                IsExpanded = false,
                Content = advancedBody,
                Margin = new Thickness(0, 2, 0, 16)
            };
            ActionsPanel.Children.Add(advanced);

            var stepPanel = ExportStepBorder.Parent as Panel;
            if (stepPanel == null)
                throw new InvalidOperationException("The task step panel is unavailable.");
            GitStepBorder = CreateStepBorder(out GitStepText, "GIT · 待命");
            PushStepBorder = CreateStepBorder(out PushStepText, "PUSH · 待命");
            stepPanel.Children.Insert(0, GitStepBorder);
            stepPanel.Children.Add(PushStepBorder);
        }

        private Border CreateProductCard(
            string eyebrow, string title, string detail, string backgroundKey)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = eyebrow,
                Style = (Style)FindResource("Eyebrow")
            });
            body.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 24,
                FontWeight = FontWeights.Black,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 5)
            });
            body.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            return new Border
            {
                Style = (Style)FindResource("CardBorder"),
                Background = ResourceBrush(backgroundKey),
                Margin = new Thickness(0, 0, 0, 18),
                Child = body
            };
        }

        private TextBox AddTextField(
            Panel parent, string label, string toolTip)
        {
            parent.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            });
            var box = new TextBox
            {
                Style = (Style)FindResource("PathTextBox"),
                Margin = new Thickness(0, 0, 0, 12)
            };
            if (!string.IsNullOrWhiteSpace(toolTip)) box.ToolTip = toolTip;
            parent.Children.Add(box);
            return box;
        }

        private TextBox AddBrowseField(
            Panel parent, string label, RoutedEventHandler browseHandler)
        {
            parent.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            });
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var box = new TextBox { Style = (Style)FindResource("PathTextBox") };
            var browse = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Content = "浏览"
            };
            browse.Click += browseHandler;
            Grid.SetColumn(browse, 2);
            grid.Children.Add(box);
            grid.Children.Add(browse);
            parent.Children.Add(grid);
            return box;
        }

        private Button CreatePrimaryButton(
            string text, string backgroundKey, RoutedEventHandler handler)
        {
            var button = new Button
            {
                Style = (Style)FindResource("BrutalButton"),
                Background = ResourceBrush(backgroundKey),
                Content = text
            };
            button.Click += handler;
            return button;
        }

        private Border CreateStepBorder(out TextBlock text, string value)
        {
            text = new TextBlock
            {
                Text = value,
                FontWeight = FontWeights.Black
            };
            return new Border
            {
                Style = (Style)FindResource("StepBorder"),
                Child = text
            };
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

        private async void OnRestoreFromGitClick(object sender, RoutedEventArgs e)
        {
            if (!RequireIdle() || !RequireEnvironment()) return;

            var repositoryUrl = RestoreGitUrlTextBox.Text.Trim();
            var output = RestoreOutputTextBox.Text.Trim();
            if (!RequireGitUrl(repositoryUrl) ||
                !RequireFolderValue(output, "请选择 fresh .ap21 工程的输出目录。"))
                return;

            BeginOperation("从 GitHub 还原 TIA 工程", "正在准备 Git 仓库并校验 tia-source/。");
            ResetSteps();
            try
            {
                SetStep(GitStepBorder, GitStepText, "GIT", "CLONE / PULL", "YellowBrush");
                var repository = await Task.Run(() =>
                    _git.PrepareRepository(repositoryUrl, AppendLog));
                SetStep(GitStepBorder, GitStepText, "GIT", "完成", "GreenBrush");

                var catalog = await Task.Run(() =>
                    RepositorySourceManager.DiscoverProjects(repository));
                LogProjectCatalogProblems(catalog);
                if (catalog.Projects.Count == 0)
                {
                    SetBlocked("仓库中没有可还原的 TIA 工程",
                        catalog.Problems.Count == 0
                            ? "没有发现 tia-projects/<槽位>/tia-source/ 或旧版 tia-source/。"
                            : "检测到了工程槽位，但都没有通过 canonical source 校验；详见技术日志。");
                    return;
                }

                RepositoryProjectSource selectedProject;
                if (catalog.Projects.Count == 1)
                {
                    selectedProject = catalog.Projects[0];
                }
                else
                {
                    var picker = new ProjectPickerWindow(
                        catalog, ProjectPickerPurpose.Restore)
                    {
                        Owner = this
                    };
                    if (picker.ShowDialog() != true)
                    {
                        SetBlocked("还原已取消", "没有选择要从仓库还原的 TIA 工程。");
                        return;
                    }
                    selectedProject = picker.SelectedProject;
                }

                var sourceRoot = selectedProject.SourcePath;
                AppendLog("repository project: " +
                    (selectedProject.IsLegacy
                        ? "legacy tia-source"
                        : selectedProject.Slot) +
                    " -> " + selectedProject.RelativeSourcePath);
                var sourceInput = await Task.Run(() =>
                    RoundTripBuildInput.LoadSource(sourceRoot));
                AppendLog("tia-source: canonical source validation PASS");

                SetStep(BuildStepBorder, BuildStepText, "BUILD", "运行中", "PurpleBrush");
                var build = await RunStaAsync(() =>
                    RoundTripBuilder.Build(sourceRoot, output, ReportStage));
                SetStep(BuildStepBorder, BuildStepText, "BUILD", "完成", "GreenBrush");
                AppendLog("rebuilt project: " + build.ProjectFile);
                AppendLog("compileErrors=" + build.CompileErrors);
                AppendLog("compileWarnings=" + build.CompileWarnings);

                SetStep(VerifyStepBorder, VerifyStepText, "VERIFY", "运行中", "CyanBrush");
                var verify = await RunStaAsync(() =>
                    RoundTripVerifier.VerifySourceAgainstProject(
                        sourceRoot, build.ProjectFile, ReportStage));
                AppendLog(RoundTripJson.Serialize(verify).Trim());
                if (!string.Equals(verify.Verdict, "pass", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyVerifyResult(verify);
                    return;
                }

                SetStep(VerifyStepBorder, VerifyStepText, "VERIFY", "PASS", "GreenBrush");
                SetOutput(Path.GetDirectoryName(build.ProjectFile));

                var binaryIdentityError = VerifyRestoredFileIdentity(
                    sourceInput.Manifest.Project, build.ProjectFile);
                if (binaryIdentityError != null)
                {
                    SetBlocked("还原产物与上传前不完全一致", binaryIdentityError);
                    return;
                }

                var hasBinaryIdentity = sourceInput.Manifest.Project.OriginalSizeBytes.HasValue;
                SetSuccess("还原完成", hasBinaryIdentity
                    ? "文件名、大小、SHA-256 与上传前完全一致；同时 Verify PASS。"
                    : "Fresh .ap21 已从 tia-source/ 重建、编译并通过 canonical source 完整性复核。");
            }
            catch (OpennessAccessException error)
            {
                SetBlocked("Openness 权限不可用", error.Message);
            }
            catch (Exception error)
            {
                SetError("还原 TIA 工程失败", error);
            }
            finally
            {
                EndOperation();
            }
        }

        private async void OnPublishToGitClick(object sender, RoutedEventArgs e)
        {
            if (!RequireIdle() || !RequireEnvironment()) return;

            var project = PublishProjectTextBox.Text.Trim();
            var repositoryUrl = PublishGitUrlTextBox.Text.Trim();
            var message = PublishCommitTextBox.Text.Trim();
            if (!RequireAp21(project, "请选择自己拥有的 .ap21 工程。") ||
                !RequireGitUrl(repositoryUrl))
                return;

            string exportStage = null;
            BeginOperation("发布 TIA 工程到 GitHub", "正在准备 Git 仓库与安全导出 staging。");
            ResetSteps();
            try
            {
                SetStep(GitStepBorder, GitStepText, "GIT", "CLONE / PULL", "YellowBrush");
                var repository = await Task.Run(() =>
                    _git.PrepareRepository(repositoryUrl, AppendLog));

                await Task.Run(() =>
                    _git.EnsureCommitIdentityFromSystemGitHub(repository, AppendLog));
                var catalog = await Task.Run(() =>
                    RepositorySourceManager.DiscoverProjects(repository));
                LogProjectCatalogProblems(catalog);
                SetStep(GitStepBorder, GitStepText, "GIT", "完成", "GreenBrush");

                var picker = new ProjectPickerWindow(
                    catalog,
                    ProjectPickerPurpose.Publish,
                    RepositorySourceManager.SuggestProjectSlot(
                        Path.GetFileName(project)))
                {
                    Owner = this
                };
                if (picker.ShowDialog() != true)
                {
                    SetBlocked("发布已取消", "没有选择要更新的工程，也没有添加新工程。");
                    return;
                }

                var existingTarget = picker.SelectedProject;
                var createNew = picker.CreateNew;
                var newSlot = picker.NewSlot;

                exportStage = CreateExportStage();
                var stagedSource = Path.Combine(exportStage,
                    RepositorySourceManager.ManagedDirectoryName);
                SetStep(ExportStepBorder, ExportStepText, "EXPORT", "运行中", "YellowBrush");
                var manifest = await RunStaAsync(() =>
                {
                    using (var session = TiaProjectSession.OpenOfflineCopy(project))
                        return session.ExportRoundTripSource(stagedSource, ReportStage);
                });
                AppendLog(RoundTripJson.Serialize(manifest).Trim());
                if (!manifest.RoundTripReady)
                {
                    SetStep(ExportStepBorder, ExportStepText, "EXPORT", "BLOCKED", "RedBrush");
                    SetBlocked("工程不能安全发布",
                        "Export 未达到 bounded round-trip readiness；tia-source/ 与 Git 仓库均未更新。");
                    return;
                }

                await Task.Run(() => RoundTripBuildInput.LoadSource(stagedSource));
                SetStep(ExportStepBorder, ExportStepText, "EXPORT", "完成", "GreenBrush");

                string managedRelativePath;
                string targetLabel;
                if (createNew)
                {
                    var duplicate = catalog.Projects.FirstOrDefault(value =>
                    {
                        try
                        {
                            return RepositorySourceManager.IsSameProjectIdentity(
                                value.SourcePath, stagedSource);
                        }
                        catch
                        {
                            return false;
                        }
                    });
                    if (duplicate != null)
                    {
                        SetBlocked("仓库里已经有这个工程",
                            "检测到同一工程已经存在于 " +
                            duplicate.RelativeSourcePath +
                            "；请重新发布并选择“更新所选工程”，避免重复槽位。");
                        return;
                    }

                    AppendLog("tia-source: add project slot " + newSlot);
                    await Task.Run(() =>
                        RepositorySourceManager.AddProjectSource(
                            repository, newSlot, stagedSource));
                    managedRelativePath =
                        RepositorySourceManager.RelativeManagedSourcePath(newSlot);
                    targetLabel = newSlot;
                }
                else
                {
                    await Task.Run(() =>
                        RepositorySourceManager.EnsureSameProjectIdentity(
                            existingTarget.SourcePath, stagedSource));

                    if (existingTarget.IsLegacy)
                    {
                        AppendLog("tia-source: update legacy single-project source");
                        await Task.Run(() =>
                            RepositorySourceManager.ReplaceManagedSource(
                                repository, stagedSource));
                    }
                    else
                    {
                        AppendLog("tia-source: update project slot " + existingTarget.Slot);
                        await Task.Run(() =>
                            RepositorySourceManager.ReplaceProjectSource(
                                repository, existingTarget.Slot, stagedSource));
                    }

                    managedRelativePath = existingTarget.RelativeSourcePath;
                    targetLabel = existingTarget.IsLegacy
                        ? "旧版单工程 tia-source"
                        : existingTarget.Slot;
                }

                SetStep(PushStepBorder, PushStepText, "PUSH", "COMMIT", "PurpleBrush");
                var committed = await Task.Run(() =>
                    _git.CommitManagedSource(
                        repository, managedRelativePath, message, AppendLog));
                SetStep(PushStepBorder, PushStepText, "PUSH", "上传中", "CyanBrush");
                await Task.Run(() => _git.Push(repository, AppendLog));
                SetStep(PushStepBorder, PushStepText, "PUSH", "完成", "GreenBrush");

                SetOutput(repository);
                SetSuccess("发布完成", committed
                    ? "工程“" + targetLabel +
                      "”的 tia-source/ 已安全更新、提交并推送到远端；其他工程未修改。"
                    : "工程“" + targetLabel +
                      "”没有新的源码差异；其他工程未修改。");
            }
            catch (OpennessAccessException error)
            {
                SetBlocked("Openness 权限不可用", error.Message);
            }
            catch (Exception error)
            {
                SetError("发布到 GitHub 失败", error);
            }
            finally
            {
                DeleteExportStage(exportStage);
                EndOperation();
            }
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
            ProgressStatusText.Visibility = Visibility.Visible;
            ResultBadgeBorder.Background = ResourceBrush("YellowBrush");
            ResultBadgeText.Text = "RUNNING";
            ResultTitleText.Text = title;
            ResultDetailText.Text = detail;

            _activeStage = null;
            _activeStageLabel = detail;
            _stageStopwatch.Reset();
            _operationStopwatch.Restart();
            _elapsedTimer.Start();
            UpdateProgressStatus();
            AppendLog("---- " + title + " ----");
        }

        private void EndOperation()
        {
            FinishActiveStage();
            _operationStopwatch.Stop();
            _elapsedTimer.Stop();
            if (_operationStopwatch.Elapsed > TimeSpan.Zero)
                AppendLog("operation duration=" + FormatElapsed(_operationStopwatch.Elapsed));

            _busy = false;
            ActionsPanel.IsEnabled = true;
            BusyProgress.Visibility = Visibility.Collapsed;
            ProgressStatusText.Visibility = Visibility.Collapsed;
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

        private bool RequireGitUrl(string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) return true;
            MessageBox.Show(this, "请输入 Git 仓库 URL。", "TIA-Guard",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void ReportStage(string stage)
        {
            Dispatcher.BeginInvoke(new Action(() =>
                StartStage(stage)));
        }

        private void StartStage(string stage)
        {
            FinishActiveStage();
            _activeStage = stage;
            _activeStageLabel = FriendlyStage(stage);
            _stageStopwatch.Restart();
            ResultDetailText.Text = _activeStageLabel;
            AppendLog("stage=" + stage + " started");
            UpdateProgressStatus();
        }

        private void FinishActiveStage()
        {
            if (string.IsNullOrWhiteSpace(_activeStage) || !_stageStopwatch.IsRunning)
                return;

            _stageStopwatch.Stop();
            AppendLog("stage=" + _activeStage + " duration=" +
                FormatElapsed(_stageStopwatch.Elapsed));
        }

        private void UpdateProgressStatus()
        {
            if (!_busy) return;
            var label = string.IsNullOrWhiteSpace(_activeStageLabel)
                ? "正在处理"
                : _activeStageLabel;
            ProgressStatusText.Text = label + "  ·  已用时 " +
                FormatElapsed(_operationStopwatch.Elapsed);
        }

        private static string FriendlyStage(string stage)
        {
            switch (stage)
            {
                case "preflight-read":
                    return "正在读取 TIA 工程；首次/较大工程可能需要几分钟，请勿关闭窗口";
                case "compile-preparation":
                    return "正在准备 TIA 工程编译与导出；此阶段可能需要几分钟";
                case "capture-export":
                    return "正在导出工程结构与程序块";
                case "hints":
                    return "正在整理硬件、PLC 与变量信息";
                case "materialize":
                    return "正在生成 Git canonical source";
                case "validate-source":
                    return "正在校验 Git source";
                case "create-project":
                    return "正在启动 TIA Portal V21 并创建新工程；可能需要几分钟";
                case "create-device":
                    return "正在重建 S7-1200 设备";
                case "create-tags":
                    return "正在重建 PLC 变量表";
                case "import-ob1":
                    return "正在导入 Main / OB1 / LAD";
                case "save":
                    return "正在保存 TIA 工程";
                case "compile":
                case "compile-rebuilt-copy":
                    return "正在编译还原工程；TIA Portal 可能需要几分钟";
                case "publish":
                    return "正在发布最终工程到你选择的目录";
                case "export-original":
                case "export-rebuilt":
                    return "正在导出工程用于完整性验证";
                case "compare":
                    return "正在比较 canonical source";
                case "done":
                    return "正在完成最后检查";
                default:
                    return "当前阶段：" + stage;
            }
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1)
                return elapsed.ToString(@"hh\:mm\:ss");
            return elapsed.ToString(@"mm\:ss");
        }

        private void LogProjectCatalogProblems(RepositoryProjectCatalog catalog)
        {
            if (catalog == null || catalog.Problems.Count == 0) return;
            foreach (var problem in catalog.Problems)
            {
                AppendLog("repository project problem: " +
                    problem.RelativePath + " [" + problem.ProblemType + "]");
            }
        }

        private string VerifyRestoredFileIdentity(
            RoundTripProjectV1 project, string restoredProjectFile)
        {
            if (project == null || !project.OriginalSizeBytes.HasValue ||
                string.IsNullOrWhiteSpace(project.OriginalFileName) ||
                string.IsNullOrWhiteSpace(project.OriginalSha256))
                return null;

            var file = new FileInfo(restoredProjectFile);
            string sha256;
            using (var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create())
                sha256 = BitConverter.ToString(sha.ComputeHash(stream))
                    .Replace("-", string.Empty).ToLowerInvariant();

            AppendLog("binary identity expected: name=" + project.OriginalFileName +
                " size=" + project.OriginalSizeBytes.Value +
                " sha256=" + project.OriginalSha256);
            AppendLog("binary identity actual: name=" + file.Name +
                " size=" + file.Length + " sha256=" + sha256);

            if (!string.Equals(file.Name, project.OriginalFileName, StringComparison.Ordinal))
                return "还原后的 .ap21 文件名与上传前不同，TIA-Guard 已停止把它标记为完整还原。";
            if (file.Length != project.OriginalSizeBytes.Value)
                return "还原后的 .ap21 文件大小与上传前不同；语义 Verify 已通过，但二进制身份不一致。";
            if (!string.Equals(sha256, project.OriginalSha256, StringComparison.Ordinal))
                return "还原后的 .ap21 SHA-256 与上传前不同；语义 Verify 已通过，但二进制身份不一致。";

            AppendLog("binary identity: PASS");
            return null;
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

            var git = error as GitOperationException;
            if (git != null) return git.FriendlyMessage;

            if (error.Message.IndexOf("tia-source", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.Message.IndexOf("canonical source", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.Message.IndexOf("canonical source tree", StringComparison.OrdinalIgnoreCase) >= 0)
                return "所选工程的 tia-source/ 不是可安全使用的 TIA-Guard canonical source，已停止以避免覆盖未知内容。";

            if (error is IOException &&
                error.Message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
                return "目标路径已经存在。为防止覆盖工程，TIA-Guard 要求使用一个尚不存在的新目录。";

            if (error is DirectoryNotFoundException)
                return "找不到目标路径的上级目录，请先选择一个已经存在的父目录。";

            if (error.Message.IndexOf("too long", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error.Message.IndexOf("143", StringComparison.OrdinalIgnoreCase) >= 0)
                return "TIA Portal V21 的内部工程路径仍超过限制。TIA-Guard 已使用短 staging；请检查工程名称是否异常过长。";

            if (error is UnauthorizedAccessException)
                return "Windows 拒绝访问该路径，请更换目录或检查文件权限。";

            return error.Message;
        }

        private void ResetSteps()
        {
            SetStep(GitStepBorder, GitStepText, "GIT", "待命", null);
            SetStep(ExportStepBorder, ExportStepText, "EXPORT", "待命", null);
            SetStep(BuildStepBorder, BuildStepText, "BUILD", "待命", null);
            SetStep(VerifyStepBorder, VerifyStepText, "VERIFY", "待命", null);
            SetStep(PushStepBorder, PushStepText, "PUSH", "待命", null);
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

        private void OnBrowseRestoreOutput(object sender, RoutedEventArgs e)
        {
            BrowseNewFolderTarget(RestoreOutputTextBox,
                "选择还原工程的父目录", "tia-restored");
        }

        private void OnBrowsePublishProject(object sender, RoutedEventArgs e)
        {
            BrowseProject(PublishProjectTextBox);
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

        private static string CreateExportStage()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TG", "e");
            Directory.CreateDirectory(root);
            var stage = Path.Combine(root, "e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            return stage;
        }

        private void DeleteExportStage(string stage)
        {
            if (string.IsNullOrWhiteSpace(stage) || !Directory.Exists(stage)) return;
            try
            {
                var full = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar);
                var expectedParent = Path.GetFullPath(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TG", "e")).TrimEnd(Path.DirectorySeparatorChar);
                var name = Path.GetFileName(full);
                if (!string.Equals(Path.GetDirectoryName(full), expectedParent,
                        StringComparison.OrdinalIgnoreCase) ||
                    !name.StartsWith("e-", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(name.Substring(2), "N", out _))
                    throw new InvalidOperationException(
                        "Refusing to remove an unowned GUI export stage.");
                Directory.Delete(full, true);
            }
            catch (Exception error)
            {
                AppendLog("export staging cleanup failed: " + error.Message);
            }
        }

    }
}
