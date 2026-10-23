using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.4.0: capturas de comparación (sin VHS) de la cabina del ascensor (interior/exterior) y del Nivel 1 (aparcamiento).
//   V740_DIR=/workspace/v740/compare/<antes|despues> xvfb-run -a Unity -batchmode -projectPath <p> -executeMethod V740Shots.Run
[InitializeOnLoad]
public static class V740Shots
{
    private static IEnumerator routine; private static double waitUntil;
    static string Dir { get { return System.Environment.GetEnvironmentVariable("V740_DIR") ?? "/workspace/v740/compare/raw"; } }
    static string Only { get { return System.Environment.GetEnvironmentVariable("V740_ONLY") ?? ""; } }   // "elev", "l1" o vacío (todo)
    static V740Shots() { if (SessionState.GetString("smoke_phase", "") != "v740shots") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v740shots");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Main();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Info("ERROR " + e); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; Debug.Log("[V740Shots] FIN"); EditorApplication.Exit(0); }
    static readonly System.Text.StringBuilder info = new System.Text.StringBuilder();
    static void Info(string s) { info.AppendLine(s); Directory.CreateDirectory(Dir); File.WriteAllText(Dir + "/info.txt", info.ToString()); Debug.Log("[V740Shots] " + s); }

    static void Shot(string name)
    {
        const int W = 1280, H = 720;
        Camera cam = Camera.main;
        VhsEffect v = cam.GetComponent<VhsEffect>(); if (v != null) v.enabled = false;
        RenderTexture rt = new RenderTexture(W, H, 24); RenderTexture prev = cam.targetTexture;
        cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        Texture2D t = new Texture2D(W, H, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply();
        Directory.CreateDirectory(Dir); File.WriteAllBytes(Dir + "/" + name + ".png", t.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = prev; Object.DestroyImmediate(rt); Object.DestroyImmediate(t);
        Info(name + " cam " + cam.transform.position.ToString("F2"));
    }
    static void At(Vector3 pos, Vector3 target)
    {
        Transform c = Camera.main.transform; c.position = pos; c.rotation = Quaternion.LookRotation(target - pos);
    }
    static IEnumerator Load(int lv)
    {
        GameBootstrap gb = GameBootstrap.Instance; int ld = gb.LoadCount;
        UIManager.Instance.StartGame(lv);
        float wt = 0f;
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        yield return 1.0f;
        GameManager.HideControlsHint = true;
        foreach (MonsterAI m in GameManager.Instance.monsters) if (m != null) m.Stop();
        HideBody();
    }
    // cámara libre: se oculta el cuerpo del jugador y se apaga la linterna
    static void HideBody()
    {
        GameManager gm = GameManager.Instance; if (gm == null || gm.player == null) return;
        foreach (Renderer r in gm.player.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        Flashlight fl = Object.FindObjectOfType<Flashlight>(); if (fl != null) fl.SetOn(false);
    }

    private static IEnumerator Main()
    {
        GameSettings.ResetDefaults();
        GameSettings.Quality = int.Parse(System.Environment.GetEnvironmentVariable("V740_QUALITY") ?? "2");
        ElevatorCabin.InstantForTests = false;
        GameBootstrap gb = GameBootstrap.Instance; float w0 = 0f;
        while ((gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) && w0 < 120f) { yield return 0.2f; w0 += 0.2f; gb = GameBootstrap.Instance; }
        gb.useRandomSeed = false; gb.seed = 7400;
        { IEnumerator l = Load(1); while (l.MoveNext()) yield return l.Current; }
        Info("GPU: " + SystemInfo.graphicsDeviceName + " calidad " + GameSettings.Quality);
        ElevatorCabin cab = ElevatorCabin.Current;
        Transform ct = cab.transform;
        System.Func<float, float, float, Vector3> L = (x, y, z) => ct.TransformPoint(new Vector3(x, y, z));
        if (Only != "l1")
        {
            Info("cabina modelo=" + cab.HasModel);
            // interior durante el descenso (luz encendida)
            cab.SeekPreview(2.2f, false); yield return 0.2f; cab.SeekPreview(2.2f, false);
            At(L(0.75f, 1.55f, -1.0f), L(-0.35f, 1.05f, 1.35f)); Shot("e01_interior_puertas");
            At(L(-0.55f, 1.6f, 0.9f), L(0.55f, 1.05f, -1.15f)); Shot("e02_interior_fondo_botonera");
            At(L(-0.1f, 1.65f, 0.2f), L(0f, 2.14f, 1.3f)); Shot("e03_indicador_planta");
            At(L(0.3f, 1.4f, -0.3f), L(-0.2f, 2.3f, 0.6f)); Shot("e04_techo_luz");
            At(L(0.4f, 1.3f, 0.6f), L(1.17f, 1.0f, -0.6f)); Shot("e05_pasamanos_pared");
            // frenazo: parpadeo/tenue
            cab.SeekPreview(5.6f, false); yield return 0.1f; cab.SeekPreview(5.6f, false);
            At(L(0.75f, 1.55f, -1.0f), L(-0.35f, 1.05f, 1.35f)); Shot("e06_frenazo_parpadeo");
            // puertas abriéndose desde dentro
            cab.SeekPreview(7.45f, false); yield return 0.1f; cab.SeekPreview(7.45f, false);
            At(L(0f, 1.6f, -0.6f), L(0f, 1.3f, 2.5f)); Shot("e07_puertas_abriendo");
            // exterior abierto y cerrado
            cab.SeekPreview(8.2f, false); yield return 0.1f;
            Vector3 op = cab.ExitPoint + cab.Forward * 3.0f + Vector3.up * 1.5f + ct.right * 0.8f;
            At(op, cab.InteriorCenter + Vector3.up * 1.1f); Shot("e08_exterior_abierto");
            cab.SeekClosed(); yield return 0.1f;
            At(op, cab.InteriorCenter + Vector3.up * 1.1f); Shot("e09_exterior_cerrado");
        }
        if (Only != "elev")
        {
            cab.SeekPreview(8.2f, false); yield return 0.1f;
            GameManager gm = GameManager.Instance;
            gm.player.Teleport(cab.ExitPoint + cab.Forward * 2f, ct.eulerAngles.y);
            Flashlight fl = Object.FindObjectOfType<Flashlight>();
            yield return 0.5f;
            HideBody();
            Vector3 b = cab.ExitPoint + cab.Forward * 2.5f; float y = b.y;
            Vector3 f = cab.Forward, r = ct.right;
            At(b + Vector3.up * 1.6f, b + f * 14f + Vector3.up * 1.0f); Shot("p01_pasillo_frente");
            At(b + Vector3.up * 1.6f, b + r * 14f + Vector3.up * 0.6f); Shot("p02_lado_derecho");
            At(b + Vector3.up * 1.6f, b - r * 14f + Vector3.up * 0.6f); Shot("p03_lado_izquierdo");
            At(b + Vector3.up * 1.7f + f * 2f, b + f * 5f + Vector3.down * 0.1f); Shot("p04_suelo_lineas");
            At(b + Vector3.up * 1.5f + f * 3f, b + f * 7f + Vector3.up * 3.6f); Shot("p05_techo");
            if (fl != null)
            {
                fl.SetOn(true); yield return 0.3f;
                foreach (Renderer rr in gm.player.GetComponentsInChildren<Renderer>(true)) rr.enabled = false;
                At(b + Vector3.up * 1.6f + f * 4f, b + f * 12f + r * 3f + Vector3.up * 0.5f); Shot("p06_linterna_pilares");
                fl.SetOn(false);
            }
            // versión aclarada (solo para comparar texturas: más luz ambiente y menos niebla; el juego no cambia)
            Color amb = RenderSettings.ambientLight; float fd = RenderSettings.fogDensity;
            RenderSettings.ambientLight = amb * 5f + new Color(0.05f, 0.05f, 0.05f); RenderSettings.fogDensity = fd * 0.3f;
            At(b + Vector3.up * 1.6f, b + f * 14f + Vector3.up * 1.0f); Shot("p11_pasillo_frente_aclarado");
            At(b + Vector3.up * 1.7f + f * 2f, b + f * 5f + Vector3.down * 0.1f); Shot("p14_suelo_lineas_aclarado");
            At(b + Vector3.up * 1.5f + f * 3f, b + f * 7f + Vector3.up * 3.6f); Shot("p15_techo_aclarado");
            At(b + Vector3.up * 1.6f, b - r * 14f + Vector3.up * 0.6f); Shot("p13_lado_izquierdo_aclarado");
            RenderSettings.ambientLight = amb; RenderSettings.fogDensity = fd;
            Info("aceite " + GarageDecor.OilStains + " charcos " + GarageDecor.WetPatches);
            GameObject og = GameObject.Find("GarageOilStains");
            if (og != null)
            {
                MeshRenderer omr = og.GetComponent<MeshRenderer>(); Mesh om = og.GetComponent<MeshFilter>().sharedMesh;
                Vector3 v0 = og.transform.TransformPoint(om.vertices[0]);
                RaycastHit hit; bool h = Physics.Raycast(v0 + Vector3.up * 1f, Vector3.down, out hit, 3f);
                Info("oil bounds " + omr.bounds + " active " + og.activeInHierarchy + " en " + omr.enabled + " v0 " + v0 + " floorhit " + (h ? hit.point.ToString("F3") + " " + hit.collider.name : "none") + " shader " + omr.sharedMaterial.shader.name + " q " + omr.sharedMaterial.renderQueue + " tex " + (omr.sharedMaterial.mainTexture ? omr.sharedMaterial.mainTexture.name + " " + omr.sharedMaterial.mainTexture.width + " fmt " + ((Texture2D)omr.sharedMaterial.mainTexture).format : "NULL") + " kw " + string.Join(",", omr.sharedMaterial.shaderKeywords) + " col " + omr.sharedMaterial.color + " uv0 " + om.uv[0] + " uv2 " + om.uv[2] + " n " + om.normals[0]);
                Vector3 c4 = (og.transform.TransformPoint(om.vertices[0]) + og.transform.TransformPoint(om.vertices[2])) * 0.5f;
                RenderSettings.ambientLight = amb * 5f + new Color(0.05f, 0.05f, 0.05f);
                At(c4 + Vector3.up * 2.2f + Vector3.forward * 0.8f, c4); Shot("p16_mancha_aceite_aclarado");
                RenderSettings.ambientLight = amb;
            }
        }
        yield return 0.2f;
    }
}
