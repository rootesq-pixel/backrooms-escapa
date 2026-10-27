using UnityEngine;

// v7.9.2: postura sentada en el todoterreno (cuerpo propio visto desde fuera y avatares remotos): sobre la animación de reposo
// se doblan las caderas y las rodillas (muslos al frente, espinillas hacia abajo) para que las piernas no salgan por el suelo.
public static class SeatedLegs
{
    public const float Hip = 82f, Knee = 35f;   // v7.9.3: piernas hacia los pedales (con 86° y la cadera en el cojín las botas salían por el suelo)
    // v7.9.3 (t461u): el traje (cabeza grande) no cabía bajo el techo: antes se hundía el cuerpo hasta que la cabeza quedaba a
    // 1.40 m y la cadera acababa en el suelo del coche (solo asomaba el casco por la ventanilla, sin hombros: «una bola amarilla»).
    // Ahora se sienta en el cojín (cadera a SeatHipY sobre los pies del asiento) y se encoge un poco (SeatScale) para que
    // hombros, brazos y casco se vean por las ventanillas y la capucha quede bajo el techo.
    public const float SeatHipY = 0.66f, SeatScale = 0.8f;   // cadera sobre el suelo de la cabina (0.48 m), casco en la ventanilla (1.35–1.91 m) con los hombros asomando
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
            Transform up = Find(model, "UpperLeg." + s), lo = Find(model, "LowerLeg." + s), ft = Find(model, "Foot." + s);
            // v7.9.3: en este esqueleto el pie NO cuelga de la espinilla: si solo se doblan cadera y rodilla, la bota se queda
            // donde estaba de pie y la malla se estira como una cinta (por eso el conductor «no parecía el personaje»).
            // Se guarda el pie respecto a la espinilla y se vuelve a colocar después de doblar.
            bool carry = ft != null && lo != null && !ft.IsChildOf(lo);
            Vector3 fp = carry ? lo.InverseTransformPoint(ft.position) : Vector3.zero;
            Quaternion fr = carry ? Quaternion.Inverse(lo.rotation) * ft.rotation : Quaternion.identity;
            if (up != null) up.rotation = Quaternion.AngleAxis(-Hip, right) * up.rotation;
            if (lo != null) lo.rotation = Quaternion.AngleAxis(Knee, right) * lo.rotation;
            if (carry) { ft.position = lo.TransformPoint(fp); ft.rotation = lo.rotation * fr; }
        }
    }
    private static Transform Find(Transform r, string n)
    {
        if (r.name == n) return r;
        for (int i = 0; i < r.childCount; i++) { Transform t = Find(r.GetChild(i), n); if (t != null) return t; }
        return null;
    }
}
