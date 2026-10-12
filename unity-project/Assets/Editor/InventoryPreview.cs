using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Captura PNG del inventario y del HUD (hotbar) en Play con dispositivo gráfico (xvfb, sin -nographics).
[InitializeOnLoad]
public static class InventoryPreview
{
    private static IEnumerator routine;
    private static double waitUntil;

    static InventoryPreview()
    {
        if (SessionState.GetString("smoke_phase", "") != "invprev") return;
        EditorApplication.update += Tick;
    }

    public static void Export()
    {
        Directory.CreateDirectory("/workspace/monster-previews");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "invprev");
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) { bool sw = SessionState.GetBool("invprev_sweep", false); SessionState.SetBool("invprev_sweep", false); routine = sw ? Sweep() : Run(); }
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try
        {
            if (!routine.MoveNext()) Finish();
            else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current;
        }
        catch (System.Exception e) { Debug.LogError("[InventoryPreview] " + e); Finish(); }
    }

    private static void Finish()
    {
        SessionState.SetString("smoke_phase", "");
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    // Barrido de resoluciones con objetivos largos: /workspace/monster-previews/invsweep/
    public static void ExportSweep()
    {
        Directory.CreateDirectory("/workspace/monster-previews/invsweep");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "invprev");
        SessionState.SetBool("invprev_sweep", true);
        EditorApplication.EnterPlaymode();
    }

    static readonly string[] LongObj = {
        "encuentra la salida de las Oficinas amarillas sin que te atrapen; revisa cada pasillo, cada despacho y cada rincón húmedo de moqueta antes de que se apaguen las luces otra vez.",
        "cruza el Aparcamiento subterráneo, esquiva lo que acecha entre los coches abandonados y localiza la rampa de salida señalizada en amarillo, sin gastar todas las pilas.",
        "atraviesa las Poolrooms: nada entre las piscinas interminables, sigue los azulejos agrietados y encuentra la escalera de servicio antes de que el agua te arrastre al fondo.",
        "registra el Hotel abandonado habitación por habitación, recupera la llave del ascensor del vestíbulo y escapa sin despertar a ¿quién? sigue llamando a la puerta 217.",
        "escapa del Complejo industrial: activa los tres generadores, abre la compuerta principal y sal antes de que la maquinaria vuelva a encenderse y te encuentre allí dentro." };
    static readonly int[,] Res = { { 1280, 720 }, { 1920, 1080 }, { 2560, 1440 }, { 3840, 2160 }, { 1024, 768 }, { 1600, 1200 }, { 1280, 1024 }, { 1440, 900 }, { 1920, 1200 }, { 2560, 1080 }, { 3440, 1440 }, { 5120, 1440 } };
    private static IEnumerator Sweep()
    {
        yield return 3f;
        UIManager ui = UIManager.Instance;
        string report = "";
        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            ui.StartGame(lv);
            yield return 1f;
            while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded || GameManager.Instance == null) yield return 0.3f;
            yield return 1.5f;
            Inventory inv = Inventory.Instance;
            inv.Add(ItemType.Battery, 3); inv.Add(ItemType.AlmondWater, 2); inv.Add(ItemType.Glowstick, 3); inv.Add(ItemType.VhsTape, 1);
            InventoryUI.DebugObjective = LongObj[lv];
            InventoryUI.DebugLevelName = lv == 3 ? "Hotel abandonado — ala este, planta subterránea" : null;
            if (uiCam == null) SetupUi(); else SetupUi2();
            ui.ToggleInventory();
            yield return 1.0f;
            ui.InventoryUi.Select(lv == 4 ? 4 : 2);
            for (int r = 0; r < Res.GetLength(0); r++)
            {
                if (lv != 0 && lv != 3 && r != 0 && r != 4 && r != 11) continue;
                int w = Res[r, 0], h = Res[r, 1];
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                uiCam.targetTexture = rt;
                yield return 0.3f;
                string of = ui.InventoryUi.OverflowingTexts() + (ui.InventoryUi.PanelFits() ? "" : " PANEL_FUERA");
                report += "L" + lv + " " + w + "x" + h + ": " + (of.Length == 0 ? "OK" : "DESBORDA " + of) + "\n";
                CaptureRt(rt, string.Format("/workspace/monster-previews/invsweep/L{0}_{1}x{2}.png", lv, w, h));
                uiCam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
            }
            ui.ToggleInventory();
            yield return 0.5f;
            ui.SetScreen(UIManager.Screen.None);
        }
        File.WriteAllText("/workspace/monster-previews/invsweep/report.txt", report);
        Debug.Log("[InventoryPreview] barrido\n" + report);
        InventoryUI.DebugObjective = null; InventoryUI.DebugLevelName = null;
    }

    private static void SetupUi2()
    {
        foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = uiCam; c.planeDistance = 1f;
            foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
        }
    }

    private static void CaptureRt(RenderTexture rt, string path)
    {
        int w = rt.width, h = rt.height;
        Camera game = Camera.main;
        if (game != null) { RenderTexture o = game.targetTexture; game.targetTexture = rt; game.Render(); game.targetTexture = o; }
        Canvas.ForceUpdateCanvases();
        uiCam.Render();
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    private static IEnumerator Run()
    {
        yield return 3f;
        UIManager ui = UIManager.Instance;
        ui.StartGame(0);
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded || GameManager.Instance == null) yield return 0.3f;
        yield return 2f;
        Inventory inv = Inventory.Instance;
        inv.Add(ItemType.Battery, 3);
        inv.Add(ItemType.AlmondWater, 2);
        inv.Add(ItemType.Glowstick, 3);
        inv.Add(ItemType.VhsTape, 1);
        inv.AddNote(LoreNotes.IdFor(0, 0));
        inv.AddNote(LoreNotes.IdFor(0, 1));
        Flashlight fl = Object.FindObjectOfType<Flashlight>();
        if (fl != null) fl.SetBattery(63f);
        yield return 1.5f; SetupUi();
        yield return 1.0f;
        Capture("/workspace/monster-previews/hud_hotbar.png");
        yield return 0.8f;
        ui.ToggleInventory();
        yield return 1.5f;
        ui.InventoryUi.Select(1);
        yield return 1.0f;
        Capture("/workspace/monster-previews/inventory_preview.png");
        yield return 0.8f;
        ui.InventoryUi.Select(2);
        yield return 1.0f;
        Capture("/workspace/monster-previews/inventory_preview_item.png");
        ui.InventoryUi.SetTab(true);
        yield return 0.6f;
        ui.InventoryUi.SelectNote(LoreNotes.IdFor(0, 0));
        yield return 0.8f;
        Capture("/workspace/monster-previews/inventory_preview_notas.png");
        yield return 1f;
    }

    private static Camera uiCam;

    private static void SetupUi()
    {
        GameObject ugo = new GameObject("PreviewUiCam");
        uiCam = ugo.AddComponent<Camera>();
        uiCam.clearFlags = CameraClearFlags.Depth;
        uiCam.cullingMask = 1 << 31;
        uiCam.orthographic = true;
        uiCam.depth = 100;
        uiCam.enabled = false;
        foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = uiCam; c.planeDistance = 1f;
            foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
        }
    }

    private static void Capture(string path)
    {
        const int W = 1280, H = 720;
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        Camera game = Camera.main;
        if (game == null) { Debug.LogError("[InventoryPreview] sin cámara principal"); return; }
        RenderTexture oldTarget = game.targetTexture;
        game.targetTexture = rt;
        game.Render();
        game.targetTexture = oldTarget;
        Canvas.ForceUpdateCanvases();
        uiCam.targetTexture = rt;
        uiCam.Render();
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        RenderTexture.ReleaseTemporary(rt);
        Debug.Log("[InventoryPreview] captura " + path);
    }
}
