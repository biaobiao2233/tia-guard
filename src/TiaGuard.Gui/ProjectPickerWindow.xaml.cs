using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TiaGuard.Openness;

namespace TiaGuard.Gui
{
    internal enum ProjectPickerPurpose
    {
        Restore,
        Publish
    }

    internal sealed class ProjectPickerItem
    {
        internal RepositoryProjectSource Source { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
    }

    public partial class ProjectPickerWindow : Window
    {
        private readonly RepositoryProjectCatalog _catalog;
        private readonly ProjectPickerPurpose _purpose;

        internal RepositoryProjectSource SelectedProject { get; private set; }
        internal string NewSlot { get; private set; }
        internal bool CreateNew { get; private set; }

        internal ProjectPickerWindow(
            RepositoryProjectCatalog catalog,
            ProjectPickerPurpose purpose,
            string suggestedSlot = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _purpose = purpose;
            InitializeComponent();

            var items = catalog.Projects
                .OrderBy(value => value.IsLegacy ? 0 : 1)
                .ThenBy(value => value.Manifest?.Project?.Name, StringComparer.OrdinalIgnoreCase)
                .Select(value => new ProjectPickerItem
                {
                    Source = value,
                    Title = value.Manifest?.Project?.Name ?? "(未命名工程)",
                    Detail = BuildDetail(value)
                })
                .ToList();

            ProjectList.ItemsSource = items;
            if (items.Count > 0) ProjectList.SelectedIndex = 0;

            if (purpose == ProjectPickerPurpose.Restore)
            {
                Title = "选择要还原的 TIA 工程";
                TitleText.Text = "选择要还原的工程";
                DescriptionText.Text =
                    "这个 Git 仓库包含多个 TIA-Guard 工程。选择其中一个，只会构建该工程。";
                NewProjectPanel.Visibility = Visibility.Collapsed;
                AddNewButton.Visibility = Visibility.Collapsed;
                UseExistingButton.Content = "还原所选工程";
                if (items.Count == 0) UseExistingButton.IsEnabled = false;
            }
            else
            {
                Title = "选择发布目标";
                TitleText.Text = "发布到哪个工程槽位？";
                DescriptionText.Text =
                    "可以更新仓库中的现有工程，也可以添加新工程。TIA-Guard 只会修改所选工程自己的 tia-source/。";
                NewSlotTextBox.Text = suggestedSlot ?? string.Empty;
                UseExistingButton.Content = "更新所选工程";
                if (items.Count == 0) UseExistingButton.IsEnabled = false;
            }

            if (catalog.Problems.Count != 0)
            {
                ProblemText.Text =
                    "检测到 " + catalog.Problems.Count +
                    " 个损坏或不完整的工程槽位；它们不会阻止其他正常工程，但不会被当作可还原工程。";
            }
            else
            {
                ProblemText.Visibility = Visibility.Collapsed;
            }

            StatusText.Text = items.Count == 0
                ? "当前仓库还没有可用的 TIA-Guard 工程。"
                : "已发现 " + items.Count + " 个可用工程。";
        }

        private static string BuildDetail(RepositoryProjectSource value)
        {
            var project = value.Manifest?.Project;
            var file = string.IsNullOrWhiteSpace(project?.OriginalFileName)
                ? "原文件名：旧版 source 未记录"
                : "原文件：" + project.OriginalFileName;
            var location = value.IsLegacy
                ? "位置：tia-source/（旧版单工程布局）"
                : "位置：" + value.RelativeSourcePath + "/";
            return file + "  ·  TIA " + (value.Manifest?.TiaVersion ?? "?") +
                   Environment.NewLine + location;
        }

        private void OnUseExistingClick(object sender, RoutedEventArgs e)
        {
            var item = ProjectList.SelectedItem as ProjectPickerItem;
            if (item == null)
            {
                StatusText.Text = "请先选择一个工程。";
                return;
            }

            SelectedProject = item.Source;
            CreateNew = false;
            DialogResult = true;
        }

        private void OnAddNewClick(object sender, RoutedEventArgs e)
        {
            if (_purpose != ProjectPickerPurpose.Publish) return;
            var slot = (NewSlotTextBox.Text ?? string.Empty).Trim();
            if (!RepositorySourceManager.IsSafeProjectSlot(slot))
            {
                StatusText.Text = "工程槽位名称不安全；请使用普通文字、数字、短横线，且不要使用路径字符。";
                NewSlotTextBox.Focus();
                return;
            }

            if (_catalog.Projects.Any(value =>
                    string.Equals(value.Slot, slot, StringComparison.OrdinalIgnoreCase)) ||
                _catalog.Problems.Any(value =>
                    string.Equals(value.Slot, slot, StringComparison.OrdinalIgnoreCase)))
            {
                StatusText.Text = "这个工程槽位已经存在，请选择现有工程或换一个槽位名称。";
                NewSlotTextBox.Focus();
                return;
            }

            NewSlot = slot;
            CreateNew = true;
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
