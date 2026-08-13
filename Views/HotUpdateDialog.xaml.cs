using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace RemoteDebugServer.ViewModels
{
    /// <summary>
    /// 触发热更新对话框。支持三种模式：
    /// - AB 包增量热更新（源目录 -> persistentData/{platform}）
    /// - 沙盒持久化目录更新（源目录 -> persistentData 根目录）
    /// - 热更散文件（多选文件 -> persistentData 根目录，按文件名）
    /// </summary>
    public partial class HotUpdateDialog : Window
    {
        /// <summary>更新类型：assetbundle / persistent / loose</summary>
        public string UpdateType { get; private set; } = "assetbundle";

        /// <summary>源目录（assetbundle / persistent 模式）</summary>
        public string SourceDirectory { get; private set; } = "";

        /// <summary>散文件模式选中的多个文件（完整路径）</summary>
        public List<string> LooseFiles { get; private set; } = new List<string>();

        public HotUpdateDialog()
        {
            InitializeComponent();
        }

        private bool IsLooseChecked => RbLoose.IsChecked == true;

        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择热更新文件所在目录",
                ShowNewFolderButton = false,
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SourceDirectory = dlg.SelectedPath;
                TxtSourceDir.Text = SourceDirectory;
                RefreshDirPreview();
            }
        }

        private void SelectLooseFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择热更散文件（可多选）",
                Multiselect = true,
                CheckFileExists = true,
                CheckPathExists = true,
            };
            if (dlg.ShowDialog() == true)
            {
                LooseFiles = dlg.FileNames.ToList();
                TxtLooseFiles.Text = LooseFiles.Count == 1
                    ? Path.GetFileName(LooseFiles[0])
                    : $"已选 {LooseFiles.Count} 个文件";
                RefreshLoosePreview();
            }
        }

        private void RefreshDirPreview()
        {
            if (string.IsNullOrEmpty(SourceDirectory) || !Directory.Exists(SourceDirectory))
            {
                TxtFilePreview.Text = "请先选择源目录";
                return;
            }
            var files = Directory.GetFiles(SourceDirectory, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                TxtFilePreview.Text = "（目录为空，将不会推送任何文件）";
                return;
            }
            var lines = files.Select(f => Path.GetRelativePath(SourceDirectory, f)).ToList();
            TxtFilePreview.Text = $"共 {files.Length} 个文件：\n" + string.Join("\n", lines.Take(50));
            if (lines.Count > 50) TxtFilePreview.Text += $"\n… 其余 {lines.Count - 50} 个文件省略";
        }

        private void RefreshLoosePreview()
        {
            if (LooseFiles.Count == 0)
            {
                TxtLoosePreview.Text = "散文件模式将逐个以文件名写入持久化根目录（不保留子目录）。";
                return;
            }
            var lines = LooseFiles.Select(Path.GetFileName).ToList();
            TxtLoosePreview.Text = $"共 {LooseFiles.Count} 个文件：\n" + string.Join("\n", lines.Take(50));
            if (lines.Count > 50) TxtLoosePreview.Text += $"\n… 其余 {lines.Count - 50} 个文件省略";
        }

        private void Execute_Click(object sender, RoutedEventArgs e)
        {
            if (RbAssetBundle.IsChecked == true)
            {
                UpdateType = "assetbundle";
                if (string.IsNullOrEmpty(SourceDirectory) || !Directory.Exists(SourceDirectory))
                {
                    System.Windows.MessageBox.Show("请先选择有效的源目录。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (RbPersistentData.IsChecked == true)
            {
                UpdateType = "persistent";
                if (string.IsNullOrEmpty(SourceDirectory) || !Directory.Exists(SourceDirectory))
                {
                    System.Windows.MessageBox.Show("请先选择有效的源目录。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else // RbLoose
            {
                UpdateType = "loose";
                if (LooseFiles.Count == 0)
                {
                    System.Windows.MessageBox.Show("请先选择至少一个散文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            DialogResult = true;
            Close();
        }
    }
}
