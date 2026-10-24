// v7.4.0: prueba de entrada a las casas del Campo infinito (nivel 5), compartida por la prueba en modo juego (SeedReachCheck)
// y por las capturas del ejecutable (ShotBuildCheck). Pone al jugador DE PIE delante de la puerta de una casa abierta, lo hace
// andar hacia dentro con la entrada de depuración (sin agacharse), se mueve por dentro y comprueba que se puede poner de pie.
// Monstruos: camino por el NavMesh de fuera de la puerta al interior.
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public static class HouseEntryProbe
{
    public struct Result
    {
        public bool done, entered, neverCrouched, standsInside, movedInside, navOk, hasNav;
        public float depth, moved, minHeight, navLen; public string navWhy; public Vector3 enterPos;
        public string Describe()
        {
            return "entra " + entered + " (" + depth.ToString("F2") + " m dentro), sin agacharse " + neverCrouched + " (altura mín. " + minHeight.ToString("F2") +
                " m), se mueve dentro " + movedInside + " (" + moved.ToString("F1") + " m), de pie dentro " + standsInside +
                (hasNav ? ", monstruos (NavMesh) " + navOk + " (" + navLen.ToString("F1") + " m" + (navOk ? "" : "; " + navWhy) + ")" : ", sin NavMesh");
        }
        public bool Ok(bool needNav) { return entered && neverCrouched && standsInside && movedInside && (!needNav || !hasNav || navOk); }
    }

    public static Transform HouseTransform(FieldWorld fw, int k)
    {
        Transform hr = fw != null ? fw.transform.Find("Houses") : null;
        if (hr == null) return null;
        foreach (Transform t in hr) if (t.name.StartsWith("House_" + k + "_")) return t;
        return null;
    }

    static float Ground(Vector3 p)
    {   // fuera de la casa: la superficie más alta (terreno o escalón; encima no hay nada a 2,3 m de la puerta)
        float best = -1e9f;
        foreach (RaycastHit h in Physics.RaycastAll(p + Vector3.up * 6f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.GetComponentInParent<FirstPersonController>() == null) best = Mathf.Max(best, h.point.y);
        return best > -1e8f ? best : p.y;
    }

    public static bool StandFree(FirstPersonController pw, Vector3 feet)
    {
        CharacterController cc = pw.GetComponent<CharacterController>();
        float r = cc.radius * 0.95f;
        foreach (Collider c in Physics.OverlapCapsule(feet + Vector3.up * (r + 0.05f), feet + Vector3.up * (pw.standHeight - r), r, ~0, QueryTriggerInteraction.Ignore))
            if (c != cc && !c.transform.IsChildOf(pw.transform)) return false;
        return true;
    }

    static float PathLen(Vector3 a, Vector3 b, out string why)
    {
        NavMeshPath path = new NavMeshPath(); why = "";
        bool sa = NavMesh.SamplePosition(a, out NavMeshHit ha, 1.5f, NavMesh.AllAreas), sb = NavMesh.SamplePosition(b, out NavMeshHit hb, 1.0f, NavMesh.AllAreas);
        if (!sa || !sb) { why = "muestra fuera " + sa + " dentro " + sb; return -1f; }
        bool ok = NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path);
        if (!ok || path.status != NavMeshPathStatus.PathComplete) { why = "camino " + path.status + " fin " + (path.corners.Length > 0 ? (path.corners[path.corners.Length - 1] - hb.position).ToString("F1") : "-") + " de " + ha.position.ToString("F1") + " a " + hb.position.ToString("F1"); return -1f; }
        float s = 0f; for (int i = 1; i < path.corners.Length; i++) s += Vector3.Distance(path.corners[i - 1], path.corners[i]); return s;
    }

    // diagnóstico: NavMesh a lo largo del eje de la puerta (de 2,4 m fuera a 2 m dentro), altura respecto al suelo de la casa o '.'
    static string NavProfile(Transform house, float dx, float D)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        float fy = house.TransformPoint(new Vector3(0f, 0.15f, 0f)).y;
        for (float z = D * 0.5f + 2.4f; z > D * 0.5f - 2f; z -= 0.2f)
        {
            Vector3 q = house.TransformPoint(new Vector3(dx, 0.15f, z));
            bool hit = NavMesh.SamplePosition(q, out NavMeshHit nh, 0.9f, NavMesh.AllAreas) && new Vector2(nh.position.x - q.x, nh.position.z - q.z).magnitude < 0.12f;
            sb.Append(hit ? (nh.position.y - fy).ToString("F1") : ".").Append(Mathf.Abs(z - D * 0.5f) < 0.11f ? "|" : " ");
        }
        return sb.ToString();
    }

    // res[0] recibe el resultado. Deja al jugador dentro de la casa (debugInput apagado al terminar).
    public static IEnumerator Run(FirstPersonController pw, Transform house, FieldPlan.House h, Result[] res)
    {
        Result r = new Result { neverCrouched = true, minHeight = 99f };
        Vector2 sz = FieldPlan.TemplateSize[h.template]; float dx = FieldWorld.DoorX(h.template), D = sz.y;
        Vector3 outL = new Vector3(dx, 0f, D * 0.5f + 2.3f);
        Vector3 outW = house.TransformPoint(outL); outW.y = Ground(outW) + 0.05f;
        Vector3 fwdIn = -house.forward;
        int steps = 0; foreach (Transform ch in house) if (ch.name.StartsWith("Step")) steps++;
        CharacterController cc = pw.GetComponent<CharacterController>();
        if (UIManager.Instance != null && UIManager.InputBlocked) UIManager.Instance.HideAll();   // menú principal abierto en las pruebas
        Time.timeScale = 1f; pw.InputEnabled = true;
        pw.debugInput = true; pw.debugCrouch = false; pw.debugSprint = false; pw.debugJump = false; pw.debugMove = Vector2.zero;
        pw.Teleport(outW, Mathf.Atan2(fwdIn.x, fwdIn.z) * Mathf.Rad2Deg);
        for (int i = 0; i < 20; i++) yield return null;
        yield return new WaitForSeconds(0.3f);
        // hacia dentro: hasta 1,6 m pasada la puerta (o 5 s)
        float t0 = Time.time; pw.debugMove = new Vector2(0f, 1f);
        while (Time.time - t0 < 5f)
        {
            if (pw.IsCrouching) r.neverCrouched = false;
            r.minHeight = Mathf.Min(r.minHeight, cc.height);
            float z = house.InverseTransformPoint(pw.transform.position).z;
            r.depth = D * 0.5f - 0.2f - z;
            if (r.depth > 1.6f) break;
            yield return null;
        }
        pw.debugMove = Vector2.zero; r.enterPos = pw.transform.position;
        r.entered = r.depth > 1.0f;
        // por dentro: a los lados y hacia el fondo
        Vector3 last = pw.transform.position; float moved = 0f;
        if (r.entered)
            foreach (Vector2 mv in new[] { new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f) })
            {
                pw.debugMove = mv; float t1 = Time.time;
                while (Time.time - t1 < 0.6f)
                {
                    yield return null;
                    if (pw.IsCrouching) r.neverCrouched = false;
                    r.minHeight = Mathf.Min(r.minHeight, cc.height);
                    Vector3 p = pw.transform.position; moved += Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(last.x, 0f, last.z)); last = p;
                }
            }
        pw.debugMove = Vector2.zero; r.moved = moved; r.movedInside = r.entered && moved > 1.5f;
        // agacharse y volver a ponerse de pie dentro
        pw.debugCrouch = true; yield return new WaitForSeconds(0.5f); pw.debugCrouch = false; yield return new WaitForSeconds(0.7f);
        Vector3 lp = house.InverseTransformPoint(pw.transform.position);
        r.standsInside = r.entered && !pw.IsCrouching && cc.height > pw.standHeight - 0.02f && StandFree(pw, pw.transform.position) && lp.z < D * 0.5f - 0.3f;
        // monstruos: NavMesh de fuera de la puerta al interior
        r.hasNav = NavMesh.SamplePosition(outW, out NavMeshHit _, 3f, NavMesh.AllAreas);
        if (r.hasNav)
        {
            Vector3 inW = house.TransformPoint(new Vector3(dx, 0.15f, D * 0.5f - 1.4f));   // suelo de la casa (y local 0,15)
            r.navWhy = "";
            r.navLen = PathLen(outW, inW, out r.navWhy); r.navOk = r.navLen > 0f && r.navLen < 10f; if (!r.navOk) r.navWhy += "; perfil " + NavProfile(house, dx, D) + "; escalones " + steps + ", subida " + (house.position.y + 0.15f - outW.y + 0.05f).ToString("F2") + " m";
        }
        pw.debugInput = false;
        r.done = true; res[0] = r;
    }
}
