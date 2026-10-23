// v7.3.0: medición del audio por nivel (siseo / «viento»). -executeMethod AudioCapRunner.Run. Entra en Play, recorre los niveles,
// para los monstruos (queda el «lecho» continuo) y graba 8 s: la mezcla real del AudioListener (si el motor de audio corre) y la
// mezcla offline de las fuentes. Escribe /workspace/logs/audiocap/<tag>/L*_{real,offline}.wav y sources.txt; luego sale.
using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AudioCapRunner
{
    static IEnumerator routine; static double waitUntil; static Func<bool> cond; static double deadline;
    static string Dir { get { return "/workspace/logs/audiocap/" + (Environment.GetEnvironmentVariable("AUDIOCAP_TAG") ?? "x"); } }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "audiocap");
        EditorApplication.EnterPlaymode();
    }

    static AudioCapRunner()
    {
        if (SessionState.GetString("smoke_phase", "") != "audiocap") return;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Body();
        double now = EditorApplication.timeSinceStartup;
        if (now < waitUntil) return;
        if (cond != null) { bool ok = false; try { ok = cond(); } catch { ok = true; } if (!ok && now < deadline) return; cond = null; }
        bool more;
        try { more = routine.MoveNext(); } catch (Exception e) { File.AppendAllText(Dir + "/sources.txt", "EXCEPTION " + e + "\n"); more = false; }
        if (!more) { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); return; }
        object c = routine.Current;
        if (c is float) waitUntil = now + (float)c;
        else if (c is Func<bool>) { cond = (Func<bool>)c; deadline = now + 120; }
    }

    static IEnumerator Body()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(Dir + "/sources.txt", "");
        GameSettings.ResetDefaults();
        ElevatorCabin.InstantForTests = true; BallPitLurker.DisabledForTests = true; ExitLock.AutoKeyForTests = true;
        yield return (Func<bool>)(() => GameBootstrap.Instance != null && GameBootstrap.Instance.IsLoaded);
        GameBootstrap gb = GameBootstrap.Instance;
        File.AppendAllText(Dir + "/sources.txt", "outputSampleRate " + AudioSettings.outputSampleRate + " speakerMode " + AudioSettings.speakerMode + " listenerVol " + AudioListener.volume + "\n");
        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            int lc = gb.LoadCount; gb.LoadLevel(lv);
            yield return (Func<bool>)(() => gb.LoadCount > lc && gb.IsLoaded && GameManager.Instance != null && FirstPersonController.LocalInstance != null && !ElevatorCabin.PlayerLocked);
            yield return 2.5f;
            foreach (MonsterAI m in GameManager.Instance.monsters) if (m != null) m.Stop();
            yield return 1.0f;
            string[] spots = lv == 7 ? new[] { "inicio", "mar", "bajo_agua" } : new[] { "inicio" };
            foreach (string s in spots)
            {
                FirstPersonController pw = FirstPersonController.LocalInstance;
                if (s == "mar") pw.Teleport(new Vector3(40f, 0.2f, 228f), 0f);
                if (s == "bajo_agua") pw.Teleport(new Vector3(50f, -18f, 232f), 0f);
                if (s != "inicio") yield return 3.0f;
                AudioCapture cap = AudioCapture.Attach();
                cap.Begin();
                yield return 8.0f;
                float[] real = cap.End();
                string tag = "L" + lv + "_" + s;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("== " + tag + " (" + LevelConfig.Get(lv).name + ") callbacks=" + cap.Callbacks + " realSamples=" + real.Length + " ch=" + cap.Channels);
                int sr = AudioSettings.outputSampleRate;
                float[] off = AudioCapture.OfflineMix(8f, sr, sb);
                sb.Append(AudioCapture.FilterReaders());
                sb.Append(AudioCensus.Dump(tag));
                File.AppendAllText(Dir + "/sources.txt", sb.ToString());
                if (real.Length > 0) AudioCapture.WriteWav(Dir + "/" + tag + "_real.wav", real, cap.Channels, sr);
                AudioCapture.WriteWav(Dir + "/" + tag + "_offline.wav", off, 1, sr);
            }
        }
    }
}
