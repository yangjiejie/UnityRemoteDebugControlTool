using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Google.Protobuf;
using RemoteDebugServer.Helpers;
using RemoteDebugServer.Models;
using RemoteDebugServer.Protocol;

namespace RemoteDebugServer.ViewModels
{
    /// <summary>
    /// 模拟游戏服务器下发 SC 协议的视图模型：
    /// 选择协议 -> 反射绘制字段 -> 填写 -> 发送 -> 记录最近 100 条。
    /// 最近发送记录会持久化到本地（sent_requests.json），下次启动仍可见。
    /// </summary>
    public class MockServerViewModel : INotifyPropertyChanged
    {
        private const int MaxRecentCount = 100;

        private readonly MainViewModel _main;
        private ProtocolInfo? _selectedProtocol;
        private ProtoFieldNode? _root;
        private string _statusText = "选择协议后填写字段，点击发送即可模拟服务器下发（目标为左侧列表选中的设备）。";
        private int _saveScheduled;

        public ObservableCollection<ProtocolInfo> Protocols { get; }
        public ObservableCollection<SentRequestEntry> SentRequests { get; } = new();

        public ICommand SendCommand { get; }
        public ICommand AddItemCommand { get; }
        public ICommand RemoveItemCommand { get; }
        public ICommand ResendCommand { get; }

        /// <summary>
        /// 协议代码生成面板（proto 目录选择 + 一键生成 C#）。
        /// </summary>
        public ProtoGenViewModel ProtoGen { get; } = new ProtoGenViewModel();

        public MockServerViewModel(MainViewModel main)
        {
            _main = main;
            Protocols = new ObservableCollection<ProtocolInfo>(
                ProtocolRegistry.ServerToClientProtocols.OrderBy(p => p.Name));

            // 启动时加载上次保存的最近发包记录
            foreach (var entry in SentRequestsStore.LoadAll())
                SentRequests.Add(entry);

            SendCommand = new RelayCommand(_ => SendAsync(),
                _ => _selectedProtocol != null && _main.SelectedDevice != null);

            // 左侧列表设备选择变化时需要刷新发送按钮可用性
            _main.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.SelectedDevice))
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            };
            AddItemCommand = new RelayCommand(o =>
            {
                AddItem((ProtoFieldNode)o!);
                return Task.CompletedTask;
            });
            RemoveItemCommand = new RelayCommand(o =>
            {
                RemoveItem((ProtoFieldNode)o!);
                return Task.CompletedTask;
            });
            ResendCommand = new RelayCommand(o => ResendAsync((SentRequestEntry)o!));
        }

        public ProtocolInfo? SelectedProtocol
        {
            get => _selectedProtocol;
            set
            {
                _selectedProtocol = value;
                OnPropertyChanged();
                Root = value != null
                    ? ProtoMessageBuilder.CreateTree(((IMessage)Activator.CreateInstance(value.MessageType)!).Descriptor)
                    : null;
            }
        }

        public ProtoFieldNode? Root
        {
            get => _root;
            set
            {
                _root = value;
                OnPropertyChanged();
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        private async Task SendAsync()
        {
            var device = _main.SelectedDevice;
            if (_selectedProtocol == null || device == null || _root == null)
            {
                StatusText = "请先在左侧列表选中目标设备，并选择协议后填写字段。";
                return;
            }
            try
            {
                var msg = ProtoMessageBuilder.Build(
                    ((IMessage)Activator.CreateInstance(_selectedProtocol.MessageType)!).Descriptor, _root);
                byte[] bytes = msg.ToByteArray();
                var (ok, err) = await _main.SendMockProtocolAsync(device.DeviceId, _selectedProtocol.MsgId, bytes);
                if (ok)
                {
                    AddRecent(_selectedProtocol, msg.ToString() ?? "", bytes);
                    StatusText = $"已发送 {_selectedProtocol.Name}（{bytes.Length} 字节）→ {device.DeviceName}";
                }
                else
                {
                    StatusText = $"发送失败：{err}";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"构建/发送失败：{ex.Message}";
            }
        }

        private async Task ResendAsync(SentRequestEntry entry)
        {
            if (entry == null) return;
            var device = _main.SelectedDevice;
            if (device == null)
            {
                StatusText = "请先在左侧列表选中目标设备再重发。";
                return;
            }
            var (ok, err) = await _main.SendMockProtocolAsync(device.DeviceId, entry.MsgId, entry.RawBytes);
            if (ok)
            {
                entry.Resent = true;
                entry.ResentAt = DateTime.Now;
                SaveSentRequestsDebounced();
            }
            StatusText = ok
                ? $"已重发 {entry.ProtocolName} → {device.DeviceName}"
                : $"重发失败：{err}";
        }

        private void AddItem(ProtoFieldNode repeatedNode)
        {
            if (repeatedNode.Descriptor == null) return;
            var item = ProtoMessageBuilder.CreateRepeatedItem(repeatedNode.Descriptor);
            item.Parent = repeatedNode;
            repeatedNode.Items.Add(item);
        }

        private void RemoveItem(ProtoFieldNode item)
        {
            item.Parent?.Items.Remove(item);
        }

        private void AddRecent(ProtocolInfo info, string msgText, byte[] bytes)
        {
            var summary = msgText.Replace("\n", " ").Replace("\r", " ");
            if (summary.Length > 120) summary = summary.Substring(0, 120) + "…";

            var entry = new SentRequestEntry
            {
                ProtocolName = info.Name,
                MsgId = info.MsgId,
                TimeText = DateTime.Now.ToString("HH:mm:ss"),
                SentAt = DateTime.Now,
                Summary = summary,
                RawBytes = bytes,
                Base64 = Convert.ToBase64String(bytes),
            };

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                SentRequests.Insert(0, entry);
                while (SentRequests.Count > MaxRecentCount)
                    SentRequests.RemoveAt(SentRequests.Count - 1);
                SaveSentRequestsDebounced();
            });
        }

        /// <summary>去抖保存：连续发包时合并写盘，最多每 600ms 落盘一次。</summary>
        private void SaveSentRequestsDebounced()
        {
            if (Interlocked.Exchange(ref _saveScheduled, 1) == 1)
                return;
            Task.Delay(600).ContinueWith(_ =>
            {
                _saveScheduled = 0;
                var snapshot = SentRequests.ToList();
                SentRequestsStore.Save(snapshot);
            });
        }

        /// <summary>立即把当前记录保存到本地（应用退出时调用）。</summary>
        public void SaveSentRequestsNow()
        {
            try
            {
                SentRequestsStore.Save(SentRequests.ToList());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Mock] 保存发包记录失败: {ex.Message}");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>轻量 ICommand 实现（异步执行）。</summary>
        public class RelayCommand : ICommand
        {
            private readonly Func<object?, Task> _exec;
            private readonly Func<object?, bool>? _can;

            public RelayCommand(Func<object?, Task> exec, Func<object?, bool>? can = null)
            {
                _exec = exec;
                _can = can;
            }

            public bool CanExecute(object? parameter) => _can == null || _can(parameter);

            public void Execute(object? parameter) => _ = _exec(parameter);

            public event EventHandler? CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }
        }
    }
}
