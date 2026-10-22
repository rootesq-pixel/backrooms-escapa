using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.3.0 Talasofobia: utilidades de geometría (cajas, muros con huecos, escaleras, tubos, tejados) sobre MeshBuilder.
// Todo en coordenadas locales del objeto que recibe la malla; UV proyectadas en metros (tile = metros por repetición).
public static class OceanGeo
{
    public static void Box(MeshBuilder mb, int sub, Vector3 a, Vector3 b, float tile)
    {
        Vector3 mn = Vector3.Min(a, b), mx = Vector3.Max(a, b);
        if (mx.x - mn.x < 0.001f || mx.y - mn.y < 0.001f || mx.z - mn.z < 0.001f) return;
        float x0 = mn.x, x1 = mx.x, y0 = mn.y, y1 = mx.y, z0 = mn.z, z1 = mx.z;
        mb.AddQuad(sub, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, Vector3.right, Vector3.up, tile, tile, 0f, 0f);
        mb.AddQuad(sub, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.back, Vector3.right, Vector3.up, tile, tile, 0.37f, 0f);
        mb.AddQuad(sub, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.right, Vector3.forward, Vector3.up, tile, tile, 0.61f, 0f);
        mb.AddQuad(sub, new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, Vector3.forward, Vector3.up, tile, tile, 0.13f, 0f);
        mb.AddQuad(sub, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.up, Vector3.right, Vector3.forward, tile, tile, 0f, 0f);
        mb.AddQuad(sub, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down, Vector3.right, Vector3.forward, tile, tile, 0f, 0f);
    }

    public struct Hole { public float a0, a1, y0, y1; public Hole(float a0, float a1, float y0, float y1) { this.a0 = a0; this.a1 = a1; this.y0 = y0; this.y1 = y1; } }

    // muro recto con huecos (puertas/ventanas). alongX: el muro va en X a la altura z = c (grosor t hacia +c); si no, en Z a x = c.
    public static void Wall(MeshBuilder mb, int sub, bool alongX, float c, float t, float from, float to, float y0, float y1, float tile, params Hole[] holes)
    {
        List<Hole> hs = new List<Hole>(holes ?? new Hole[0]);
        hs.Sort((p, q) => p.a0.CompareTo(q.a0));
        float cur = from;
        foreach (Hole h in hs)
        {
            float a0 = Mathf.Clamp(h.a0, from, to), a1 = Mathf.Clamp(h.a1, from, to);
            if (a0 > cur) Seg(mb, sub, alongX, c, t, cur, a0, y0, y1, tile);
            if (h.y0 > y0) Seg(mb, sub, alongX, c, t, a0, a1, y0, h.y0, tile);
            if (h.y1 < y1) Seg(mb, sub, alongX, c, t, a0, a1, h.y1, y1, tile);
            cur = Mathf.Max(cur, a1);
        }
        if (to > cur) Seg(mb, sub, alongX, c, t, cur, to, y0, y1, tile);
    }

    private static void Seg(MeshBuilder mb, int sub, bool alongX, float c, float t, float a0, float a1, float y0, float y1, float tile)
    {
        if (alongX) Box(mb, sub, new Vector3(a0, y0, c), new Vector3(a1, y1, c + t), tile);
        else Box(mb, sub, new Vector3(c, y0, a0), new Vector3(c + t, y1, a1), tile);
    }

    // marco de ventana/puerta (cuatro listones) en un muro alongX a z=c
    public static void Frame(MeshBuilder mb, int sub, bool alongX, float c, float t, Hole h, float w)
    {
        float e = 0.03f;
        if (alongX)
        {
            Box(mb, sub, new Vector3(h.a0 - w, h.y0 - w, c - e), new Vector3(h.a0, h.y1 + w, c + t + e), 0.5f);
            Box(mb, sub, new Vector3(h.a1, h.y0 - w, c - e), new Vector3(h.a1 + w, h.y1 + w, c + t + e), 0.5f);
            Box(mb, sub, new Vector3(h.a0, h.y1, c - e), new Vector3(h.a1, h.y1 + w, c + t + e), 0.5f);
            Box(mb, sub, new Vector3(h.a0, h.y0 - w, c - e), new Vector3(h.a1, h.y0, c + t + e), 0.5f);
        }
        else
        {
            Box(mb, sub, new Vector3(c - e, h.y0 - w, h.a0 - w), new Vector3(c + t + e, h.y1 + w, h.a0), 0.5f);
            Box(mb, sub, new Vector3(c - e, h.y0 - w, h.a1), new Vector3(c + t + e, h.y1 + w, h.a1 + w), 0.5f);
            Box(mb, sub, new Vector3(c - e, h.y1, h.a0), new Vector3(c + t + e, h.y1 + w, h.a1), 0.5f);
            Box(mb, sub, new Vector3(c - e, h.y0 - w, h.a0), new Vector3(c + t + e, h.y0, h.a1), 0.5f);
        }
    }

