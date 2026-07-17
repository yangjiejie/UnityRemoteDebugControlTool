using System.Windows;
using System.Windows.Controls;

// 消除 System.Windows.Forms 与 WPF 命名冲突
using MessageBox = System.Windows.MessageBox;

namespace RemoteDebugServer.ViewModels
{
    public partial class FileRequestDialog : Window
    {
        public string FileType { get; private set; } = "log";
        public string FilePath { get; private set; } = "";
        public string Wildcard { get; private set; } = "*";
        public bool Recursive { get; private set; } = false;

        public FileRequestDialog()
        {
            InitializeComponent();
        }

        private void Request_Click(object sender, RoutedEventArgs e)
        {
            FileType = ((ComboBoxItem)FileTypeCombo.SelectedItem).Tag?.ToString() ?? "log";
            FilePath = FilePathBox.Text.Trim();
            Wildcard = WildcardBox.Text.Trim();
            Recursive = RecursiveCheck.IsChecked ?? false;

            if (string.IsNullOrWhiteSpace(FilePath))
            {
                MessageBox.Show("请输入文件路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
