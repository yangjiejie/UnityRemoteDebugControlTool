using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class SmokeBuild {
 public static void Build() {
    var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
    var camera=new GameObject("Camera"); camera.AddComponent<Camera>(); camera.AddComponent<AudioListener>(); camera.AddComponent<RecordingSmoke>();
    EditorSceneManager.SaveScene(scene,"Assets/Smoke.unity");
    PlayerSettings.defaultScreenWidth=640; PlayerSettings.defaultScreenHeight=360;
    PlayerSettings.fullScreenMode=FullScreenMode.Windowed; PlayerSettings.runInBackground=true;
    var result=BuildPipeline.BuildPlayer(new[]{"Assets/Smoke.unity"},"Build/RecordingSmoke.exe",BuildTarget.StandaloneWindows64,BuildOptions.Development);
    if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new System.Exception("Build failed");
 }
}
