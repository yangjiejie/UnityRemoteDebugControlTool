using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using RemoteDebugServer.Helpers;

namespace RemoteDebugServer.ViewModels
{
    /// <summary>
    /// 协议代码生成面板 ViewModel：
    /// - 选择 proto 文件目录
    /// - 选择 protoc 可执行文件（PATH 中不存在时手动指定）
    /// - 一键生成 C# 协议代码（调用 protoc --csharp_out）
    /// </summary>
    public class ProtoGenViewModel : INotifyPropertyChanged
    {
        private string _protoDirectory = "";
        private string _outputDirectory = "";
        private string _protocPath = "";
        private string _statusText = "";
        private bool _isGenerating;

        public ProtoGenViewModel()
        {
            var s = ProtoGenSettingsStore.Load();
            _protoDirectory = s.ProtoDirectory;
            _outputDirectory = s.OutputDirectory;
            _protocPath = s.ProtocPath;

            // 输出目录默认指向本工程的 Protocol 文件夹（生成的 C# 会被编译进工具）
            if (string.IsNullOrWhiteSpace(_outputDirectory))
                _outputDirectory = DefaultOutputDirectory();

            // 未显式指定 protoc 时尝试自动探测，便于直接在界面回显实际使用的路径
            if (string.IsNullOrWhiteSpace(_protocPath))
            {
                var detected = ProtoCodeGenerator.ResolveProtoc(null);
                if (detected != null) _protocPath = detected;
            }

            // 记忆提示：首次（无记录目录）需手动指定；之后自动沿用上次记录的目录
            if (string.IsNullOrWhiteSpace(_protoDirectory))
                _statusText = "首次使用：请点击『选择目录』指定 proto 文件所在目录（之后会自动记忆该目录）。";
            else if (!Directory.Exists(_protoDirectory))
                _statusText = $"已记忆的 proto 目录不存在: {_protoDirectory}，请重新点击『选择目录』指定。";
            else
                _statusText = $"已自动使用上次记录的 proto 目录: {_protoDirectory}（直接点『生成 C# 协议代码』即可）。";

            BrowseProtoDirCommand = new RelayCommand(_ => BrowseProtoDir());
            BrowseOutputDirCommand = new RelayCommand(_ => BrowseOutputDir());
            BrowseProtocCommand = new RelayCommand(_ => BrowseProtoc());
            GenerateCommand = new RelayCommand(_ => _ = GenerateAsync(), _ => !IsGenerating);
        }

        public string ProtoDirectory
        {
            get => _protoDirectory;
            set { _protoDirectory = value; OnPropertyChanged(); Save(); }
        }

        public string OutputDirectory
        {
            get => _outputDirectory;
            set { _outputDirectory = value; OnPropertyChanged(); Save(); }
        }

        public string ProtocPath
        {
            get => _protocPath;
            set { _protocPath = value; OnPropertyChanged(); Save(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            set { _isGenerating = value; OnPropertyChanged(); }
        }

        public RelayCommand BrowseProtoDirCommand { get; }
        public RelayCommand BrowseOutputDirCommand { get; }
        public RelayCommand BrowseProtocCommand { get; }
        public RelayCommand GenerateCommand { get; }

        private void BrowseProtoDir()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择包含 .proto 文件的目录",
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(_protoDirectory) ? _protoDirectory : "",
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ProtoDirectory = dlg.SelectedPath;
                StatusText = $"已选择 proto 目录: {_protoDirectory}";
            }
        }

        private void BrowseOutputDir()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择生成的 C# 文件输出目录",
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(_outputDirectory) ? _outputDirectory : "",
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputDirectory = dlg.SelectedPath;
            }
        }

        private void BrowseProtoc()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 protoc 可执行文件",
                Filter = "protoc 可执行文件|protoc.exe|所有文件|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog() == true)
            {
                ProtocPath = dlg.FileName;
                StatusText = $"已指定 protoc: {_protocPath}";
            }
        }

        private async Task GenerateAsync()
        {
            if (IsGenerating) return;
            IsGenerating = true;
            try
            {
                var protocPath = ProtoCodeGenerator.ResolveProtoc(_protocPath);
                if (protocPath == null)
                {
                    StatusText = "✗ 未找到 protoc.exe：请将 protobuf 编译器所在目录加入 PATH，或点击『选择 protoc』手动指定。";
                    return;
                }

                // 回显实际使用的 protoc 路径
                if (!string.Equals(_protocPath, protocPath, StringComparison.OrdinalIgnoreCase))
                    ProtocPath = protocPath;

                // proto 目录：优先使用记忆的目录；首次（未记录或目录已失效）则要求手动指定
                if (string.IsNullOrWhiteSpace(_protoDirectory))
                {
                    StatusText = "✗ 尚未指定 proto 目录：请点击『选择目录』指定 .proto 文件所在位置（之后会自动记忆）。";
                    return;
                }
                if (!Directory.Exists(_protoDirectory))
                {
                    StatusText = $"✗ 已记忆的 proto 目录不存在: {_protoDirectory}，请重新点击『选择目录』指定。";
                    return;
                }

                if (string.IsNullOrWhiteSpace(_outputDirectory) || !Directory.Exists(_outputDirectory))
                {
                    StatusText = "✗ 请先选择有效的输出目录。";
                    return;
                }

                StatusText = "正在调用 protoc 生成 C# 协议代码...";
                var result = await Task.Run(() =>
                    ProtoCodeGenerator.Generate(protocPath, _protoDirectory, _outputDirectory));

                if (result.Success)
                    StatusText = $"✓ {result.Output}\n（生成后需重新生成并重启本工具，新协议才会被 ProtocolRegistry 加载）";
                else
                    StatusText = "✗ " + result.Error;
            }
            catch (Exception ex)
            {
                StatusText = "✗ 生成异常：" + ex.Message;
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private static string DefaultOutputDirectory()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            // 运行时通常在 bin/Debug/netX/，向上两级为工程根，其下 Protocol 文件夹
            var projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", ".."));
            var protocolDir = Path.Combine(projectRoot, "Protocol");
            return Directory.Exists(protocolDir) ? protocolDir : baseDir;
        }

        private void Save()
        {
            ProtoGenSettingsStore.Save(new ProtoGenSettings
            {
                ProtoDirectory = _protoDirectory,
                OutputDirectory = _outputDirectory,
                ProtocPath = _protocPath,
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
