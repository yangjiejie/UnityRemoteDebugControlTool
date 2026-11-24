# Unity Remote Debugger - 商业级远程调试工具

跨平台的 Unity 远程调试系统，支持在 Windows WPF 桌面应用中实时监控多个 Unity 设备。

## 项目架构

```
┌─────────────────────────────────────────────────────────────┐
│  WPF Server (D:/Tools/RemoteDebugServer/)                    │
│  ┌─────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐    │
│  │ 设备管理  │  │ 实时日志  │  │ 场景树   │  │ 指令控制  │    │
│  └─────────┘  └──────────┘  └──────────┘  └──────────┘    │
│                          │ TCP (多设备并发)                    │
└──────────────────────────┼──────────────────────────────────┘
                           │
          ┌────────────────┼────────────────┐
          │                │                │
    ┌─────▼─────┐   ┌─────▼─────┐   ┌─────▼─────┐
    │Unity客户端A │   │Unity客户端B │   │Unity客户端C │
    │(Editor)   │   │(Android)  │   │(iOS)      │
    └───────────┘   └───────────┘   └───────────┘
```

## 目录结构

### Unity 客户端 (`d:/na2/Assets/Scripts/RemoteDebugger/`)
```
RemoteDebugger/
├── RemoteDebuggerMessages.cs   # 消息协议定义、编解码器
├── RemoteDebuggerConfig.cs     # 客户端配置
├── RemoteDebugClient.cs        # TCP客户端、重连、收发
├── UnityLogForwarder.cs        # Unity日志捕获转发
├── SceneTreeProvider.cs        # 场景层级结构采集
├── RemoteCommandHandler.cs     # 服务端指令处理
├── RemoteDebuggerSetup.cs      # MonoBehaviour启动入口 + 静态API
└── Editor/
    └── RemoteDebuggerWindow.cs # Unity Editor配置窗口
```

### WPF 服务端 (`D:/Tools/RemoteDebugServer/`)
```
RemoteDebugServer/
├── RemoteDebugServer.sln        # 解决方案
├── RemoteDebugServer.csproj     # .NET 8 WPF项目
├── App.xaml / App.xaml.cs       # 应用入口
├── MainWindow.xaml / .cs        # 主窗口 + 转换器
├── Models/
│   ├── RemoteDebugMessage.cs    # 消息协议(与服务端一致)
│   └── ConnectedDevice.cs       # 设备数据模型
├── Services/
│   └── TcpServerService.cs     # 高性能多客户端TCP服务器
├── ViewModels/
│   └── MainViewModel.cs        # MVVM主视图模型
└── Views/
    └── FileRequestDialog.xaml/.cs  # 文件请求对话框
```

## 通信协议

### 帧格式
```
[4字节消息长度(小端)] [UTF-8 JSON消息体]
```

### 消息类型
| 类型 | 方向 | 说明 |
|------|------|------|
| Register | C→S | 设备注册(平台/版本等) |
| Heartbeat | C→S | 心跳保活(每5秒) |
| Log | C→S | 单条日志 |
| LogBatch | C→S | 批量日志(优化网络) |
| SceneTree | C→S | 场景层级树 |
| Command | S→C | 服务端指令 |
| CommandResult | C→S | 指令执行结果 |
| FileTransfer | C→S | 文件传输(Base64) |
| FileRequest | S→C | 请求客户端文件 |

### 支持的指令
| 指令 | 说明 |
|------|------|
| Ping | 设备心跳响应(含FPS/内存) |
| GetSceneTree | 请求场景层级树 |
| ClearCache | 清理Unity缓存 |
| SendLogFiles | 发送所有日志文件 |
| SendFile | 发送指定文件 |
| TakeScreenshot | 截图并发送 |
| GetRecordingStatus | 查询视频协议版本、录制状态和错误 |
| StartRecording | 设备端开始录制游戏画面及游戏声音 |
| StopRecording | 停止编码、上传本次 MP4，服务器校验成功后删除客户端原文件 |
| SetLogLevel | 设置日志级别(log/warning/error) |

