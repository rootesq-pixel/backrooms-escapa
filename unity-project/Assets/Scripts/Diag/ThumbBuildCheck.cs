// v7.3.0: «-thumbs <carpeta>» en el ejecutable: una captura real (sin VHS) de cada nivel para las miniaturas del menú «Seleccionar nivel»
// (thumb_N.ppm), y luego la captura del propio menú con esas miniaturas (menu_niveles_*.ppm). Semilla fija 7110. No toca preferencias.
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

public class ThumbBuildCheck : MonoBehaviour
{
    string dir; readonly StringBuilder rep = new StringBuilder();
    public static void MaybeStart(GameObject host)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == "-thumbs")
            {
                ThumbBuildCheck c = host.AddComponent<ThumbBuildCheck>(); c.dir = a[i + 1];
                GameBootstrap gb = host.GetComponent<GameBootstrap>(); if (gb != null) { gb.useRandomSeed = false; gb.seed = 7110; }
            }
    }
    void Info(string m) { rep.AppendLine(m); Debug.Log("[ThumbBuildCheck] " + m); }

    const float SX = (16f - 0.2f) / 14f, SZ = (12f - 0.2f) / 10f, DECK = 0.28f;
    static Vector3 B(float x, float y, float z) { return new Vector3(x * SX, z + DECK, y * SZ); }

    IEnumerator Start()
    {
        Directory.CreateDirectory(dir);
        float t0 = Time.realtimeSinceStartup;
        while ((GameBootstrap.Instance == null || UIManager.Instance == null) && Time.realtimeSinceStartup - t0 < 120f) yield return null;
        GameBootstrap gb = GameBootstrap.Instance;
        ElevatorCabin.InstantForTests = true; ExitLock.AutoKeyForTests = true; GameManager.HideControlsHint = true;
        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            int lc = gb.LoadCount; gb.LoadLevel(lv);
            float w = 0f;
            while (w < 180f && !(gb.LoadCount > lc && gb.IsLoaded && GameManager.Instance != null && FirstPersonController.LocalInstance != null)) { yield return null; w += Time.unscaledDeltaTime; }
            if (ElevatorCabin.Current != null) ElevatorCabin.Current.Skip();
            w = 0f; while (ElevatorCabin.PlayerLocked && w < 20f) { yield return new WaitForSecondsRealtime(0.25f); w += 0.25f; }
            GameManager gm = GameManager.Instance; FirstPersonController pw = gm.player;
            foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
            Camera mc = Camera.main;
            VhsEffect vx = mc != null ? mc.GetComponent<VhsEffect>() : null; if (vx != null) vx.intensityOverride = 0f;
            Flashlight fl = pw.GetComponentInChildren<Flashlight>(); if (fl != null) fl.SetOn(lv == 1 || lv == 3 || lv == 4);
            mc.cullingMask &= ~(1 << 8);
            Vector3 p, t, stand;
            if (OceanWorld.Active != null) { stand = new Vector3(15f, -0.3f, 30.6f); p = stand + Vector3.up * 1.62f; t = new Vector3(13f, 1.2f, 70f); }
            else if (PoolRoomSetPiece.Root != null)
            {
                Transform root = PoolRoomSetPiece.Root.transform; Transform bk = root.Find("PoolRoomBaked"); if (bk != null) root = bk;
                p = root.TransformPoint(B(-6.3f, -4.4f, 1.65f)); t = root.TransformPoint(B(1.5f, 0.8f, -0.4f)); stand = root.TransformPoint(B(-6.2f, -4.3f, 0.02f));
            }
            else
            {   // la dirección más despejada desde donde aparece el jugador (vista de pasillo / campo)
                Vector3 e = pw.transform.position + Vector3.up * 1.62f; float best = -1f; Vector3 bd = pw.transform.forward;
                for (int k = 0; k < 16; k++)
                {
                    Vector3 d = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                    float L = Physics.Raycast(e, d, out RaycastHit h, 60f, ~((1 << 2) | (1 << 8)), QueryTriggerInteraction.Ignore) ? h.distance : 60f;
                    if (L > best) { best = L; bd = d; }
                }
                stand = pw.transform.position; p = e; t = e + bd * 10f + Vector3.down * 0.9f;
                Info("nivel " + lv + ": dirección más larga " + best.ToString("F1") + " m");
            }
            pw.Teleport(stand, Mathf.Atan2(t.x - p.x, t.z - p.z) * Mathf.Rad2Deg);
            float s0 = Time.realtimeSinceStartup;
            for (int k = 0; k < 40 || Time.realtimeSinceStartup - s0 < 2.5f; k++) { mc.transform.position = p; mc.transform.rotation = Quaternion.LookRotation(t - p); mc.fieldOfView = 62f; yield return null; }
            if (PoolPlanarReflection.Instance != null) PoolPlanarReflection.Instance.Refresh(mc);
            if (FieldGrass.Active != null) FieldGrass.Active.Submit();
            Texture2D tex = Grab(mc, 1920, 1080);
            WritePpm(Path.Combine(dir, "thumb_" + lv + ".ppm"), tex);
            LevelThumbs.Override[lv] = tex;
            Info("nivel " + lv + " (" + LevelConfig.Get(lv).name + "): media " + Mean(tex).ToString("F3"));
            if (vx != null) vx.intensityOverride = -1f;
        }
        // el menú de selección de nivel con estas miniaturas
        gb.GoToMainMenu();
        float ww = 0f; while (ww < 60f && !gb.InMenu) { yield return null; ww += Time.unscaledDeltaTime; }
        yield return new WaitForSecondsRealtime(2f);
        UIManager ui = UIManager.Instance;
        ui.RefreshLevelThumbs();
        ui.SetScreen(UIManager.Screen.Levels);
        Camera cam = Camera.main; VhsEffect v2 = cam != null ? cam.GetComponent<VhsEffect>() : null; if (v2 != null) v2.intensityOverride = 0f;
        foreach (int sel in new[] { 7, 0, 2 })
        {
            ui.SelectLevelForTests(sel);
            for (int k = 0; k < 30; k++) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            Texture2D m = ui.CaptureMenu(cam, 1920, 1080);
            WritePpm(Path.Combine(dir, "menu_niveles_sel" + sel + ".ppm"), m);
            Info("menú (nivel " + sel + " seleccionado): media " + Mean(m).ToString("F3") + " foco " + ui.LevelFocus);
            Destroy(m);
        }
        File.WriteAllText(Path.Combine(dir, "thumbs.txt"), rep.ToString() + "END\n");
        Application.Quit(0);
    }

    static Texture2D Grab(Camera c, int W, int H)
    {
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24); RenderTexture prev = c.targetTexture;
        c.targetTexture = rt; c.Render(); RenderTexture.active = rt;
        Texture2D t = new Texture2D(W, H, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply();
        RenderTexture.active = null; c.targetTexture = prev; RenderTexture.ReleaseTemporary(rt);
        return t;
    }
    static float Mean(Texture2D t) { Color32[] px = t.GetPixels32(); double s = 0; for (int i = 0; i < px.Length; i += 37) s += (px[i].r + px[i].g + px[i].b) / 765.0; return (float)(s / (px.Length / 37.0)); }
    static void WritePpm(string path, Texture2D t)
    {
        int w = t.width, h = t.height; Color32[] px = t.GetPixels32();
        byte[] head = Encoding.ASCII.GetBytes("P6\n" + w + " " + h + "\n255\n");
        byte[] b = new byte[head.Length + w * h * 3]; System.Buffer.BlockCopy(head, 0, b, 0, head.Length);
        int o = head.Length;
        for (int y = h - 1; y >= 0; y--) for (int x = 0; x < w; x++) { Color32 q = px[y * w + x]; b[o++] = q.r; b[o++] = q.g; b[o++] = q.b; }
        File.WriteAllBytes(path, b);
    }
}
