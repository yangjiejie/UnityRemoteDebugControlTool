param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f2c1/Editor/Unity.exe',
    [string]$ClientScripts = 'D:/na2/Assets/Scripts/RemoteDebugger',
    [string]$Ffmpeg = 'D:/Tools/ffmpeg/bin/ffmpeg.exe'
)
$ErrorActionPreference = 'Stop'
$smokeDirectory = Join-Path $env:TEMP ('RemoteDebuggerSmoke_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$smokeDirectory/Assets/Editor", "$smokeDirectory/Packages", "$smokeDirectory/ProjectSettings" -Force | Out-Null
Copy-Item -LiteralPath "$ClientScripts/VideoRecording.cs", "$ClientScripts/WindowsVideoRecorder.cs", "$ClientScripts/RecordedVideoDelivery.cs" -Destination "$smokeDirectory/Assets"
Copy-Item -LiteralPath "$PSScriptRoot/RecordingSmoke.cs" -Destination "$smokeDirectory/Assets"
Copy-Item -LiteralPath "$PSScriptRoot/SmokeBuild.cs" -Destination "$smokeDirectory/Assets/Editor"
Set-Content -LiteralPath "$smokeDirectory/Packages/manifest.json" -Encoding UTF8 -Value '{"dependencies":{"com.unity.modules.audio":"1.0.0","com.unity.modules.screencapture":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}'
Set-Content -LiteralPath "$smokeDirectory/ProjectSettings/ProjectVersion.txt" -Encoding UTF8 -Value 'm_EditorVersion: 2022.3.62f2c1'
$build = Start-Process $Unity -ArgumentList @('-batchmode','-quit','-projectPath',('"'+$smokeDirectory+'"'),'-executeMethod','SmokeBuild.Build','-logFile',('"'+$smokeDirectory+'/build.log"')) -WindowStyle Hidden -PassThru
if (!$build.WaitForExit(300000)) { $build.Kill(); throw "Smoke build timed out: $smokeDirectory/build.log" }
if ($build.ExitCode -ne 0) { throw "Smoke build failed: $smokeDirectory/build.log" }
$previousOutput = $env:RD_SMOKE_DIR
$previousFfmpeg = $env:REMOTE_DEBUGGER_FFMPEG
try {
    $env:RD_SMOKE_DIR = $smokeDirectory
    $env:REMOTE_DEBUGGER_FFMPEG = $Ffmpeg
    $player = Start-Process "$smokeDirectory/Build/RecordingSmoke.exe" -ArgumentList @('-screen-fullscreen','0','-screen-width','640','-screen-height','360','-logFile',('"'+$smokeDirectory+'/player.log"')) -WindowStyle Hidden -PassThru
    if (!$player.WaitForExit(120000)) { $player.Kill(); throw "Smoke player timed out: $smokeDirectory/player.log" }
    $result = Get-Content -LiteralPath "$smokeDirectory/result.txt" -Raw
    if ($player.ExitCode -ne 0 -or !$result.StartsWith('Ready')) { throw $result }
    Write-Output "Encoded test video: $smokeDirectory/smoke.mp4"
    $probe = Join-Path (Split-Path -Parent $Ffmpeg) 'ffprobe.exe'
    & $probe -v error -show_entries 'stream=codec_name,codec_type,width,height,duration,r_frame_rate,sample_rate,channels' -of json "$smokeDirectory/smoke.mp4"
} finally {
    $env:RD_SMOKE_DIR = $previousOutput
    $env:REMOTE_DEBUGGER_FFMPEG = $previousFfmpeg
}
