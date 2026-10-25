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
        if (routine == null) { bool sw = SessionState.GetBool("invprev_sweep", false), nt = SessionState.GetBool("invprev_notes", false); SessionState.SetBool("invprev_sweep", false); SessionState.SetBool("invprev_notes", false); bool v9 = SessionState.GetBool("invprev_v790", false); SessionState.SetBool("invprev_v790", false); routine = v9 ? V790Shots() : nt ? Notes() : sw ? Sweep() : Run(); }
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

    // v7.9.0: pestaña Notas (27 notas, Nivel 114, una nota encontrada) a 16:9, 16:10, 4:3, 21:9 y 720p; con informe de
    // maquetación (barras encima de la lista, lista cortada, sin desplazamiento). Salida en $NOTES_OUT (por defecto invnotes/).
    public static void ExportNotes()
    {
        Directory.CreateDirectory(NotesOut);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "invprev");
        SessionState.SetBool("invprev_notes", true);
        EditorApplication.EnterPlaymode();
    }
    static string NotesOut { get { string o = System.Environment.GetEnvironmentVariable("NOTES_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/monster-previews/invnotes" : o; } }
    static readonly int[,] NotesRes = { { 1920, 1080 }, { 1280, 720 }, { 1920, 1200 }, { 1440, 900 }, { 1024, 768 }, { 1600, 1200 }, { 2560, 1080 }, { 3440, 1440 } };
    private static IEnumerator Notes()
    {
        yield return 3f;
        UIManager ui = UIManager.Instance;
        int lv = 0; for (int i = 0; i < LevelConfig.Count; i++) if (LevelConfig.Get(i).layout == LayoutKind.Desert) lv = i;
        ui.StartGame(lv);
        yield return 1f;
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded || GameManager.Instance == null) yield return 0.3f;
        yield return 2f;
        Inventory inv = Inventory.Instance;
        int nid = LoreNotes.IdFor(lv, 2); inv.AddNote(nid);
        SetupUi();
        ui.ToggleInventory();
        yield return 1.0f;
        ui.InventoryUi.SetTab(true);
        ui.InventoryUi.SelectNote(nid);
        yield return 0.6f;
        string report = "notas " + LoreNotes.Total + "\n";
        System.Reflection.MethodInfo lay = typeof(InventoryUI).GetMethod("NotesLayoutProblems");
        System.Reflection.MethodInfo scr = typeof(InventoryUI).GetMethod("DebugScrollNotes");
        for (int r = 0; r < NotesRes.GetLength(0); r++)
        {
            int w = NotesRes[r, 0], h = NotesRes[r, 1];
            for (int pass = 0; pass < (scr != null ? 2 : 1); pass++)
            {
                if (scr != null) scr.Invoke(ui.InventoryUi, new object[] { pass == 0 ? 1f : 0f });
                RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                uiCam.targetTexture = rt;
                yield return 0.3f;
                Canvas.ForceUpdateCanvases();
                string lp = lay != null ? (string)lay.Invoke(ui.InventoryUi, null) : "(sin comprobación)";
                string of = ui.InventoryUi.OverflowingTexts() + (ui.InventoryUi.PanelFits() ? "" : " PANEL_FUERA");
                report += w + "x" + h + (pass == 1 ? " (abajo)" : "") + ": maquetación " + (lp.Length == 0 ? "OK" : lp) + "; textos " + (of.Length == 0 ? "OK" : of) + "\n";
                CaptureRt(rt, string.Format("{0}/notas_{1}x{2}{3}.png", NotesOut, w, h, pass == 1 ? "_abajo" : ""));
                uiCam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
        File.WriteAllText(NotesOut + "/report.txt", report);
        Debug.Log("[InventoryPreview] notas\n" + report);
    }

    // v7.9.0: capturas en la caja (xvfb, render por software: calidad menor que en el PC): HUD con VIDA, inventario con
    // VIDA y huellas de cerca en el Nivel 114.  Salida en $NOTES_OUT/v790/.
    public static void ExportV790()
    {
        Directory.CreateDirectory(NotesOut + "/v790");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "invprev");
        SessionState.SetBool("invprev_v790", true);
        EditorApplication.EnterPlaymode();
    }
    private static IEnumerator V790Shots()
    {
        string o = NotesOut + "/v790";
        yield return 3f;
        UIManager ui = UIManager.Instance;
        ElevatorCabin.InstantForTests = true;
        ui.StartGame(0);
        yield return 1f;
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded || GameManager.Instance == null) yield return 0.3f;
        yield return 2.5f;
        if (Health.Instance != null) { Health.Instance.value = 72f; Health.Instance.Damage(0.01f, "captura"); }
        SetupUi();
        yield return 0.8f;
        Shot(o + "/hud_vida_nivel0.png", 1920, 1080, null);
        ui.ToggleInventory(); yield return 1.0f;
        Shot(o + "/inventario_vida.png", 1920, 1080, null);
        ui.ToggleInventory(); yield return 0.5f; ui.SetScreen(UIManager.Screen.None);
        // Nivel 114 a mediodía: andar, correr y agacharse por la arena, y mirar las huellas de cerca
        int lv = 0; for (int i = 0; i < LevelConfig.Count; i++) if (LevelConfig.Get(i).layout == LayoutKind.Desert) lv = i;
        int ld = GameBootstrap.Instance.LoadCount;
        ui.StartGame(lv);
        while (!(GameBootstrap.Instance.LoadCount > ld && GameBootstrap.Instance.IsLoaded && GameManager.Instance != null)) yield return 0.3f;
        float wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 1.5f;
        DesertSky.ForceWeather = 0; DesertSky.ForceHour = 17.2f;
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player; DesertWorld w = DesertWorld.Active;
        if (Health.Instance != null) Health.DisableForTests = true;
        // salir de la cabina hacia la arena, en un sitio llano
        Vector3 st = w.levelRoot.InverseTransformPoint(p.transform.position);
        Vector3 spot = st + new Vector3(18f, 0f, 9f);
        for (int k = 0; k < 40; k++) { Vector3 q = st + new Vector3(Mathf.Cos(k * 2.4f), 0f, Mathf.Sin(k * 2.4f)) * (14f + k * 3f); float x = Mathf.Repeat(q.x, w.L), z = Mathf.Repeat(q.z, w.L); if (DesertHeight.Normal(x, z).y > 0.985f && w.plan.QuickAt(new Vector2(x, z), 10f) < 0) { spot = q; break; } }
        spot.y = DesertHeight.H(Mathf.Repeat(spot.x, w.L), Mathf.Repeat(spot.z, w.L));
        p.Teleport(w.levelRoot.TransformPoint(spot + Vector3.up * 0.3f), 30f); yield return 1.0f;
        p.debugInput = true; p.debugMove = new Vector2(0f, 1f); yield return 2.2f;
        p.debugSprint = true; yield return 1.4f; p.debugSprint = false; p.debugCrouch = true; yield return 2.0f;
        p.debugCrouch = false; p.debugMove = Vector2.zero; yield return 0.6f;
        Shot(o + "/hud_vida_desierto.png", 1920, 1080, null);
        // cámara baja detrás del rastro, mirando hacia atrás y abajo
        Vector3 back = -p.transform.forward;
        Vector3 eye = p.transform.position + back * 0.9f + Vector3.up * 0.95f;
        Vector3 look = p.transform.position + back * 2.4f;
        p.InputEnabled = false;
        Flashlight fl = p.GetComponentInChildren<Flashlight>(); if (fl != null && fl.IsOn) fl.Toggle();   // de día, sin el foco de la linterna
        yield return 0.3f;
        Shot(o + "/huellas_cerca.png", 1920, 1080, cam => { cam.transform.position = eye; cam.transform.rotation = Quaternion.LookRotation(look - eye); });
        Vector3 eye2 = p.transform.position + back * 4.5f + Vector3.up * 0.7f + p.transform.right * 0.6f;
        Shot(o + "/huellas_cerca_2.png", 1920, 1080, cam => { cam.transform.position = eye2; cam.transform.rotation = Quaternion.LookRotation((p.transform.position + back * 6.5f) - eye2); });
        DesertSky.ForceWeather = -1; DesertSky.ForceHour = -1f; Health.DisableForTests = false;
    }
    private static void Shot(string path, int w, int h, System.Action<Camera> place)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        uiCam.targetTexture = rt;
        Camera game = Camera.main;
        if (place != null && game != null)
        {
            Vector3 p0 = game.transform.position; Quaternion r0 = game.transform.rotation;
            place(game);
            RenderTexture o = game.targetTexture; game.targetTexture = rt; game.Render(); game.targetTexture = o;
            game.transform.position = p0; game.transform.rotation = r0;
            RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        }
        else CaptureRt(rt, path);
        uiCam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
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
