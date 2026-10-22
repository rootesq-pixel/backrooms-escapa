using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// v7.1.1 «calidad GPU»: lightmaps de 4096 horneados en la GPU (Bajo: 2048), texturas PBR de 4096 en el Nivel 0, sondas y
// cubemap de la Poolrooms, y el arreglo de las manchas negras del Parque de bolas (sin paredes de piscina coplanarias con el
// borde acolchado, tobogán cerrado). La prueba de manchas sobre el lightmap está en Tools/ballpit/audit_black.py.
public static class V711Checks
{
    public static void AssetChecks(SmokeTest.Report r)
    {
        const string T = "[v7.1.1] ";
        Texture2D bl = Resources.Load<Texture2D>("Ballpit/Ballpit_LM"), bll = Resources.Load<Texture2D>("Ballpit/Ballpit_LM_Low");
        Texture2D pl = Resources.Load<Texture2D>("PoolRoom/PoolRoom_LM"), pll = Resources.Load<Texture2D>("PoolRoom/PoolRoom_LM_Low");
        r.Check(bl != null && bl.width == 4096, T + "lightmap del Parque de bolas de 4096 (" + (bl != null ? bl.width : 0) + ")");
        r.Check(bll != null && bll.width == 2048, T + "lightmap reducido del Parque para Bajo de 2048 (" + (bll != null ? bll.width : 0) + ")");
        r.Check(pl != null && pl.width == 4096, T + "lightmap de la Poolrooms de 4096 (" + (pl != null ? pl.width : 0) + ")");
        r.Check(pll != null && pll.width == 2048, T + "lightmap reducido de la Poolrooms para Bajo de 2048 (" + (pll != null ? pll.width : 0) + ")");
        Cubemap cube = Resources.Load<Cubemap>("PoolRoom/PoolRoom_Cube");
        r.Check(cube != null && cube.width >= 512, T + "cubemap de reflexión de la Poolrooms (Cycles GPU) " + (cube != null ? cube.width : 0));
        TextAsset pj = Resources.Load<TextAsset>("PoolRoom/PoolRoomProbes");
        int np = pj != null ? JsonUtility.FromJson<ProbesJ>(pj.text).probes.Length : 0;
        r.Check(np >= 30, T + "sondas de la Poolrooms (" + np + ")");
        foreach (string n in new[] { "WallpaperYellow", "CarpetBrown", "CeilingBeige" })
        {
            Texture2D a = Resources.Load<Texture2D>("Textures/" + n + "_a");
            r.Check(a != null && a.width == 4096, T + "textura " + n + " de 4096 (" + (a != null ? a.width : 0) + ")");
        }
        int ok = 0; string[] all = { "WallpaperYellow", "WallpaperHotel", "CarpetBrown", "CarpetRed", "CeilingBeige", "CeilingPool", "CeilingDark", "ConcreteWall", "ConcreteFloor", "ConcreteCeiling", "PoolWall", "PoolFloor", "MetalWall", "MetalFloor" };
        foreach (string n in all) { Texture2D a = Resources.Load<Texture2D>("Textures/" + n + "_a"); if (a != null && a.width >= 2048 && Resources.Load<Texture2D>("Textures/" + n + "_n") != null && Resources.Load<Texture2D>("Textures/" + n + "_m") != null) ok++; }
        r.Check(ok == all.Length, T + "14 materiales PBR horneados en GPU (>= 2048, con normal y máscara): " + ok);
        {   // v7.1.1: la máscara (R metal, G oclusión, A suavidad) debe conservar la oclusión: un fallo de canales la dejó a 0 y oscurecía techos y paredes
            int good = 0; string bad = "";
            foreach (string n in all)
            {
                string fp = "Assets/Resources/Textures/" + n + "_m.png";
                if (!System.IO.File.Exists(fp)) { bad += " " + n; continue; }
                Texture2D t = new Texture2D(2, 2); t.LoadImage(System.IO.File.ReadAllBytes(fp));
                Color32[] px = t.GetPixels32(); double g = 0; int step = Mathf.Max(1, px.Length / 65536), cnt = 0;
                for (int i = 0; i < px.Length; i += step) { g += px[i].g; cnt++; }
                Object.DestroyImmediate(t);
                if (g / Mathf.Max(1, cnt) / 255.0 > 0.4) good++; else bad += " " + n;
            }
            r.Check(good == all.Length, T + "máscaras PBR con oclusión válida (G media > 0.4): " + good + "/" + all.Length + bad);
        }
        {   // v7.1.2: sin este .mat el build quitaba la variante con instancing y en el ejecutable no se dibujaba ninguna bola
            Material km = Resources.Load<Material>("Ballpit/BallInstancedKeeper");
            r.Check(km != null && km.enableInstancing && km.shader != null && km.shader.name == "Backrooms/BallInstanced", T + "bolas: Resources/Ballpit/BallInstancedKeeper.mat existe con instancing (conserva INSTANCING_ON en el build)");
            string gs = System.IO.File.ReadAllText("ProjectSettings/GraphicsSettings.asset");
            r.Info(T + "GraphicsSettings: " + (gs.Contains("m_InstancingStripping: 0") ? "Instancing Variants = Strip Unused (por eso hace falta el .mat)" : "Instancing Variants distinto de Strip Unused"));
        }
    }
    [System.Serializable] class ProbesJ { public ProbeJ[] probes; }
    [System.Serializable] class ProbeJ { public float[] p; }

