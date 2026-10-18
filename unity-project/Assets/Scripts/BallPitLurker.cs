using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// v7.1.0 El Sumergido: el monstruo del Parque de bolas se esconde bajo el fondo de las piscinas de bolas. Sumergido se arrastra
// despacio (se ve un bulto moviéndose entre las bolas y se oye el roce) y acecha a quien entra; si alguien se mete en la piscina
// o se acerca demasiado, sale de golpe entre una lluvia de bolas y persigue. Solo decide el anfitrión (o la partida local);
// los clientes ven la posición sincronizada y BallPitMonsterFx reproduce los efectos en todos.
public class BallPitLurker : MonoBehaviour
{
    public enum Phase { Roaming, Submerged, Emerging }
    public Phase Current { get; private set; }
    public int Emerges { get; private set; }
    public int Submerges { get; private set; }
    public static BallPitLurker Active { get; private set; }
    public static bool DisabledForTests;
    public const float SinkBelow = 2.35f, CrawlSpeed = 0.55f, StalkSpeed = 0.9f, EmergeTime = 1.1f;

    private MonsterAI ai; private NavMeshAgent agent;
    private float timer, submergedFor, nearTimer, emergeT;
    private BallPitField field; private int pit; private Vector3 wander; private float floorY, subY;
    private System.Random rng;
    private bool chaseOnEmerge;

    public static BallPitLurker Attach(MonsterAI ai, int seed)
    {
        BallPitLurker l = ai.gameObject.AddComponent<BallPitLurker>();
        l.ai = ai; l.agent = ai.GetComponent<NavMeshAgent>(); l.rng = new System.Random(seed * 41 + 3);
        l.timer = 3f;   // al empezar se esconde enseguida en una piscina
        Active = l;
        return l;
    }

    private void OnDestroy() { if (Active == this) Active = null; }

    private static bool Playing { get { return GameManager.Instance == null || GameManager.Instance.State == GameManager.GameState.Playing; } }

    // jugadores vivos (local + avatares remotos)
    private static readonly List<Transform> players = new List<Transform>();
    private static float nextScan;
    public static List<Transform> Players()
    {
        if (Time.time >= nextScan || players.Count == 0)
        {
            nextScan = Time.time + 1f; players.Clear();
            if (FirstPersonController.LocalInstance != null) players.Add(FirstPersonController.LocalInstance.transform);
            foreach (RemotePlayer rp in FindObjectsOfType<RemotePlayer>()) if (!rp.IsMimic && !rp.Downed) players.Add(rp.transform);
        }
        players.RemoveAll(t => t == null);
        return players;
    }

    private void Update()
    {
        if (ai == null || ai.netProxy || DisabledForTests || BallPitField.All.Count == 0) return;
        float dt = Time.deltaTime;
        switch (Current)
        {
            case Phase.Roaming: UpdateRoaming(dt); break;
            case Phase.Submerged: UpdateSubmerged(dt); break;
            case Phase.Emerging: UpdateEmerging(dt); break;
        }
    }

    private void UpdateRoaming(float dt)
    {
        if (!Playing || ai.IsStopped) return;
        if (ai.CurrentState != MonsterAI.State.Patrol) { timer = Mathf.Max(timer, 12f); return; }
        timer -= dt;
        if (timer > 0f) return;
        // nadie debe verlo desaparecer
        foreach (Transform p in Players())
        {
            Vector3 d = p.position - transform.position;
            if (d.magnitude < 12f || (d.magnitude < 30f && !Physics.Linecast(transform.position + Vector3.up * 1.6f, p.position + Vector3.up * 1.6f, ~0, QueryTriggerInteraction.Ignore)))
            { timer = 4f; return; }
        }
        // sala más cercana a algún jugador (pero sin aparecer pegado a él)
        BallPitField best = null; float bd = float.MaxValue;
        foreach (BallPitField f in BallPitField.All)
        {
            float md = float.MaxValue;
            foreach (Transform p in Players()) md = Mathf.Min(md, Vector3.Distance(p.position, f.transform.position));
            if (md < 10f) continue;
            float score = md + (float)rng.NextDouble() * 20f;
            if (score < bd) { bd = score; best = f; }
        }
        if (best == null) { timer = 5f; return; }
        Submerge(best, rng.NextDouble() < 0.75 ? 0 : 1);
    }

