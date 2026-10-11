using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renderiza /workspace/monster-previews/intro_*.png: inserción de cinta, aviso, estudio, título y fundido hacia el menú.
//   xvfb-run -a Unity -batchmode -projectPath <p> -executeMethod IntroPreview.Export -force-glcore
[InitializeOnLoad]
public static class IntroPreview
{
    private static IEnumerator routine;
    private static double waitUntil;
    private static Camera uiCam;

    static IntroPreview()
    {
        if (SessionState.GetString("smoke_phase", "") != "introprev") return;
        EditorApplication.update += Tick;
    }

    public static void Export()
    {
        Directory.CreateDirectory("/workspace/monster-previews");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "introprev");
        SessionState.SetBool("intro_real", true);
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
        catch (System.Exception e) { Debug.LogError("[IntroPreview] " + e); Finish(); }
    }

    private static void Finish()
    {
        SessionState.SetString("smoke_phase", "");
        SessionState.SetBool("intro_real", false);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    private static void SetupUi()
    {
        if (uiCam == null)
        {
            GameObject ugo = new GameObject("PreviewUiCam");
            uiCam = ugo.AddComponent<Camera>();
            uiCam.clearFlags = CameraClearFlags.Depth; uiCam.cullingMask = 1 << 31; uiCam.orthographic = true; uiCam.depth = 100; uiCam.enabled = false;
        }
        foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = uiCam; c.planeDistance = 1f;
            foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
        }
    }

    private static void Capture(string path, bool menuBehind)
    {
        const int W = 1280, H = 720;
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        Camera game = Camera.main;
        RenderTexture old = game != null ? game.targetTexture : null;
        if (game != null) { game.targetTexture = rt; game.Render(); game.targetTexture = old; }
        SetupUi(); Canvas.ForceUpdateCanvases();
        // el menú y el fondo siguen a su ritmo: solo se dibujan los canvases (intro incluida)
        uiCam.targetTexture = rt; uiCam.Render();
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); RenderTexture.ReleaseTemporary(rt);
        Debug.Log("[IntroPreview] captura " + path);
    }

    private static IEnumerator Run()
    {
        yield return 0.5f;
        IntroSequence intro = IntroSequence.Current;
        if (intro == null) { Debug.LogError("[IntroPreview] no hay intro"); yield break; }
        string d = "/workspace/monster-previews/";
        while (intro != null && intro.T < 0.45f) yield return 0.03f;
        Capture(d + "intro_1_insercion_cinta.png", false);
        while (intro != null && intro.T < 2.3f) yield return 0.05f;
        Capture(d + "intro_2_aviso.png", false);
        while (intro != null && intro.T < 4.7f) yield return 0.05f;
        Capture(d + "intro_3_estudio.png", false);
        while (intro != null && intro.T < 7.0f) yield return 0.05f;
        Capture(d + "intro_4_titulo.png", false);
        while (intro != null && intro.T < 8.95f) yield return 0.03f;
        Capture(d + "intro_5_fundido.png", true);
        while (IntroSequence.Active) yield return 0.1f;
        yield return 1f;
    }
}
