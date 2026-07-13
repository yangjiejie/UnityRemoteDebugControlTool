using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using RemoteDebugServer.Models;
using RemoteDebugServer.ViewModels;

namespace RemoteDebugServer
{
    /// <summary>
    /// Unity远程调试服务器主窗口
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            this.Closing += MainWindow_Closing;
        }

        private async void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_viewModel.IsServerRunning)
            {
                await _viewModel.StopServer();
            }
            _viewModel.Dispose();
        }

        /// <summary>
        /// 点击设备卡片选择设备
        /// </summary>
        private void DeviceCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is ConnectedDevice device)
            {
                _viewModel.SelectedDevice = device;
            }
        }

        private bool _initialized = false;

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            _initialized = true;
        }

        /// <summary>
        /// 日志Tab切换
        /// </summary>
        private void TabLogs_Checked(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;
            if (LogScrollViewer == null || SceneTreePanel == null || CommandsPanel == null) return;
            LogScrollViewer.Visibility = Visibility.Visible;
            SceneTreePanel.Visibility = Visibility.Collapsed;
            CommandsPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 场景树Tab切换
        /// </summary>
        private void TabSceneTree_Checked(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;
            if (LogScrollViewer == null || SceneTreePanel == null || CommandsPanel == null) return;
            LogScrollViewer.Visibility = Visibility.Collapsed;
            SceneTreePanel.Visibility = Visibility.Visible;
            CommandsPanel.Visibility = Visibility.Collapsed;

            // 刷新场景树
            if (_viewModel.SelectedDevice?.SceneTree != null)
            {
                RenderSceneTree(_viewModel.SelectedDevice.SceneTree);
            }
        }

        /// <summary>
        /// 命令Tab切换
        /// </summary>
        private void TabCommands_Checked(object sender, RoutedEventArgs e)
        {
            if (!_initialized) return;
            if (LogScrollViewer == null || SceneTreePanel == null || CommandsPanel == null) return;
            LogScrollViewer.Visibility = Visibility.Collapsed;
            SceneTreePanel.Visibility = Visibility.Collapsed;
            CommandsPanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 渲染场景树到TreeView
        /// </summary>
        private void RenderSceneTree(SceneTreeData tree)
        {
            SceneTreeView.Items.Clear();

            foreach (var scene in tree.Scenes)
            {
                var sceneItem = new TreeViewItem
                {
                    Header = $"📄 {scene.Name}" + (scene.IsLoaded ? " (已加载)" : ""),
                    Tag = scene,
                    Foreground = new SolidColorBrush(Colors.White),
                    IsExpanded = true,
                };

                if (scene.RootObjects != null)
                {
                    foreach (var rootObj in scene.RootObjects)
                    {
                        sceneItem.Items.Add(CreateTreeViewItem(rootObj));
                    }
                }

                SceneTreeView.Items.Add(sceneItem);
            }
        }

        private TreeViewItem CreateTreeViewItem(SceneTreeNode node)
        {
            var header = $"{GetActiveIcon(node)} {node.Name}";
            if (node.Components != null && node.Components.Count > 0)
            {
                header += $" [{string.Join(", ", node.Components.Take(5))}" +
                         (node.Components.Count > 5 ? "..." : "") + "]";
            }

            var item = new TreeViewItem
            {
                Header = header,
                Tag = node,
                Foreground = node.ActiveInHierarchy
                    ? new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
                    : new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
            };

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    item.Items.Add(CreateTreeViewItem(child));
                }
            }

            // 右键菜单
            item.ContextMenu = new ContextMenu();
            var copyPath = new MenuItem { Header = "复制路径" };
            copyPath.Click += (s, e) => Clipboard.SetText(node.Path);
            item.ContextMenu.Items.Add(copyPath);

            var copyName = new MenuItem { Header = "复制名称" };
            copyName.Click += (s, e) => Clipboard.SetText(node.Name);
            item.ContextMenu.Items.Add(copyName);

            item.ContextMenu.Items.Add(new Separator());

            var toggleActive = new MenuItem { Header = node.ActiveSelf ? "禁用" : "启用" };
            toggleActive.Click += async (s, e) =>
            {
                if (_viewModel.SelectedDevice != null)
                {
                    var param = $"{{\"path\":\"{node.Path}\",\"action\":\"{(node.ActiveSelf ? "disable" : "enable")}\"}}";
                    await _viewModel.SendCommand("ExecuteCode", param);
                }
            };
            item.ContextMenu.Items.Add(toggleActive);

            return item;
        }

        private static string GetActiveIcon(SceneTreeNode node)
        {
            if (!node.ActiveInHierarchy) return "⚫";
            if (!node.ActiveSelf) return "⚫";
            return "🟢";
        }
    }

    #region 值转换器

    public class LogColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string colorStr)
            {
                try
                {
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorStr));
                }
                catch { }
            }
            return new SolidColorBrush(Colors.White);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = false;
            if (value is int count) boolValue = count > 0;
            else if (value is bool b) boolValue = b;
            else if (value is ObservableCollection<ConnectedDevice> coll) boolValue = coll.Count > 0;

            if (parameter is string p && p == "inv")
                boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : true;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : true;
    }

    #endregion
}
