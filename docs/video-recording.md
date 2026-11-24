# 游戏视频录制

录制在 Unity 设备端执行；WPF 负责开始、停止、接收。输出为包含游戏声音的 MP4，不采集麦克风。原先的 PNG 序列录制已移除。

## 使用

1. 同时更新 WPF 程序和 `D:/na2/Assets/Scripts/RemoteDebugger` 客户端及新增的原生插件，重新编译客户端。
2. 选中设备，点击「录制」。移动端按系统提示授权。进入录制状态后按钮变成「停止」。
3. 点击「停止」，客户端完成本次视频封装并自动上传；服务器落盘、校验并确认后，客户端删除本次原视频，随后返回操作成功。服务端日志显示最终文件路径。
4. 无需「取回视频」操作。上传失败或确认超时时保留本次原文件，再次点击「停止」重试；新录制不会覆盖尚未完成上传清理的本次视频。

设备端成品保存在 `Application.persistentDataPath/RemoteDebuggerVideos`，仅作为临时录制文件；本次上传获得服务器确认后自动删除。服务端保存在 `ReceivedFiles/<设备别名>_<设备ID前8位>/video`，与截图、日志共用同一个设备目录。每次传输独立命名，不覆盖旧视频。

## 平台实现

| 平台 | 视频 | 游戏声音 | 要求 |
| --- | --- | --- | --- |
| Windows Player / Windows Editor Play 模式 | Unity GPU 异步读取，内存管道输入 FFmpeg，H.264 | AudioListener 混音，AAC | 支持 AsyncGPUReadback 的 GPU；FFmpeg 包含 libx264 和 AAC 编码器 |
| Android | MediaProjection → MediaRecorder / H.264 | 仅当前应用 UID 的 AudioPlaybackCapture → AAC | Android 10+，每次系统录屏授权及 RECORD_AUDIO 权限 |
| iOS | ReplayKit，导出 MP4 | ReplayKit 应用声音；microphoneEnabled=false | iOS 14+，系统录屏可用；在 Xcode 中编译原生插件 |

Windows / Android 初始目标为 30 fps、长边不超过 1280。Android 横竖屏变化由系统在固定编码画布中缩放，可能出现黑边；iOS 的分辨率和帧率由 ReplayKit 控制。音视频在设备端编码完成后传输，不提供实时投屏。

### Windows

FFmpeg 查找顺序：

1. `REMOTE_DEBUGGER_FFMPEG` 环境变量指定的完整可执行文件路径。
2. `StreamingAssets/RemoteDebugger/ffmpeg.exe`。
3. `PATH` 中的 `ffmpeg.exe`。

当前开发机已有 `D:/Tools/ffmpeg/bin/ffmpeg.exe`。发布 Windows 客户端时应配置或携带 FFmpeg，环境变量变更后要重启 Unity。不要把 Windows 的 FFmpeg 随 Android/iOS 包一起分发。

场景需要独立的、启用的 AudioListener，Mono 或 Stereo 输出。不要把 AudioSource 和该 AudioListener 放在同一对象上，否则 Unity 无法确定音频滤镜应附着在哪个组件，可能只录到单个音源。使用 FMOD/Wwise 等绕过 Unity AudioListener 的音频系统需要另外接入其混音输出。

录制使用固定数量的画面缓冲和有界音频队列，后台写入编码器。来不及采集的画面以重复上一帧保持时间长度；音频队列溢出则明确失败。停止时将临时视频和游戏音轨封装为 MP4，成功后移除本次中间文件。异常退出可能留下中间文件，不将它们作为成功视频发送。Editor 应保持 Game 视图正常渲染，切换到 Scene 视图可能暂停帧末采集。

场景切换若销毁了正在采集的 AudioListener，会结束并保存当前录制；需要跨场景连续录制时，应让 AudioListener 随音频管理对象持久存在。

### Android

原生库位于 `Assets/Plugins/Android/RemoteDebuggerRecording.androidlib`，通过清单合并添加录屏授权 Activity 和 mediaProjection 前台服务，无需替换现有 Unity Activity。

