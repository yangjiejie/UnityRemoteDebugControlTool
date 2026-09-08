using System.Windows.Controls;

namespace RemoteDebugServer.Views
{
    /// <summary>
    /// 模拟游戏服务器下发 SC 协议的界面。DataContext 在 MainWindow 中设置为 MockServerViewModel。
    /// </summary>
    public partial class MockServerView : System.Windows.Controls.UserControl
    {
        public MockServerView()
        {
            InitializeComponent();
        }
    }
}
