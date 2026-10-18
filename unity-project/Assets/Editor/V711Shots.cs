using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.1.1: capturas antes/después desde ángulos idénticos (misma semilla, cámara fija en coordenadas locales de cada sala):
// Parque de bolas (tobogán, paredes de las piscinas, vistas generales), Poolrooms y Nivel 0.
//   V711_DIR=/ruta V711_LEVELS=6,2,0 Unity -batchmode -executeMethod V711Shots.Run  (con gráficos, sin -nographics)
[InitializeOnLoad]
public static class V711Shots
{
    private static IEnumerator routine; private static double waitUntil;
    static string Dir { get { return System.Environment.GetEnvironmentVariable("V711_DIR") ?? "/workspace/gpu-previews/after"; } }
    static V711Shots() { if (SessionState.GetString("smoke_phase", "") != "v711shots") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v711shots");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Main();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V711Shots] " + e); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; Debug.Log("[V711Shots] FIN"); EditorApplication.Exit(0); }

    private static void Shot(Camera cam, string name, int w = 1600, int h = 900)
    {
        RenderTexture rt = new RenderTexture(w, h, 24); RenderTexture prev = cam.targetTexture;
        cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        Texture2D t = new Texture2D(w, h, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        Directory.CreateDirectory(Dir); File.WriteAllBytes(Dir + "/" + name + ".png", t.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = prev; Object.DestroyImmediate(rt); Object.DestroyImmediate(t);
        Debug.Log("[V711Shots] " + name);
    }

    struct View { public string n; public Vector3 p, t; public float fov; public View(string n, Vector3 p, Vector3 t, float fov = 70f) { this.n = n; this.p = p; this.t = t; this.fov = fov; } }

    static readonly View[] BallpitViews = {
        new View("ballpit_slide_top", new Vector3(2.7f, 2.45f, 4.1f), new Vector3(0.2f, 0.6f, 5.1f)),
        new View("ballpit_slide_side", new Vector3(0.9f, 1.25f, 2.3f), new Vector3(0.6f, 0.75f, 4.95f)),
        new View("ballpit_slide_exit", new Vector3(-2.2f, 1.5f, 3.2f), new Vector3(0.3f, 0.8f, 4.95f)),
        new View("ballpit_pitwall_south", new Vector3(-0.6f, 1.45f, -0.5f), new Vector3(-4.6f, 0.25f, 1.0f)),
        new View("ballpit_pitwall_north", new Vector3(-1.0f, 1.7f, 3.6f), new Vector3(-4.8f, 0.2f, 7.0f)),
        new View("ballpit_pitwall_west", new Vector3(-3.0f, 1.6f, 2.0f), new Vector3(-6.4f, 0.2f, 5.2f)),
        new View("ballpit_pit2", new Vector3(-3.6f, 1.6f, -3.0f), new Vector3(-7.2f, 0.1f, -5.4f)),
        new View("ballpit_overview_a", new Vector3(2.8f, 1.62f, -6.6f), new Vector3(-2.5f, 0.6f, 4.0f)),
        new View("ballpit_overview_b", new Vector3(-8.6f, 1.62f, -1.2f), new Vector3(2.0f, 1.2f, 4.2f)),
        new View("ballpit_structure", new Vector3(9.3f, 1.62f, -4.4f), new Vector3(2.0f, 1.6f, 3.0f)),
        new View("ballpit_tube", new Vector3(4.4f, 1.5f, -3.9f), new Vector3(8.0f, 1.0f, -1.9f)),
        new View("ballpit_tube_exit", new Vector3(7.6f, 0.9f, -6.1f + 1.5f), new Vector3(7.9f, 0.4f, -2.6f)),
        new View("ballpit_party_room", new Vector3(5.2f, 1.6f, -5.4f), new Vector3(8.6f, 0.6f, -6.6f)),
        new View("ballpit_counter", new Vector3(-2.0f, 1.6f, -4.2f), new Vector3(-2.2f, 1.0f, -7.6f)),
    };
    static readonly View[] PoolViews = {
        new View("pool_a", new Vector3(-6.5f, 1.85f, -4.6f), new Vector3(3.0f, 0.4f, 2.0f)),
        new View("pool_b", new Vector3(6.5f, 1.85f, 4.6f), new Vector3(-3.0f, 0.8f, -2.0f)),
    };

    private static IEnumerator Main()
    {
        string lvs = System.Environment.GetEnvironmentVariable("V711_LEVELS") ?? "6,2,0";
        int q = int.Parse(System.Environment.GetEnvironmentVariable("V711_QUALITY") ?? "2");
        string suf = q == 2 ? "" : "_q" + q;
        GameSettings.Quality = q;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        foreach (string s in lvs.Split(','))
        {
            int lv = int.Parse(s.Trim());
            if (!gb.InMenu) { gb.GoToMainMenu(); yield return 1.0f; float mw = 0f; while (!(gb.IsLoaded && gb.InMenu) && mw < 60f) { yield return 0.2f; mw += 0.2f; } yield return 1.0f; }
            gb.useRandomSeed = false; gb.seed = 7110;
            int ld = gb.LoadCount;
            UIManager.Instance.StartGame(lv);
            float wt = 0f;
            while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
            if (ElevatorCabin.Current != null) ElevatorCabin.Current.Skip();
            float w = 0f; while (ElevatorCabin.PlayerLocked && w < 20f) { yield return 0.25f; w += 0.25f; }
            GameManager gm = GameManager.Instance; FirstPersonController pw = gm.player;
            Camera cam = pw.GetComponentInChildren<Camera>();
            GameManager.HideControlsHint = true;
            foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
            Flashlight fl = pw.GetComponentInChildren<Flashlight>(); if (fl != null) fl.SetOn(false);
            yield return 1.5f;
            int mask = cam.cullingMask; cam.cullingMask &= ~(1 << 8);
            float fov0 = cam.fieldOfView;
            Transform root = null; View[] views = null; string tag = "";
            if (lv == 6 && BallpitSetPiece.Roots.Count > 0) { root = BallpitSetPiece.Roots[0].transform; views = BallpitViews; }
            else if (lv == 2 && PoolRoomSetPiece.Root != null) { root = PoolRoomSetPiece.Root.transform; Transform bk = root.Find("PoolRoomBaked"); if (bk != null) root = bk; views = PoolViews; }   // coordenadas de la malla horneada
            if (root != null)
            {
                foreach (View v in views)
                {
                    Vector3 wp = root.TransformPoint(v.p), wtg = root.TransformPoint(v.t);
                    pw.Teleport(wp - Vector3.up * 1.6f, 0f); yield return 0.6f;
                    cam.transform.position = wp; cam.transform.rotation = Quaternion.LookRotation(wtg - wp); cam.fieldOfView = v.fov;
                    Shot(cam, v.n + suf);
                }
            }
            else
            {   // Nivel 0 (u otro): desde el punto de inicio, 3 direcciones
                BackroomsGenerator gen = gb.Generator;
                Vector3 st = gen.PlayerStart;
                Vector3 fwd = Quaternion.Euler(0f, gen.PlayerStartYaw, 0f) * Vector3.forward;
                float d = 4f; RaycastHit hh;
                if (Physics.Raycast(st + Vector3.up * 1.6f, fwd, out hh, 4.4f)) d = Mathf.Max(0f, hh.distance - 0.4f);
                Vector3 bs = st + fwd * d;   // fuera de la cabina del ascensor
                for (int k = 0; k < 3; k++)
                {
                    float yaw = gen.PlayerStartYaw + (k == 0 ? 0f : (k == 1 ? 75f : -75f));
                    pw.Teleport(bs + Vector3.up * 0.05f, yaw); yield return 0.8f;
                    cam.transform.position = bs + Vector3.up * 1.6f; cam.transform.rotation = Quaternion.Euler(6f, yaw, 0f); cam.fieldOfView = 70f;
                    Shot(cam, "level" + lv + "_" + (char)('a' + k) + suf);
                }
            }
            cam.cullingMask = mask; cam.fieldOfView = fov0;
            tag = lv.ToString(); Debug.Log("[V711Shots] nivel " + tag + " listo, semilla " + gb.Generator.UsedSeed);
        }
    }
}