虽然系统把权限称为「录音」，实现使用的是播放捕获，并未打开麦克风音源。低于 Android 10 明确拒绝启动带游戏声音的录制。系统或播放器若禁止音频被捕获，音轨可能是静音；需在目标设备验证 Unity 音频及第三方音频 SDK 的播放捕获策略。

Android 14+ 可选择只共享本应用，推荐这样授权。切到后台时 Unity 生命周期会请求结束录制；系统撤销录屏或用户点击前台通知的停止操作也会结束并封装视频。屏幕关闭、应用被强杀或编码器失败可能导致本次录制无法完成。

### iOS

`Assets/Plugins/iOS/RemoteDebuggerRecording.mm` 使用 ReplayKit；构建后处理自动链接 ReplayKit、AVFoundation，并为该文件设置 ARC。ReplayKit 输出经 AVAssetExportSession 导出为 MP4，检查音视频轨道后才发布成品。不会仅修改文件后缀冒充 MP4。

系统中断、进入后台、授权被拒绝都会进入停止或错误处理。ReplayKit 不可用及版本不足时明确报错。iOS 原生实现需要 Mac/Xcode 编译及真机测试，Windows 上的 C# 编译检查不代表 iOS 验收完成。

## 协议与可靠性

- `GetRecordingStatus` 返回协议版本 3、Idle/Starting/Recording/Stopping/Ready/Failed 状态、错误及当前输出路径。
- 每条 `VideoChunk` 最多含 256 KiB 原始数据；不把完整视频载入内存。
- 块包含传输 ID、总字节数和偏移；结束块带 SHA-256。服务端验证长度和摘要后，将 `.partial` 文件改为 `.mp4` 并返回 `VideoTransferResult`。
- 每个连接同时接收一份视频，单份上限 32 GiB。断线清理不完整接收文件，未获服务器确认的客户端视频保留。当前重试从头传输，不是断点续传。
- 客户端和服务端各自串行化套接字写入，避免视频与日志、心跳交错破坏消息。
- 录制与文件传输操作互斥，防止重复点击；服务端异步操作固定到开始时选中的设备，切换选中项不改变命令目标。
- StopRecording 是停止、上传、清理的一体操作，只处理当前会话的录制路径，不扫描旧视频。删除失败会报错，重试只清理已确认上传的同一文件，不重复上传。
- 协议 v3 不兼容旧版停止语义，请同时更新两端。旧 RequestVideo 指令仅返回已移除提示。

## 验证

服务端：`dotnet build RemoteDebugServer.csproj`。

传输回归：`dotnet run --project Tests/VideoTransferChecks/VideoTransferChecks.csproj`，覆盖逐字节还原、错误摘要、乱序、传输混入、路径越界、超长块、提前结束、中断清理及重新传输，以及服务器确认前保留、确认后删除、上传失败保留、清理失败重试和本次视频隔离。

Windows 编码冒烟测试：`Tests/UnityRecordingSmoke/Run.ps1`。脚本在独立临时 Unity 项目中构建测试程序，以隐藏窗口运行，主动绘制红蓝画面并播放测试音，验证 GPU 采集到 H.264/AAC MP4 的完整链路，不修改实际游戏场景。隐藏窗口不保证正常游戏渲染，主动绘制测试图不等同于实际游戏 UI 验收。

本机已通过：WPF 构建、三个平台 C# 条件编译、Android Java 编译、28 项传输与清理断言；Windows 测试文件为 640×360、30 fps、48 kHz 双声道，画面方向正确，音视频长度差约 30 ms。Android/iOS 尚未完成真机验证，iOS 原生插件尚需 Xcode 编译。

发布前在每个平台录制至少一段带游戏声音和明显动作的片段，确认：画面方向、UI、声音、音画同步；拒绝授权、连续开始停止、切换设备、进入后台、系统撤销录制；传输中断后重试及成功后的客户端文件删除；较长录制的内存和帧率。Android 应覆盖 Android 10 和当前目标系统版本；iOS 必须使用真机。

参考：[Unity GPU 截图接口](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ScreenCapture.CaptureScreenshotIntoRenderTexture.html)、[Android 播放捕获](https://developer.android.com/media/platform/av-capture)、[Android 录屏](https://developer.android.com/media/grow/media-projection)、[ReplayKit](https://developer.apple.com/documentation/replaykit)。
