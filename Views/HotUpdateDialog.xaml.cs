using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace RemoteDebugServer.ViewModels
{
    /// <summary>
    /// 热更新文件条目：本地路径 + 在目标端保留的相对路径
    /// </summary>
    public class HotUpdateFileEntry
    {
        public string LocalPath { get; set; } = "";
        public string RelativePath { get; set; } = "";
    }

    /// <summary>
    /// 触发热更新对话框。支持三种模式：
    /// - AB 包增量热更新（源目录 -> persistentData/{platform}）
    /// - 沙盒持久化目录更新（源目录 -> persistentData 根目录）
    /// - 热更散文件/文件夹（批量多选 -> persistentData 根目录，文件放根目录，文件夹保留结构）
    /// </summary>
    public partial class HotUpdateDialog : Window, INotifyPropertyChanged
    {
        /// <summary>更新类型：assetbundle / persistent / loose</summary>
        public string UpdateType { get; private set; } = "assetbundle";

        /// <summary>源目录（assetbundle / persistent 模式）</summary>
        public string SourceDirectory { get; private set; } = "";

        /// <summary>散文件模式下选中的文件/文件夹条目（完整路径）</summary>
        public ObservableCollection<string> LooseEntries { get; } = new ObservableCollection<string>();

        /// <summary>
        /// 散文件模式下展开后的所有文件条目：
        /// - 单个文件直接放到目标根目录（RelativePath 为文件名）
        /// - 文件夹保留其内部目录结构（RelativePath 包含文件夹名）
        /// </summary>
        public IReadOnlyList<HotUpdateFileEntry> ExpandedFiles
        {
            get
            {
                var dict = new Dictionary<string, HotUpdateFileEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in LooseEntries)
                {
                    if (File.Exists(entry))
                    {
                        string rel = Path.GetFileName(entry);
                        dict[rel] = new HotUpdateFileEntry
                        {
                            LocalPath = entry,
                            RelativePath = rel,
                        };
                    }
                    else if (Directory.Exists(entry))
                    {
                        string folderName = Path.GetFileName(entry.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        foreach (var file in Directory.GetFiles(entry, "*", SearchOption.AllDirectories))
                        {
                            string rel = Path.Combine(folderName, Path.GetRelativePath(entry, file)).Replace('\\', '/');
                            dict[rel] = new HotUpdateFileEntry
                            {
                                LocalPath = file,
                                RelativePath = rel,
                            };
                        }
                    }
                }
                return dict.Values.ToList();
            }
        }

        public HotUpdateDialog()
        {
            InitializeComponent();
            DataContext = this;
            LstLooseEntries.ItemsSource = LooseEntries;
            LooseEntries.CollectionChanged += (_, _) => RefreshLoosePreview();
            RbAssetBundle.Checked += (_, _) => UpdateMode();
            RbPersistentData.Checked += (_, _) => UpdateMode();
            RbLoose.Checked += (_, _) => UpdateMode();
        }

        public bool IsLooseChecked => RbLoose.IsChecked == true;
        public Visibility SourcePreviewVisibility => IsLooseChecked ? Visibility.Collapsed : Visibility.Visible;
        public Visibility LooseVisibility => IsLooseChecked ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void UpdateMode()
        {
            OnPropertyChanged(nameof(IsLooseChecked));
            OnPropertyChanged(nameof(SourcePreviewVisibility));
            OnPropertyChanged(nameof(LooseVisibility));
        }

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

        private void AddLooseFiles_Click(object sender, RoutedEventArgs e)
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
                foreach (var file in dlg.FileNames)
                {
                    if (!LooseEntries.Contains(file, StringComparer.OrdinalIgnoreCase))
                        LooseEntries.Add(file);
                }
            }
        }

        private void AddLooseFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择热更散文件夹（会递归推送该文件夹下所有文件）",
                ShowNewFolderButton = false,
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (!LooseEntries.Contains(dlg.SelectedPath, StringComparer.OrdinalIgnoreCase))
                    LooseEntries.Add(dlg.SelectedPath);
            }
        }

        private void RemoveLooseSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = LstLooseEntries.SelectedItems.Cast<string>().ToList();
            foreach (var item in selected)
                LooseEntries.Remove(item);
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
            if (LooseEntries.Count == 0)
            {
                TxtLoosePreview.Text = "散文件模式：文件直接写入持久化根目录，文件夹保留子目录结构。\n点击“添加文件…”或“添加文件夹…”批量选择。";
                return;
            }

            var entries = ExpandedFiles;
            var lines = entries.Select(e => e.RelativePath).ToList();
            var summary = $"条目 {LooseEntries.Count} 个，展开后共 {entries.Count} 个文件：\n" + string.Join("\n", lines.Take(50));
            if (lines.Count > 50) summary += $"\n… 其余 {lines.Count - 50} 个文件省略";
            TxtLoosePreview.Text = summary;
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
                if (LooseEntries.Count == 0)
                {
                    System.Windows.MessageBox.Show("请先添加至少一个文件或文件夹。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (ExpandedFiles.Count == 0)
                {
                    System.Windows.MessageBox.Show("选中的文件夹为空，或未包含有效文件。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            DialogResult = true;
            Close();
        }
    }
}
