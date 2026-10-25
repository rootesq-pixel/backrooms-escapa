// v7.7.0 Nivel 114 «El Desierto Cálido»: pruebas de datos (edición) y recorrido en juego.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class V770Checks
{
    static IEnumerator WaitFor(System.Func<bool> cond, float max)
    {
        float end = Time.time + max;
        while (Time.time < end && !cond()) yield return null;
    }

    // ---------------- edición ----------------
    public static void Edit(SmokeTest.Report r, string credits, string ui)
    {
        string T = "[v7.7.0] ";
        LevelConfig c = LevelConfig.Get(8);
        r.Check(c.layout == LayoutKind.Desert && c.DisplayNumber == "114" && !c.HasMonsterAI && c.monsterCount == 0 && c.name.Contains("Desierto"), T + "nivel 8 = Nivel 114 «" + c.name + "», sin entidades");
        r.Check(LevelConfig.Get(2).name == "Poolrooms" && LevelConfig.Get(3).name == "Hotel abandonado", T + "Poolrooms y Hotel siguen en su sitio");
        foreach (ItemType t in new[] { ItemType.RoyalRations, ItemType.CashewWater, ItemType.NeonWater })
        {
            ItemDefs.Def d = ItemDefs.Get(t);
            r.Check(d != null && d.type == t && d.usable && !string.IsNullOrEmpty(d.name) && d.description.Length > 40, T + "objeto " + t + " definido (" + (d != null ? d.name : "null") + ")");
            r.Check(ItemModels.IdFor(t, false) == t.ToString(), T + "icono/modelo de " + t);
        }
        // reparto determinista de la arena: siempre al menos un agua de neón y sobre todo agua de almendras
        bool det = true, neon = true; int alm = 0, rr = 0, cw = 0, tot = 0;
        for (int s = 1; s <= 40; s++)
        {
            List<ItemType> a = DesertItems.Plan(s * 97, 22), b = DesertItems.Plan(s * 97, 22);
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) det = false;
            if (!a.Contains(ItemType.NeonWater)) neon = false;
            foreach (ItemType t in a) { tot++; if (t == ItemType.AlmondWater) alm++; else if (t == ItemType.RoyalRations) rr++; else if (t == ItemType.CashewWater) cw++; }
        }
        r.Check(det && neon && alm > rr && rr > 0 && cw > 0, T + "objetos de la arena deterministas, siempre hay agua de neón (almendras " + alm + ", raciones " + rr + ", anacardo " + cw + " de " + tot + ")");
        // clima: determinista, empieza despejado, aparecen todos los tipos y la nieve / el clima hermoso son raros
        int[] n = new int[7]; bool wdet = true, start = true;
        for (int s = 1; s <= 60; s++)
        {
            if (DesertSky.WeatherOf(s, 0) != DesertSky.Weather.Clear) start = false;
            for (int g = 1; g < 60; g++) { DesertSky.Weather w = DesertSky.WeatherOf(s, g); if (w != DesertSky.WeatherOf(s, g)) wdet = false; n[(int)w]++; }
        }
        bool all = true; for (int i = 0; i < 7; i++) if (n[i] == 0) all = false;
        r.Check(wdet && start && all && n[(int)DesertSky.Weather.Snow] < n[(int)DesertSky.Weather.Clear] / 4 && n[(int)DesertSky.Weather.Beautiful] < n[(int)DesertSky.Weather.Clear] / 3,
            T + "clima determinista por semilla, el primer tramo despejado, salen los 7 climas (despejado " + n[0] + ", nieve " + n[5] + ", hermoso " + n[6] + ")");
        r.Check(Mathf.Approximately(DesertSky.SegLength * 4f, DesertSky.DayLength), T + "el clima cambia cada «6 h» (4 tramos por día de " + DesertSky.DayLength + " s)");
        r.Check(credits.Contains("Sonicelmaspro2") && credits.Contains("Omaigad12") && credits.Contains("Poly Haven") && ui.Contains("Sonicelmaspro2") && ui.Contains("Omaigad12"), T + "créditos: Sonicelmaspro2, Omaigad12 y Poly Haven (CREDITS.md y pantalla de créditos)");
        r.Check(UpdateCore.Compare(GameVersion.Current, "7.7.0") >= 0 && NetSession.Protocol.StartsWith("BackroomsEscape-MP-1"), T + "versión " + GameVersion.Current + ", protocolo " + NetSession.Protocol);
        foreach (string s in new[] { "Desert/Shaders/DesertSky", "Desert/Shaders/DesertSand" })
        {
            Shader sh = Resources.Load<Shader>(s);
            r.Check(sh != null && sh.isSupported, T + "shader " + s + " carga y está soportado");
        }
        int props = 0; foreach (string p in new[] { "DRock0", "DCliff0", "DPlant1", "FTable0", "FChair0_broken", "FBed_broken", "FBucket" }) if (Resources.Load<GameObject>("Desert/Props/" + p) != null) props++;
        r.Check(props == 7, T + "modelos del desierto en Resources (" + props + "/7)");
        r.Check(LevelThumbs.Get(8) != null, T + "miniatura del nivel 8 en la selección de nivel");
    }

    // ---------------- en juego ----------------
    public static IEnumerator DesertPlay(SmokeTest.Report r, string tag, GameManager gm)
    {
        string T = tag + "[Desierto] ";
        DesertWorld w = DesertWorld.Active; DesertSky sky = DesertSky.Active; DesertFx fx = DesertFx.Active;
        r.Check(w != null && sky != null && fx != null, T + "mundo, cielo y efectos del desierto creados");
        if (w == null || sky == null || fx == null) yield break;
        FirstPersonController p = gm.player;
        Camera cam = gm.playerCamera.GetComponent<Camera>();
        r.Check(gm.monsters.Length == 0 && MonsterAI.Instances.Count == 0, T + "sin entidades (nivel seguro, clase 0)");
        r.Check(ExitLock.Active == null && gm.exit != null && w.ExitTransform != null, T + "salida = puerta de piedra, sin cerradura");
        r.Check(w.houseCount >= 12 && w.wellCount >= 2 && w.caveCount >= 1 && w.veinCount > 0 && w.plantCount > 20 && w.furnitureCount > 10, T + "aldeas: " + w.houseCount + " casas, " + w.wellCount + " pozos, " + w.furnitureCount + " muebles, " + w.caveCount + " cuevas (" + w.veinCount + " vetas), " + w.plantCount + " plantas");
        r.Check(cam.farClipPlane >= 290f && cam.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null, T + "cielo propio y horizonte lejano (" + cam.farClipPlane + " m)");
        r.Check(DesertItems.Placed >= 12 && DesertItems.NeonPlaced >= 1, T + DesertItems.Placed + " objetos en la arena (" + DesertItems.NeonPlaced + " de agua de neón)");
        int sandPk = 0; foreach (ItemPickup ip in Object.FindObjectsOfType<ItemPickup>()) if (ip.type == ItemType.NeonWater || ip.type == ItemType.RoyalRations || ip.type == ItemType.CashewWater) sandPk++;
        r.Check(sandPk > 0, T + "recogibles del desierto en escena (" + sandPk + ")");
        r.Check(sky.TempC >= 10f && sky.TempC <= 17f, T + "temperatura " + sky.TempC.ToString("F1") + " °C (10-17)");

        // ---- clima y hora forzados
        bool temps = true; string wn = "";
        for (int k = 0; k < 7; k++)
        {
            DesertSky.ForceWeather = k; DesertSky.ForceHour = 13f; yield return null; yield return null;
            if (sky.Current != (DesertSky.Weather)k) wn += " " + k + "!=" + sky.Current;
            if (sky.TempC < 10f || sky.TempC > 17f) temps = false;
        }
        r.Check(wn == "" && temps, T + "los 7 climas se aplican y la temperatura queda en 10-17 °C" + wn);
        DesertSky.ForceWeather = 0; DesertSky.ForceHour = 12f; yield return null;
        r.Check(sky.Night < 0.05f && sky.SunElev > 30f, T + "mediodía: sol alto (" + sky.SunElev.ToString("F0") + "°)");
        DesertSky.ForceHour = 23.5f; yield return 0.3f;
        r.Check(sky.Night > 0.8f && fx.BirdsVisible == 0 && RenderSettings.ambientSkyColor.grayscale < 0.08f, T + "noche cerrada: oscuro (ambiente " + RenderSettings.ambientSkyColor.grayscale.ToString("F3") + "), sin pájaros");
        {   // v7.7.1: la cordura no baja nunca en el desierto (de noche y con la linterna apagada)
            Sanity sn = Sanity.Instance; Flashlight fl0 = gm.player.GetComponentInChildren<Flashlight>();
            if (fl0 != null) fl0.SetOn(false);
            float v0 = 70f; if (sn != null) sn.value = v0;
            Time.captureDeltaTime = 0.1f; for (int q = 0; q < 50; q++) yield return null; Time.captureDeltaTime = 0f;   // 5 s de juego a paso fijo
            r.Check(sn != null && Sanity.NoDrainLevel && sn.value >= v0 - 0.001f && sn.DrainRate >= 0f, T + "cordura: de noche y con la linterna apagada no baja (" + (sn != null ? v0.ToString("F0") + " -> " + sn.value.ToString("F1") + ", tasa " + sn.DrainRate.ToString("F2") : "sin Sanity") + ")");
            if (sn != null) sn.value = 100f;
        }
        DesertSky.ForceHour = 12f; yield return null;

        // ---- objetos nuevos
        Inventory inv = Inventory.Instance;
        if (inv != null)
        {
            inv.Add(ItemType.NeonWater, 1);
            int slot = -1; for (int i = 0; i < inv.slots.Length; i++) if (inv.slots[i].type == ItemType.NeonWater && !inv.slots[i].Empty) slot = i;
            p.SetStaminaForTests(1f); p.SetStamina01(0.1f); if (inv.sanity != null) inv.sanity.value = 30f;
            bool used = slot >= 0 && inv.UseSlot(slot);
            r.Check(used && p.Stamina01 > 0.95f && (inv.sanity == null || inv.sanity.value > 70f), T + "beber agua de neón: resistencia " + p.Stamina01.ToString("F2") + ", cordura " + (inv.sanity != null ? inv.sanity.value.ToString("F0") : "-"));
            inv.Add(ItemType.CashewWater, 1);
            slot = -1; for (int i = 0; i < inv.slots.Length; i++) if (inv.slots[i].type == ItemType.CashewWater && !inv.slots[i].Empty) slot = i;
            p.SetStamina01(0.1f); used = slot >= 0 && inv.UseSlot(slot);
            r.Check(used && p.Stamina01 > 0.7f, T + "beber agua de anacardo recupera resistencia (" + p.Stamina01.ToString("F2") + ")");
            if (inv.sanity != null) inv.sanity.value = 100f;
        }

        // ---- arenas movedizas: te tragan y apareces en otra zona (sin morir)
        DesertFx.HazardsOn = true; DesertFx.VoidOn = false;
        if (w.plan.quick.Count > 0)
        {
            Vector2 q = w.plan.quick[0].c;
            Vector3 at = w.levelRoot.TransformPoint(new Vector3(q.x, DesertHeight.H(q.x, q.y) + 0.2f, q.y));
            int s0 = fx.QuickSinks; p.Teleport(at, 0f);
            { IEnumerator wf = WaitFor(() => fx.QuickSinks > s0, 12f); while (wf.MoveNext()) yield return wf.Current; }
            { IEnumerator wf = WaitFor(() => !fx.Busy, 6f); while (wf.MoveNext()) yield return wf.Current; }
            Vector3 lp = w.levelRoot.InverseTransformPoint(p.transform.position);
            float dx = Mathf.Abs(Mathf.DeltaAngle(lp.x / w.L * 360f, q.x / w.L * 360f)) / 360f * w.L, dz = Mathf.Abs(Mathf.DeltaAngle(lp.z / w.L * 360f, q.y / w.L * 360f)) / 360f * w.L;
            r.Check(fx.QuickSinks > s0 && !fx.Busy && new Vector2(dx, dz).magnitude > 15f && gm.State == GameManager.GameState.Playing && p.standEyeHeight > 1.2f,
                T + "arenas movedizas: te hunden y apareces a " + new Vector2(dx, dz).magnitude.ToString("F0") + " m, vivo, con la vista normal (" + p.standEyeHeight.ToString("F2") + " m)");
        }
        else r.Check(false, T + "hay arenas movedizas en el plano");
        DesertFx.HazardsOn = false;

        // ---- desierto sin fin: al cruzar el borde del tramo te recoloca sin que se note
        {
            float y = DesertHeight.H(w.L - 1.5f, 40f);
            p.Teleport(w.levelRoot.TransformPoint(new Vector3(w.L - 1.5f, y + 0.2f, 40f)), 90f);
            int wc = w.WrapCount; yield return 0.3f;
            p.debugInput = true; p.debugMove = new Vector2(0f, 1f);
            { IEnumerator wf = WaitFor(() => w.WrapCount > wc, 4f); while (wf.MoveNext()) yield return wf.Current; }
            p.debugMove = Vector2.zero; p.debugInput = false;
            Vector3 lp = w.levelRoot.InverseTransformPoint(p.transform.position);
            r.Check(w.WrapCount > wc && lp.x >= -1f && lp.x < 8f, T + "desierto sin fin: cruzar el borde te pasa al otro lado (x local " + lp.x.ToString("F1") + ")");
        }

        // ---- a la puerta de piedra
        DesertSky.ForceWeather = -1; DesertSky.ForceHour = -1f;
        Vector3 door = w.ExitTransform.position;
        Vector3 stand = w.levelRoot.TransformPoint(w.ExitLocal);
        p.Teleport(stand + Vector3.up * 0.1f, Mathf.Atan2(door.x - stand.x, door.z - stand.z) * Mathf.Rad2Deg);
    }
}
