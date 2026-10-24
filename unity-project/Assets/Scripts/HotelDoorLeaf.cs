using UnityEngine;

// v7.5.0: hoja de puerta del hotel que se abre sola al acercarse alguien (jugador local, jugadores remotos o un monstruo) y se
// vuelve a cerrar al rato. Sin sonido (nada de crujidos). Al depender solo de las posiciones que ya ven todos, en multijugador
// cada cliente la ve igual sin mensajes de red. Cerrada tiene colisión; en cuanto empieza a abrirse deja de tenerla.
public class HotelDoorLeaf : MonoBehaviour
{
    public Vector3 center;          // centro del hueco (mundo)
    public float openAngle = 96f;   // grados (signo = hacia dentro de la habitación)
    public float radius = 1.9f, holdTime = 3.5f, speed = 3.2f;
    public Collider blocker;
    public float Angle { get; private set; }
    public bool IsOpen { get { return Mathf.Abs(Angle) > 30f; } }
    public static int OpenCount;
    float target, hold, tick;
    Quaternion closedRot;

    void Start() { closedRot = transform.localRotation; tick = Random.value * 0.1f; }

    void Update()
    {
        float dt = Time.deltaTime;
        tick -= dt;
        if (tick <= 0f)
        {
            tick = 0.1f;
            if (Near()) hold = holdTime;
        }
        hold -= dt;
        target = hold > 0f ? openAngle : 0f;
        float prev = Angle;
        Angle = Mathf.MoveTowards(Angle, target, Mathf.Abs(openAngle) * speed * dt * (target == 0f ? 0.45f : 1f));
        if (Angle != prev) transform.localRotation = closedRot * Quaternion.Euler(0f, Angle, 0f);
        if (blocker != null) blocker.enabled = Mathf.Abs(Angle) < 4f && target == 0f;
        bool o = Mathf.Abs(Angle) > 30f, po = Mathf.Abs(prev) > 30f;
        if (o && !po) OpenCount++;
    }

    bool Near()
    {
        float r2 = radius * radius;
        FirstPersonController p = FirstPersonController.LocalInstance;
        if (p != null && Flat(p.transform.position) < r2) return true;
        foreach (RemotePlayer rp in RemotePlayer.All) if (rp != null && Flat(rp.transform.position) < r2) return true;
        foreach (MonsterAI m in MonsterAI.Instances) if (m != null && Flat(m.transform.position) < r2) return true;
        return false;
    }
    float Flat(Vector3 q) { float dx = q.x - center.x, dz = q.z - center.z, dy = q.y - center.y; return dy > 3f || dy < -3f ? 1e9f : dx * dx + dz * dz; }

    public void ForceOpen(float seconds) { hold = seconds; }   // capturas / pruebas
}
