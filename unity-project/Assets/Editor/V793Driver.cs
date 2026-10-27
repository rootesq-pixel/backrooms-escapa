using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.9.2: capturas (xvfb con gráficos) del todoterreno en 3.ª persona con el conductor, de las rodadas y de la arena del
// Nivel 114 a varias distancias del ascensor, y del HUD en el coche a varias resoluciones. Salida en $V793_OUT.
[InitializeOnLoad]
public static class V793Driver
{
    private static IEnumerator routine; private static double waitUntil;
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V793_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v793/shots" : o; } }
    static V793Driver() { if (SessionState.GetString("smoke_phase", "") != "v793drv") return; EditorApplication.update += Tick; }
    public static void Export()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v793drv");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V793Driver] " + e); File.AppendAllText(Out + "/report.txt", "EXC " + e + "\n"); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); }
    static void Log(string s) { File.AppendAllText(Out + "/report.txt", s + "\n"); Debug.Log("[V793Driver] " + s); }

    private static Camera uiCam;
    private static void SetupUi()
    {
        if (uiCam == null)
        {
            GameObject ugo = new GameObject("PreviewUiCam"); uiCam = ugo.AddComponent<Camera>();
            uiCam.clearFlags = CameraClearFlags.Depth; uiCam.cullingMask = 1 << 31; uiCam.orthographic = true; uiCam.depth = 100; uiCam.enabled = false;
        }
        foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = uiCam; c.planeDistance = 1f;
            foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
        }
    }
    // escena (cámara del juego o una colocada a mano) + HUD (opcional)
    private static void Shot(string name, int w, int h, bool hud, System.Action<Camera> place = null)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        Camera game = Camera.main;
        Vector3 p0 = game.transform.position; Quaternion r0 = game.transform.rotation;
        if (place != null) place(game);
        RenderTexture o = game.targetTexture; game.targetTexture = rt; game.Render(); game.targetTexture = o;
        game.transform.position = p0; game.transform.rotation = r0;
        if (hud) { SetupUi(); Canvas.ForceUpdateCanvases(); uiCam.targetTexture = rt; uiCam.Render(); uiCam.targetTexture = null; }
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev; File.WriteAllBytes(Out + "/" + name + ".png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        RenderTexture.ReleaseTemporary(rt);
        Log("captura " + name + " " + w + "x" + h);
    }

    static string Bones(Transform model)
    {
        string o = "";
        foreach (string b in new[] { "Hips", "Torso", "Chest", "Neck", "Head", "UpperArm.R", "LowerArm.R", "UpperLeg.L", "LowerLeg.L", "Foot.L" })
        {
            Transform t = FindDeep(model, b); if (t == null) continue;
            o += " " + b + " esc " + t.localScale.ToString("F2") + " pos " + model.InverseTransformPoint(t.position).ToString("F2") + ";";
        }
        return o;
    }
    static Transform FindDeep(Transform t, string n) { if (t.name == n) return t; for (int i = 0; i < t.childCount; i++) { Transform r = FindDeep(t.GetChild(i), n); if (r != null) return r; } return null; }
    static void Around(string pre, Vector3 center, Vector3 fwd, Vector3 right, float dist, float h)
    {
        Vector3 c = center;
        Vector3 e1 = c + (fwd * 0.7f + right * 0.7f).normalized * dist + Vector3.up * h;
        Shot(pre + "_34", 1920, 1080, false, cm => { cm.transform.position = e1; cm.transform.rotation = Quaternion.LookRotation(c - e1); });
        Vector3 e2 = c + right * dist + Vector3.up * h;
        Shot(pre + "_lado", 1920, 1080, false, cm => { cm.transform.position = e2; cm.transform.rotation = Quaternion.LookRotation(c - e2); });
        Vector3 e3 = c + fwd * dist + Vector3.up * h;
        Shot(pre + "_frente", 1920, 1080, false, cm => { cm.transform.position = e3; cm.transform.rotation = Quaternion.LookRotation(c - e3); });
    }

    private static IEnumerator Run()
    {
        File.WriteAllText(Out + "/report.txt", "");
        ElevatorCabin.InstantForTests = true;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        gb.useRandomSeed = false; gb.seed = 12345;
        int ld = gb.LoadCount; int lv = 0; for (int i = 0; i < LevelConfig.Count; i++) if (LevelConfig.Get(i).layout == LayoutKind.Desert) lv = i;
        UIManager.Instance.StartGame(lv);
        float wt = 0f; while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 1.5f;
        DesertSky.ForceWeather = 0; DesertSky.ForceHour = 12.5f;
        if (Health.Instance != null) Health.DisableForTests = true;
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player; DesertCars dc = DesertCars.Active;
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        VhsEffect vx = Camera.main.GetComponent<VhsEffect>(); if (vx != null) vx.intensityOverride = 0f;
        DesertCar car = dc.Drivable;
        if (car == null) { Log("SIN COCHE"); yield break; }
        Transform ct = car.transform; float side = car.driverSide;

        // ---- referencia: el prefab del jugador tal cual, de pie, a 6 m del coche
        GameObject refp = Object.Instantiate(Resources.Load<GameObject>(PlayerBody.PrefabResource));
        foreach (Collider c in refp.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        Vector3 rp = ct.position + ct.right * (-side * 6f) + ct.forward * 2f; 
        if (Physics.Raycast(rp + Vector3.up * 20f, Vector3.down, out RaycastHit rh, 60f)) rp = rh.point;
        refp.transform.position = rp; refp.transform.rotation = Quaternion.LookRotation(ct.forward);
        PlayerBody pb = p.GetComponentInChildren<PlayerBody>();
        Transform pm = pb != null ? pb.transform.Find("Model") : null;
        if (pm != null) refp.transform.localScale = pm.localScale;
        Animator ra = refp.GetComponentInChildren<Animator>(); if (ra != null) { ra.applyRootMotion = false; ra.Play("Idle", 0, 0f); }
        yield return 0.6f;
        Log("referencia: escala " + refp.transform.localScale.ToString("F2") + Bones(refp.transform));
        foreach (SkinnedMeshRenderer r in refp.GetComponentsInChildren<SkinnedMeshRenderer>()) Log("   ref renderer " + r.name + " mats " + string.Join(",", System.Array.ConvertAll(r.sharedMaterials, x => x != null ? x.name : "-")));
        Around("ref", rp + Vector3.up * 0.95f, ct.forward, ct.right * (-side), 3.2f, 0.2f);
        refp.SetActive(false);

        // ---- al volante, cámara exterior
        p.Teleport(ct.TransformPoint(car.DoorLocal(0)) + Vector3.up * 0.1f, 0f); yield return 0.6f;
        DesertCars.DebugInteract = true; yield return 0.1f; yield return 2.0f;
        DesertCars.DebugDrive = true; DesertCars.DbgThrottle = 0f; DesertCars.DbgHandbrake = true; DesertCars.DbgSteer = 0f;
        DesertCars.CamView = 1; yield return 1.5f;
        Log("montado " + p.IsMounted + " ExternalView " + PlayerBody.ExternalView + " visible " + (pb != null && pb.Visible) + " cabeza " + (pb != null && pb.HeadShown));
        if (pm != null)
        {
            Log("conductor: escala " + pm.localScale.ToString("F2") + Bones(pm));
            foreach (SkinnedMeshRenderer r in pm.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Log("   renderer " + r.name + " activo " + r.enabled + "/" + r.gameObject.activeInHierarchy + " sombras " + r.shadowCastingMode + " capa " + r.gameObject.layer + " mats " + string.Join(",", System.Array.ConvertAll(r.sharedMaterials, x => x != null ? x.name : "-")));
            Transform hd = FindDeep(pm, "Head");
            if (hd != null) Log("cabeza a " + ct.InverseTransformPoint(hd.position).ToString("F2") + " (local del coche); techo " + car.ModelHeight.ToString("F2"));
            foreach (SkinnedMeshRenderer r in pm.GetComponentsInChildren<SkinnedMeshRenderer>()) Log("   " + r.name + " altura local " + ct.InverseTransformPoint(r.bounds.min).y.ToString("F2") + "–" + ct.InverseTransformPoint(r.bounds.max).y.ToString("F2"));
            Transform sh = FindDeep(pm, "UpperArm.R"), hp = FindDeep(pm, "Hips");
            Log("hombro " + (sh != null ? ct.InverseTransformPoint(sh.position).y.ToString("F2") : "-") + ", cadera " + (hp != null ? ct.InverseTransformPoint(hp.position).ToString("F2") : "-") + ", asiento " + car.SeatLocal(0).ToString("F2") + ", inclinación al volante " + pb.WheelLean.ToString("F2") + ", pitch " + p.Pitch.ToString("F0"));
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>()) if (r.name.StartsWith("DCar_Glass") || r.name.StartsWith("DCar_Body")) Log("   coche " + r.name + " y " + ct.InverseTransformPoint(r.bounds.min).y.ToString("F2") + "–" + ct.InverseTransformPoint(r.bounds.max).y.ToString("F2") + " mats " + string.Join(",", System.Array.ConvertAll(r.sharedMaterials, x => x != null ? x.name + (x.HasProperty("_Color") ? x.color.ToString("F2") : "") : "-")));
        }
        Vector3 seat = ct.TransformPoint(car.SeatLocal(0)) + ct.up * 1.45f;
        Around("conductor", seat, ct.forward, ct.right * side, 2.6f, 0.15f);
        { Vector3 eb = seat - ct.forward * 3.2f + ct.up * 0.35f + ct.right * side * 0.2f; Shot("conductor_detras", 1920, 1080, false, cm => { cm.transform.position = eb; cm.transform.rotation = Quaternion.LookRotation(seat - eb); }); }
        List<Renderer> carR = new List<Renderer>(); foreach (Renderer r in car.GetComponentsInChildren<Renderer>()) if (r.enabled && (pm == null || !r.transform.IsChildOf(pm))) { carR.Add(r); }
        foreach (Renderer r in carR) r.enabled = false;
        Around("conductor_sincoche", seat - ct.up * 0.5f, ct.forward, ct.right * side, 2.8f, 0.2f);
        foreach (Renderer r in carR) r.enabled = true;
        Shot("conductor_camara_exterior", 1920, 1080, false);
        DesertCars.CamView = 0; DesertCars.DebugDrive = false;
        DesertSky.ForceWeather = -1; DesertSky.ForceHour = -1f; Health.DisableForTests = false;
        Log("FIN");
    }
}
