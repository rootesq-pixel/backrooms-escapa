using System.Collections.Generic;
using UnityEngine;

// v7.3.0: jaula metálica colgada de la grúa al final del muelle. Sólo baja si hay alguien dentro y TODOS los que están dentro
// llevan el casco de buzo. Cierra la puerta (3 s), baja a 2,5 m/s hasta el fondo de la fosa y abre allí la puerta.
// Vacía durante 8 s en el fondo, vuelve a subir. Durante el movimiento los de dentro van con ella: pueden andar y mirar
// dentro de la jaula, pero no salir.
// Autoridad: anfitrión (estado y altura en el snapshot).
[DefaultExecutionOrder(-40)]
public class OceanCage : MonoBehaviour
{
    public enum CageState { TopOpen = 0, Closing = 1, Descending = 2, BottomOpen = 3, Ascending = 4 }
    public const float W = 2.4f, H = 2.6f, CloseTime = 3f, DownSpeed = 2.5f, UpSpeed = 3f, ReturnDelay = 8f;

    public OceanWorld world;
    public CageState State { get; private set; }
    public float Y { get; private set; }                 // altura del suelo de la jaula
    public float topY, bottomY;
    public int Descents { get; private set; }
    public bool LocalInside { get; private set; }
    public float stateT;
    private float emptyT;
    private Transform door, cable, floorT;
    private Collider doorCol;
    private AudioSource src;
    private AudioClip clank, winch;
    private readonly HashSet<int> riders = new HashSet<int>();
    private bool localMounted;
    public string LastRefusal { get; private set; }

    public static OceanCage Create(OceanWorld w, System.Action<Object> track)
    {
        GameObject g = new GameObject("Cage"); g.transform.SetParent(w.root, false);
        OceanCage c = g.AddComponent<OceanCage>();
        c.world = w; c.topY = OceanWorld.CageTop.y; c.bottomY = w.CageBottom.y; c.Y = c.topY;
        c.Build(track);
        c.Place();
        return c;
    }

