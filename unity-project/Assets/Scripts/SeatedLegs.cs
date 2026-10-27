using UnityEngine;

// v7.9.2: postura sentada en el todoterreno (cuerpo propio visto desde fuera y avatares remotos): sobre la animación de reposo
// se doblan las caderas y las rodillas (muslos al frente, espinillas hacia abajo) para que las piernas no salgan por el suelo.
public static class SeatedLegs
{
    public const float Hip = 82f, Knee = 86f;
    // v7.9.3 (t461u): el traje (cabeza grande) no cabía bajo el techo: antes se hundía el cuerpo hasta que la cabeza quedaba a
    // 1.40 m y la cadera acababa en el suelo del coche (solo asomaba el casco por la ventanilla, sin hombros: «una bola amarilla»).
    // Ahora se sienta en el cojín (cadera a SeatHipY sobre los pies del asiento) y se encoge un poco (SeatScale) para que
    // hombros, brazos y casco se vean por las ventanillas y la capucha quede bajo el techo.
    public const float SeatHipY = 0.53f, SeatScale = 0.85f;   // casco entero en la ventanilla (1.35–1.91 m) con los hombros asomando
    // sube/baja el cuerpo (moviendo «move») para que la cadera quede a SeatHipY sobre el punto del asiento
    public static void SitOnSeat(Transform hips, Vector3 seatPos, Vector3 up, Transform move)
    {
        if (hips == null || move == null) return;
        float dy = Vector3.Dot(seatPos + up * SeatHipY - hips.position, up);
        move.position += up * Mathf.Clamp(dy, -0.8f, 0.8f);
    }
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
