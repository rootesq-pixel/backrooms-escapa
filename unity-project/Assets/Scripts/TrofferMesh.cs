using System.Collections.Generic;
using UnityEngine;

// v7.3.0c: pantalla fluorescente empotrada de oficina (nivel 0): marco de chapa con algo de fondo y difusor prismático horneado en
// Blender (Textures/L0/Troffer_on / Troffer_off, atlas 2 × 2 con 4 variantes: limpio, amarillento, sucio con bichos, amarillento con
// manchas). Origen en el plano del techo; el panel mide 1,6 × 0,8 m a lo largo de X local. Mallas compartidas: se combinan por bloques.
public static class TrofferMesh
{
    public const float OuterX = 0.80f, OuterZ = 0.40f, InnerX = 0.74f, InnerZ = 0.34f, Lip = -0.030f, Diff = -0.010f;
    static readonly Mesh[] diffusers = new Mesh[4];
    static Mesh frame;

    // variante determinista por celda y semilla: 40 % limpio, 30 % amarillento, 12 % sucio con bichos, 18 % amarillento con manchas
    public static int Variant(int seed, int x, int z, out bool flip)
    {
        unchecked
        {
            uint n = (uint)(seed * 2654435761u) ^ (uint)(x * 40503) ^ (uint)(z * 2246822519u) ^ 0x51ED27u;
            n ^= n >> 15; n *= 2246822519u; n ^= n >> 13; n *= 3266489917u; n ^= n >> 16;
            flip = (n & 0x10000u) != 0;
            float h = (n & 0xffffu) / 65535f;
            return h < 0.40f ? 0 : h < 0.70f ? 1 : h < 0.82f ? 2 : 3;
        }
    }

    public static Mesh Diffuser(int variant)
    {
        variant = Mathf.Clamp(variant, 0, 3);
        if (diffusers[variant] != null) return diffusers[variant];
        float u0 = (variant % 2) * 0.5f + 0.002f, v0 = (variant / 2) * 0.5f + 0.002f, us = 0.496f;
        var m = new Mesh { name = "TrofferDiffuser" + variant };
        m.vertices = new[] { new Vector3(-InnerX, Diff, -InnerZ), new Vector3(InnerX, Diff, -InnerZ), new Vector3(InnerX, Diff, InnerZ), new Vector3(-InnerX, Diff, InnerZ) };
        m.uv = new[] { new Vector2(u0, v0), new Vector2(u0 + us, v0), new Vector2(u0 + us, v0 + us), new Vector2(u0, v0 + us) };
        m.normals = new[] { Vector3.down, Vector3.down, Vector3.down, Vector3.down };
        m.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
        m.triangles = Orient(m.vertices, new[] { 0, 1, 2, 0, 2, 3 }, Vector3.down);
        m.RecalculateBounds();
        return diffusers[variant] = m;
    }

    public static Mesh Frame()
    {
        if (frame != null) return frame;
        var v = new List<Vector3>(); var nr = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            int o = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int i = 0; i < 4; i++) { nr.Add(n); uv.Add(Vector2.zero); }
            int[] q = Orient(new[] { a, b, c, d }, new[] { 0, 1, 2, 0, 2, 3 }, n);
            for (int i = 0; i < 6; i++) t.Add(o + q[i]);
        }
        float ox = OuterX, oz = OuterZ, ix = InnerX, iz = InnerZ;
        // reborde inferior (anillo)
        Quad(new Vector3(-ox, Lip, -oz), new Vector3(ox, Lip, -oz), new Vector3(ix, Lip, -iz), new Vector3(-ix, Lip, -iz), Vector3.down);
        Quad(new Vector3(ox, Lip, -oz), new Vector3(ox, Lip, oz), new Vector3(ix, Lip, iz), new Vector3(ix, Lip, -iz), Vector3.down);
        Quad(new Vector3(ox, Lip, oz), new Vector3(-ox, Lip, oz), new Vector3(-ix, Lip, iz), new Vector3(ix, Lip, iz), Vector3.down);
        Quad(new Vector3(-ox, Lip, oz), new Vector3(-ox, Lip, -oz), new Vector3(-ix, Lip, -iz), new Vector3(-ix, Lip, iz), Vector3.down);
        // cantos exteriores (del techo al reborde)
        Quad(new Vector3(-ox, 0, -oz), new Vector3(ox, 0, -oz), new Vector3(ox, Lip, -oz), new Vector3(-ox, Lip, -oz), Vector3.back);
        Quad(new Vector3(ox, 0, oz), new Vector3(-ox, 0, oz), new Vector3(-ox, Lip, oz), new Vector3(ox, Lip, oz), Vector3.forward);
        Quad(new Vector3(ox, 0, -oz), new Vector3(ox, 0, oz), new Vector3(ox, Lip, oz), new Vector3(ox, Lip, -oz), Vector3.right);
        Quad(new Vector3(-ox, 0, oz), new Vector3(-ox, 0, -oz), new Vector3(-ox, Lip, -oz), new Vector3(-ox, Lip, oz), Vector3.left);
        // paredes interiores (del reborde al difusor, miran hacia dentro)
        Quad(new Vector3(-ix, Lip, -iz), new Vector3(ix, Lip, -iz), new Vector3(ix, Diff, -iz), new Vector3(-ix, Diff, -iz), Vector3.forward);
        Quad(new Vector3(ix, Lip, iz), new Vector3(-ix, Lip, iz), new Vector3(-ix, Diff, iz), new Vector3(ix, Diff, iz), Vector3.back);
        Quad(new Vector3(ix, Lip, -iz), new Vector3(ix, Lip, iz), new Vector3(ix, Diff, iz), new Vector3(ix, Diff, -iz), Vector3.left);
        Quad(new Vector3(-ix, Lip, iz), new Vector3(-ix, Lip, -iz), new Vector3(-ix, Diff, -iz), new Vector3(-ix, Diff, iz), Vector3.right);
        frame = new Mesh { name = "TrofferFrame" };
        frame.SetVertices(v); frame.SetNormals(nr); frame.SetUVs(0, uv); frame.SetTriangles(t, 0); frame.RecalculateBounds();
        return frame;
    }

    // ordena cada triángulo para que su cara delantera mire hacia n (convención de Unity: normal = (b - a) × (c - a))
    static int[] Orient(Vector3[] p, int[] tri, Vector3 n)
    {
        for (int i = 0; i < tri.Length; i += 3)
        {
            Vector3 g = Vector3.Cross(p[tri[i + 1]] - p[tri[i]], p[tri[i + 2]] - p[tri[i]]);
            if (Vector3.Dot(g, n) < 0f) { int s = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = s; }
        }
        return tri;
    }

    public static Matrix4x4 Pose(Vector3 ceilingPoint, bool alongX, bool flip)
    {
        float yaw = (alongX ? 0f : 90f) + (flip ? 180f : 0f);
        return Matrix4x4.TRS(ceilingPoint, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
    }
}
