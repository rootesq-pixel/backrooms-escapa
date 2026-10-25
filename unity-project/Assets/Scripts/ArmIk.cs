using UnityEngine;

// v7.9.0: IK analítico de dos huesos (hombro → codo → mano) para poner las manos en el volante del todoterreno
// (cuerpo en primera persona y avatares remotos). Devuelve la distancia que queda entre la mano y el objetivo.
public static class ArmIk
{
    public static float Solve(Transform upper, Transform lower, Transform hand, Vector3 target, Vector3 poleHint)
    {
        if (upper == null || lower == null || hand == null) return 99f;
        float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
        if (a < 0.01f || b < 0.01f) return 99f;
        Vector3 S = upper.position, toT = target - S;
        float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(a - b) + 0.01f, (a + b) * 0.999f);
        Vector3 dir = toT.normalized;
        Vector3 perp = poleHint - Vector3.Dot(poleHint, dir) * dir;
        if (perp.sqrMagnitude < 1e-4f) perp = Vector3.down; perp.Normalize();
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f), sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 E = S + dir * (a * cosA) + perp * (a * sinA);
        upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, E - S) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, (S + dir * d) - lower.position) * lower.rotation;
        return Vector3.Distance(hand.position, target);
    }
}
