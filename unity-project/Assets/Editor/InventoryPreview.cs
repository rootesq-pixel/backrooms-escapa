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
        if (routine == null) routine = Run();
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