    public void Submerge(BallPitField f, int pitIndex)
    {
        field = f; pit = Mathf.Clamp(pitIndex, 0, f.pits.Length - 1);
        var p = f.pits[pit];
        Rect r = BallpitSetPiece.PitInner(p, f.rim);
        Vector3 lp = new Vector3(Mathf.Lerp(r.xMin + 0.8f, r.xMax - 0.8f, (float)rng.NextDouble()), 0f, Mathf.Lerp(r.yMin + 0.8f, r.yMax - 0.8f, (float)rng.NextDouble()));
        floorY = f.transform.TransformPoint(new Vector3(0f, p.floor, 0f)).y;
        subY = floorY - SinkBelow;
        ai.Stop();
        agent = ai.GetComponent<NavMeshAgent>();   // se añade al arrancar la navegación (puede no existir al crear el componente)
        if (agent != null) agent.enabled = false;
        Vector3 wp = f.transform.TransformPoint(lp); wp.y = subY;
        transform.position = wp;
        wander = wp; submergedFor = 0f; nearTimer = 0f;
        Current = Phase.Submerged; Submerges++;
    }

    private void UpdateSubmerged(float dt)
    {
        if (!ai.IsStopped) ai.Stop();   // otro sistema (ascensor, red) pudo reanudarlo: bajo las bolas no navega
        if (!Playing) return;
        submergedFor += dt;
        var p = field.pits[pit];
        Rect r = BallpitSetPiece.PitInner(p, field.rim);
        Transform near = null; float nd = float.MaxValue; bool wading = false;
        foreach (Transform t in Players())
        {
            Vector3 d = t.position - transform.position; d.y = 0f;
            float m = d.magnitude;
            if (m < nd) { nd = m; near = t; wading = field.BallDepthAt(t.position) > 0.05f; }
        }
        Vector3 target = wander;
        float speed = CrawlSpeed;
        if (near != null && wading) { target = near.position; speed = StalkSpeed; }
        else if ((wander - transform.position).sqrMagnitude < 0.09f || submergedFor < dt * 2f)
        {
            Vector3 lp = new Vector3(Mathf.Lerp(r.xMin + 0.6f, r.xMax - 0.6f, (float)rng.NextDouble()), 0f, Mathf.Lerp(r.yMin + 0.6f, r.yMax - 0.6f, (float)rng.NextDouble()));
            wander = field.transform.TransformPoint(lp);
        }
        target.y = subY;
        // dentro de la piscina siempre
        Vector3 tl = field.transform.InverseTransformPoint(target);
        tl.x = Mathf.Clamp(tl.x, r.xMin + 0.4f, r.xMax - 0.4f); tl.z = Mathf.Clamp(tl.z, r.yMin + 0.4f, r.yMax - 0.4f);
        target = field.transform.TransformPoint(tl); target.y = subY;
        Vector3 step = Vector3.MoveTowards(transform.position, target, speed * dt);
        Vector3 mv = step - transform.position; mv.y = 0f;
        if (mv.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(mv), 1f - Mathf.Exp(-3f * dt));
        transform.position = step;

        // salir: alguien vadea a menos de 2,2 m, o se queda junto al borde a menos de 2,6 m más de 1,2 s
        bool trigger = false;
        if (near != null && submergedFor > 2.5f)
        {
            if (wading && nd < 2.2f) trigger = true;
            else if (nd < 2.6f) { nearTimer += dt; if (nearTimer > 1.2f) trigger = true; }
            else nearTimer = Mathf.Max(0f, nearTimer - dt);
        }
        if (trigger) { BeginEmerge(true); return; }
        // nadie cerca durante mucho rato: sale en silencio a buscar (y volverá a esconderse)
        if (submergedFor > 75f && (near == null || nd > 25f)) BeginEmerge(false);
    }

    public void BeginEmerge(bool chase)
    {
        if (Current != Phase.Submerged) return;
        chaseOnEmerge = chase; emergeT = 0f; Current = Phase.Emerging; Emerges++;
    }

