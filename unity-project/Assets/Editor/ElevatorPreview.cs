using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renderiza /workspace/monster-previews/elevator_*.png: interior de la cabina durante el descenso, llegada, puertas abriéndose,
// puertas abiertas al nivel, cabina industrial (luz roja de emergencia) y la cabina cerrada vista desde fuera.
//   xvfb-run -a Unity -batchmode -projectPath <p> -executeMethod ElevatorPreview.Export -force-glcore
[InitializeOnLoad]
public static class ElevatorPreview
{
    private static IEnumerator routine;
    private static double waitUntil;

    static ElevatorPreview()
    {
        if (SessionState.GetString("smoke_phase", "") != "elevprev") return;
        EditorApplication.update += Tick;
    }

    public static void Export()
    {
        Directory.CreateDirectory("/workspace/monster-previews");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "elevprev");
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
        catch (System.Exception e) { Debug.LogError("[ElevatorPreview] " + e); Finish(); }
    }

    private static void Finish()
    {
        SessionState.SetString("smoke_phase", "");
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    private static IEnumerator LoadLevel(int lv)
    {
        GameBootstrap gb = GameBootstrap.Instance;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(lv);
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu)) yield return 0.2f;
        yield return 0.6f;
    }

    private static Camera uiCam;
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

    private static void Capture(string path, bool ui)
    {
        const int W = 1280, H = 720;
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        Camera game = Camera.main;
        RenderTexture old = game.targetTexture;
        game.targetTexture = rt; game.Render(); game.targetTexture = old;
        if (ui) { SetupUi(); Canvas.ForceUpdateCanvases(); uiCam.targetTexture = rt; uiCam.Render(); }
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); RenderTexture.ReleaseTemporary(rt);
        Debug.Log("[ElevatorPreview] captura " + path);
    }

    private static void Prep()
    {
        GameManager gm = GameManager.Instance;
        foreach (MonsterAI m in gm.monsters) m.Stop();
        Camera.main.GetComponent<VhsEffect>().enabled = true;
    }

    private static IEnumerator Run()
    {
        ElevatorCabin.InstantForTests = false;
        GameSettings.ResetDefaults();
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded) yield return 0.3f;
        yield return 2f;
        IEnumerator lv0 = LoadLevel(0); while (lv0.MoveNext()) yield return lv0.Current;
        ElevatorCabin cab = ElevatorCabin.Current;
        Debug.Log("[ElevatorPreview] cabina: modelo=" + cab.HasModel + " pos=" + cab.transform.position + " interior=" + cab.InteriorCenter + " fase=" + cab.CurrentPhase + " locked=" + cab.Locked);
        Prep();
        string d = "/workspace/monster-previews/";
        // 1) descenso (nivel 0): mirando al indicador
        cab.SeekPreview(3.4f, true); yield return 0.1f; cab.SeekPreview(3.4f, true);
        Capture(d + "elevator_descent_level0.png", true);
        // 2) interior desde el fondo (vista general)
        cab.SeekPreview(2.2f, false);
        {
            Transform pv = Camera.main.transform;
            Vector3 b = pv.position; Quaternion rr = pv.rotation;
            pv.position = cab.transform.TransformPoint(new Vector3(0.7f, 1.5f, -0.95f * 1.3f)); pv.rotation = Quaternion.LookRotation(cab.transform.TransformPoint(new Vector3(-0.3f, 1.0f, 1.2f * 1.3f)) - pv.position);
            Capture(d + "elevator_interior_wide.png", false);
            pv.position = cab.transform.TransformPoint(new Vector3(-0.2f, 1.75f, 0.1f)); pv.rotation = Quaternion.LookRotation(cab.transform.TransformPoint(new Vector3(0f, 2.14f, 1.3f)) - pv.position);
            Capture(d + "elevator_display_closeup.png", false);
            pv.position = b; pv.rotation = rr;
        }
        // 3) llegada: el contador se detiene y se enciende «NIVEL 0»
        cab.SeekPreview(6.62f, true); yield return 0.1f; cab.SeekPreview(6.62f, true);
        Capture(d + "elevator_arrival.png", true);
        // 4) puertas abriéndose
        cab.SeekPreview(7.45f, true); yield return 0.1f; cab.SeekPreview(7.45f, true);
        Capture(d + "elevator_doors_opening.png", true);
        // 5) puertas abiertas, se ve el nivel (cámara a la altura de los ojos, sin sacudida)
        cab.SeekPreview(8.0f, false);
        Camera.main.transform.localPosition = new Vector3(0f, 1.6f, 0f); Camera.main.transform.localRotation = Quaternion.identity;
        yield return 0.2f;
        Capture(d + "elevator_doors_open.png", true);
        // 6) desde fuera, con las puertas abiertas y luego cerradas (la cabina se queda atrás)
        {
            Transform pv = Camera.main.transform;
            Vector3 outPos = cab.ExitPoint + cab.Forward * 3.2f + Vector3.up * 1.4f;
            pv.position = outPos; pv.rotation = Quaternion.LookRotation(cab.InteriorCenter + Vector3.up * 1.1f - outPos);
            Capture(d + "elevator_exterior_open.png", false);
            cab.SeekClosed();
            Capture(d + "elevator_exterior_closed.png", false);
        }
        // 7) nivel 4: luz de emergencia roja
        IEnumerator lv4 = LoadLevel(4); while (lv4.MoveNext()) yield return lv4.Current;
        cab = ElevatorCabin.Current; Prep();
        cab.SeekPreview(3.4f, true); yield return 0.1f; cab.SeekPreview(3.4f, true);
        Capture(d + "elevator_descent_level4_industrial.png", true);
        // 8) nivel 2 (Poolrooms): llegada en cian
        IEnumerator lv2 = LoadLevel(2); while (lv2.MoveNext()) yield return lv2.Current;
        cab = ElevatorCabin.Current; Prep();
        cab.SeekPreview(6.62f, true); yield return 0.1f; cab.SeekPreview(6.62f, true);
        Capture(d + "elevator_arrival_level2_poolrooms.png", true);
        cab.SeekPreview(8.0f, false);
        Camera.main.transform.localPosition = new Vector3(0f, 1.6f, 0f); Camera.main.transform.localRotation = Quaternion.identity;
        yield return 0.2f;
        Capture(d + "elevator_doors_open_level2.png", true);
        yield return 0.3f;
    }
}
