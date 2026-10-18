using System.Collections;
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
    }
}
