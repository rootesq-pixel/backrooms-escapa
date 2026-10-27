using UnityEngine;

// v7.9.0: cámara del todoterreno (C en el coche): 0 desde el asiento (la de siempre), 1 por detrás (persecución, se gira con el
// ratón) y 2 sobre el capó. Solo local. Va después de todo (la cámara del jugador la coloca el controlador en Update), suave,
// y nunca se mete en las dunas ni atraviesa rocas o casas (rayo desde el coche + altura de la arena).
[DefaultExecutionOrder(1000)]
public class DesertCarCam : MonoBehaviour
{
    public const float ChaseDist = 7.0f, ChaseHeight = 2.7f, MinAboveSand = 0.7f;
    public static int AppliedView { get; private set; }
    public static float LastClearance { get; private set; }
    private Vector3 pos, vel, lastCar; private bool has;
    // v7.9.4 (t470u): el coche salta de golpe al cruzar el borde del desierto (toroidal, 512 m), al reaparecer o al recolocarlo;
    // la cámara lo seguía volando por el mapa (SmoothDamp, 80 m/s) como una cámara libre. Ahora se mueve con el salto.
    public const float JumpDist = 12f, SnapDist = 20f;
    public static bool DebugOldFollow;   // solo pruebas: el seguimiento de la v7.9.3 (para medir el antes)
    public static int Jumps { get; private set; }
    public static float MaxCarDist; public static int Frames, OutFrames;   // medida de las pruebas (se pone a 0 desde fuera)
    private Camera cam;
    private void OnDisable() { PlayerBody.ExternalView = false; }

    private void LateUpdate()
    {
        DesertCars dc = DesertCars.Active; FirstPersonController fpc = GameManager.Instance != null ? GameManager.Instance.player : null;
        DesertCar car = dc != null ? dc.LocalCar(out int _) : null;
        int view = car != null && fpc != null && fpc.IsMounted && fpc.cameraPivot != null ? DesertCars.CamView : 0;
        if (cam == null && fpc != null && fpc.cameraPivot != null) cam = fpc.cameraPivot.GetComponent<Camera>();
        if (view == 0)
        {
            PlayerBody.ExternalView = false; has = false; AppliedView = 0; return;
        }
        // v7.9.2: desde fuera se ve al conductor (antes se ocultaba la capa del cuerpo y el asiento quedaba vacío)
        PlayerBody.ExternalView = true;
        Transform ct = fpc.cameraPivot, carT = car.transform;
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        Vector3 desired; Quaternion rot;
        if (view == 1)
        {
            float yaw = fpc.transform.eulerAngles.y;   // el ratón gira la vista alrededor del coche
            Vector3 back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            Vector3 focus = carT.position + Vector3.up * 1.4f;
            desired = focus + back * ChaseDist + Vector3.up * (ChaseHeight - 1.4f);
            // sin atravesar nada: rayo desde el coche hacia la cámara (ignora el propio coche)
            Vector3 dir = desired - focus; float len = dir.magnitude; dir /= Mathf.Max(1e-4f, len);
            float best = len;
            foreach (RaycastHit h in Physics.SphereCastAll(focus, 0.3f, dir, len, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(carT) && !(h.collider is CharacterController) && h.distance < best) best = h.distance;
            desired = focus + dir * Mathf.Max(1.5f, best - 0.2f);
            desired = AboveSand(desired);
            if (!has) { pos = desired; vel = Vector3.zero; has = true; lastCar = carT.position; }
            if (!DebugOldFollow)
            {
                Vector3 jump = carT.position - lastCar;
                if (jump.magnitude > JumpDist) { pos += jump; Jumps++; }      // salto del coche (borde, reaparición): la cámara salta con él
                if ((pos - desired).magnitude > SnapDist) { pos = desired; vel = Vector3.zero; }
            }
            lastCar = carT.position;
            pos = Vector3.SmoothDamp(pos, desired, ref vel, 0.12f, 80f, dt);
            pos = AboveSand(pos);
            rot = Quaternion.LookRotation((focus + Vector3.up * 0.3f) - pos, Vector3.up);
        }
        else
        {
            desired = carT.TransformPoint(new Vector3(0f, 1.62f, 1.05f));
            pos = desired; has = false;
            rot = Quaternion.LookRotation(carT.forward, carT.up) * Quaternion.Euler(4f, 0f, 0f);
        }
        ct.position = pos; ct.rotation = rot;
        AppliedView = view;
        if (view == 1)
        {
            Vector3 f = carT.position + Vector3.up * 1.0f; MaxCarDist = Mathf.Max(MaxCarDist, Vector3.Distance(pos, f)); Frames++;
            Vector3 d = Quaternion.Inverse(rot) * (f - pos);   // ¿el coche en la imagen? (cono de ±25°)
            if (d.z <= 0f || Mathf.Abs(d.x) > d.z * 0.47f || Mathf.Abs(d.y) > d.z * 0.47f) OutFrames++;
        }
    }

    private static Vector3 AboveSand(Vector3 world)
    {
        DesertWorld dw = DesertWorld.Active; if (dw == null) return world;
        Vector3 l = dw.levelRoot.InverseTransformPoint(world);
        float h = DesertHeight.H(Mathf.Repeat(l.x, dw.L), Mathf.Repeat(l.z, dw.L));
        LastClearance = l.y - h;
        if (l.y < h + MinAboveSand) { l.y = h + MinAboveSand; world = dw.levelRoot.TransformPoint(l); LastClearance = MinAboveSand; }
        return world;
    }
}
