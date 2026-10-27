using UnityEngine;

// v7.9.2: postura sentada en el todoterreno (cuerpo propio visto desde fuera y avatares remotos): sobre la animación de reposo
// se doblan las caderas y las rodillas (muslos al frente, espinillas hacia abajo) para que las piernas no salgan por el suelo.
public static class SeatedLegs
{
    public const float Hip = 82f, Knee = 86f;
    public static void Apply(Transform model, Transform basis)
    {
        if (model == null || basis == null) return;
        Vector3 right = basis.right;
        foreach (string s in new[] { "L", "R" })
        {
            Transform up = Find(model, "UpperLeg." + s), lo = Find(model, "LowerLeg." + s);
            if (up != null) up.rotation = Quaternion.AngleAxis(-Hip, right) * up.rotation;
            if (lo != null) lo.rotation = Quaternion.AngleAxis(Knee, right) * lo.rotation;
        }
    }
    private static Transform Find(Transform r, string n)
    {
        if (r.name == n) return r;
        for (int i = 0; i < r.childCount; i++) { Transform t = Find(r.GetChild(i), n); if (t != null) return t; }
        return null;
    }
}
