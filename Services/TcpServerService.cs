using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RemoteDebugServer.Models;

namespace RemoteDebugServer.Services
{
    /// <summary>
    /// 高性能多客户端TCP调试服务器
    /// </summary>
    public class TcpServerService : IDisposable
    {
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();

        private int _port;
        private IPAddress _bindAddress;

        // 文件接收目录
        public string ReceivedFilesDirectory { get; set; }

        // 事件通知
        public event Action<ConnectedDevice>? OnDeviceConnected;
        public event Action<ConnectedDevice>? OnDeviceDisconnected;
        public event Action<ConnectedDevice, LogDisplayEntry>? OnLogReceived;
        public event Action<ConnectedDevice, SceneTreeData>? OnSceneTreeReceived;
        public event Action<ConnectedDevice, CommandResult>? OnCommandResultReceived;
        public event Action<ConnectedDevice, string, string>? OnFileReceived;
        public event Action<string>? OnServerLog;

        public bool IsRunning { get; private set; }

        public TcpServerService(int port = 19527, string bindAddress = "0.0.0.0")
        {
            _port = port;
            _bindAddress = IPAddress.Parse(bindAddress);
            ReceivedFilesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ReceivedFiles");
        }

        /// <summary>
        /// 启动服务器
        /// </summary>
        public async Task StartAsync()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _tcpListener = new TcpListener(_bindAddress, _port);
            _tcpListener.Start();
            IsRunning = true;

            Directory.CreateDirectory(ReceivedFilesDirectory);

            Log($"服务器已启动 - 监听 {_bindAddress}:{_port}");
            Log($"文件接收目录: {ReceivedFilesDirectory}");
            Log($"等待Unity客户端连接...");

            _ = AcceptClientsLoop(_cts.Token);
            await Task.CompletedTask; // 保持异步签名，实际启动在后台进行
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public async Task StopAsync()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _cts?.Cancel();

            try { _tcpListener?.Stop(); } catch { }

            foreach (var session in _sessions.Values)
            {
                try { session.Dispose(); } catch { }
            }
            _sessions.Clear();

            Log("服务器已停止");
            await Task.CompletedTask;
        }

        /// <summary>
        /// 获取所有已连接设备
        /// </summary>
        public IEnumerable<ConnectedDevice> GetConnectedDevices()
        {
            foreach (var session in _sessions.Values)
            {
                if (session.Device != null)
                    yield return session.Device;
            }
        }

