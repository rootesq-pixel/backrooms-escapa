using UnityEngine;

// v7.0.1: monstruo imitador. A veces (según la semilla, igual para todos en MP) un monstruo aparece disfrazado de jugador:
// mismo modelo y traje, linterna encendida y, en multijugador, el nombre de un compañero (elegido por la semilla).
// Pistas: se mueve a tirones, no hace ruido al pisar, se queda mirando, su linterna no ilumina y la etiqueta falla.
// Si te acercas o lo miras fijamente demasiado tiempo, se transforma y ataca (persecución + susto normales).
// Autoridad: un jugador solo / el anfitrión decide; los clientes reciben el estado "disfrazado" en el byte de estado del monstruo.
public class MimicDisguise : MonoBehaviour
{
    public const float Chance = 0.30f;           // probabilidad por nivel (niveles 1+)
    public const float RevealDistance = 3.2f, StareSeconds = 2.2f, StareDot = 0.965f, StareRange = 22f, MaxDisguise = 50f, HoldDistance = 7f;
    public const int NetBit = 0x40;

    public MonsterAI ai;
    public RemotePlayer puppet;
    public bool Disguised { get; private set; }
    public float Stare { get; private set; }
    public float DisguiseTime { get; private set; }
    public int CopiedId { get; private set; }
    public string CopiedName { get; private set; }
    public int Reveals { get; private set; }
    public static MimicDisguise Active;

    // ¿este nivel tiene imitador? (determinista: misma respuesta en anfitrión y clientes)
    public static bool LevelHasMimic(int seed, int level)
    {
        if (level < 1) return false;
        if (ForceForTests >= 0) return ForceForTests == 1;
        uint h = Hash((uint)seed * 2654435761u ^ (uint)(level * 40503 + 977));
        return (h % 1000u) < (uint)(Chance * 1000f);
    }
    public static int ForceForTests = -1;        // pruebas: 1 siempre, 0 nunca, -1 según la semilla

    public static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }

    // a quién copia: en MP un jugador elegido por la semilla entre los ids ordenados; solo, el propio jugador (sin etiqueta)
    public static void PickCopy(int seed, int level, out int id, out string name)
    {
        id = -1; name = "";
        NetSession ns = NetSession.Instance;
        if (!NetSession.Active || ns == null || ns.Players.Count == 0) return;
        System.Collections.Generic.List<NetPlayer> ps = new System.Collections.Generic.List<NetPlayer>(ns.Players);
        ps.Sort((a, b) => a.id.CompareTo(b.id));
        NetPlayer p = ps[(int)(Hash((uint)seed ^ 0x9e3779b9u ^ (uint)level) % (uint)ps.Count)];
        id = p.id; name = p.name;
    }

    public static MimicDisguise Attach(MonsterAI ai, int seed, int level, StepKind step)
    {
        MimicDisguise d = ai.gameObject.AddComponent<MimicDisguise>();
        d.ai = ai;
        PickCopy(seed, level, out int id, out string nm);
        d.CopiedId = id; d.CopiedName = nm;
        d.puppet = RemotePlayer.CreateMimic(ai.transform.parent, id < 0 ? 7 : id, nm, step);
        d.puppet.ShowTag = id >= 0;
        d.jitterSeed = (int)(Hash((uint)seed + 31u) & 0xffff);
        d.SetDisguised(true);
        ai.mimic = d;
        Active = d;
        return d;
    }

    private int jitterSeed;
    private float nextPush, glitchT;

    public void SetDisguised(bool on)
    {
        if (Disguised == on) return;
        bool wasDisguised = Disguised;
        Disguised = on;
        if (ai != null && ai.visual != null)
            foreach (Renderer r in ai.visual.GetComponentsInChildren<Renderer>(true))
                if (puppet == null || !r.transform.IsChildOf(puppet.transform)) r.enabled = !on;
        if (puppet != null) { puppet.gameObject.SetActive(on); if (on) puppet.RestartAnimator(); }
        if (!on && wasDisguised)
        {
            Reveals++;
            if (ai != null && ai.visual != null) ai.visual.OnChaseStart();   // chillido de transformación
        }
        if (on) { Stare = 0f; DisguiseTime = 0f; }
    }

    // anfitrión / solo: ¿lo están mirando fijamente?
    public bool Watched(Transform viewer)
    {
        if (viewer == null) return false;
        Vector3 eye, fwd;
        FirstPersonController f = viewer.GetComponent<FirstPersonController>();
        Camera cam = f != null ? f.GetComponentInChildren<Camera>() : null;
        if (cam != null) { eye = cam.transform.position; fwd = cam.transform.forward; }
        else { eye = viewer.position + Vector3.up * 1.6f; fwd = viewer.forward; }
        Vector3 to = transform.position + Vector3.up * 1.5f - eye;
        float d = to.magnitude;
        if (d > StareRange || d < 0.01f) return false;
        if (Vector3.Dot(fwd, to / d) <= StareDot) return false;
        return d < 6f || !Physics.Linecast(eye, eye + to * 0.9f, ~0, QueryTriggerInteraction.Ignore);
    }

    // anfitrión / solo: devuelve true si se ha descubierto este fotograma
    public bool Tick(Transform target, float dt)
    {
        if (!Disguised) return false;
        DisguiseTime += dt;
        Vector3 to = target.position - transform.position; to.y = 0f;
        bool watched = Watched(target);
        Stare = watched ? Stare + dt : Mathf.Max(0f, Stare - dt * 0.5f);
        if (to.magnitude < RevealDistance || Stare >= StareSeconds || DisguiseTime > MaxDisguise) { SetDisguised(false); return true; }
        return false;
    }

    private void LateUpdate()
    {
        if (!Disguised || puppet == null) return;
        // movimiento a tirones: la copia solo se actualiza a saltos irregulares
        float t = Time.time;
        if (t >= nextPush)
        {
            float speed = ai != null ? ai.VisSpeed : 0f;
            puppet.Push(transform.position, transform.eulerAngles.y + Mathf.Sin(t * 7.3f + jitterSeed) * 6f, -4f, Mathf.Min(speed, 2.2f), RemotePlayer.FlagFlash);
            nextPush = t + 0.09f + Mathf.Abs(Mathf.Sin(t * 3.1f + jitterSeed)) * 0.22f;
        }
        glitchT += Time.deltaTime;
    }

    private void OnDestroy() { if (Active == this) Active = null; if (puppet != null) Destroy(puppet.gameObject); }
}
