using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RemoteDebugServer.Models
{
    /// <summary>
    /// 已连接的设备信息
    /// </summary>
    public class ConnectedDevice : INotifyPropertyChanged
    {
        private bool _isConnected;

        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string Platform { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public string UnityVersion { get; set; } = "";
        public DateTime ConnectedTime { get; set; } = DateTime.Now;
        public long StartTime { get; set; }
        public string IpAddress { get; set; } = "";

        // 实时日志
        public ObservableCollection<LogDisplayEntry> Logs { get; set; } = new();
        public const int MaxLogEntries = 5000; // 限制日志条数防止内存溢出

        // 场景树
        public SceneTreeData? SceneTree { get; set; }

        // 最近指令结果
        public ObservableCollection<CommandResult> CommandResults { get; set; } = new();

        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
        }

        public string StatusText => IsConnected ? "在线" : "离线";
        public string DisplayInfo => $"{DeviceName} ({Platform}) - {IpAddress}";
        public string ConnectionDuration => (DateTime.Now - ConnectedTime).ToString(@"hh\:mm\:ss");

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// UI显示的日志条目
    /// </summary>
    public class LogDisplayEntry : INotifyPropertyChanged
    {
        public DateTime Time { get; set; }
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
        public string StackTrace { get; set; } = "";
        public string Color { get; set; } = "White";
        public string DeviceName { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
