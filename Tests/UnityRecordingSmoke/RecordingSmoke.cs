using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using RemoteDebugger;
public class RecordingSmoke : MonoBehaviour {
    private WindowsVideoRecorder recorder;
    private async void Start() {
        Application.runInBackground=true;
        try {
            var audio=new GameObject("AudioSource").AddComponent<AudioSource>();
            var clip=AudioClip.Create("tone",48000,1,48000,false);
            var samples=new float[48000]; for(int i=0;i<samples.Length;i++) samples[i]=0.1f*Mathf.Sin(2*Mathf.PI*440*i/48000);
            clip.SetData(samples,0); audio.clip=clip; audio.loop=true; audio.Play();
            recorder=new GameObject("Recorder").AddComponent<WindowsVideoRecorder>();
            string dir=Environment.GetEnvironmentVariable("RD_SMOKE_DIR");
            StartCoroutine(DrawPattern());
            await Task.Delay(800);
            await recorder.StartAsync(Path.Combine(dir,"smoke.mp4"));
            await Task.Delay(4000);
            await recorder.StopAsync();
            File.WriteAllText(Path.Combine(dir,"result.txt"),recorder.State+" "+recorder.Error);
            recorder.Dispose(); Application.Quit(0);
        } catch(Exception ex) { File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RD_SMOKE_DIR"),"result.txt"),ex.ToString()); Application.Quit(1); }
    }
    private System.Collections.IEnumerator DrawPattern() {
        var texture=new Texture2D(640,360,TextureFormat.RGBA32,false);
        var colors=new Color32[640*360];
        for(int y=0;y<360;y++) for(int x=0;x<640;x++) colors[y*640+x]= y>180 ? new Color32(255,0,0,255) : new Color32(0,0,255,255);
        texture.SetPixels32(colors); texture.Apply();
        while(true) { yield return new WaitForEndOfFrame(); Graphics.Blit(texture,(RenderTexture)null); RenderTexture.active=null; }
    }
    private void OnGUI() {
        GUI.color=Color.red; GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height/2),Texture2D.whiteTexture);
        GUI.color=Color.blue; GUI.DrawTexture(new Rect(0,Screen.height/2,Screen.width,Screen.height/2),Texture2D.whiteTexture);
        GUI.color=Color.white; GUI.Label(new Rect(20,20,500,40),"TOP / elapsed "+Time.time);
        GUI.DrawTexture(new Rect((Time.time*100)%Screen.width,Screen.height/2-20,40,40),Texture2D.whiteTexture);
    }
}
