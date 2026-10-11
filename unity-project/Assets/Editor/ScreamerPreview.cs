using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renderiza /workspace/monster-previews/screamer_<monstruo>_{1,2,3}.png: los tres planos del susto (cara, ojos, plano abierto)
// de cada uno de los 5 monstruos, tal y como los ve el jugador (con el glitch VHS reducido del plano de la cara).
//   xvfb-run -a Unity -batchmode -projectPath <p> -executeMethod ScreamerPreview.Export -force-glcore
[InitializeOnLoad]
public static class ScreamerPreview
{
    private static IEnumerator routine;
    private static double waitUntil;

    static ScreamerPreview()
    {
        if (SessionState.GetString("smoke_phase", "") != "screamprev") return;
        EditorApplication.update += Tick;
    }

    public static void Export()
    {
        Directory.CreateDirectory("/workspace/monster-previews");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "screamprev");
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try
        {
            if (!routine.MoveNext()) Finish();
            else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current;
        }
        catch (System.Exception e) { Debug.LogError("[ScreamerPreview] " + e); Finish(); }
    }

    private static void Finish()
    {
        SessionState.SetString("smoke_phase", "");
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    private static void Capture(string path)
    {
        const int W = 1280, H = 720;
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        Camera game = Camera.main;
        RenderTexture old = game.targetTexture;
        game.targetTexture = rt; game.Render(); game.targetTexture = old;
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); RenderTexture.ReleaseTemporary(rt);
    }

    private static IEnumerator Run()
    {
        ElevatorCabin.InstantForTests = true;
        GameSettings.ResetDefaults();
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded) yield return 0.3f;
        yield return 2f;
        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            GameBootstrap gb = GameBootstrap.Instance;
            int ld = gb.LoadCount;
            UIManager.Instance.StartGame(lv);
            while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu)) yield return 0.2f;
            yield return 1.0f;
            GameManager gm = GameManager.Instance;
            foreach (MonsterAI m in gm.monsters) m.Stop();
            MonsterAI mon = gm.monsters[0];
            Vector3 pp = gm.player.transform.position;
            mon.transform.position = pp + gm.player.transform.forward * 3f;
            gm.lostRestartSeconds = 60f;
            mon.ForceCatch();
            yield return 0.1f;
            Screamer sc = gm.screamer;
            string id = mon.visual.profile.id;
            float[] at = { 0.30f, 0.50f, 1.05f };   // t de la secuencia: plano A, plano B (ojos), plano C (abierto)
            float[] ts = { 0.44f + 0.45f, 0.44f + 1.05f, 0.44f + 1.9f };
            for (int i = 0; i < 3; i++)
            {
                sc.PreviewSeek = ts[i];
                yield return 0.25f;
                Capture("/workspace/monster-previews/screamer_" + id + "_" + (i + 1) + ".png");
                Debug.Log("[ScreamerPreview] " + id + " plano " + (i + 1) + " t=" + sc.T.ToString("F2") + " shot=" + sc.Shot + " dist=" + sc.FaceDistance.ToString("F2") + " alto=" + sc.HeadHeight.ToString("F2") + " visible=" + sc.MonsterVisibleToCamera + " cabeza=" + sc.HeadScreenFraction.ToString("F2") + " fov=" + Camera.main.fieldOfView.ToString("F0") + " hueso=" + sc.HeadBoneName);
            }
            gm.Restart();
            yield return 1f;
        }
    }
}
