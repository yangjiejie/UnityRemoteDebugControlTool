using System.IO;
using System.Linq;
using System.Windows;

// 消除 System.Windows.Forms 与 WPF 命名冲突
using MessageBox = System.Windows.MessageBox;

namespace RemoteDebugServer.ViewModels
{
    public partial class HotUpdateDialog : Window
    {
        /// <summary>
        /// "AssetBundle" = AB包增量 / "PersistentData" = 沙盒全量
        /// </summary>
        public string SelectedUpdateType { get; private set; } = "AssetBundle";

        /// <summary>
        /// 服务端源目录路径
        /// </summary>
        public string SourceDirectory { get; private set; } = "";

        public HotUpdateDialog()
        {
            InitializeComponent();
        }

        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择热更新源目录",
                ShowNewFolderButton = false,
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtSourceDir.Text = dlg.SelectedPath;
                PreviewFiles(dlg.SelectedPath);
            }
        }

        private void PreviewFiles(string dirPath)
        {
            try
            {
                var files = Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories);
                int count = files.Length;
                long totalSize = files.Sum(f => new FileInfo(f).Length);

                if (count == 0)
                {
                    TxtFilePreview.Text = "⚠ 所选目录为空，没有文件可传输";
                }
                else
                {
                    string sizeStr = totalSize < 1024 * 1024
                        ? $"{totalSize / 1024f:F1} KB"
                        : $"{totalSize / (1024f * 1024f):F1} MB";
                    TxtFilePreview.Text = $"📦 {count} 个文件, 共 {sizeStr}\n源目录: {dirPath}";
                }
            }
            catch
            {
                TxtFilePreview.Text = "⚠ 无法读取目录内容";
            }
        }

        private void Execute_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtSourceDir.Text))
            {
                MessageBox.Show("请先选择源目录", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(TxtSourceDir.Text))
            {
                MessageBox.Show("所选目录不存在", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SelectedUpdateType = RbPersistentData.IsChecked == true ? "PersistentData" : "AssetBundle";
            SourceDirectory = TxtSourceDir.Text;
            DialogResult = true;
            Close();
        }
    }
}