    private void UpdateEmerging(float dt)
    {
        emergeT += dt;
        float k = Mathf.Clamp01(emergeT / EmergeTime);
        float e = 1f - (1f - k) * (1f - k) * (1f - k);
        Vector3 pos = transform.position; pos.y = Mathf.Lerp(subY, floorY, e); transform.position = pos;
        Transform near = null; float nd = float.MaxValue;
        foreach (Transform t in Players()) { float m = Vector3.Distance(t.position, pos); if (m < nd) { nd = m; near = t; } }
        if (near != null) { Vector3 l = near.position - pos; l.y = 0f; if (l.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(l), 1f - Mathf.Exp(-6f * dt)); }
        if (k < 1f) return;
        agent = ai.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = true;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit h, 2f, NavMesh.AllAreas)) agent.Warp(h.position);
        }
        Current = Phase.Roaming;
        timer = 35f + (float)rng.NextDouble() * 30f;
        if (!Playing) return;
        ai.Resume();
        if (chaseOnEmerge && near != null && nd < 14f) ai.ChaseForTests();
    }

    public bool IsHidden { get { return Current == Phase.Submerged; } }
}

// Efectos en todos los equipos (anfitrión y clientes): detecta por la posición si el monstruo está bajo una piscina; apaga
// sus luces de ojos, suena el roce mientras se arrastra y, al salir, lluvia de bolas + estruendo de plástico
public class BallPitMonsterFx : MonoBehaviour
{
    private Light[] lights; private bool wasUnder; private AudioSource crawl, burst; private Vector3 lastPos;
    public static int Bursts { get; private set; }
    public bool Under { get; private set; }

    private void Start()
    {
        lights = GetComponentsInChildren<Light>(true);
        crawl = gameObject.AddComponent<AudioSource>();
        crawl.clip = BallPitField.RustleClip; crawl.loop = true; crawl.spatialBlend = 1f; crawl.minDistance = 1.5f; crawl.maxDistance = 16f;
        crawl.rolloffMode = AudioRolloffMode.Linear; crawl.volume = 0f; crawl.pitch = 0.75f; crawl.playOnAwake = false; crawl.dopplerLevel = 0f;
        burst = gameObject.AddComponent<AudioSource>();
        burst.spatialBlend = 1f; burst.minDistance = 3f; burst.maxDistance = 30f; burst.rolloffMode = AudioRolloffMode.Linear; burst.playOnAwake = false; burst.dopplerLevel = 0f;
        lastPos = transform.position;
    }

    private void Update()
    {
        Vector3 p = transform.position;
        float head = BallPitField.DepthAt(p + Vector3.up * 1.9f);
        Under = head > 0f && BallPitField.DepthAt(p + Vector3.up * 0.2f) > 0f && p.y < BallPitField.SurfaceY(p) - 1.2f;
        if (lights != null) foreach (Light l in lights) if (l != null) l.enabled = head <= 0f;   // ojos bajo las bolas: sin luz
        float spd = (p - lastPos).magnitude / Mathf.Max(Time.deltaTime, 1e-4f); lastPos = p;
        float want = Under ? Mathf.Clamp01(spd / 0.8f) * 0.55f * GameSettings.SfxVolume : 0f;
        crawl.volume = Mathf.MoveTowards(crawl.volume, want, Time.deltaTime * 2f);
        if (crawl.volume > 0.001f) { if (!crawl.isPlaying) crawl.Play(); } else if (crawl.isPlaying) crawl.Pause();
        // de sumergido a asomar: estallido
        if (wasUnder && !Under && BallPitField.DepthAt(p + Vector3.up * 0.1f) > 0f)
        {
            Bursts++;
            BallPitField.SpawnBurst(new Vector3(p.x, BallPitField.SurfaceY(p), p.z), 70);
            burst.pitch = 1.25f; burst.PlayOneShot(BallPitField.RustleClip, 1.0f * GameSettings.SfxVolume);
            burst.PlayOneShot(BallPitField.RustleClip, 0.8f * GameSettings.SfxVolume);
        }
        wasUnder = Under;
    }
}
