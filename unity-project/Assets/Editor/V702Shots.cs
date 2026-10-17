using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.0.2: capturas en juego (con gráficos): encuentro con el imitador desde la vista del jugador y, en el campo,
// el interior de una casa con objetos sobre los muebles.  V702_LEVEL=5 Unity -batchmode -executeMethod V702Shots.Run -force-glcore
[InitializeOnLoad]
public static class V702Shots
{
    private static IEnumerator routine; private static double waitUntil;
    const string Dir = "/workspace/v702shots";
    static V702Shots() { if (SessionState.GetString("smoke_phase", "") != "v702shots") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v702shots");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Main();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V702Shots] " + e); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); }

    private static void Shot(Camera cam, string name, int w = 1920, int h = 1080)
    {
        RenderTexture rt = new RenderTexture(w, h, 24); RenderTexture prev = cam.targetTexture;
        cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        Texture2D t = new Texture2D(w, h, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        Directory.CreateDirectory(Dir); File.WriteAllBytes(Dir + "/" + name + ".png", t.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = prev; Object.DestroyImmediate(rt); Object.DestroyImmediate(t);
        Debug.Log("[V702Shots] " + name);
    }

    private static IEnumerator Main()
    {
        int lv = int.Parse(System.Environment.GetEnvironmentVariable("V702_LEVEL") ?? "5");
        GameSettings.Quality = 2;
        MimicDisguise.FirstStageOverride = 3f;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(lv);
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu)) yield return 0.2f;
        if (ElevatorCabin.Current != null) ElevatorCabin.Current.Skip();
        float w = 0f; while (ElevatorCabin.PlayerLocked && w < 20f) { yield return 0.25f; w += 0.25f; }
        GameManager gm = GameManager.Instance; FirstPersonController pw = gm.player;
        Camera cam = pw.GetComponentInChildren<Camera>();
        GameManager.HideControlsHint = true;
        MimicDisguise md = null;
        foreach (MonsterAI m in gm.monsters) { if (m == null) continue; if (m.mimic != null) md = m.mimic; else m.Stop(); }
        Flashlight fl = pw.GetComponentInChildren<Flashlight>(); if (fl != null) { fl.SetBattery(100f); fl.SetOn(true); }
        yield return 1.0f;
        // ---- objetos en una casa (campo) ----
        if (lv == 5 && FieldWorld.Active != null)
        {
            if (md != null) md.ai.Stop();   // que el imitador no intervenga en estas capturas
            BackroomsGenerator gen = gb.Generator;
            System.Collections.Generic.List<Transform> items = new System.Collections.Generic.List<Transform>();
            foreach (ItemPickup ip in Object.FindObjectsOfType<ItemPickup>()) { items.Add(ip.transform); ip.enabled = false; }
            foreach (BatteryPickup bp in Object.FindObjectsOfType<BatteryPickup>()) { items.Add(bp.transform); bp.enabled = false; }
            int shots = 0;
            foreach (Transform it in items)
            {
                if (shots >= 3) break;
                // casa más cercana
                Transform hg = null; float bd = 1e9f;
                foreach (Transform t in gen.Root.GetComponentsInChildren<Transform>()) if (t.name.StartsWith("House_")) { float d = (t.position - it.position).sqrMagnitude; if (d < bd) { bd = d; hg = t; } }
                if (hg == null) continue;
                Vector3 best = Vector3.zero; bool ok = false; int nearCount = 0;
                foreach (Transform o in items) if (Vector3.Distance(o.position, it.position) < 2.2f) nearCount++;
                if (shots == 0 && nearCount < 2 && it != items[items.Count - 1]) { }
                for (int k = 0; k < 48 && !ok; k++)
                {
                    float r = k < 12 ? 1.9f : k < 24 ? 1.55f : k < 36 ? 1.25f : 0.95f; float ang = k * 30f;
                    Vector3 c = it.position + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * r;
                    if (!Physics.Raycast(c + Vector3.up * 0.3f, Vector3.down, out RaycastHit fh, 2.5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 feet = fh.point;
                    Vector3 lp = hg.InverseTransformPoint(feet);
                    if (Mathf.Abs(lp.y) > 0.6f || lp.y < 0f) continue;
                    if (Physics.CheckCapsule(feet + Vector3.up * 0.45f, feet + Vector3.up * 1.6f, 0.25f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 eye = feet + Vector3.up * 1.6f;
                    if (Physics.Linecast(eye, it.position + Vector3.up * 0.08f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    best = feet; ok = true;
                }
                if (!ok) { Debug.Log("[V702Shots] sin sitio para " + it.name); continue; }
                Vector3 dir = it.position - best; dir.y = 0f;
                pw.Teleport(best + Vector3.up * 0.05f, Quaternion.LookRotation(dir).eulerAngles.y);
                yield return 1.2f;
                cam.transform.LookAt(it.position + Vector3.up * 0.05f);
                int mask = cam.cullingMask; cam.cullingMask &= ~(1 << 8);   // sin el brazo del jugador
                Shot(cam, "field_house_items" + (shots == 0 ? "" : (shots + 1).ToString()));
                cam.cullingMask = mask;
                Debug.Log("[V702Shots] item " + it.name + " en " + hg.name);
                shots++;
            }
            foreach (ItemPickup ip in Object.FindObjectsOfType<ItemPickup>()) ip.enabled = true;
            foreach (BatteryPickup bp in Object.FindObjectsOfType<BatteryPickup>()) bp.enabled = true;
            // en campo abierto, fuera de la cabina del ascensor
            Vector3 fw = Quaternion.Euler(0f, gen.PlayerStartYaw, 0f) * Vector3.forward;
            Vector3 op = gen.PlayerStart + fw * 30f;
            if (Physics.Raycast(op + Vector3.up * 30f, Vector3.down, out RaycastHit oh, 80f, ~0, QueryTriggerInteraction.Ignore)) op = oh.point;
            pw.Teleport(op + Vector3.up * 0.05f, gen.PlayerStartYaw);
            yield return 1.0f;
        }
        // ---- encuentro con el imitador ----
        if (md != null)
        {
            md.ai.Resume(); md.ForceStageSoon(1.5f);
            pw.debugInput = true; pw.debugMove = new Vector2(0f, 0.5f);
            float t = 0f; bool seen = false; Vector3 lastP = pw.transform.position; float stuck = 0f; float lt = Time.realtimeSinceStartup;
            while (t < 60f)
            {
                yield return null; float now = Time.realtimeSinceStartup; float dt = now - lt; lt = now; t += dt;
                if (md.OnScreen(pw.transform)) { seen = true; break; }
                if ((pw.transform.position - lastP).sqrMagnitude < 0.0004f) stuck += dt; else stuck = 0f;
                lastP = pw.transform.position;
                if (stuck > 0.6f) { pw.Teleport(pw.transform.position, pw.transform.eulerAngles.y + 90f); stuck = 0f; }
            }
            pw.debugMove = Vector2.zero; pw.debugInput = false;
            yield return 0.3f;
            Shot(cam, "mimic_encounter_lv" + lv + "_first");
            yield return 1.2f;
            Debug.Log("[V702Shots] imitador visible=" + seen + " t=" + t.ToString("F1") + " dist=" + Vector3.Distance(md.transform.position, pw.transform.position).ToString("F1") + " fase " + md.CurrentPhase);
            Shot(cam, "mimic_encounter_lv" + lv);
            {
                int nr = 0, ne = 0; Bounds bb = new Bounds(md.puppet.transform.position, Vector3.zero);
                foreach (Renderer rr in md.puppet.GetComponentsInChildren<Renderer>(true)) { nr++; if (rr.enabled && rr.gameObject.activeInHierarchy) { ne++; bb.Encapsulate(rr.bounds); } }
                Debug.Log("[V702Shots] diag puppet activo=" + md.puppet.gameObject.activeInHierarchy + " pos=" + md.puppet.transform.position + " ai=" + md.transform.position + " rend " + ne + "/" + nr + " bounds " + bb + " vp " + cam.WorldToViewportPoint(md.transform.position + Vector3.up * 1.3f) + " mask " + cam.cullingMask + " layer " + md.puppet.gameObject.layer + " anim " + md.puppet.AnimDiag);
                float fov = cam.fieldOfView; Quaternion rot = cam.transform.rotation;
                cam.transform.LookAt(md.puppet.transform.position + Vector3.up * 1.1f); cam.fieldOfView = 18f;
                Shot(cam, "mimic_encounter_lv" + lv + "_zoom");
                cam.fieldOfView = fov; cam.transform.rotation = rot;
            }
            md.ai.Stop();
        }
    }
}
