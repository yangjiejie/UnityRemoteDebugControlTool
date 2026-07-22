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
        private bool _isSelected;
        private string _customAlias = "";

        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string Platform { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public string UnityVersion { get; set; } = "";
        /// <summary>资源热更新版本号（客户端 remoteConfig.version）。</summary>
        public string HotUpdateVersion { get; set; } = "";
        public DateTime ConnectedTime { get; set; } = DateTime.Now;
        public long StartTime { get; set; }
        public string IpAddress { get; set; } = "";

        /// <summary>
        /// 用户自定义别名（留空则显示原始设备名）
        /// setter 会自动触发 AliasChanged 事件，ViewModel 监听并持久化
        /// </summary>
        public string Alias
        {
            get => string.IsNullOrWhiteSpace(_customAlias) ? DeviceName : _customAlias;
            set
            {
                string trimmed = value?.Trim() ?? "";
                if (trimmed == DeviceName || string.IsNullOrWhiteSpace(trimmed))
                    _customAlias = "";
                else
                    _customAlias = trimmed;
                OnPropertyChanged();
                AliasChanged?.Invoke(this, _customAlias);
            }
        }

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

        /// <summary>
        /// 是否被选中（用于 UI 高亮）
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string StatusText => IsConnected ? "在线" : "离线";
        public string DisplayInfo => $"{Alias} ({Platform}) - {IpAddress}";
        public string ConnectionDuration => (DateTime.Now - ConnectedTime).ToString(@"hh\:mm\:ss");

        /// <summary>
        /// 别名变更事件（用于 ViewModel 监听并持久化）
        /// </summary>
        public event EventHandler<string>? AliasChanged;

        /// <summary>
        /// 内部设置别名（不触发事件，用于从文件加载时初始化）
        /// </summary>
        public void SetAliasSilently(string alias)
        {
            _customAlias = alias?.Trim() ?? "";
            OnPropertyChanged(nameof(Alias));
        }

        /// <summary>
        /// 重连后通知 UI 刷新可能变化的字段
        /// </summary>
        public void NotifyReconnectInfoChanged()
        {
            OnPropertyChanged(nameof(ConnectedTime));
            OnPropertyChanged(nameof(IpAddress));
            OnPropertyChanged(nameof(HotUpdateVersion));
            OnPropertyChanged(nameof(DisplayInfo));
            OnPropertyChanged(nameof(ConnectionDuration));
        }

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
