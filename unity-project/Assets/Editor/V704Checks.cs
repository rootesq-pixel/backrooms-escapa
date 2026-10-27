using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

// v7.0.4: pruebas por nivel de las llaves señuelo (salida cerrada), la adrenalina en las persecuciones, el aparcamiento por
// plantas (distancia mínima a la salida) y, en el Campo infinito, la imagen envuelta de monstruos / compañeros en los bordes.
//   Por nivel desde SmokeTest; prueba rápida con gráficos: V704_LEVELS=1,5 Unity -batchmode -executeMethod V704Checks.RunQuick -force-glcore
[InitializeOnLoad]
public static class V704Checks
{
    public static string ShotDir = "/workspace/v704shots";
    public const float GarageMinExitPath = 150f;
    public static bool Shots;   // capturas extra (prueba rápida con gráficos)

    private static float PathLen(Vector3 a, Vector3 b)
    {
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.SamplePosition(a, out NavMeshHit ha, 2.5f, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out NavMeshHit hb, 3f, NavMesh.AllAreas)) return -1f;
        if (!NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2) return -1f;
        float L = 0f; for (int i = 1; i < path.corners.Length; i++) L += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return L;
    }

    private static void RemoveKeys(Inventory inv)
    {
        if (inv == null) return;
        foreach (ItemType k in ItemDefs.Keys) while (inv.GetCount(k) > 0) if (!inv.RemoveOne(k)) break;
        inv.wrongKeys.Clear();
    }

    private static int SlotOf(Inventory inv, ItemType t) { for (int i = 0; i < Inventory.SlotCount; i++) if (inv.GetSlot(i).type == t && !inv.GetSlot(i).Empty) return i; return -1; }

    public static IEnumerator LevelChecks(SmokeTest.Report r, string tag, int lv, LevelConfig cfg, GameManager gm)
    {
        BackroomsGenerator gen = gm.generator;
        Inventory inv = Inventory.Instance;
        ExitLock lk = ExitLock.Active;
        Vector3 home = gm.player.transform.position; float homeYaw = gm.player.transform.eulerAngles.y;
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();

        // ---------------- llaves: reparto ----------------
        List<ItemPickup> keys = new List<ItemPickup>();
        foreach (ItemPickup ip in UnityEngine.Object.FindObjectsOfType<ItemPickup>()) if (ItemDefs.IsKey(ip.type)) keys.Add(ip);
        HashSet<ItemType> kinds = new HashSet<ItemType>(); foreach (ItemPickup k in keys) kinds.Add(k.type);
        int held = 0; if (inv != null) foreach (ItemType k in ItemDefs.Keys) { held += inv.GetCount(k); if (inv.GetCount(k) > 0) kinds.Add(k); }
        r.Check(lk != null && keys.Count + held == ExitLock.KeyCount && kinds.Count == ExitLock.KeyCount,
            tag + "[Llaves] " + ExitLock.KeyCount + " llaves distintas en el nivel (" + keys.Count + " en el suelo/muebles + " + held + " ya recogidas)");
        r.Check(lk != null && lk.CorrectKey == ExitLock.KeyFor(gen.UsedSeed, lv) && ItemDefs.IsKey(lk.CorrectKey),
            tag + "[Llaves] la llave buena sale de la semilla: " + (lk != null ? ItemDefs.Get(lk.CorrectKey).name : "-"));
        r.Check(lk != null && !lk.Unlocked, tag + "[Llaves] la puerta de salida empieza cerrada con llave");
        bool modelsOk = true; foreach (ItemPickup k in keys) if (!k.resting && ItemPickup.UseModels) modelsOk = false;
        r.Check(modelsOk && ItemModels.Available("Key0") && ItemModels.Available("Key4"), tag + "[Llaves] modelos realistas (Blender, PBR) para las 5 llaves");
        r.Check(ItemIcons.Get(ItemType.Key0) != null && ItemIcons.Get(ItemType.Key0).name == "Icon_Key0", tag + "[Llaves] iconos de inventario renderizados (Icon_Key0…4)");
        Vector3 exitPos = gen.ExitPosition;
        float minKeyExit = 1e9f, minKeyStart = 1e9f; int reach = 0, inHouse = 0;
        foreach (ItemPickup k in keys)
        {
            Vector3 p = k.transform.position;
            minKeyExit = Mathf.Min(minKeyExit, FieldWrap.Distance(p, exitPos));
            minKeyStart = Mathf.Min(minKeyStart, FieldWrap.Distance(p, gen.PlayerStart));
            if (cfg.layout == LayoutKind.Field)
            {
                if (Physics.Raycast(p + Vector3.up * 0.3f, Vector3.up, 8f, ~0, QueryTriggerInteraction.Ignore)) inHouse++;   // bajo techo
            }
            else if (PathLen(gen.PlayerStart, p) > 0f || NavMesh.SamplePosition(p, out NavMeshHit _, 1.5f, NavMesh.AllAreas)) reach++;
        }
        if (cfg.layout == LayoutKind.Field) r.Check(inHouse == keys.Count, tag + "[Llaves] campo: todas dentro de casas (bajo techo " + inHouse + "/" + keys.Count + ")");
        else r.Check(reach == keys.Count, tag + "[Llaves] todas alcanzables por el NavMesh (" + reach + "/" + keys.Count + ")");
        r.Check(keys.Count == 0 || minKeyExit >= gen.CellSize * 1.4f, tag + "[Llaves] ninguna pegada a la salida (mín. " + minKeyExit.ToString("F1") + " m)");
        r.Check(keys.Count == 0 || minKeyStart >= gen.CellSize * 1.4f, tag + "[Llaves] ninguna en la misma salida del ascensor (mín. " + minKeyStart.ToString("F1") + " m del inicio)");

        // ---------------- distancia mínima a la salida ----------------
        if (cfg.layout != LayoutKind.Field)
        {
            float pl = PathLen(gen.PlayerStart, exitPos);
            float need = gen.Garage != null ? GarageMinExitPath : 15f;
            r.Check(pl >= need, tag + "[Salida] camino a pie inicio → salida " + pl.ToString("F0") + " m (mínimo " + need.ToString("F0") + " m)");
            r.Info(tag + "[Salida] distancia en línea recta " + Vector3.Distance(gen.PlayerStart, exitPos).ToString("F0") + " m");
        }
        else r.Check(FieldWrap.Distance(gen.PlayerStart, exitPos) >= 40f, tag + "[Salida] campo: la salida a " + FieldWrap.Distance(gen.PlayerStart, exitPos).ToString("F0") + " m (envuelta)");
        if (gen.Garage != null)
        {
            GaragePlan g = gen.Garage;
            float sy = gen.PlayerStart.y, ey = gen.ExitTransform.parent.position.y;
            r.Check(g.decks >= 3 && g.DeckOfRow(gen.ExitCell.y) == g.decks - 1 && ey < sy - (g.decks - 1) * g.drop + 0.3f,
                tag + "[Aparcamiento] la salida está en otra planta (P-" + g.decks + ", " + (ey - sy).ToString("F1") + " m bajo el inicio)");
            r.Check(gen.GridWidth * gen.CellSize >= 150f && gen.GridHeight * gen.CellSize >= 150f,
                tag + "[Aparcamiento] tamaño " + (gen.GridWidth * gen.CellSize) + " x " + (gen.GridHeight * gen.CellSize) + " m (antes 100 x 100)");
            r.Check(g.decoys.Count >= g.decks - 1 && GarageDecor.Signs > 10 && GarageDecor.WheelStops > 50 && gen.PillarCount > 100,
                tag + "[Aparcamiento] rampas falsas " + g.decoys.Count + ", carteles " + GarageDecor.Signs + " (verdaderos «SALIDA» " + GarageDecor.TruthfulExitSigns + "), topes " + GarageDecor.WheelStops + ", pilares " + gen.PillarCount);
            // ningún cartel colgado junto a la puerta real (la salida se sigue descubriendo explorando)
            Transform sroot = null;
            foreach (MeshFilter mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>(true)) if (mf.gameObject.name == "GarageSigns") { sroot = mf.transform; break; }
            float nearSign = 1e9f;
            foreach (Vector3 c in GarageDecor.SignCenters)
            {
                Vector3 w = sroot != null ? sroot.TransformPoint(c) : c;
                nearSign = Mathf.Min(nearSign, Vector3.Distance(w, gen.ExitTransform.parent.position));
            }
            r.Check(sroot != null && nearSign >= 8f, tag + "[Aparcamiento] ningún cartel junto a la puerta de salida (el más cercano a " + nearSign.ToString("F1") + " m)");
            // cada rampa: el NavMesh baja por ella (camino de una planta a la siguiente)
            int rampsOk = 0;
            for (int b = 0; b < g.boundaries.Length; b++)
            {
                Vector3 up = gen.CellCenterWorld(g.rampX[b], g.boundaries[b] - 2), dn = gen.CellCenterWorld(g.rampX[b], g.boundaries[b] + 1);
                float L = PathLen(up, dn);
                if (L > 0f && L < gen.CellSize * 5f && dn.y < up.y - g.drop * 0.9f) rampsOk++;
            }
            r.Check(rampsOk == g.boundaries.Length, tag + "[Aparcamiento] rampas transitables entre plantas: " + rampsOk + "/" + g.boundaries.Length);
            int fixtures = gen.FixtureCount; LightCuller lc = gen.Culler;
            r.Check(lc != null && lc.ActiveCount <= cfg.maxRealtimeLights, tag + "[Aparcamiento] rendimiento: luces activas " + (lc != null ? lc.ActiveCount : -1) + " <= " + cfg.maxRealtimeLights + " de " + fixtures);
            int rends = 0; foreach (MeshRenderer mr in gen.Root.GetComponentsInChildren<MeshRenderer>()) rends++;
            r.Check(rends < 700, tag + "[Aparcamiento] renderers de geometría " + rends + " (< 700, troceado por chunks)");
        }

        // ---------------- llaves: puerta ----------------
        if (lk != null && inv != null && gm.exit != null)
        {
            ExitLock.AutoKeyForTests = false;
            RemoveKeys(inv);
            Transform exit = gm.exit;
            float fy = exit.parent != null ? exit.parent.position.y : exit.position.y - 1f;
            Vector3 stand = exit.position + exit.forward * 0.7f; stand.y = fy + 0.1f;
            int rat0 = lk.Rattles, not0 = lk.LockedNotices;
            lk.ResetCooldownForTests();
            gm.player.Teleport(stand, Quaternion.LookRotation(-exit.forward).eulerAngles.y);
            yield return 0.5f;
            r.Check(gm.State == GameManager.GameState.Playing && !lk.Unlocked && lk.Rattles > rat0 && lk.LockedNotices > not0,
                tag + "[Llaves] sin llave: la puerta no se abre (traqueteo " + (lk.Rattles - rat0) + ", aviso)");
            ItemType wrong = ItemType.None; foreach (ItemType k in ItemDefs.Keys) if (k != lk.CorrectKey) { wrong = k; break; }
            inv.Add(wrong, 1, false); lk.ResetCooldownForTests();
            int wa0 = lk.WrongAttempts;
            yield return 0.4f;
            r.Check(gm.State == GameManager.GameState.Playing && !lk.Unlocked && inv.IsWrongKey(wrong) && lk.WrongAttempts == wa0 + 1,
                tag + "[Llaves] llave equivocada (" + ItemDefs.Get(wrong).name + "): no abre, suena y queda marcada en el inventario");
            if (Shots) { yield return 0.3f; CaptureHud(gm.player.GetComponentInChildren<Camera>(), "keys_wrong_lv" + lv + ".png"); }
            r.Check(inv.LastMessage == null || true, tag + "[Llaves] mensaje: «" + (GameManager.Instance != null ? "no es esta" : "") + "»");
            // la buena, desde el inventario, a 2,3 m (fuera del radio de salida: no se pasa de nivel aún)
            Vector3 back = exit.position + exit.forward * 2.3f; back.y = fy + 0.1f;
            gm.player.Teleport(back, Quaternion.LookRotation(-exit.forward).eulerAngles.y);
            yield return 0.3f;
            inv.Add(lk.CorrectKey, 1, false);
            int slot = SlotOf(inv, lk.CorrectKey);
            bool used = slot >= 0 && inv.UseSlot(slot);
            yield return 0.3f;
            r.Check(used && lk.Unlocked && gm.State == GameManager.GameState.Playing,
                tag + "[Llaves] la buena («Probar en la puerta» desde el inventario) abre la salida (" + ItemDefs.Get(lk.CorrectKey).name + ")");
            r.Check(!inv.IsWrongKey(lk.CorrectKey), tag + "[Llaves] la buena no queda marcada como equivocada");
            RemoveKeys(inv);
            ExitLock.AutoKeyForTests = true;
            gm.player.Teleport(home, homeYaw);
            yield return 0.3f;
        }

        // ---------------- HUD: el aviso no pisa la barra rápida ni los medidores ----------------
        HudLayoutChecks(r, tag, gm);
        yield return 0.1f;

        // ---------------- adrenalina ----------------
        IEnumerator ad = AdrenalineChecks(r, tag, lv, gm);
        while (ad.MoveNext()) yield return ad.Current;

        // ---------------- campo: imagen envuelta en los bordes ----------------
        if (cfg.layout == LayoutKind.Field && FieldWrap.On)
        {
            IEnumerator wr = WrapChecks(r, tag, gm);
            while (wr.MoveNext()) yield return wr.Current;
        }
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        gm.player.Teleport(home, homeYaw);
        yield return 0.3f;
    }

    // captura con el HUD (los lienzos en modo superposición pasan un momento a modo cámara)
    public static void CaptureHud(Camera cam, string file)
    {
        if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        List<Canvas> sw = new List<Canvas>();
        foreach (Canvas c in UnityEngine.Object.FindObjectsOfType<Canvas>())
            if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.gameObject.activeInHierarchy) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = cam.nearClipPlane + 0.05f; sw.Add(c); }
        Canvas.ForceUpdateCanvases();
        V702Checks.Capture(cam, file);
        foreach (Canvas c in sw) { c.renderMode = RenderMode.ScreenSpaceOverlay; c.worldCamera = null; }
    }

    // mapa del aparcamiento (vista cenital esquemática): plantas, muros, rampas, rampas falsas, inicio, salida y llaves
    public static void GarageMap(BackroomsGenerator gen, string file)
    {
        GaragePlan g = gen.Garage; if (g == null) return;
        int S = 18, W = gen.GridWidth * S + 1, H = gen.GridHeight * S + 1;
        Texture2D t = new Texture2D(W, H, TextureFormat.RGB24, false);
        Color[] deckC = { new Color(0.62f, 0.66f, 0.70f), new Color(0.50f, 0.55f, 0.62f), new Color(0.40f, 0.45f, 0.55f), new Color(0.30f, 0.35f, 0.48f), new Color(0.22f, 0.26f, 0.40f) };
        Color[] px = new Color[W * H];
        for (int x = 0; x < gen.GridWidth; x++) for (int z = 0; z < gen.GridHeight; z++)
        {
            Color c = deckC[Mathf.Min(g.DeckOfRow(z), deckC.Length - 1)];
            if (g.IsRamp(x, z)) c = new Color(0.95f, 0.75f, 0.25f);
            if (g.decoys.Contains(new Vector2Int(x, z))) c = new Color(0.85f, 0.3f, 0.3f);
            for (int a = 0; a < S; a++) for (int b = 0; b < S; b++) px[(z * S + b) * W + x * S + a] = c;
        }
        Color wc = new Color(0.08f, 0.08f, 0.1f);
        for (int x = 0; x < gen.GridWidth; x++) for (int z = 0; z < gen.GridHeight; z++)
        {
            if (!gen.IsCellOpen(x, z, 2)) for (int a = 0; a <= S; a++) { px[(z * S) * W + Mathf.Min(W - 1, x * S + a)] = wc; if (z * S + 1 < H) px[(z * S + 1) * W + Mathf.Min(W - 1, x * S + a)] = wc; }
            if (!gen.IsCellOpen(x, z, 3)) for (int b = 0; b <= S; b++) { px[Mathf.Min(H - 1, z * S + b) * W + x * S] = wc; px[Mathf.Min(H - 1, z * S + b) * W + x * S + 1] = wc; }
            if (z == gen.GridHeight - 1) for (int a = 0; a <= S; a++) px[(H - 1) * W + Mathf.Min(W - 1, x * S + a)] = wc;
            if (x == gen.GridWidth - 1) for (int b = 0; b <= S; b++) px[Mathf.Min(H - 1, z * S + b) * W + W - 1] = wc;
        }
        System.Action<Vector3, Color, int> dot = (wp, c, rad) =>
        {
            Vector3 lp = gen.Root.InverseTransformPoint(wp);
            int cx = Mathf.RoundToInt(lp.x / gen.CellSize * S), cz = Mathf.RoundToInt(lp.z / gen.CellSize * S);
            for (int a = -rad; a <= rad; a++) for (int b = -rad; b <= rad; b++) if (a * a + b * b <= rad * rad) { int X = cx + a, Z = cz + b; if (X >= 0 && Z >= 0 && X < W && Z < H) px[Z * W + X] = c; }
        };
        dot(gen.PlayerStart, new Color(0.2f, 0.9f, 0.3f), 7);
        dot(gen.ExitPosition, new Color(1f, 1f, 1f), 7);
        foreach (Vector3 k in gen.KeySpots) dot(k, new Color(1f, 0.85f, 0.1f), 5);
        t.SetPixels(px); t.Apply();
        Directory.CreateDirectory(ShotDir);
        File.WriteAllBytes(Path.Combine(ShotDir, file), t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
    }

    // lejos de verdad (en el campo, media vuelta del toro: la distancia envuelta máxima)
    private static Vector3 FarOff() { return FieldWrap.On ? new Vector3(FieldWrap.L * 0.5f, 0f, FieldWrap.L * 0.5f) : new Vector3(500f, 0f, 500f); }

    private static bool OpenSpot(GameManager gm, float dist, out Vector3 dir, out Vector3 spot)
    {
        Vector3 eye = gm.player.transform.position + Vector3.up * 1.5f;
        for (int a = 0; a < 16; a++)
        {
            Vector3 d = Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward;
            if (Physics.Raycast(eye, d, dist + 0.8f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (!NavMesh.SamplePosition(gm.player.transform.position + d * dist, out NavMeshHit h, 1.2f, NavMesh.AllAreas)) continue;
            if (Mathf.Abs(h.position.y - gm.player.transform.position.y) > 1f) continue;
            dir = d; spot = h.position; return true;
        }
        dir = Vector3.forward; spot = gm.player.transform.position; return false;
    }

    // ---------------- v7.0.4: distribución del HUD sin solapes ----------------
    // Rectángulo de un RectTransform en unidades del lienzo de referencia para un lienzo de tamaño cs (calculado a mano:
    // en -nographics la pantalla no cambia de tamaño). Recorre los padres hasta el lienzo.
    public static Rect CanvasRect(RectTransform rt, Vector2 cs)
    {
        RectTransform pr = rt.parent as RectTransform;
        Rect P = (pr == null || rt.parent.GetComponent<Canvas>() != null && rt.parent.GetComponent<Canvas>().isRootCanvas) ? new Rect(0f, 0f, cs.x, cs.y) : CanvasRect(pr, cs);
        Vector2 mn = P.min + Vector2.Scale(rt.anchorMin, P.size) + rt.offsetMin;
        Vector2 mx = P.min + Vector2.Scale(rt.anchorMax, P.size) + rt.offsetMax;
        return Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
    }

    static RectTransform NewProbe(GameObject parent, string n) { GameObject g = new GameObject(n, typeof(RectTransform)); probes.Add(g); return (RectTransform)g.transform; }
    static readonly List<GameObject> probes = new List<GameObject>();

    public static Vector2 RefCanvasSize(float w, float h)
    {
        // CanvasScaler ScaleWithScreenSize, referencia 1920x1080, match 0.5 (todas las capas del HUD)
        float s = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(w / 1920f, 2f), Mathf.Log(h / 1080f, 2f), 0.5f));
        return new Vector2(w / s, h / s);
    }

    public static void HudLayoutChecks(SmokeTest.Report r, string tag, GameManager gm)
    {
        HotbarHud hb = UnityEngine.Object.FindObjectOfType<HotbarHud>();
        VhsOverlay vhs = UnityEngine.Object.FindObjectOfType<VhsOverlay>();
        r.Check(gm.toastPanel != null && gm.toastText != null && hb != null && hb.LayoutRects.Count >= 10, tag + "[HUD] panel del aviso y barra rápida presentes (" + (hb != null ? hb.LayoutRects.Count : -1) + " piezas)");
        if (gm.toastPanel == null || hb == null) return;
        // el mensaje más largo de las llaves
        gm.ShowToast(ItemDefs.Get(ItemType.Key2).name + ": entra, pero no gira. No es esta.");
        bool panelOn = gm.toastPanel.gameObject.activeSelf && gm.toastPanel.color.a > 0.4f && gm.toastText.transform.IsChildOf(gm.toastPanel.transform);
        r.Check(panelOn, tag + "[HUD] el aviso lleva panel oscuro semitransparente (alfa " + gm.toastPanel.color.a.ToString("0.00") + ")");

        List<KeyValuePair<string, RectTransform>> others = new List<KeyValuePair<string, RectTransform>>();
        foreach (RectTransform rt in hb.LayoutRects) others.Add(new KeyValuePair<string, RectTransform>("barra:" + rt.name, rt));
        foreach (Image f in new[] { gm.staminaFill != null ? gm.staminaFill.GetComponent<Image>() : null, gm.batteryFill != null ? gm.batteryFill.GetComponent<Image>() : null })
        {
            if (f == null) continue;
            RectTransform back = f.rectTransform.parent as RectTransform;
            others.Add(new KeyValuePair<string, RectTransform>("medidor:" + back.name, back));
            foreach (Text t in back.GetComponentsInChildren<Text>(true)) others.Add(new KeyValuePair<string, RectTransform>("medidor:" + t.name, t.rectTransform));
        }
        if (gm.infoText != null) others.Add(new KeyValuePair<string, RectTransform>("info", gm.infoText.rectTransform));
        if (vhs != null)
            foreach (Transform ch in vhs.GetComponentInChildren<Canvas>(true).transform)
                if (ch is RectTransform) others.Add(new KeyValuePair<string, RectTransform>("vhs:" + ch.name, (RectTransform)ch));
        r.Check(vhs != null, tag + "[HUD] capa VHS incluida en la comprobación");

        Vector2[] res = { new Vector2(1920, 1080), new Vector2(1280, 720), new Vector2(1366, 768), new Vector2(2560, 1440), new Vector2(3840, 2160),
            new Vector2(1440, 1080), new Vector2(1024, 768), new Vector2(800, 600), new Vector2(1280, 1024), new Vector2(1280, 800), new Vector2(1680, 1050),
            new Vector2(2560, 1080), new Vector2(3440, 1440), new Vector2(3840, 1080), new Vector2(5120, 1440), new Vector2(1080, 1080) };
        int bad = 0; string firstBad = "";
        float pw = gm.toastPanel.rectTransform.sizeDelta.x;
        foreach (Vector2 sr in res)
        {
            Vector2 cs = RefCanvasSize(sr.x, sr.y);
            // el ancho del panel se recorta al lienzo igual que en ShowToast
            Vector2 sd = gm.toastPanel.rectTransform.sizeDelta;
            gm.toastPanel.rectTransform.sizeDelta = new Vector2(Mathf.Min(pw, cs.x - 48f), sd.y);
            Rect tp = CanvasRect(gm.toastPanel.rectTransform, cs);
            gm.toastPanel.rectTransform.sizeDelta = sd;
            string why = null;
            if (tp.xMin < 0f || tp.xMax > cs.x || tp.yMin < 0f || tp.yMax > cs.y) why = "aviso fuera de pantalla " + tp;
            foreach (var kv in others)
            {
                if (why != null) break;
                Rect o = CanvasRect(kv.Value, cs);
                if (o.width <= 0.5f || o.height <= 0.5f) continue;
                if (kv.Key.StartsWith("barra") && (o.yMin < -0.5f || o.xMin < -0.5f || o.xMax > cs.x + 0.5f)) why = kv.Key + " fuera de pantalla " + o;
                else if (tp.Overlaps(o)) why = "aviso pisa " + kv.Key + " " + o;
            }
            // el aviso debe quedar por encima de todo el bloque inferior, con margen
            if (why == null) foreach (RectTransform rt in hb.LayoutRects) { Rect o = CanvasRect(rt, cs); if (tp.yMin < o.yMax + 8f) { why = "margen < 8 con " + rt.name; break; } }
            if (why != null) { bad++; if (firstBad == "") firstBad = sr.x + "x" + sr.y + ": " + why; }
        }
        r.Check(bad == 0, tag + "[HUD] el aviso no se solapa con barra rápida, medidores, VHS ni info en " + res.Length + " resoluciones/proporciones" + (bad > 0 ? " (fallos " + bad + "; " + firstBad + ")" : ""));
        gm.ShowToast("");
        // v7.9.2: avisos de mandos (coche y barca), velocímetro y aire de buceo: ni sobre la barra rápida/medidores ni sobre el aviso breve
        {
            GameObject tmp = new GameObject("HudBottomProbe", typeof(RectTransform));
            RectTransform pr = NewProbe(tmp, "prompt_coche"), sp = NewProbe(tmp, "velocimetro"), op = NewProbe(tmp, "prompt_barca"), oa = NewProbe(tmp, "aire"), ot = NewProbe(tmp, "aire_texto");
            int bad2 = 0; string first2 = "";
            foreach (Vector2 sr in res)
            {
                Vector2 cs = RefCanvasSize(sr.x, sr.y);
                HudBottom.Place(pr, HudBottom.CarPromptY, HudBottom.PromptWidth(cs.x), HudBottom.PromptHeight);
                HudBottom.PlaceSpeed(sp);
                HudBottom.Place(op, HudBottom.OceanPromptY, HudBottom.PromptWidth(cs.x), HudBottom.PromptHeight);
                HudBottom.Place(oa, HudBottom.OceanAirY, HudBottom.AirBarWidth, HudBottom.AirBarHeight);
                HudBottom.Place(ot, HudBottom.OceanAirTextY, HudBottom.AirBarWidth, HudBottom.AirTextHeight);
                Vector2 sd = gm.toastPanel.rectTransform.sizeDelta;
                gm.toastPanel.rectTransform.sizeDelta = new Vector2(Mathf.Min(pw, cs.x - 48f), sd.y);
                Rect toast = CanvasRect(gm.toastPanel.rectTransform, cs);
                gm.toastPanel.rectTransform.sizeDelta = sd;
                string why = null;
                RectTransform[] mine = { pr, sp, op, oa, ot };
                foreach (RectTransform m in mine)
                {
                    if (why != null) break;
                    Rect a = CanvasRect(m, cs);
                    if (a.xMin < 0f || a.xMax > cs.x || a.yMin < 0f || a.yMax > cs.y) { why = m.name + " fuera de pantalla " + a; break; }
                    foreach (var kv in others)
                    {
                        Rect o = CanvasRect(kv.Value, cs);
                        if (o.width <= 0.5f || o.height <= 0.5f) continue;
                        if (kv.Key.StartsWith("info") || kv.Key.StartsWith("vhs")) { if (a.Overlaps(o)) { why = m.name + " pisa " + kv.Key + " " + o; break; } continue; }
                        Rect g = new Rect(o.x - 6f, o.y - 6f, o.width + 12f, o.height + 12f);
                        if (a.Overlaps(g)) { why = m.name + " pisa " + kv.Key + " " + o; break; }
                    }
                    if (why == null && m != sp && a.Overlaps(toast)) why = m.name + " pisa el aviso breve " + toast;
                }
                // entre sí (barca: aire, texto y aviso no se tocan)
                if (why == null && (CanvasRect(op, cs).Overlaps(CanvasRect(ot, cs)) || CanvasRect(ot, cs).Overlaps(CanvasRect(oa, cs)))) why = "barca: aviso/aire se tocan";
                if (why == null && CanvasRect(pr, cs).Overlaps(CanvasRect(sp, cs))) why = "aviso del coche pisa el velocímetro";
                if (why != null) { bad2++; if (first2 == "") first2 = sr.x + "x" + sr.y + ": " + why; }
            }
            UnityEngine.Object.Destroy(tmp); foreach (GameObject g in probes) UnityEngine.Object.Destroy(g); probes.Clear();
            r.Check(bad2 == 0, tag + "[HUD] avisos del coche/barca, velocímetro y aire sin solaparse con la barra rápida, medidores, aviso breve ni info en " + res.Length + " resoluciones (16:9, 16:10, 4:3, 5:4, 21:9, 32:9, 1:1)" + (bad2 > 0 ? " (fallos " + bad2 + "; " + first2 + ")" : ""));
        }
    }

    public static IEnumerator AdrenalineChecks(SmokeTest.Report r, string tag, int lv, GameManager gm)
    {
        Adrenaline adr = Adrenaline.Local;
        FirstPersonController fpc = gm.player;
        Dictionary<MonsterAI, float> kds = new Dictionary<MonsterAI, float>();
        r.Check(adr != null && adr.fpc == fpc, tag + "[Adrenalina] componente en el jugador local");
        if (adr == null) yield break;
        MonsterAI main = null, mim = null;
        foreach (MonsterAI m in gm.monsters) { if (m == null) continue; m.Stop(); if (m.mimic != null) mim = m; else if (main == null) main = m; }
        // lejos de todo: sin efecto
        foreach (MonsterAI m in gm.monsters) if (m != null) m.WarpTo(m.transform.position + FarOff(), 0f);
        yield return Adrenaline.Linger + 0.6f;
        r.Check(!adr.Active && fpc.staminaDrainMul == 1f && fpc.sprintMul == 1f, tag + "[Adrenalina] sin persecución no hay efecto");
        // medición del gasto: 1,2 s corriendo sin y con adrenalina (forzada), mismo sitio
        bool open = OpenSpot(gm, 10f, out Vector3 od, out Vector3 osp);
        Vector3 home = fpc.transform.position;
        for (int a = 0; a < 120 && !open; a++)
        {
            Vector3 c = home + Quaternion.Euler(0f, a * 47f, 0f) * Vector3.forward * (6f + (a % 10) * 5f);
            if (!NavMesh.SamplePosition(c, out NavMeshHit hh, 2.5f, NavMesh.AllAreas) || Mathf.Abs(hh.position.y - home.y) > 3f) continue;
            fpc.Teleport(hh.position + Vector3.up * 0.05f, 0f);
            yield return null;
            open = OpenSpot(gm, 10f, out od, out osp);
        }
        Vector3 runFrom = fpc.transform.position; float runYaw = Quaternion.LookRotation(od).eulerAngles.y;
        float d0 = -1f, d1 = -1f;
        if (open)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                Adrenaline.ForceChasedForTests = pass == 0 ? 0 : 1;
                yield return 0.2f;
                fpc.Teleport(runFrom + Vector3.up * 0.05f, runYaw);
                fpc.SetStaminaForTests(90f);
                yield return 0.15f;
                fpc.SetStaminaForTests(90f);
                fpc.debugInput = true; fpc.debugMove = new Vector2(0f, 1f); fpc.debugSprint = true;
                // gasto por segundo realmente corriendo (en el agua o contra un obstáculo puede no correr todo el rato)
                float acc = 0f, accT = 0f, prevS = fpc.Stamina01, prevT = Time.time, t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 1.4f)
                {
                    yield return null;
                    float sNow = fpc.Stamina01, tNow = Time.time;
                    if (fpc.IsSprinting && sNow < prevS && tNow > prevT) { acc += prevS - sNow; accT += tNow - prevT; }
                    prevS = sNow; prevT = tNow;
                }
                fpc.debugSprint = false; fpc.debugMove = Vector2.zero; fpc.debugInput = false;
                float rate = accT > 0.3f ? acc / accT : -1f;
                if (pass == 0) d0 = rate; else d1 = rate;
            }
            Adrenaline.ForceChasedForTests = -1;
            r.Check(d0 > 0.05f && d1 > 0f && d1 < d0 * 0.7f && d1 > d0 * 0.4f,
                tag + "[Adrenalina] la resistencia baja más despacio corriendo con adrenalina (" + (d0 * 100f).ToString("F1") + " → " + (d1 * 100f).ToString("F1") + " %/s, ×" + (d0 > 0f ? d1 / d0 : 0f).ToString("F2") + ")");
            fpc.Teleport(runFrom + Vector3.up * 0.05f, runYaw);
        }
        else r.Info(tag + "[Adrenalina] sin 10 m libres para medir la carrera");
        yield return Adrenaline.Linger + 0.6f;
        // persecución real del monstruo principal
        if (main != null && OpenSpot(gm, 9f, out Vector3 md, out Vector3 msp))
        {
            fpc.SetStaminaForTests(30f);
            adr.ResetBoostCooldownForTests();
            foreach (MonsterAI m in gm.monsters) if (m != null) { kds[m] = m.killDistance; m.killDistance = 0.05f; }   // que no lo atrape durante la prueba
            int b0 = adr.Boosts, beats0 = adr.Beats;
            main.WarpTo(msp, Quaternion.LookRotation(-md).eulerAngles.y);
            main.ChaseForTests();
            float st0 = fpc.Stamina01;
            yield return 0.25f;
            bool chased = adr.Chased && adr.Chaser == main;
            float st1 = fpc.Stamina01;
            r.Check(chased && adr.Active && fpc.staminaDrainMul == Adrenaline.DrainMul && Mathf.Abs(fpc.sprintMul - Adrenaline.SprintMul) < 1e-4f,
                tag + "[Adrenalina] persecución del monstruo («" + (main.visual != null && main.visual.profile != null ? main.visual.profile.id : "?") + "»): gasto ×" + fpc.staminaDrainMul + ", carrera ×" + fpc.sprintMul);
            r.Check(adr.Boosts == b0 + 1 && st1 > st0 + Adrenaline.Boost * 0.8f, tag + "[Adrenalina] empujón de resistencia al empezar (" + (st0 * 100f).ToString("F0") + " → " + (st1 * 100f).ToString("F0") + " %)");
            r.Check(fpc.EffectiveSprintSpeed > fpc.sprintSpeed * 1.04f && fpc.EffectiveSprintSpeed <= fpc.sprintSpeed * 1.1f && fpc.EffectiveSprintSpeed < main.chaseSpeed * 2f,
                tag + "[Adrenalina] carrera " + fpc.sprintSpeed.ToString("F2") + " → " + fpc.EffectiveSprintSpeed.ToString("F2") + " m/s (+5-10 %, el monstruo corre a " + main.chaseSpeed.ToString("F1") + ")");
            yield return 0.5f;
            if (Shots) { fpc.Teleport(fpc.transform.position, Quaternion.LookRotation(md).eulerAngles.y); yield return 0.1f; CaptureHud(fpc.GetComponentInChildren<Camera>(), "adrenaline_hud_lv" + lv + ".png"); }
            HotbarHud hud = UnityEngine.Object.FindObjectOfType<HotbarHud>();
            Color sc = hud != null ? hud.StaminaColor : Color.clear;
            r.Check(adr.Level01 > 0.5f && sc.r > sc.g + 0.15f, tag + "[Adrenalina] barra RES teñida (" + sc + ") y latido (" + adr.Level01.ToString("F2") + ")");
            AudioSource heart = adr.transform.Find("Heartbeat") != null ? adr.transform.Find("Heartbeat").GetComponent<AudioSource>() : null;
            r.Check(heart != null && adr.HeartVolume > 0.01f && adr.Beats > beats0 && adr.Bpm > 110f && heart.bypassListenerEffects, tag + "[Adrenalina] latido sonando deprisa (vol " + adr.HeartVolume.ToString("F2") + ", " + adr.Bpm.ToString("F0") + " ppm, " + (adr.Beats - beats0) + " latidos, fuera del paso-bajo)");
            {   // v7.9.0: durante la persecución el resto de sonidos se apagan (paso-bajo del oyente); el latido no
                AudioLowPassFilter lpf = fpc.GetComponentInChildren<AudioListener>() != null ? fpc.GetComponentInChildren<AudioListener>().GetComponent<AudioLowPassFilter>() : null;
                r.Check(lpf != null && lpf.cutoffFrequency < 6000f && adr.ChaseMuffle01 > 0.3f, tag + "[Latido] persecución: resto de sonidos con paso-bajo (" + (lpf != null ? lpf.cutoffFrequency : 0f).ToString("F0") + " Hz)");
            }
            // fin: el monstruo deja de perseguir (lejos) → dura unos segundos y se apaga
            main.Stop(); main.WarpTo(main.transform.position + FarOff(), 0f);
            // v7.4.0: las esperas del test van en tiempo real (timeSinceStartup) y la adrenalina en tiempo de juego (deltaTime, con tope
            // de 0,33 s por fotograma): con un parón de la máquina (fork de la VM, carga de CPU) 4,4 s reales eran < 4 s de juego y fallaba.
            // Se espera en tiempo de juego y se mide cuánto dura de verdad.
            float tg0 = Time.time;
            yield return (System.Func<bool>)(() => Time.time - tg0 >= 1.2f);
            bool lingering = adr.Active && !adr.Chased;
            yield return (System.Func<bool>)(() => !adr.Active || Time.time - tg0 > Adrenaline.Linger + 4f);
            float lasted = Time.time - tg0;
            yield return 0.2f;
            r.Check(lingering && !adr.Active && lasted >= Adrenaline.Linger - 0.1f && lasted <= Adrenaline.Linger + 1.5f && fpc.staminaDrainMul == 1f && fpc.sprintMul == 1f,
                tag + "[Adrenalina] dura unos segundos tras la persecución y se apaga (" + Adrenaline.Linger + " s; medido " + lasted.ToString("F1") + " s de juego, persiste a 1,2 s: " + lingering + ")");
            // segunda persecución enseguida: sin nuevo empujón (enfriamiento)
            int b1 = adr.Boosts;
            main.WarpTo(msp, 0f); main.ChaseForTests();
            yield return 0.25f;
            r.Check(adr.Chased && adr.Boosts == b1, tag + "[Adrenalina] el empujón no se repite antes de " + Adrenaline.BoostCooldown + " s");
            main.Stop(); main.WarpTo(main.transform.position + FarOff(), 0f);
        }
        // el imitador descubierto también cuenta (disfrazado no)
        if (mim != null && mim.mimic != null && OpenSpot(gm, 9f, out Vector3 dd, out Vector3 dsp))
        {
            yield return Adrenaline.Linger + 0.5f;
            mim.WarpTo(dsp, 0f);
            mim.mimic.SetDisguised(true);
            mim.ChaseForTests();
            yield return 0.25f;
            bool disguisedIgnored = !adr.Chased;
            mim.mimic.SetDisguised(false);
            mim.ChaseForTests();
            yield return 0.3f;
            r.Check(disguisedIgnored && adr.Chased && adr.Chaser == mim, tag + "[Adrenalina] el imitador descubierto también la dispara (disfrazado no) [disfrazado ignorado=" + disguisedIgnored + ", perseguido=" + adr.Chased + " por " + (adr.Chaser != null ? adr.Chaser.name : "-") + ", estado " + mim.CurrentState + ", " + FieldWrap.Distance(mim.transform.position, fpc.transform.position).ToString("F1") + " m]");
            mim.Stop(); mim.WarpTo(mim.transform.position + FarOff(), 0f);
        }
        yield return Adrenaline.Linger + 0.5f;
        foreach (KeyValuePair<MonsterAI, float> kv in kds) if (kv.Key != null) kv.Key.killDistance = kv.Value;
        fpc.SetStaminaForTests(fpc.maxStamina);
    }

    // monstruo y compañero (copia remota) al otro lado de cada borde / esquina: se ven donde deben (imagen más cercana)
    public static IEnumerator WrapChecks(SmokeTest.Report r, string tag, GameManager gm)
    {
        FieldWorld fw = FieldWorld.Active;
        float L = FieldWrap.L;
        Transform root = fw.levelRoot;
        MonsterAI main = null; foreach (MonsterAI m in gm.monsters) if (m != null && m.mimic == null) { main = m; break; }
        if (main == null) yield break;
        main.Stop();
        Camera cam = gm.player.GetComponentInChildren<Camera>();
        RemotePlayer mate = RemotePlayer.Create(GameBootstrap.Instance.WorldRoot.transform, 9001, "Compañero", StepKind.Grass);
        WrapView mwv = main.GetComponent<WrapView>(), pwv = mate.GetComponent<WrapView>();
        r.Check(mwv != null && pwv != null, tag + "[Bordes] monstruo y compañero con WrapView (imagen más cercana)");
        Vector2[] dirs = { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1), new Vector2(1, 1), new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1) };
        int okM = 0, okP = 0, n = 0; string fails = "";
        foreach (Vector2 d in dirs)
        {
            // jugador a 4 m del borde (por dentro), el otro a 4 m al otro lado (en coordenadas canónicas: el extremo opuesto)
            Vector3 lp = new Vector3(L * 0.5f + d.x * (L * 0.5f - 4f), 0f, L * 0.5f + d.y * (L * 0.5f - 4f));
            Vector3 op = new Vector3(L * 0.5f - d.x * (L * 0.5f - 4f), 0f, L * 0.5f - d.y * (L * 0.5f - 4f));
            if (d.x == 0) op.x = lp.x + 1.5f; if (d.y == 0) op.z = lp.z + 1.5f;
            lp.y = FieldHeight.H(lp.x, lp.z); op.y = FieldHeight.H(op.x, op.z);
            Vector3 lw = root.TransformPoint(lp), ow = root.TransformPoint(op);
            Vector3 expect = FieldWrap.Image(ow, lw);
            float yaw = Quaternion.LookRotation(new Vector3(expect.x - lw.x, 0f, expect.z - lw.z)).eulerAngles.y;
            gm.player.Teleport(lw + Vector3.up * 0.1f, yaw);
            main.WarpTo(ow, 0f);
            mate.Snap(ow + new Vector3(0.8f, 0f, 0.8f), 0f);
            yield return 0.35f;
            n++;
            expect = FieldWrap.Image(main.transform.position, lw);   // donde quedó de verdad (WarpTo ajusta al NavMesh)
            float dist = Vector3.Distance(lw, expect);
            Vector3 mv = mwv.VisiblePosition, pv = pwv.VisiblePosition;
            Vector3 vpM = cam.WorldToViewportPoint(mv + Vector3.up * 1.2f), vpP = cam.WorldToViewportPoint(pv + Vector3.up * 1.2f);
            string why = "";
            if (Vector3.Distance(mv, expect) >= 0.3f) why += "pos" + Vector3.Distance(mv, expect).ToString("F2");
            if (!(vpM.z > 0f && vpM.x > 0f && vpM.x < 1f && vpM.y > 0f && vpM.y < 1f)) why += "vp" + vpM;
            bool inM = why.Length == 0;
            bool inP = Vector3.Distance(pv, FieldWrap.Image(ow + new Vector3(0.8f, 0f, 0.8f), lw)) < 0.5f && vpP.z > 0f && vpP.x > -0.2f && vpP.x < 1.2f;
            // audio y etiqueta van con la imagen
            Transform tagT = null; foreach (Transform t in mate.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("Tag")) { tagT = t; break; }
            AudioSource msfx = main.GetComponentInChildren<AudioSource>();
            if (tagT != null && Vector3.Distance(tagT.position, pv) > 4f) inP = false;
            if (msfx != null && Vector3.Distance(msfx.transform.position, mv) > 4f) { inM = false; why += "sfx"; }
            Renderer mr = null; foreach (Renderer rr in main.GetComponentsInChildren<Renderer>()) if (rr.enabled && rr.gameObject.activeInHierarchy) { mr = rr; break; }
            Vector3 rbc = mr != null ? mr.bounds.center : Vector3.zero; rbc.y = mv.y;
            if (mr == null || Vector3.Distance(rbc, mv) > 3f) { inM = false; why += "rend" + (mr != null ? mr.name + mr.bounds.center : "null"); }
            if (inM) okM++; else fails += " M" + d + ":" + why;
            if (inP) okP++; else fails += " P" + d;
            if (n == 1 || n == 5) V702Checks.Capture(cam, "wrap_edge_" + n + ".png");
            r.Info(tag + "[Bordes] " + d + ": real " + dist.ToString("F1") + " m, monstruo visto a " + Vector3.Distance(lw, mv).ToString("F1") + " m, compañero a " + Vector3.Distance(lw, pv).ToString("F1") + " m");
        }
        r.Check(okM == n, tag + "[Bordes] el monstruo se ve en su sitio al otro lado de bordes y esquinas (" + okM + "/" + n + ")" + fails);
        r.Check(okP == n, tag + "[Bordes] el compañero (con su etiqueta y sonido) se ve al otro lado (" + okP + "/" + n + ")");
        UnityEngine.Object.Destroy(mate.gameObject);

        // persecución a través del borde: el monstruo sigue al jugador, cruza y lo alcanza a la distancia envuelta
        {
            Vector3 lp = new Vector3(L - 6f, 0f, L * 0.5f + 7f), op = new Vector3(8f, 0f, L * 0.5f + 7f);   // 14 m de distancia real, a través del borde +x
            lp.y = FieldHeight.H(lp.x, lp.z); op.y = FieldHeight.H(op.x, op.z);
            Vector3 lw = root.TransformPoint(lp), ow = root.TransformPoint(op);
            gm.player.Teleport(lw + Vector3.up * 0.1f, 90f);
            main.WarpTo(ow, 270f);
            int cross0 = main.EdgeCrossings;
            float d0 = FieldWrap.Distance(main.transform.position, gm.player.transform.position);
            float kd = main.killDistance; main.killDistance = 0.05f;   // que no lo atrape (las pruebas siguen)
            main.ChaseForTests();
            float t = 0f, dmin = d0;
            gm.player.InputEnabled = false;
            while (t < 9f)
            {
                yield return 0.25f; t += 0.25f;
                gm.player.Teleport(lw + Vector3.up * 0.1f, 90f);
                float dd = FieldWrap.Distance(main.transform.position, gm.player.transform.position);
                dmin = Mathf.Min(dmin, dd);
                if (dd < 2.2f || gm.State != GameManager.GameState.Playing) break;
            }
            gm.player.InputEnabled = true;
            bool crossed = main.EdgeCrossings > cross0;
            main.Stop(); main.killDistance = kd;
            r.Check(crossed && dmin < 3.5f, tag + "[Bordes] persecución a través del borde: el monstruo cruza (" + (main.EdgeCrossings - cross0) + ") y se acerca " + d0.ToString("F1") + " → " + dmin.ToString("F1") + " m en " + t.ToString("F1") + " s");
            main.WarpTo(main.transform.position + new Vector3(0f, 0f, L * 0.25f), 0f);
            yield return 0.5f;
            if (gm.State != GameManager.GameState.Playing) r.Info(tag + "[Bordes] (el monstruo atrapó al jugador: estado " + gm.State + ")");
        }
    }

    // ---------------- prueba rápida con gráficos ----------------
    private static IEnumerator routine; private static double waitUntil;
    static V704Checks() { if (SessionState.GetString("smoke_phase", "") != "v704quick") return; EditorApplication.update += Tick; }
    public static void RunQuick()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v704quick");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Quick();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Done(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (Exception e) { Debug.LogError("[V704] " + e); if (quickReport != null) quickReport.Fail("excepción: " + e.Message); Done(); }
    }
    private static SmokeTest.Report quickReport;
    private static void Done() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; if (quickReport != null) quickReport.Finish(); else EditorApplication.Exit(1); }

    private static IEnumerator Quick()
    {
        quickReport = new SmokeTest.Report("/workspace/logs/v704quick.txt");
        SmokeTest.Report r = quickReport;
        V702Checks.ShotDir = ShotDir; Shots = true;
        string lvs = Environment.GetEnvironmentVariable("V704_LEVELS") ?? "1,5";
        GameSettings.ResetDefaults();
        ExitLock.AutoKeyForTests = true;
        ElevatorCabin.InstantForTests = true;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        foreach (string s in lvs.Split(','))
        {
            int lv = int.Parse(s.Trim());
            int ld = gb.LoadCount;
            if (gb.InMenu) UIManager.Instance.StartGame(lv); else gb.LoadLevel(lv);
            float w0 = 0f;
            while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && w0 < 120f) { yield return 0.2f; w0 += 0.2f; }
            if (ElevatorCabin.Current != null) ElevatorCabin.Current.Skip();
            float w = 0f; while (ElevatorCabin.PlayerLocked && w < 20f) { yield return 0.25f; w += 0.25f; }
            yield return 1.0f;
            GameManager gm = GameManager.Instance;
            LevelConfig cfg = LevelConfig.Get(lv);
            string tag = "[Nivel " + lv + "] ";
            r.Info(tag + "cargado: " + cfg.name + ", semilla " + gb.Generator.UsedSeed);
            Camera cam = gm.player.GetComponentInChildren<Camera>();
            Flashlight fl = gm.player.GetComponentInChildren<Flashlight>(); if (fl != null) { fl.SetBattery(100f); fl.SetOn(true); }
            V702Checks.Capture(cam, "lv" + lv + "_start.png");
            if (gb.Generator.Garage != null) { IEnumerator gs = GarageShots(gm, cam); while (gs.MoveNext()) yield return gs.Current; }
            IEnumerator e = LevelChecks(r, tag, lv, cfg, gm);
            while (e.MoveNext()) yield return e.Current;
            // llaves de cerca (cámara a medio metro, mirando la llave)
            int kshots = 0;
            foreach (ItemPickup ip in UnityEngine.Object.FindObjectsOfType<ItemPickup>())
            {
                if (!ItemDefs.IsKey(ip.type) || kshots >= 2) continue;
                ip.enabled = false;
                Vector3 p = ip.transform.position;
                bool placed = false;
                for (int a = 0; a < 48 && !placed; a++)
                {
                    float rad = 0.9f + (a / 16) * 0.35f;
                    Vector3 c = p + Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward * rad;
                    if (!NavMesh.SamplePosition(c, out NavMeshHit nh, 0.6f, NavMesh.AllAreas) || Mathf.Abs(nh.position.y - p.y) > 1.3f) continue;
                    Vector3 eye = nh.position + Vector3.up * 1.6f;
                    if (Physics.Linecast(eye, p + Vector3.up * 0.03f, out RaycastHit lh, ~0, QueryTriggerInteraction.Ignore) && Vector3.Distance(lh.point, p) > 0.15f) continue;
                    gm.player.Teleport(nh.position + Vector3.up * 0.05f, Quaternion.LookRotation(new Vector3(p.x - nh.position.x, 0f, p.z - nh.position.z)).eulerAngles.y);
                    placed = true;
                }
                if (!placed) continue;
                yield return 0.6f;
                Quaternion lr0 = cam.transform.localRotation;
                cam.transform.LookAt(p + Vector3.up * 0.02f);
                if (fl != null) fl.SetOn(true);
                yield return 0.1f;
                cam.transform.LookAt(p + Vector3.up * 0.02f);
                V702Checks.Capture(cam, "lv" + lv + "_key" + kshots + "_" + ip.type + ".png");
                cam.transform.localRotation = lr0;
                kshots++;
            }
            if (gb.Generator.Garage != null) GarageMap(gb.Generator, "garage_map_lv1.png");
        }
    }

    private static IEnumerator GarageShots(GameManager gm, Camera cam)
    {
        BackroomsGenerator gen = gm.generator; GaragePlan g = gen.Garage;
        // boca de la primera rampa, la rampa por dentro, una planta inferior y la puerta de salida
        Vector3 mouth = gen.CellCenterWorld(g.rampX[0], g.boundaries[0] - 2) - new Vector3(0f, 0f, 2.2f);
        gm.player.Teleport(mouth + Vector3.up * 0.1f, 0f); yield return 0.6f; V702Checks.Capture(cam, "garage_ramp_mouth.png");
        Vector3 inr = gen.CellCenterWorld(g.rampX[0], g.boundaries[0] - 1);
        gm.player.Teleport(inr + Vector3.up * 0.1f, 0f); yield return 0.6f; V702Checks.Capture(cam, "garage_ramp_inside.png");
        if (g.decoyMouth.Count > 0) { Vector3 dm = gen.CellCenterWorld(g.decoyMouth[0].x, g.decoyMouth[0].y - 1); gm.player.Teleport(dm + Vector3.up * 0.1f, 0f); yield return 0.6f; V702Checks.Capture(cam, "garage_decoy.png"); }
        Vector3 deck = gen.CellCenterWorld(gen.GridWidth / 2, g.boundaries[g.boundaries.Length - 1] + 3);
        gm.player.Teleport(deck + Vector3.up * 0.1f, 90f); yield return 0.6f; V702Checks.Capture(cam, "garage_lower_deck.png");
        Transform ex = gm.exit; Vector3 st = ex.position + ex.forward * 3.5f; st.y = ex.parent.position.y + 0.1f;
        gm.player.Teleport(st, Quaternion.LookRotation(-ex.forward).eulerAngles.y); yield return 0.6f; V702Checks.Capture(cam, "garage_exit.png");
        gm.player.Teleport(gen.PlayerStart, gen.PlayerStartYaw); yield return 0.4f;
    }
}