## 快速开始

视频录制的部署要求、平台差异和验证步骤见 [录制说明](docs/video-recording.md)。服务端和 Unity 客户端须一起更新。

### 1. 启动 WPF 服务端

```bash
cd D:/Tools/RemoteDebugServer
dotnet run
```

或双击 `RemoteDebugServer.exe`

- 默认监听 `0.0.0.0:19527`
- 可在界面修改IP和端口后点击"启动"

### 2. Unity 客户端

**方式A: Editor窗口**
- Unity菜单: `Tools > Remote Debugger > Open Window`
- 配置服务器IP(默认 192.168.1.100:19527)
- 点击"连接"

**方式B: 场景自动连接**
- 在场景中创建空 GameObject
- 添加 `RemoteDebuggerSetup` 组件
- 配置Inspector面板中的参数
- 运行即自动连接

**方式C: 代码手动连接**
```csharp
using RemoteDebugger;

// 连接
RemoteDebuggerSetup.Connect("192.168.1.100", 19527);

// 发送自定义日志
RemoteDebuggerSetup.SendCustomLog("自定义消息");

// 断开
RemoteDebuggerSetup.Disconnect();
```

### 3. 局域网配置

1. 将服务端IP改为你的**局域网内网IP**（如 192.168.x.x）
2. Unity 的 `Assets/Scripts/RemoteDebugger/RemoteDebuggerSetup.cs` 中通过 `DefaultServerIp` 写死调试电脑地址，`serverPort` 默认 19527。编辑器与真机共用该默认地址；组件显式填写的地址优先，不读取 Resources 或 JSON 配置。
3. 确保防火墙允许端口 19527 的TCP流量

调试服务器地址与 `RemoteMgr.serverIp`（热更新服务器）相互独立。当前配置为 `172.17.24.119:19527`；电脑地址变化时需要更新配置。手机必须能访问该电脑的局域网地址，同一 Wi-Fi 的客户端隔离也可能阻止连接。

项目仍通过 `GameController.StartUpWhiteDevice` 启动调试模块：编辑器、含 `DebugMod` 的包或管理员白名单设备才会启动。`RemoteDebugger/link.xml` 保留反射加载的模块，避免打包裁剪。真机日志中搜索 `[RemoteDebugger]`：初始化日志会显示实际目标，连接失败会给出原因；TCP 连接超过 10 秒会超时并自动退避重试。没有初始化日志时，应先检查启动条件及模块是否被打入安装包。

## 扩展指南

### 客户端配置
```csharp
var config = new RemoteDebuggerConfig
{
    ServerIp = "192.168.1.100",
    ServerPort = 19527,
    ReconnectIntervalSeconds = 3f,   // 重连间隔
    LogBatchSize = 50,               // 批量发送阈值
    HeartbeatIntervalSeconds = 5f,   // 心跳间隔
    AutoConnect = true,
    CaptureUnityLogs = true,
};
```

### Unity编译宏

客户端代码根据Unity平台自动切换行为：
- `UNITY_EDITOR` - Editor编辑模式下的特殊处理
- `UNITY_ANDROID` / `UNITY_IOS` - 移动端适配
- `UNITY_STANDALONE_WIN` / `UNITY_STANDALONE_OSX` - 桌面端

## 技术特点

- ✅ 全异步TCP通信，无阻塞
- ✅ 自动重连(可配置间隔)
- ✅ 批量日志发送(减少网络开销)
- ✅ 长度前缀帧协议(可靠拆包)
- ✅ 多设备并发支持
- ✅ MVVM架构(服务端)
- ✅ 深色主题UI
- ✅ 内存保护(日志上限5000条/设备)
- ✅ 线程安全的数据结构
- ✅ WPF虚拟化列表(高性能)