    public static IEnumerator LevelChecks(SmokeTest.Report r, string tag, int lv, LevelConfig cfg, GameManager gm)
    {
        BackroomsGenerator gen = gm.generator;
        if (gen == null || gen.Halls.Count == 0) yield break;
        string T = tag + "[v7.1.1 Parque] ";
        r.Check(BallpitSetPiece.LightmapSize == (GameSettings.Quality == 0 ? 2048 : 4096), T + "lightmap en uso " + BallpitSetPiece.LightmapSize + " (calidad " + GameSettings.Quality + ")");
        Mesh m = BallpitSetPiece.LoadBakedMesh(out Mesh col);
        BallpitSetPiece.HallJ d = BallpitSetPiece.Data;
        // ninguna cara de la pared interior de una piscina atraviesa z=0 (antes iba del fondo al borde, coplanaria con la cara
        // interior del borde acolchado: z-fighting y negro en el horneado)
        Vector3[] v = m.vertices; int bad = 0, onPlane = 0;
        for (int k = 0; k < m.subMeshCount; k++)
        {
            if (k == BallpitSetPiece.M_BALL || k == BallpitSetPiece.M_EMIT) continue;
            int[] t = m.GetTriangles(k);
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                float ylo = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), yhi = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                foreach (BallpitSetPiece.PitJ p in d.pits)
                {
                    float[] px = { p.x0 + d.rim, p.x1 - d.rim }, pz = { p.z0 + d.rim, p.z1 - d.rim };
                    bool on = false;
                    foreach (float x in px) if (Mathf.Abs(a.x - x) < 0.003f && Mathf.Abs(b.x - x) < 0.003f && Mathf.Abs(c.x - x) < 0.003f) on = true;
                    foreach (float z in pz) if (Mathf.Abs(a.z - z) < 0.003f && Mathf.Abs(b.z - z) < 0.003f && Mathf.Abs(c.z - z) < 0.003f) on = true;
                    if (!on) continue;
                    onPlane++;
                    if (ylo < -0.02f && yhi > 0.02f) bad++;
                }
            }
        }
        r.Check(onPlane > 0 && bad == 0, T + "paredes interiores de las piscinas sin solaparse con el borde acolchado (" + bad + " de " + onPlane + " triángulos)");
        // tobogán: sólido cerrado (toda arista compartida por 2 caras) y normales coherentes
        int[] st = m.GetTriangles(BallpitSetPiece.M_PLY);
        var edges = new System.Collections.Generic.Dictionary<long, int>();
        var keyOf = new System.Collections.Generic.Dictionary<Vector3, int>();
        System.Func<Vector3, int> id = q => { Vector3 r3 = new Vector3(Mathf.Round(q.x * 1000f), Mathf.Round(q.y * 1000f), Mathf.Round(q.z * 1000f)); if (!keyOf.TryGetValue(r3, out int i)) { i = keyOf.Count; keyOf[r3] = i; } return i; };
        int slideTris = 0;
        for (int i = 0; i + 2 < st.Length; i += 3)
        {
            Vector3 a = v[st[i]], b = v[st[i + 1]], c = v[st[i + 2]];
            Vector3 cc = (a + b + c) / 3f;
            if (!(cc.x > -0.6f && cc.x < 2.05f && cc.z > 4.5f && cc.z < 5.4f && cc.y > 0.15f)) continue;   // zona del tobogán
            slideTris++;
            int[] ids = { id(a), id(b), id(c) };
            for (int e = 0; e < 3; e++) { int p0 = ids[e], p1 = ids[(e + 1) % 3]; if (p0 == p1) continue; long key = p0 < p1 ? ((long)p0 << 32) | (uint)p1 : ((long)p1 << 32) | (uint)p0; edges[key] = edges.TryGetValue(key, out int n0) ? n0 + 1 : 1; }
        }
        int open = 0; foreach (var kv in edges) if (kv.Value != 2) open++;
        r.Check(slideTris > 500 && open == 0, T + "tobogán de plástico cerrado (" + slideTris + " triángulos, aristas abiertas " + open + ")");
        yield return null;

        // ---------------- bolas (v7.1.1): todas se mueven, sin capa plana «foto» visible, presupuesto ----------------
        if (BallPitField.All.Count == 0) yield break;
        BallPitField f0 = BallPitField.All[0];
        float cov = 1f;   // v7.1.2: con el vaivén aplicado en 4 instantes (peor de ellos), margen 2 mm
        foreach (float tq in new[] { 0.3f, 1.7f, 3.1f, 5.9f }) cov = Mathf.Min(cov, f0.TopCoverage(4000, 2, 0.002f, tq));
        r.Check(cov >= 0.975f, T + "bolas: desde arriba no se ve la capa plana ni el fondo (las 2 capas que siempre se dibujan tapan el " + (cov * 100f).ToString("F2") + " %)");
        {
            int[] bt = m.GetTriangles(BallpitSetPiece.M_BALL); float maxRel = -9f, minFloor = 9f; int nb = 0;
            foreach (int i in bt)
                foreach (BallpitSetPiece.PitJ p in d.pits)
                    if (v[i].x > p.x0 && v[i].x < p.x1 && v[i].z > p.z0 && v[i].z < p.z1) { maxRel = Mathf.Max(maxRel, v[i].y - p.ball); minFloor = Mathf.Min(minFloor, v[i].y - p.floor); nb++; }
            r.Check(nb > 0 && maxRel < -0.25f && minFloor > 0f, T + "bolas: la capa plana queda como fondo a " + (-maxRel).ToString("F2") + " m bajo la superficie (sobre el suelo de la piscina: " + minFloor.ToString("F2") + " m)");
            MeshRenderer hr = null;
            foreach (MeshRenderer mr in BallpitSetPiece.Roots[0].GetComponentsInChildren<MeshRenderer>()) if (mr.name == "BallpitHallBaked") hr = mr;
            Color bc = hr != null && hr.sharedMaterials.Length > BallpitSetPiece.M_BALL ? hr.sharedMaterials[BallpitSetPiece.M_BALL].GetColor("_Color") : Color.white;
            r.Check(bc.grayscale < 0.4f, T + "bolas: el fondo es oscuro (tinte " + bc.grayscale.ToString("F2") + "), no una foto de bolas");
        }
        int total = 0; foreach (BallPitField f in BallPitField.All) total += f.BallCount;
        r.Check(total > 50000 && total <= 79613, T + "bolas: " + total + " instancias (v7.1.0: 79613; menos bolas, solo las capas que se ven)");
        bool anim = true; foreach (BallPitField f in BallPitField.All) anim &= f.TilesAnimated;
        r.Check(anim && Shader.GetGlobalFloat("_BallAmbient") > 0.5f, T + "bolas: todas las baldosas de bolas tienen vaivén (amplitud " + Shader.GetGlobalFloat("_BallAmbient").ToString("F2") + ")");

        FirstPersonController fpc = gm.player;
        Vector3 home = fpc.transform.position; float homeYaw = fpc.transform.eulerAngles.y;
        BallpitSetPiece.PitJ pit = f0.pits[0];
        Rect inner = BallpitSetPiece.PitInner(pit, f0.rim);
        Vector3 a0 = f0.transform.TransformPoint(new Vector3(inner.xMin + 1.0f, pit.floor + 0.05f, inner.center.y));
        fpc.Teleport(a0, 90f); yield return 0.5f;
        int rip0 = BallPitField.RipplesEmitted, roll0 = BallPitField.RollingCount;
        int maxRoll = 0;
        Vector3 dir = f0.transform.TransformDirection(Vector3.right);
        float t0 = Time.time;
        while (Time.time - t0 < 2.0f)
        {   // vadear 2 s a ~1,6 m/s (por tiempo: en batchmode los fotogramas duran ~1 ms)
            fpc.Teleport(a0 + dir * ((Time.time - t0) * 1.6f) + Vector3.up * 0.02f, 90f);
            maxRoll = Mathf.Max(maxRoll, BallPitField.RollingCount);
            yield return null;
        }
        int rips = BallPitField.RipplesEmitted - rip0;
        r.Check(rips >= 3 && BallPitField.RippleCount > 0, T + "bolas: al vadear salen ondas que recorren la piscina (" + rips + " ondas, activas " + BallPitField.RippleCount + ")");
        r.Check(GameSettings.Quality == 0 || maxRoll > 0, T + "bolas: algunas salen rodando por la superficie (" + maxRoll + " a la vez)");
        {   // v7.1.2: desplazamiento real de las bolas (réplica en CPU del shader) junto al jugador parado en la piscina y vaivén lejos
            Material bm = BallPitField.SharedMaterial;
            r.Check(bm != null && bm.enableInstancing && BallPitField.MaterialFromKeeper && bm.shader != null && bm.shader.name == "Backrooms/BallInstanced",
                T + "bolas: material instanciado creado desde Resources/Ballpit/BallInstancedKeeper.mat (la variante con instancing va en el build)");
            yield return 0.4f;
            Vector3 pp = fpc.transform.position;
            List<Vector3> cs = new List<Vector3>(); List<float> tops = new List<float>();
            f0.TopBallsNear(pp, 1.2f, cs, tops);
            float tt = Time.timeSinceLevelLoad; int nN = 0, nR = 0; float sN = 0f, sR = 0f, minN = 9f;
            for (int i = 0; i < cs.Count; i++)
            {
                float rr = new Vector2(cs[i].x - pp.x, cs[i].z - pp.z).magnitude;
                Vector3 o = BallPitField.OffsetAt(cs[i], tt, tops[i], 1f); float hz = new Vector2(o.x, o.z).magnitude;
                if (rr < 0.3f) { nN++; sN += hz; minN = Mathf.Min(minN, hz); }
                else if (rr > 0.7f && rr < 1.1f) { nR++; sR += hz; }
            }
            r.Check(nN >= 5 && sN / nN > 0.15f && minN > 0.05f, T + "bolas: junto al jugador se apartan de verdad (" + nN + " bolas a < 0,3 m: media " + (nN > 0 ? sN / nN : 0f).ToString("F2") + " m, mín. " + minN.ToString("F2") + " m)");
            r.Check(nR >= 5 && sR / nR > 0.012f, T + "bolas: a 0,7-1,1 m también se apartan en cadena (" + nR + " bolas, media " + (nR > 0 ? sR / nR : 0f).ToString("F3") + " m)");
            Vector3 pf = f0.transform.TransformPoint(new Vector3(inner.xMax - 0.8f, pit.ball, inner.yMax - 0.8f));
            cs.Clear(); tops.Clear(); f0.TopBallsNear(pf, 0.6f, cs, tops);
            int mv = 0; float sm = 0f;
            for (int i = 0; i < cs.Count; i++) { float dm = (BallPitField.OffsetAt(cs[i], tt + 0.5f, tops[i], 1f) - BallPitField.OffsetAt(cs[i], tt, tops[i], 1f)).magnitude; sm += dm; if (dm > 0.002f) mv++; }
            r.Check(cs.Count >= 20 && mv >= cs.Count * 0.97f && sm / cs.Count > 0.006f, T + "bolas: lejos de todos, todas se mecen solas (" + mv + "/" + cs.Count + " se mueven > 2 mm en 0,5 s, media " + (cs.Count > 0 ? sm / cs.Count * 1000f : 0f).ToString("F1") + " mm)");
            {   // MP: un compañero (RemotePlayer) metido en la piscina también aparta las bolas
                Vector3 pr = f0.transform.TransformPoint(new Vector3(inner.xMax - 0.8f, pit.floor + 0.05f, inner.yMin + 0.8f));
                GameObject rgo = new GameObject("TestRemoteInPit"); rgo.transform.position = pr; rgo.AddComponent<RemotePlayer>();
                yield return 1.3f;   // los cuerpos remotos se buscan cada segundo
                cs.Clear(); tops.Clear(); f0.TopBallsNear(pr, 0.3f, cs, tops);
                float tr = Time.timeSinceLevelLoad, sh = 0f;
                for (int i = 0; i < cs.Count; i++) { Vector3 o = BallPitField.OffsetAt(cs[i], tr, tops[i], 1f); sh += new Vector2(o.x, o.z).magnitude; }
                bool farFromLocal = new Vector2(pr.x - fpc.transform.position.x, pr.z - fpc.transform.position.z).magnitude > 1.5f;
                r.Check(farFromLocal && cs.Count >= 5 && sh / cs.Count > 0.12f, T + "bolas: también se apartan alrededor de otro jugador en MP (" + cs.Count + " bolas a < 0,3 m del compañero: media " + (cs.Count > 0 ? sh / cs.Count : 0f).ToString("F2") + " m, empujadores " + BallPitField.PusherCount + ")");
                Object.Destroy(rgo);
                yield return null;
            }
        }
        {   // un monstruo dentro de la piscina también aparta bolas y hace ondas
            MonsterAI mon = null; foreach (MonsterAI mm in MonsterAI.Instances) if (mm != null && mm.gameObject.activeInHierarchy) { mon = mm; break; }
            if (mon != null)
            {
                Vector3 mh = mon.transform.position;
                UnityEngine.AI.NavMeshAgent ag = mon.GetComponent<UnityEngine.AI.NavMeshAgent>(); bool agOn = ag != null && ag.enabled;
                if (ag != null) ag.enabled = false;
                int rm0 = BallPitField.RipplesEmitted, pc = 0;
                Vector3 b0 = f0.transform.TransformPoint(new Vector3(inner.xMax - 1.0f, pit.floor + 0.02f, inner.yMin + 1.0f));
                float tm = Time.time;
                while (Time.time - tm < 1.5f) { mon.transform.position = b0 + dir * (-(Time.time - tm) * 1.2f); yield return null; pc = Mathf.Max(pc, BallPitField.PusherCount); }
                r.Check(pc >= 2 && BallPitField.RipplesEmitted - rm0 >= 2, T + "bolas: el monstruo dentro de la piscina también empuja y hace ondas (empujadores " + pc + ", ondas " + (BallPitField.RipplesEmitted - rm0) + ")");
                mon.transform.position = mh; if (ag != null) { ag.enabled = agOn; if (agOn) ag.Warp(mh); }
            }
        }
        {   // presupuesto: bolas dibujadas y llamadas mirando la piscina grande desde el borde
            Vector3 eye = f0.transform.TransformPoint(new Vector3(inner.center.x, pit.ball + 0.05f, inner.yMin - 0.6f));
            fpc.Teleport(f0.transform.TransformPoint(new Vector3(inner.center.x, 0.02f, inner.yMin - 0.6f)), 0f); yield return 0.4f;
            float acc = 0f; int fr = 0, vis = 0, calls = 0;
            for (int k = 0; k < 60; k++) { yield return null; acc += Time.unscaledDeltaTime; fr++; vis = Mathf.Max(vis, f0.LastVisible); calls = Mathf.Max(calls, f0.LastCalls); }
            r.Info(T + "bolas: tiempo medio de fotograma mirando la piscina " + (acc / fr * 1000f).ToString("F1") + " ms (calidad " + GameSettings.Quality + "), " + vis + " bolas, " + calls + " llamadas");
            r.Check(vis > 1000 && vis <= 26000 && calls <= 160, T + "bolas: presupuesto de dibujo (" + vis + " <= 26000 bolas, " + calls + " <= 160 llamadas; tiempo real de GPU medido en el PC)");
        }
        fpc.Teleport(home, homeYaw); yield return 0.3f;
    }
}
