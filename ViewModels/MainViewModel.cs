

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
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
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

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
        private string _appVersion = GetAppVersion();
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
        private bool _logViewActive; // 日志页是否可见，仅在可见时才重建/更新日志文本框

        // 正在录制视频的设备 DeviceId 集合
        private readonly HashSet<string> _recordingDevices = new();

        public ObservableCollection<ConnectedDevice> Devices { get; } = new();
        public List<LogDisplayEntry> FilteredLogs { get; } = new();
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

        /// <summary>本工具自身的版本（对应 Unity 的 Application.version，取自程序集信息版本）。</summary>
        public string AppVersion
        {
            get => _appVersion;
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
                OnPropertyChanged(nameof(IsRecordingForSelectedDevice));
                OnPropertyChanged(nameof(RecordButtonContent));
                UpdateFilteredLogs(); 
            }
        }

        public bool IsDeviceSelected => _selectedDevice != null;

        /// <summary>当前选中设备是否正在录制视频</summary>
        public bool IsRecordingForSelectedDevice => _selectedDevice != null && _recordingDevices.Contains(_selectedDevice.DeviceId);

        /// <summary>录制视频按钮显示文本</summary>
        public string RecordButtonContent => IsRecordingForSelectedDevice ? "⏹ 停止" : "🎥 录制";

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

        /// <summary>
        /// 日志页是否可见。进入日志页时立即按需重建日志文本，离开后停止更新，
        /// 避免在非日志页选中设备或实时收日志时仍反复拼接大字符串并重排文本框。
        /// </summary>
        public bool LogViewActive
        {
            get => _logViewActive;
            set
            {
                if (_logViewActive == value) return;
                _logViewActive = value;
                OnPropertyChanged();
                if (_logViewActive) UpdateFilteredLogs();
            }
        }

        // 指令
        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand SendCommandCommand { get; }
        public ICommand RequestSceneTreeCommand { get; }
        public ICommand RequestLogsCommand { get; }
        public ICommand RequestScreenshotCommand { get; }
        public ICommand RecordVideoCommand { get; }
        public ICommand ClearCacheCommand { get; }
        public ICommand PingDeviceCommand { get; }
        public ICommand SendFileRequestCommand { get; }
        public ICommand RestartGameCommand { get; }
        public ICommand HotUpdateCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ClearServerLogCommand { get; }
        public ICommand CopyLogsCommand { get; }
        public ICommand SaveLogsCommand { get; }
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
            RecordVideoCommand = new RelayCommand(async _ => await ToggleRecordingAsync(), _ => IsDeviceSelected);
            ClearCacheCommand = new RelayCommand(async _ => await SendCommand("ClearCache"), _ => IsDeviceSelected);
            PingDeviceCommand = new RelayCommand(async _ => await SendCommand("Ping"), _ => IsDeviceSelected);
            SendFileRequestCommand = new RelayCommand(async _ => await SendFileRequest(), _ => IsDeviceSelected);
            RestartGameCommand = new RelayCommand(async _ => await SendCommand("RestartGame"), _ => IsDeviceSelected);
            HotUpdateCommand = new RelayCommand(async _ => await TriggerHotUpdate(), _ => IsDeviceSelected);
            ClearLogsCommand = new RelayCommand(_ => ClearLogs());
            ClearServerLogCommand = new RelayCommand(_ => ClearServerLog());
            CopyLogsCommand = new RelayCommand(_ => CopyAllLogs());
            SaveLogsCommand = new RelayCommand(_ => SaveLogs());
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
            //commonId具有唯一性  客户端执行完毕之后会把guid回传 
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
                //这里tcs虽然是临时变量 但是注册到了_pendingCommands中了，服务器通过
                //接收处理MainModleView: OnCommandResult 中对缓存的tcs进行trySetResule来通知 
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)); //异步任务需要考虑超时 
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
                //消费掉 commonId不然这个容器越来越大 最后会有性能问题
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

        /// <summary>
        /// 模拟游戏服务器向设备下发 SC 协议（委托给当前 TcpServerService 实例）。
        /// </summary>
        public Task<(bool, string)> SendMockProtocolAsync(string deviceId, short msgId, byte[] bytes)
        {
            if (_serverService == null) return Task.FromResult<(bool, string)>((false, "服务未启动"));
            return _serverService.SendMockProtocolAsync(deviceId, msgId, bytes);
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
                string updateType = dialog.UpdateType; // assetbundle / persistent / loose
                // 散文件与持久化目录都写入持久化根目录；只有 AB 模式写入 platform 子目录
                string fileType = updateType == "assetbundle" ? "hotupdate_ab" : "hotupdate_persistent";
                string zipFileType = updateType == "assetbundle" ? "hotupdate_zip_ab" : "hotupdate_zip_persistent";
                string label = updateType == "persistent" ? "沙盒全量更新"
                              : updateType == "loose" ? "热更散文件" : "AB包增量更新";

                // 散文件模式：展开对话框中添加的文件和文件夹为最终推送文件列表
                HotUpdateFileEntry[] files;
                string sourceDir = "";
                if (updateType == "loose")
                {
                    files = dialog.ExpandedFiles.ToArray();
                }
                else
                {
                    sourceDir = dialog.SourceDirectory;
                    files = System.IO.Directory.GetFiles(sourceDir, "*", System.IO.SearchOption.AllDirectories)
                        .Select(f => new HotUpdateFileEntry
                        {
                            LocalPath = f,
                            RelativePath = GetRelativePath(sourceDir, f),
                        })
                        .ToArray();
                }

                if (files.Length == 0)
                {
                    AppendServerLog($"热更新失败: 未选择任何文件");
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

                // 文件数大于1时打包成zip一次性发送；单文件保持原逻辑
                if (files.Length > 1)
                {
                    string zipName = $"hotupdate_{updateType}_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
                    AppendServerLog($"开始打包 {files.Length} 个文件为 {zipName}...");
                    byte[] zipBytes = CreateHotUpdateZip(files);
                    AppendServerLog($"打包完成，zip 大小 {zipBytes.Length / 1024}KB，开始发送...");

                    bool ok = await _serverService.SendFileToDeviceAsync(
                        _selectedDevice.DeviceId, zipBytes, zipName, zipName, zipFileType);
                    if (ok)
                        AppendServerLog($"热更新完成: zip 包发送成功 -> {_selectedDevice.Alias}");
                    else
                        AppendServerLog($"热更新失败: zip 包发送失败 -> {_selectedDevice.Alias}");
                }
                else
                {
                    var entry = files[0];
                    bool ok = await _serverService.SendFileToDeviceAsync(
                        _selectedDevice.DeviceId, entry.LocalPath, entry.RelativePath, fileType);
                    if (ok)
                        AppendServerLog($"热更新完成: 已发送 {entry.RelativePath} -> {_selectedDevice.Alias}");
                    else
                        AppendServerLog($"热更新失败: 发送 {entry.RelativePath} 失败 -> {_selectedDevice.Alias}");
                }
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

        /// <summary>
        /// 将热更新文件列表打包为 zip（每个条目保留自己的 RelativePath 目录结构）
        /// </summary>
        private static byte[] CreateHotUpdateZip(HotUpdateFileEntry[] entries)
        {
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                foreach (var entry in entries)
                {
                    string entryName = entry.RelativePath.Replace('\\', '/');
                    archive.CreateEntryFromFile(entry.LocalPath, entryName);
                }
            }
            return ms.ToArray();
        }

        #endregion

        #region 日志过滤

        private void UpdateFilteredLogs()
        {
            FilteredLogs.Clear();

            if (_selectedDevice == null)
            {
                if (_logViewActive) LogDisplayText = "";
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
                if (_logViewActive)
                    sb.AppendLine($"{log.Time:HH:mm:ss.fff} {log.Level} {log.Message}");
            }

            if (_logViewActive)
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
                if (_logViewActive) LogDisplayText = "";
            }
        }

        private void ClearServerLog()
        {
            ServerLogText = "";
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

        private void SaveLogs()
        {
            if (FilteredLogs.Count == 0)
            {
                MessageBox.Show("当前没有日志可保存", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var deviceName = _selectedDevice?.Alias ?? "unknown";
            var dlg = new SaveFileDialog
            {
                Title = "保存日志文件",
                Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                FileName = $"{deviceName}_logs_{DateTime.Now:yyyyMMdd_HHmmss}.log",
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"# 设备: {deviceName}");
                sb.AppendLine($"# 导出时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"# 日志条数: {FilteredLogs.Count}");
                sb.AppendLine(new string('#', 60));

                foreach (var log in FilteredLogs)
                {
                    sb.AppendLine($"{log.Time:yyyy-MM-dd HH:mm:ss.fff} {log.Level} {log.Message}");
                    if (!string.IsNullOrEmpty(log.StackTrace))
                        sb.AppendLine($"  StackTrace: {log.StackTrace}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                AppendServerLog($"已保存 {FilteredLogs.Count} 条日志到: {dlg.FileName}");
            }
            catch (Exception ex)
            {
                AppendServerLog($"保存日志失败: {ex.Message}");
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

        /// <summary>
        /// 切换当前选中设备的视频录制状态。
        /// 未录制时发送 StartRecording；录制中时发送 StopRecording 并自动请求视频帧。
        /// </summary>
        private async Task ToggleRecordingAsync()
        {
            if (_selectedDevice == null) return;

            bool isRecording = _recordingDevices.Contains(_selectedDevice.DeviceId);

            if (!isRecording)
            {
                var result = await SendCommandAndWaitAsync("StartRecording");
                if (result?.Success == true)
                {
                    _recordingDevices.Add(_selectedDevice.DeviceId);
                    OnPropertyChanged(nameof(IsRecordingForSelectedDevice));
                    OnPropertyChanged(nameof(RecordButtonContent));
                    AppendServerLog($"[录制] 开始录制视频 -> {_selectedDevice.Alias}");
                }
                else
                {
                    AppendServerLog($"[录制] 开始录制失败 -> {_selectedDevice.Alias}");
                }
            }
            else
            {
                var result = await SendCommandAndWaitAsync("StopRecording");
                if (result?.Success == true)
                {
                    _recordingDevices.Remove(_selectedDevice.DeviceId);
                    OnPropertyChanged(nameof(IsRecordingForSelectedDevice));
                    OnPropertyChanged(nameof(RecordButtonContent));
                    AppendServerLog($"[录制] 已停止录制，正在请求视频帧 -> {_selectedDevice.Alias}");
                    await SendCommand("RequestVideo");
                }
                else
                {
                    AppendServerLog($"[录制] 停止录制失败 -> {_selectedDevice.Alias}");
                }
            }
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
                _recordingDevices.Remove(device.DeviceId);
                OnPropertyChanged(nameof(HasDevices));
                if (_selectedDevice == device)
                {
                    SelectedDevice = null;
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
                    if (_logViewActive)
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

        /// <summary>
        /// 读取程序集信息版本（对应 Unity 的 Application.version）。
        /// 需要在 csproj 中设置 &lt;Version&gt; 或 &lt;InformationalVersion&gt;，否则回退为 "1.0.0"。
        /// </summary>
        public static string GetAppVersion()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                var info = System.Reflection.CustomAttributeData.GetCustomAttributes(asm)
                    .FirstOrDefault(a => a.AttributeType.Name == "AssemblyInformationalVersionAttribute");
                if (info != null && info.ConstructorArguments.Count > 0)
                {
                    var v = info.ConstructorArguments[0].Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(v))
                        return v!;
                }
                var ver = asm.GetName().Version;
                if (ver != null)
                    return ver.ToString();
            }
            catch { }
            return "1.0.0";
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

