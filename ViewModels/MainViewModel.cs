using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using RemoteDebugServer.Models;
using RemoteDebugServer.Services;

namespace RemoteDebugServer.ViewModels
{
    /// <summary>
    /// 主窗口ViewModel - MVVM核心
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private TcpServerService _serverService;

        // 绑定属性
        private int _port = 19527;
        private string _bindAddress = "0.0.0.0";
        private string _localIP = GetLocalIPAddress();
        private bool _isServerRunning;
        private string _statusText = "服务器未启动";
        private ConnectedDevice? _selectedDevice;
        private string _commandText = "";
        private string _commandParameters = "";
        private string _serverLogText = "";

        // 日志过滤
        private bool _showLogs = true;
        private bool _showWarnings = true;
        private bool _showErrors = true;
        private string _logFilterText = "";

        public ObservableCollection<ConnectedDevice> Devices { get; } = new();
        public ObservableCollection<LogDisplayEntry> FilteredLogs { get; } = new();
        public ObservableCollection<string> CommandHistory { get; } = new();

        public bool HasDevices => Devices.Count > 0;

        public int Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(); }
        }

        public string BindAddress
        {
            get => _bindAddress;
            set { _bindAddress = value; OnPropertyChanged(); }
        }

        public string LocalIP
        {
            get => _localIP;
        }

        public string ConnectionInfo => $"ws://{_localIP}:{_port}";

        public bool IsServerRunning
        {
            get => _isServerRunning;
            set { _isServerRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsServerStopped)); }
        }

        public bool IsServerStopped => !_isServerRunning;

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public ConnectedDevice? SelectedDevice
        {
            get => _selectedDevice;
            set { _selectedDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDeviceSelected)); UpdateFilteredLogs(); }
        }

        public bool IsDeviceSelected => _selectedDevice != null;

        public string CommandText
        {
            get => _commandText;
            set { _commandText = value; OnPropertyChanged(); }
        }

        public string CommandParameters
        {
            get => _commandParameters;
            set { _commandParameters = value; OnPropertyChanged(); }
        }

        public string ServerLogText
        {
            get => _serverLogText;
            set { _serverLogText = value; OnPropertyChanged(); }
        }

        public bool ShowLogs
        {
            get => _showLogs;
            set { _showLogs = value; OnPropertyChanged(); UpdateFilteredLogs(); }
        }

        public bool ShowWarnings
        {
            get => _showWarnings;
            set { _showWarnings = value; OnPropertyChanged(); UpdateFilteredLogs(); }
        }

        public bool ShowErrors
        {
            get => _showErrors;
            set { _showErrors = value; OnPropertyChanged(); UpdateFilteredLogs(); }
        }

        public string LogFilterText
        {
            get => _logFilterText;
            set { _logFilterText = value; OnPropertyChanged(); UpdateFilteredLogs(); }
        }

        // 指令
        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand SendCommandCommand { get; }
        public ICommand RequestSceneTreeCommand { get; }
        public ICommand RequestLogsCommand { get; }
        public ICommand RequestScreenshotCommand { get; }
        public ICommand ClearCacheCommand { get; }
        public ICommand PingDeviceCommand { get; }
        public ICommand SendFileRequestCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand OpenFileDirectoryCommand { get; }

        public MainViewModel()
        {
            _serverService = CreateServerService();

            StartServerCommand = new RelayCommand(async _ => await StartServer(), _ => !IsServerRunning);
            StopServerCommand = new RelayCommand(async _ => await StopServer(), _ => IsServerRunning);
            SendCommandCommand = new RelayCommand(async _ => await SendCustomCommand(), _ => IsDeviceSelected);
            RequestSceneTreeCommand = new RelayCommand(async _ => await SendCommand("GetSceneTree"), _ => IsDeviceSelected);
            RequestLogsCommand = new RelayCommand(async _ => await SendCommand("SendLogFiles"), _ => IsDeviceSelected);
            RequestScreenshotCommand = new RelayCommand(async _ => await SendCommand("TakeScreenshot"), _ => IsDeviceSelected);
            ClearCacheCommand = new RelayCommand(async _ => await SendCommand("ClearCache"), _ => IsDeviceSelected);
            PingDeviceCommand = new RelayCommand(async _ => await SendCommand("Ping"), _ => IsDeviceSelected);
            SendFileRequestCommand = new RelayCommand(async _ => await SendFileRequest(), _ => IsDeviceSelected);
            ClearLogsCommand = new RelayCommand(_ => ClearLogs());
            OpenFileDirectoryCommand = new RelayCommand(_ => OpenFileDirectory());
        }

        private TcpServerService CreateServerService()
        {
            var svc = new TcpServerService(_port, _bindAddress);
            svc.OnDeviceConnected += OnDeviceConnected;
            svc.OnDeviceDisconnected += OnDeviceDisconnected;
            svc.OnLogReceived += OnLogReceived;
            svc.OnSceneTreeReceived += OnSceneTreeReceived;
            svc.OnCommandResultReceived += OnCommandResult;
            svc.OnFileReceived += OnFileReceived;
            svc.OnServerLog += OnServerLog;
            return svc;
        }

        #region 服务器操作

        public async Task StartServer()
        {
            try
            {
                // 确保旧的实例被清理
                _serverService?.Dispose();
                var svc = CreateServerService();
                _serverService = svc;

                await svc.StartAsync();
                IsServerRunning = true;
                StatusText = $"服务器运行中 - 端口 {_port}";
            }
            catch (Exception ex)
            {
                StatusText = $"启动失败: {ex.Message}";
                MessageBox.Show($"启动服务器失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task StopServer()
        {
            await _serverService.StopAsync();
            IsServerRunning = false;
            StatusText = "服务器已停止";
            Devices.Clear();
        }

        #endregion

        #region 指令发送

        public async Task SendCommand(string command, string parameters = "")
        {
            if (_selectedDevice == null) return;

            var result = await _serverService.SendCommandAsync(_selectedDevice.DeviceId, command, parameters);
            AppendServerLog(result ? $"指令已发送: {command}" : $"发送失败: {command}");

            if (!CommandHistory.Contains(command))
                CommandHistory.Insert(0, command);
        }

        public async Task SendCustomCommand()
        {
            if (string.IsNullOrWhiteSpace(CommandText) || _selectedDevice == null) return;
            await SendCommand(CommandText.Trim(), CommandParameters);
            CommandText = "";
            CommandParameters = "";
        }

        public async Task SendFileRequest()
        {
            if (_selectedDevice == null) return;

            var dialog = new FileRequestDialog();
            if (dialog.ShowDialog() == true)
            {
                if (!string.IsNullOrEmpty(dialog.FilePath))
                {
                    await _serverService.RequestFileAsync(
                        _selectedDevice.DeviceId, dialog.FilePath,
                        dialog.FileType, dialog.Recursive, dialog.Wildcard);
                }
            }
        }

        #endregion

        #region 日志过滤

        private void UpdateFilteredLogs()
        {
            FilteredLogs.Clear();

            if (_selectedDevice == null) return;

            foreach (var log in _selectedDevice.Logs)
            {
                if (!ShouldShowLog(log)) continue;

                if (!string.IsNullOrEmpty(_logFilterText) &&
                    !log.Message.Contains(_logFilterText, StringComparison.OrdinalIgnoreCase))
                    continue;

                FilteredLogs.Add(log);
            }
        }

        private bool ShouldShowLog(LogDisplayEntry log)
        {
            return log.Level switch
            {
                "[LOG]" => _showLogs,
                "[WARN]" => _showWarnings,
                "[ERROR]" => _showErrors,
                "[EXCEPTION]" => _showErrors,
                "[ASSERT]" => _showErrors,
                _ => true,
            };
        }

        private void ClearLogs()
        {
            if (_selectedDevice != null)
            {
                _selectedDevice.Logs.Clear();
                FilteredLogs.Clear();
            }
        }

        private void OpenFileDirectory()
        {
            string path = _serverService.ReceivedFilesDirectory;
            if (Directory.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", path);
            else
                MessageBox.Show($"目录不存在: {path}");
        }

        #endregion

        #region 回调处理

        private void OnDeviceConnected(ConnectedDevice device)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Devices.Add(device);
                OnPropertyChanged(nameof(HasDevices));
                AppendServerLog($"✓ 设备已连接: {device.DeviceName} ({device.IpAddress})");
            });
        }

        private void OnDeviceDisconnected(ConnectedDevice device)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Devices.Remove(device);
                OnPropertyChanged(nameof(HasDevices));
                if (_selectedDevice == device)
                {
                    SelectedDevice = null;
                    FilteredLogs.Clear();
                }
                AppendServerLog($"✗ 设备断开: {device.DeviceName}");
            });
        }

        private void OnLogReceived(ConnectedDevice device, LogDisplayEntry log)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_selectedDevice == device && ShouldShowLog(log) &&
                    (string.IsNullOrEmpty(_logFilterText) ||
                     log.Message.Contains(_logFilterText, StringComparison.OrdinalIgnoreCase)))
                {
                    FilteredLogs.Add(log);
                }
            });
        }

        private void OnSceneTreeReceived(ConnectedDevice device, SceneTreeData tree)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AppendServerLog($"收到场景树: {device.DeviceName} ({tree.Scenes.Count} 个场景)");
            });
        }

        private void OnCommandResult(ConnectedDevice device, CommandResult result)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string status = result.Success ? "成功" : "失败";
                AppendServerLog($"[指令结果] {device.DeviceName}: {status} - {result.Message}");
            });
        }

        private void OnFileReceived(ConnectedDevice device, string filePath, string fileType)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AppendServerLog($"[文件] {device.DeviceName}: {Path.GetFileName(filePath)} ({fileType})");
            });
        }

        private void OnServerLog(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AppendServerLog(message);
            });
        }

        private void AppendServerLog(string message)
        {
            ServerLogText += message + Environment.NewLine;
            // 限制日志长度
            if (ServerLogText.Length > 50000)
            {
                ServerLogText = ServerLogText.Substring(ServerLogText.Length - 40000);
            }
        }

        #endregion

        #region 工具方法

        /// <summary>
        /// 自动获取本机最佳IP地址（优先以太网，其次WiFi，最后回环）
        /// </summary>
        public static string GetLocalIPAddress()
        {
            try
            {
                // 获取所有活动网络接口
                var activeInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .OrderByDescending(n => n.Speed)
                    .ToList();

                foreach (var ni in activeInterfaces)
                {
                    var props = ni.GetIPProperties();
                    var ipv4 = props.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                                             && !IPAddress.IsLoopback(a.Address));
                    if (ipv4 != null)
                        return ipv4.Address.ToString();
                }

                // 回退：使用 Dns 方式
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var fallback = host.AddressList
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                                         && !IPAddress.IsLoopback(a));
                if (fallback != null)
                    return fallback.ToString();
            }
            catch { }

            return "127.0.0.1";
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        #endregion

        public void Dispose()
        {
            _serverService?.Dispose();
        }
    }

    /// <summary>
    /// 简单的RelayCommand实现
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Func<object?, Task>? _asyncExecute;
        private readonly Action<object?>? _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Func<object?, Task> asyncExecute, Func<object?, bool>? canExecute = null)
        {
            _asyncExecute = asyncExecute;
            _canExecute = canExecute;
        }

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public async void Execute(object? parameter)
        {
            if (_asyncExecute != null)
                await _asyncExecute(parameter);
            else
                _execute?.Invoke(parameter);
        }
    }
}