    private void Build(System.Action<Object> track)
    {
        Material m = world.MetalMat;
        MeshBuilder mb = new MeshBuilder(1);
        float hw = W * 0.5f;
        OceanGeo.Box(mb, 0, new Vector3(-hw - 0.05f, -0.14f, -hw - 0.05f), new Vector3(hw + 0.05f, 0f, hw + 0.05f), 0.8f);
        OceanGeo.Box(mb, 0, new Vector3(-hw - 0.05f, H, -hw - 0.05f), new Vector3(hw + 0.05f, H + 0.1f, hw + 0.05f), 0.8f);
        foreach (Vector2 c in new[] { new Vector2(-hw, -hw), new Vector2(hw, -hw), new Vector2(-hw, hw), new Vector2(hw, hw) })
            OceanGeo.Box(mb, 0, new Vector3(c.x - 0.05f, 0f, c.y - 0.05f), new Vector3(c.x + 0.05f, H, c.y + 0.05f), 0.5f);
        // barrotes en tres lados (+x, ±z); la puerta está en -x (hacia el muelle y, abajo, hacia el acantilado)
        for (float a = -hw + 0.2f; a < hw - 0.1f; a += 0.2f)
        {
            OceanGeo.Tube(mb, 0, new Vector3(hw, 0f, a), new Vector3(hw, H, a), 0.018f, 5, 1f);
            OceanGeo.Tube(mb, 0, new Vector3(a, 0f, -hw), new Vector3(a, H, -hw), 0.018f, 5, 1f);
            OceanGeo.Tube(mb, 0, new Vector3(a, 0f, hw), new Vector3(a, H, hw), 0.018f, 5, 1f);
        }
        foreach (float y in new[] { 0.9f, 1.8f })
        {
            OceanGeo.Box(mb, 0, new Vector3(hw - 0.03f, y, -hw), new Vector3(hw + 0.03f, y + 0.05f, hw), 0.5f);
            OceanGeo.Box(mb, 0, new Vector3(-hw, y, -hw - 0.03f), new Vector3(hw, y + 0.05f, -hw + 0.03f), 0.5f);
            OceanGeo.Box(mb, 0, new Vector3(-hw, y, hw - 0.03f), new Vector3(hw, y + 0.05f, hw + 0.03f), 0.5f);
        }
        // cadenas al gancho
        foreach (Vector2 c in new[] { new Vector2(-hw, -hw), new Vector2(hw, -hw), new Vector2(-hw, hw), new Vector2(hw, hw) })
            OceanGeo.Tube(mb, 0, new Vector3(c.x, H + 0.1f, c.y), new Vector3(0f, H + 1.2f, 0f), 0.02f, 4, 1f);
        GameObject body = OceanGeo.Emit(mb, "Body", transform, new[] { m }, false, true, track);
        floorT = body.transform;
        // colliders: suelo, techo y tres paredes
        AddBox(new Vector3(0f, -0.07f, 0f), new Vector3(W + 0.1f, 0.14f, W + 0.1f));
        AddBox(new Vector3(0f, H + 0.05f, 0f), new Vector3(W + 0.1f, 0.1f, W + 0.1f));
        AddBox(new Vector3(hw, H * 0.5f, 0f), new Vector3(0.08f, H, W));
        AddBox(new Vector3(0f, H * 0.5f, -hw), new Vector3(W, H, 0.08f));
        AddBox(new Vector3(0f, H * 0.5f, hw), new Vector3(W, H, 0.08f));
        // puerta corredera (sube)
        door = new GameObject("Door").transform; door.SetParent(transform, false); door.localPosition = new Vector3(-hw, 0f, 0f);
        MeshBuilder db = new MeshBuilder(1);
        for (float a = -hw + 0.2f; a < hw - 0.1f; a += 0.2f) OceanGeo.Tube(db, 0, new Vector3(0f, 0.02f, a), new Vector3(0f, H - 0.05f, a), 0.018f, 5, 1f);
        foreach (float y in new[] { 0.05f, 1.2f, H - 0.12f }) OceanGeo.Box(db, 0, new Vector3(-0.03f, y, -hw), new Vector3(0.03f, y + 0.06f, hw), 0.5f);
        OceanGeo.Emit(db, "Bars", door, new[] { m }, false, true, track);
        BoxCollider dc = door.gameObject.AddComponent<BoxCollider>(); dc.center = new Vector3(0f, H * 0.5f, 0f); dc.size = new Vector3(0.08f, H, W);
        doorCol = dc;
        // cable hasta la grúa
        cable = new GameObject("Cable").transform; cable.SetParent(world.root, false);
        MeshBuilder cb = new MeshBuilder(1);
        OceanGeo.Tube(cb, 0, Vector3.zero, Vector3.up, 0.03f, 5, 1f);
        OceanGeo.Emit(cb, "Mesh", cable, new[] { world.DarkMat }, false, false, track);
        // luz de trabajo en la jaula (ayuda a ver la puerta abajo)
        GameObject lg = new GameObject("CageLamp"); lg.transform.SetParent(transform, false); lg.transform.localPosition = new Vector3(0.9f, H - 0.15f, 0.9f);
        Light l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.86f, 0.62f); l.intensity = 1.1f; l.range = 7f; l.shadows = LightShadows.None;
        src = gameObject.AddComponent<AudioSource>(); src.spatialBlend = 1f; src.minDistance = 2f; src.maxDistance = 40f; src.rolloffMode = AudioRolloffMode.Linear; src.dopplerLevel = 0f;
        clank = OceanAudio.ClankClip(); winch = OceanAudio.WinchClip();
        src.clip = winch; src.loop = true; src.volume = 0f;
        if (Application.isPlaying) src.Play();
    }

    private void AddBox(Vector3 c, Vector3 s)
    {
        BoxCollider b = gameObject.AddComponent<BoxCollider>(); b.center = c; b.size = s;
    }

    private void Place()
    {
        Vector3 cg = OceanWorld.CageTop;
        transform.position = new Vector3(cg.x, Y, cg.z);
        float doorOpen = State == CageState.TopOpen || State == CageState.BottomOpen ? 1f : (State == CageState.Closing ? 1f - Mathf.Clamp01(stateT / (CloseTime * 0.6f)) : 0f);
        if (State == CageState.BottomOpen) doorOpen = Mathf.Clamp01(stateT / 1.5f);
        door.localPosition = new Vector3(-W * 0.5f, doorOpen * (H - 0.25f), 0f);
        if (doorCol != null) doorCol.enabled = doorOpen < 0.6f;
        float topHook = OceanWorld.PierY + 5.6f, bot = Y + H + 1.2f;
        cable.position = new Vector3(cg.x, bot, cg.z);
        cable.localScale = new Vector3(1f, Mathf.Max(0.05f, topHook - bot), 1f);
    }

    public bool Inside(Vector3 feet)
    {
        Vector3 c = transform.position;
        return Mathf.Abs(feet.x - c.x) < W * 0.5f - 0.05f && Mathf.Abs(feet.z - c.z) < W * 0.5f - 0.05f && feet.y > c.y - 0.6f && feet.y < c.y + H - 0.4f;
    }

    public string PromptFor(bool helmet)
    {
        switch (State)
        {
            case CageState.TopOpen: return helmet ? "La jaula bajará en cuanto todos los de dentro lleven casco" : "La jaula no baja: todos los de dentro deben llevar el casco de buzo";
            case CageState.Closing: return "La puerta se cierra...";
            case CageState.Descending: return "Bajando... " + Mathf.RoundToInt(Mathf.Max(0f, -Y)) + " m";
            case CageState.BottomOpen: return "Fondo. Sal por la puerta";
            default: return null;
        }
    }

    // estado recibido del anfitrión
    public void ApplyNet(int st, float y, float t)
    {
        if ((CageState)st != State && (CageState)st == CageState.BottomOpen) PlayClank();
        if ((CageState)st != State && (CageState)st == CageState.Closing) PlayClank();
        State = (CageState)st; Y = y; stateT = t;
    }

    private void PlayClank() { if (src != null && clank != null) src.PlayOneShot(clank, 0.7f * AudioMix.Sfx); }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (OceanNet.Authority) Simulate(dt);
        else stateT += dt;
        Place();
        // jugador local: dentro / sujeto durante el movimiento
        OceanPlayer lp = OceanPlayer.Local;
        FirstPersonController fpc = lp != null ? lp.fpc : null;
        LocalInside = fpc != null && !fpc.IsMounted && Inside(fpc.transform.position);
        bool moving = State == CageState.Closing || State == CageState.Descending || State == CageState.Ascending;
        if (fpc != null)
        {
            if (moving && (LocalInside || (localMounted && fpc.mount == transform)))
            {
                if (!localMounted || fpc.mount != transform)
                {
                    Vector3 local = transform.InverseTransformPoint(fpc.transform.position);
                    local.x = Mathf.Clamp(local.x, -W * 0.5f + 0.4f, W * 0.5f - 0.4f); local.z = Mathf.Clamp(local.z, -W * 0.5f + 0.4f, W * 0.5f - 0.4f); local.y = 0.02f;
                    fpc.Mount(transform, local); localMounted = true;
                    // v7.3.0: dentro de la jaula se puede andar y mirar mientras baja (los barrotes y la puerta cerrada te retienen)
                    float m = W * 0.5f - 0.4f; fpc.mountWalk = true; fpc.mountMin = new Vector2(-m, -m); fpc.mountMax = new Vector2(m, m);
                }
                LocalInside = true;
            }
            else if (localMounted)
            {
                localMounted = false;
                if (fpc.mount == transform) fpc.Dismount(transform.TransformPoint(fpc.mountLocal) + Vector3.up * 0.05f);
            }
        }
        if (src != null) src.volume = Mathf.Lerp(src.volume, (State == CageState.Descending || State == CageState.Ascending ? 0.3f : 0f) * AudioMix.Sfx, 1f - Mathf.Exp(-4f * dt));
    }

    // ---- autoridad
    private void Simulate(float dt)
    {
        stateT += dt;
        List<OceanNet.Target> ts = OceanNet.Targets();
        int inside = 0; bool allHelmet = true;
        foreach (OceanNet.Target t in ts)
        {
            if (t.downed) continue;
            bool mountedHere = t.local ? (OceanPlayer.Local != null && OceanPlayer.Local.fpc.mount == transform) : riders.Contains(t.id);
            if (Inside(t.pos) || mountedHere) { inside++; if (!t.helmet) allHelmet = false; }
        }
        switch (State)
        {
            case CageState.TopOpen:
                Y = topY;
                if (inside > 0 && allHelmet && stateT > 1.2f) Set(CageState.Closing);
                else if (inside > 0 && !allHelmet) LastRefusal = "sin casco";
                break;
            case CageState.Closing:
                Y = topY;
                if (inside == 0 || !allHelmet) { Set(CageState.TopOpen); break; }
                if (stateT >= CloseTime) { Set(CageState.Descending); Descents++; riders.Clear(); foreach (OceanNet.Target t in ts) if (!t.downed && Inside(t.pos)) riders.Add(t.id); }
                break;
            case CageState.Descending:
                Y = Mathf.MoveTowards(Y, bottomY, DownSpeed * dt);
                if (Y <= bottomY + 0.001f) { Set(CageState.BottomOpen); riders.Clear(); }
                break;
            case CageState.BottomOpen:
                Y = bottomY;
                if (inside == 0) emptyT += dt; else emptyT = 0f;
                if (emptyT > ReturnDelay) { Set(CageState.Ascending); emptyT = 0f; }
                break;
            case CageState.Ascending:
                Y = Mathf.MoveTowards(Y, topY, UpSpeed * dt);
                if (Y >= topY - 0.001f) Set(CageState.TopOpen);
                break;
        }
    }

    private void Set(CageState s)
    {
        State = s; stateT = 0f;
        if (s == CageState.Closing || s == CageState.BottomOpen || s == CageState.TopOpen) PlayClank();
    }

    public bool IsRiderHost(int id) { return riders.Contains(id); }
    public void ForceStateForTests(CageState s, float y) { State = s; Y = y; stateT = 0f; }
}
