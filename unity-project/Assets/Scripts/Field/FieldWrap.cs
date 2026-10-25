using UnityEngine;

// v7.0.4: el Campo infinito se repite (periodo L). Todo vive en su posición «canónica» dentro del cuadrado [0, L)²
// (el jugador local se teletransporta al cruzar el borde), pero se ve, se oye y se persigue en la IMAGEN MÁS CERCANA
// (convención de imagen mínima): un compañero o un monstruo a 2 m al otro lado del borde está a 2 m, no a 262 m.
public static class FieldWrap
{
    public static bool On { get { return FieldHeight.Active && RootT != null; } }
    // v7.7.0: raíz del mundo periódico activo (Campo infinito o Desierto Cálido)
    public static Transform OtherRoot;
    private static Transform RootT { get { if (FieldWorld.Active != null && FieldWorld.Active.levelRoot != null) return FieldWorld.Active.levelRoot; return OtherRoot; } }
    private static Transform Root { get { return RootT; } }
    public static float L { get { return FieldHeight.L; } }

    // imagen de p más cercana a 'reference' (coordenadas de mundo). Fuera del campo: p tal cual.
    public static Vector3 Image(Vector3 p, Vector3 reference)
    {
        if (!On) return p;
        Transform r = Root;
        Vector3 lp = r.InverseTransformPoint(p), lr = r.InverseTransformPoint(reference);
        lp.x = lr.x + FieldHeight.Wrap(lp.x - lr.x);
        lp.z = lr.z + FieldHeight.Wrap(lp.z - lr.z);
        return r.TransformPoint(lp);
    }

    // desplazamiento (mundo) que lleva p a su imagen más cercana a 'reference'
    public static Vector3 Shift(Vector3 p, Vector3 reference) { return On ? Image(p, reference) - p : Vector3.zero; }

    public static float Distance(Vector3 a, Vector3 b) { return Vector3.Distance(Image(a, b), b); }
    public static Vector3 Delta(Vector3 from, Vector3 to) { return Image(to, from) - from; }   // vector de 'from' a la imagen de 'to'

    // ¿está p (mundo) dentro del cuadrado canónico, con margen?
    public static bool InsideCanonical(Vector3 p, float margin)
    {
        if (!On) return true;
        Vector3 lp = Root.InverseTransformPoint(p);
        return lp.x >= margin && lp.x <= L - margin && lp.z >= margin && lp.z <= L - margin;
    }

    // lleva p al cuadrado canónico (misma imagen física: el campo es periódico)
    public static Vector3 Canonical(Vector3 p)
    {
        if (!On) return p;
        Vector3 lp = Root.InverseTransformPoint(p);
        lp.x = FieldHeight.Mod(lp.x); lp.z = FieldHeight.Mod(lp.z);
        return Root.TransformPoint(lp);
    }

    // destino de navegación hacia 'dest' (posiblemente en otra imagen): si queda fuera del cuadrado canónico, el punto del
    // borde por el que se cruza (a 'inset' m dentro). crossing = eje(s) por los que hay que pasar (x: ±1, z: ±1).
    public static Vector3 NavTarget(Vector3 from, Vector3 dest, float inset, out Vector2Int crossing)
    {
        crossing = Vector2Int.zero;
        if (!On) return dest;
        Transform r = Root;
        Vector3 lf = r.InverseTransformPoint(from), ld = r.InverseTransformPoint(dest);
        if (ld.x >= 0f && ld.x < L && ld.z >= 0f && ld.z < L) return dest;
        Vector3 dir = ld - lf; float t = 1f;
        if (ld.x < 0f && dir.x < -1e-4f) { float tt = (inset - lf.x) / dir.x; if (tt < t) t = tt; crossing.x = -1; }
        if (ld.x >= L && dir.x > 1e-4f) { float tt = (L - inset - lf.x) / dir.x; if (tt < t) t = tt; crossing.x = 1; }
        if (ld.z < 0f && dir.z < -1e-4f) { float tt = (inset - lf.z) / dir.z; if (tt < t) t = tt; crossing.y = -1; }
        if (ld.z >= L && dir.z > 1e-4f) { float tt = (L - inset - lf.z) / dir.z; if (tt < t) t = tt; crossing.y = 1; }
        t = Mathf.Clamp01(t);
        Vector3 e = lf + dir * t;
        e.x = Mathf.Clamp(e.x, inset, L - inset); e.z = Mathf.Clamp(e.z, inset, L - inset);
        e.y = FieldHeight.H(e.x, e.z);
        return r.TransformPoint(e);
    }

    // distancia (local XZ) al borde más cercano en el eje indicado por 'crossing'
    public static float EdgeDistance(Vector3 p, Vector2Int crossing)
    {
        if (!On) return float.MaxValue;
        Vector3 lp = Root.InverseTransformPoint(p);
        float d = float.MaxValue;
        if (crossing.x < 0) d = Mathf.Min(d, lp.x); else if (crossing.x > 0) d = Mathf.Min(d, L - lp.x);
        if (crossing.y < 0) d = Mathf.Min(d, lp.z); else if (crossing.y > 0) d = Mathf.Min(d, L - lp.z);
        return d;
    }

    // p trasladado al otro lado del borde por los ejes de 'crossing' (queda 'inset' m dentro del lado opuesto)
    public static Vector3 CrossEdge(Vector3 p, Vector2Int crossing, float inset)
    {
        if (!On) return p;
        Transform r = Root;
        Vector3 lp = r.InverseTransformPoint(p);
        if (crossing.x < 0) lp.x = L - inset; else if (crossing.x > 0) lp.x = inset;
        if (crossing.y < 0) lp.z = L - inset; else if (crossing.y > 0) lp.z = inset;
        lp.y = FieldHeight.H(lp.x, lp.z);
        return r.TransformPoint(lp);
    }
}