        /// <summary>
        /// 向指定设备发送指令
        /// </summary>
        public async Task<bool> SendCommandAsync(string deviceId, string command, string parameters = "")
        {
            if (!_sessions.TryGetValue(deviceId, out var session) || !session.IsConnected)
                return false;

            try
            {
                var cmd = new RemoteCommand
                {
                    CommandId = Guid.NewGuid().ToString("N"),
                    Command = command,
                    Parameters = parameters,
                };

                var msg = MessageCodec.CreateMessage(MessageType.Command, "", "", cmd);
                var data = MessageCodec.Encode(msg);
                await session.Stream!.WriteAsync(data);
                await session.Stream!.FlushAsync();

                Log($"指令已发送 -> {session.Device?.DeviceName}: {command}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"发送指令失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 向指定设备请求文件
        /// </summary>
        public async Task<bool> RequestFileAsync(string deviceId, string filePath, string fileType, bool recursive = false, string wildcard = "*")
        {
            if (!_sessions.TryGetValue(deviceId, out var session) || !session.IsConnected)
                return false;

            try
            {
                var request = new FileRequestData
                {
                    FilePath = filePath,
                    FileType = fileType,
                    Recursive = recursive,
                    Wildcard = wildcard,
                };

                var msg = MessageCodec.CreateMessage(MessageType.FileRequest, "", "", request);
                var data = MessageCodec.Encode(msg);
                await session.Stream!.WriteAsync(data);
                await session.Stream!.FlushAsync();

                Log($"文件请求已发送 -> {session.Device?.DeviceName}: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Log($"请求文件失败: {ex.Message}");
                return false;
            }
        }

        #region 内部实现

        private async Task AcceptClientsLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && IsRunning)
            {
                try
                {
                    var tcpClient = await _tcpListener!.AcceptTcpClientAsync(ct);
                    tcpClient.NoDelay = true;
                    tcpClient.ReceiveTimeout = 0;

                    var session = new ClientSession(tcpClient);
                    _ = HandleClientAsync(session, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log($"接受连接错误: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(ClientSession session, CancellationToken ct)
        {
            try
            {
                // 等待第一条消息（注册消息）
                var firstMsg = await MessageCodec.DecodeAsync(session.Stream!, ct);
                if (firstMsg == null || firstMsg.Type != MessageType.Register.ToString())
                {
                    session.Dispose();
                    return;
                }

                var registerData = firstMsg.ParseData<RegisterData>();
                if (registerData == null)
                {
                    session.Dispose();
                    return;
                }

                var device = new ConnectedDevice
                {
                    DeviceId = registerData.DeviceId,
                    DeviceName = registerData.DeviceName,
                    Platform = registerData.Platform,
                    AppVersion = registerData.AppVersion,
                    UnityVersion = registerData.UnityVersion,
                    StartTime = registerData.StartTime,
                    ConnectedTime = DateTime.Now,
                    IpAddress = ((IPEndPoint)session.TcpClient.Client.RemoteEndPoint!).Address.ToString(),
                    IsConnected = true,
                };

                session.Device = device;

                // 同一 DeviceId 重新连接时，先踢掉旧会话，避免 UI 出现重复设备
                if (_sessions.TryGetValue(device.DeviceId, out var oldSession))
                {
                    _sessions.TryRemove(device.DeviceId, out _);
                    if (oldSession.Device != null)
                    {
                        oldSession.Device.IsConnected = false;
                        OnDeviceDisconnected?.Invoke(oldSession.Device);
                    }
                    oldSession.Dispose();
                }

                _sessions[device.DeviceId] = session;

                OnDeviceConnected?.Invoke(device);
                Log($"设备已连接: {device.DeviceName} [{device.Platform}] {device.UnityVersion} ({device.IpAddress})");

                // 接收消息循环
                while (!ct.IsCancellationRequested && session.IsConnected)
                {
                    try
                    {
                        var msg = await MessageCodec.DecodeAsync(session.Stream!, ct);
                        if (msg == null) continue;

                        await ProcessMessageAsync(session, msg);
                    }
                    catch (EndOfStreamException) { break; }
                    catch (IOException) { break; }
                }
            }
            catch (EndOfStreamException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                Log($"客户端处理异常: {ex.Message}");
            }
            finally
            {
                if (session.Device != null)
                {
                    // 只有当前会话仍然是字典中的活跃会话时才移除，避免旧会话清理误删已重连的新会话
                    if (_sessions.TryGetValue(session.Device.DeviceId, out var currentSession) && currentSession == session)
                    {
                        _sessions.TryRemove(session.Device.DeviceId, out _);
                    }
                    session.Device.IsConnected = false;
                    OnDeviceDisconnected?.Invoke(session.Device);
                    Log($"设备断开: {session.Device.DeviceName}");
                }
                session.Dispose();
            }
        }

        private async Task ProcessMessageAsync(ClientSession session, DebugMessage msg)
        {
            var device = session.Device!;

            if (!Enum.TryParse<MessageType>(msg.Type, out var type)) return;

            switch (type)
            {
                case MessageType.Heartbeat:
                    // 心跳消息，忽略
                    break;

                case MessageType.Log:
                    ProcessLogMessage(device, msg);
                    break;

                case MessageType.LogBatch:
                    ProcessLogBatch(device, msg);
                    break;

                case MessageType.SceneTree:
                    ProcessSceneTree(device, msg);
                    break;

                case MessageType.CommandResult:
                    ProcessCommandResult(device, msg);
                    break;

                case MessageType.FileTransfer:
                    await ProcessFileTransfer(device, msg);
                    break;

                case MessageType.Disconnect:
                    // 客户端主动断开，立即结束当前会话并触发清理
                    throw new EndOfStreamException("客户端主动断开连接");
            }
        }

        private void ProcessLogMessage(ConnectedDevice device, DebugMessage msg)
        {
            var log = msg.ParseData<LogEntry>();
            if (log == null) return;

            var entry = CreateLogDisplayEntry(log, device.DeviceName);
            AddLogToDevice(device, entry);
            OnLogReceived?.Invoke(device, entry);
        }

        private void ProcessLogBatch(ConnectedDevice device, DebugMessage msg)
        {
            var batch = msg.ParseData<LogBatchData>();
            if (batch?.Logs == null) return;

            foreach (var log in batch.Logs)
            {
                var entry = CreateLogDisplayEntry(log, device.DeviceName);
                AddLogToDevice(device, entry);
                OnLogReceived?.Invoke(device, entry);
            }
        }

        private void ProcessSceneTree(ConnectedDevice device, DebugMessage msg)
        {
            var tree = msg.ParseData<SceneTreeData>();
            if (tree != null)
            {
                device.SceneTree = tree;
                OnSceneTreeReceived?.Invoke(device, tree);
                Log($"收到场景树: {device.DeviceName} ({tree.Scenes.Count} 个场景)");
            }
        }

        private void ProcessCommandResult(ConnectedDevice device, DebugMessage msg)
        {
            var result = msg.ParseData<CommandResult>();
            if (result != null)
            {
                device.CommandResults.Add(result);
                OnCommandResultReceived?.Invoke(device, result);
            }
        }

        private async Task ProcessFileTransfer(ConnectedDevice device, DebugMessage msg)
        {
            var fileData = msg.ParseData<FileTransferData>();
            if (fileData == null || string.IsNullOrEmpty(fileData.FileData)) return;

            try
            {
                string deviceDir = Path.Combine(ReceivedFilesDirectory, device.DeviceId, fileData.FileType);
                Directory.CreateDirectory(deviceDir);

                string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{fileData.FileName}";
                string filePath = Path.Combine(deviceDir, fileName);

                byte[] fileBytes = Convert.FromBase64String(fileData.FileData);
                await File.WriteAllBytesAsync(filePath, fileBytes);

                OnFileReceived?.Invoke(device, filePath, fileData.FileType);
                Log($"收到文件: {fileData.FileName} ({fileBytes.Length / 1024}KB) -> {filePath}");
            }
            catch (Exception ex)
            {
                Log($"保存文件失败: {ex.Message}");
            }
        }

        private LogDisplayEntry CreateLogDisplayEntry(LogEntry log, string deviceName)
        {
            var (level, color) = log.LogType switch
            {
                0 => ("[LOG]", "White"),
                1 => ("[WARN]", "Yellow"),
                2 => ("[ERROR]", "#FF6B6B"),
                3 => ("[EXCEPTION]", "#FF4444"),
                4 => ("[ASSERT]", "#FF8800"),
                _ => ("[LOG]", "White"),
            };

            return new LogDisplayEntry
            {
                Time = DateTimeOffset.FromUnixTimeMilliseconds(log.Timestamp).LocalDateTime,
                Level = level,
                Message = log.Condition,
                StackTrace = log.StackTrace ?? "",
                Color = color,
                DeviceName = deviceName,
            };
        }

        private void AddLogToDevice(ConnectedDevice device, LogDisplayEntry entry)
        {
            // 通过UI线程安全操作需要Dispatcher，这里先添加，由ViewModel处理
            device.Logs.Add(entry);

            // 限制日志数量
            while (device.Logs.Count > ConnectedDevice.MaxLogEntries)
            {
                device.Logs.RemoveAt(0);
            }
        }

        private void Log(string message)
        {
            OnServerLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        #endregion

        #region 内部类

        private class ClientSession : IDisposable
        {
            public TcpClient TcpClient { get; }
            public NetworkStream? Stream { get; }
            public ConnectedDevice? Device { get; set; }
            public bool IsConnected => TcpClient?.Connected ?? false;

            public ClientSession(TcpClient tcpClient)
            {
                TcpClient = tcpClient;
                Stream = tcpClient.GetStream();
            }

            public void Dispose()
            {
                try { Stream?.Close(); } catch { }
                try { TcpClient?.Close(); } catch { }
            }
        }

        #endregion

        public void Dispose()
        {
            _cts?.Cancel();
            _tcpListener?.Stop();
            _cts?.Dispose();
        }
    }
}
