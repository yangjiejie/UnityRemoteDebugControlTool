

using System;
using System.Collections.Concurrent;
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
using RemoteDebugServer.Helpers;
using RemoteDebugServer.Models;
using RemoteDebugServer.Services;

// 消除 System.Windows.Forms 与 WPF 的命名冲突
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace RemoteDebugServer.ViewModels
{
    /// <summary>
    /// 主窗口ViewModel - MVVM核心
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private TcpServerService _serverService;

        // 等待客户端指令结果的 TaskCompletionSource，key = CommandId
        private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandResult>> _pendingCommands = new();

        // 绑定属性
        private int _port = 19527;
       
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

        // 日志纯文本显示（绑定到单一 TextBox，支持任意跨行选取）
        private string _logDisplayText = "";
        public string LogDisplayText
        {
            get => _logDisplayText;
            set { _logDisplayText = value; OnPropertyChanged(); }
        }

        public bool HasDevices => Devices.Count > 0;

        public int Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(); }
        }

    

        public string LocalIP
        {
            get => _localIP;
        }

     

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
            set 
            { 
                if (_selectedDevice != null) _selectedDevice.IsSelected = false;
                _selectedDevice = value; 
                if (_selectedDevice != null) _selectedDevice.IsSelected = true;
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IsDeviceSelected)); 
                UpdateFilteredLogs(); 
            }
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
        public ICommand RestartGameCommand { get; }
        public ICommand HotUpdateCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand CopyLogsCommand { get; }
        public ICommand OpenFileDirectoryCommand { get; }

        // 场景树更新事件（通知 View 刷新 UI）
        public event Action<SceneTreeData>? OnSceneTreeUpdated;

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
            RestartGameCommand = new RelayCommand(async _ => await SendCommand("RestartGame"), _ => IsDeviceSelected);
            HotUpdateCommand = new RelayCommand(async _ => await TriggerHotUpdate(), _ => IsDeviceSelected);
            ClearLogsCommand = new RelayCommand(_ => ClearLogs());
            CopyLogsCommand = new RelayCommand(_ => CopyAllLogs());
            OpenFileDirectoryCommand = new RelayCommand(_ => OpenFileDirectory());
        }

        private TcpServerService CreateServerService()
        {
            var svc = new TcpServerService(_port, LocalIP);
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

        public async Task<bool> SendCommand(string command, string parameters = "")
        {
            if (_selectedDevice == null) return false;

            var (result, _) = await _serverService.SendCommandAsync(_selectedDevice.DeviceId, command, parameters);
            AppendServerLog(result ? $"指令已发送: {command}" : $"发送失败: {command}");

            if (!CommandHistory.Contains(command))
                CommandHistory.Insert(0, command);

            return result;
        }

        /// <summary>
        /// 发送指令并等待客户端返回 CommandResult（带超时）
        /// 返回 null 表示发送失败或超时
        /// </summary>
        public async Task<CommandResult?> SendCommandAndWaitAsync(string command, string parameters = "", int timeoutMs = 10000)
        {
            if (_selectedDevice == null) return null;

            // 先生成 CommandId 并注册 tcs，再发送命令，避免响应先于注册到达
            string commandId = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingCommands[commandId] = tcs;

            AppendServerLog($"等待客户端响应: {command} [{commandId[..8]}]");

            var (sent, _) = await _serverService.SendCommandAsync(_selectedDevice.DeviceId, command, parameters, commandId);
            if (!sent)
            {
                _pendingCommands.TryRemove(commandId, out _);
                return null;
            }

            try
            {
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
                if (completed == tcs.Task)
                {
                    var result = tcs.Task.Result;
                    AppendServerLog($"[指令结果] {_selectedDevice.Alias}: {(result.Success ? "成功" : "失败")} - {result.Message}");
                    return result;
                }
                else
                {
                    AppendServerLog($"[超时] {command} 等待客户端响应超时 ({timeoutMs}ms)");
                    return null;
                }
            }
            finally
            {
                _pendingCommands.TryRemove(commandId, out _);
            }
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

        public async Task TriggerHotUpdate()
        {
            if (_selectedDevice == null) return;

            var dialog = new HotUpdateDialog();
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
            {
                string updateType = dialog.SelectedUpdateType;
                string sourceDir = dialog.SourceDirectory;
                string fileType = updateType == "PersistentData" ? "hotupdate_persistent" : "hotupdate_ab";
                string label = updateType == "PersistentData" ? "沙盒全量更新" : "AB包增量更新";

                // 枚举源目录所有文件
                var files = System.IO.Directory.GetFiles(sourceDir, "*", System.IO.SearchOption.AllDirectories);
                if (files.Length == 0)
                {
                    AppendServerLog($"热更新失败: 源目录为空");
                    return;
                }

                AppendServerLog($"开始 {label} -> {_selectedDevice.Alias} ({files.Length} 个文件)");

                // 发送指令通知客户端准备，并等待客户端确认就绪
                var cmdResult = await SendCommandAndWaitAsync("TriggerHotUpdate",
                    $"{{\"updateType\":\"{updateType}\"}}");
                if (cmdResult == null)
                {
                    AppendServerLog($"热更新失败: 客户端未响应");
                    return;
                }
                if (!cmdResult.Success)
                {
                    AppendServerLog($"热更新失败: 客户端准备失败 - {cmdResult.Message}");
                    return;
                }

                // 逐个发送文件
                int sent = 0;
                int failed = 0;
                foreach (var filePath in files)
                {
                    string relativePath = GetRelativePath(sourceDir, filePath);
                    bool ok = await _serverService.SendFileToDeviceAsync(
                        _selectedDevice.DeviceId, filePath, relativePath, fileType);
                    if (ok)
                    {
                        sent++;
                        AppendServerLog($"  已发送 ({sent}/{files.Length}): {relativePath}");
                    }
                    else
                    {
                        failed++;
                        AppendServerLog($"  发送失败: {relativePath}");
                    }

                    // 小延迟避免网络拥塞
                    await Task.Delay(50);
                }

                AppendServerLog($"热更新完成: 成功 {sent}, 失败 {failed} -> {_selectedDevice.Alias}");
            }
        }

        private static string GetRelativePath(string rootDir, string fullPath)
        {
            // 统一用 / 分割，兼容跨平台
            string root = rootDir.Replace('\\', '/').TrimEnd('/');
            string full = fullPath.Replace('\\', '/');

            if (full.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return full.Substring(root.Length + 1);

            return System.IO.Path.GetFileName(full);
        }

        #endregion

        #region 日志过滤

        private void UpdateFilteredLogs()
        {
            FilteredLogs.Clear();

            if (_selectedDevice == null)
            {
                LogDisplayText = "";
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var log in _selectedDevice.Logs)
            {
                if (!ShouldShowLog(log)) continue;

                if (!string.IsNullOrEmpty(_logFilterText) &&
                    !log.Message.Contains(_logFilterText, StringComparison.OrdinalIgnoreCase))
                    continue;

                FilteredLogs.Add(log);
                sb.AppendLine($"{log.Time:HH:mm:ss.fff} {log.Level} {log.Message}");
            }

            LogDisplayText = sb.ToString();
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
                LogDisplayText = "";
            }
        }

        private void CopyAllLogs()
        {
            if (FilteredLogs.Count == 0)
            {
                MessageBox.Show("当前没有日志可复制", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var log in FilteredLogs)
            {
                sb.AppendLine($"{log.Time:HH:mm:ss.fff} {log.Level} {log.Message}");
            }

            try
            {
                Clipboard.SetText(sb.ToString());
                AppendServerLog($"已复制 {FilteredLogs.Count} 条日志到剪贴板");
            }
            catch (Exception ex)
            {
                AppendServerLog($"复制失败: {ex.Message}");
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
                // 重连场景：设备对象已被服务端复用，已经在列表中（不变更位置，不影响选中状态）
                if (Devices.Contains(device))
                {
                    AppendServerLog($"设备重连: {device.Alias} ({device.IpAddress})");
                    return;
                }

                // 新设备首次连接
                var savedAlias = DeviceAliasStore.Load(device.DeviceId, device.DeviceName);
                if (!string.IsNullOrEmpty(savedAlias))
                {
                    device.SetAliasSilently(savedAlias);
                }
                device.AliasChanged += (_, alias) =>
                {
                    DeviceAliasStore.Save(device.DeviceId, device.DeviceName, alias);
                };

                InsertDeviceSorted(device);
                OnPropertyChanged(nameof(HasDevices));
                AppendServerLog($"设备已连接: {device.Alias} ({device.IpAddress})");
            });
        }

        /// <summary>
        /// 按 ConnectedTime 升序插入设备，保证连接顺序稳定不跳变
        /// </summary>
        private void InsertDeviceSorted(ConnectedDevice device)
        {
            int index = 0;
            for (; index < Devices.Count; index++)
            {
                if (Devices[index].ConnectedTime > device.ConnectedTime)
                    break;
            }
            Devices.Insert(index, device);
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
                    LogDisplayText = "";
                }
                AppendServerLog($"✗ 设备断开: {device.Alias}");
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
                    LogDisplayText += $"{log.Time:HH:mm:ss.fff} {log.Level} {log.Message}{Environment.NewLine}";
                }
            });
        }

        private void OnSceneTreeReceived(ConnectedDevice device, SceneTreeData tree)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AppendServerLog($"收到场景树: {device.Alias} ({tree.Scenes.Count} 个场景)");
                OnSceneTreeUpdated?.Invoke(tree);
            });
        }

        private void OnCommandResult(ConnectedDevice device, CommandResult result)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 如果有等待该 CommandId 的 TaskCompletionSource，先触发它
                if (_pendingCommands.TryRemove(result.CommandId, out var tcs))
                {
                    tcs.TrySetResult(result);
                }
                else
                {
                    string status = result.Success ? "成功" : "失败";
                    AppendServerLog($"[指令结果] {device.Alias}: {status} - {result.Message}");
                }
            });
        }

        private void OnFileReceived(ConnectedDevice device, string filePath, string fileType)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                AppendServerLog($"[文件] {device.Alias}: {Path.GetFileName(filePath)} ({fileType})");
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
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
           

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