    // escalera recta de peldaños macizos (cada peldaño llega hasta el suelo): de 'start' (pie del primer peldaño) en dirección 'dir' (X o Z)
    public static void Stairs(MeshBuilder mb, int sub, Vector3 start, Vector3 dir, float width, float rise, int steps, float run, float tile)
    {
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        float h = rise / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector3 p0 = start + dir * (run * i);
            Vector3 p1 = start + dir * (run * (i + 1)) + side * width + Vector3.up * (h * (i + 1));
            Box(mb, sub, new Vector3(p0.x, start.y, p0.z), p1, tile);
        }
    }

    // tejado a dos aguas con la cumbrera en X (de x0 a x1), alero 'over', del borde z0..z1 a la altura y0 hasta yRidge
    public static void Gable(MeshBuilder mb, int roofSub, int wallSub, float x0, float x1, float z0, float z1, float y0, float yRidge, float over, float tile)
    {
        float zm = (z0 + z1) * 0.5f, th = 0.12f;
        Vector3 nS = new Vector3(0f, zm - z0, -(yRidge - y0)).normalized, nN = new Vector3(0f, zm - z0, (yRidge - y0)).normalized;
        float k = (yRidge - y0) / (zm - z0);
        float zo0 = z0 - over, zo1 = z1 + over, yo = y0 - over * k;
        Vector3 dS = new Vector3(0f, yRidge - yo, zm - zo0).normalized;
        // faldón sur y norte (cara superior y cara inferior)
        mb.AddQuad(roofSub, new Vector3(x0 - over, yo, zo0), new Vector3(x1 + over, yo, zo0), new Vector3(x1 + over, yRidge, zm), new Vector3(x0 - over, yRidge, zm), nS, Vector3.right, dS, tile, tile, 0f, 0f);
        mb.AddQuad(roofSub, new Vector3(x0 - over, yo - th, zo0), new Vector3(x1 + over, yo - th, zo0), new Vector3(x1 + over, yRidge - th, zm), new Vector3(x0 - over, yRidge - th, zm), -nS, Vector3.right, dS, tile, tile, 0f, 0f);
        Vector3 dN = new Vector3(0f, yRidge - yo, zm - zo1).normalized;
        mb.AddQuad(roofSub, new Vector3(x0 - over, yo, zo1), new Vector3(x1 + over, yo, zo1), new Vector3(x1 + over, yRidge, zm), new Vector3(x0 - over, yRidge, zm), nN, Vector3.right, dN, tile, tile, 0f, 0f);
        mb.AddQuad(roofSub, new Vector3(x0 - over, yo - th, zo1), new Vector3(x1 + over, yo - th, zo1), new Vector3(x1 + over, yRidge - th, zm), new Vector3(x0 - over, yRidge - th, zm), -nN, Vector3.right, dN, tile, tile, 0f, 0f);
        // hastiales (triángulos: quad degenerado)
        foreach (float x in new[] { x0, x1 })
        {
            Vector3 n = x == x0 ? Vector3.left : Vector3.right;
            mb.AddQuad(wallSub, new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, yRidge, zm), new Vector3(x, yRidge, zm), n, Vector3.forward, Vector3.up, tile, tile, 0f, 0f);
            mb.AddQuad(wallSub, new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, yRidge, zm), new Vector3(x, yRidge, zm), -n, Vector3.forward, Vector3.up, tile, tile, 0f, 0f);
        }
    }

    // tubo de n lados entre a y b (pilotes, barandillas, cables)
    public static void Tube(MeshBuilder mb, int sub, Vector3 a, Vector3 b, float r, int sides, float tileAlong)
    {
        Vector3 ax = b - a; float len = ax.magnitude; if (len < 0.001f) return; ax /= len;
        Vector3 s = Mathf.Abs(ax.y) > 0.95f ? Vector3.right : Vector3.Cross(ax, Vector3.up).normalized;
        Vector3 u = Vector3.Cross(s, ax).normalized;
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 n0 = s * Mathf.Cos(a0) + u * Mathf.Sin(a0), n1 = s * Mathf.Cos(a1) + u * Mathf.Sin(a1);
            Vector3 nm = (n0 + n1).normalized;
            Vector3 tv = Vector3.Cross(nm, ax).normalized;
            mb.AddQuad(sub, a + n0 * r, b + n0 * r, b + n1 * r, a + n1 * r, nm, ax, tv, tileAlong, r * 6.28f, 0f, i / (float)sides);
        }
    }

    // sólido de revolución alrededor de Y (faro): perfil de (radio, altura); caras hacia fuera (o hacia dentro si inward)
    public static void Lathe(MeshBuilder mb, int sub, Vector3 c, Vector2[] profile, int sides, float tileU, float tileV, bool inward)
    {
        for (int j = 0; j < profile.Length - 1; j++)
        {
            Vector2 p = profile[j], q = profile[j + 1];
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 dm = (d0 + d1).normalized;
                Vector2 e = q - p; Vector3 n = (dm * e.y - Vector3.up * (e.x)).normalized; if (inward) n = -n;
                if (n.sqrMagnitude < 0.5f) n = inward ? -dm : dm;
                Vector3 tU = Vector3.Cross(Vector3.up, dm).normalized;
                Vector3 v0 = c + d0 * p.x + Vector3.up * p.y, v1 = c + d1 * p.x + Vector3.up * p.y, v2 = c + d1 * q.x + Vector3.up * q.y, v3 = c + d0 * q.x + Vector3.up * q.y;
                int b = mb.verts.Count;
                float u0 = (a0 * Mathf.Max(p.x, 0.5f)) / tileU, u1 = (a1 * Mathf.Max(p.x, 0.5f)) / tileU;
                mb.verts.Add(v0); mb.verts.Add(v1); mb.verts.Add(v2); mb.verts.Add(v3);
                for (int k = 0; k < 4; k++) { mb.normals.Add(n); mb.tangents.Add(new Vector4(tU.x, tU.y, tU.z, 1f)); }
                mb.uvs.Add(new Vector2(u0, p.y / tileV)); mb.uvs.Add(new Vector2(u1, p.y / tileV)); mb.uvs.Add(new Vector2(u1, q.y / tileV)); mb.uvs.Add(new Vector2(u0, q.y / tileV));
                Vector3 tn = Vector3.Cross(v1 - v0, v2 - v0);
                List<int> t = mb.tris[sub];
                if (Vector3.Dot(tn, n) >= 0f) { t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3); }
                else { t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2); }
            }
        }
    }

    // objeto con la malla y sus materiales (y collider de malla si 'collide')
    public static GameObject Emit(MeshBuilder mb, string name, Transform parent, Material[] mats, bool collide, bool shadows, System.Action<Object> track)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        Mesh m = mb.ToMesh(name); track(m);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.receiveShadows = true;
        if (collide) { MeshCollider mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = m; }
        return g;
    }

    // material Standard con albedo + normal (Resources), tinte, repetición y suavidad
    public static Material Mat(string albedo, string normal, Color tint, float smooth, float normalScale, System.Action<Object> track)
    {
        Texture2D a = string.IsNullOrEmpty(albedo) ? null : Resources.Load<Texture2D>(albedo);
        Material m = ProceduralAssets.CreateMaterial(tint, a, Vector2.one, smooth); track(m);
        Texture2D n = string.IsNullOrEmpty(normal) ? null : Resources.Load<Texture2D>(normal);
        if (n != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", normalScale); m.EnableKeyword("_NORMALMAP"); }
        return m;
    }

    public static Material Unlit(string shader, Color c, System.Action<Object> track)
    {
        Shader s = Shader.Find(shader);
        if (s == null) s = Shader.Find("Unlit/Color");
        Material m = new Material(s); track(m);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        return m;
    }
}
