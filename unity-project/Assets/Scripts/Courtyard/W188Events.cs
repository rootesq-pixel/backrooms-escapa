using UnityEngine;
using UnityEngine.UI;

// v7.12.0 Nivel 188 — lo que pasa en el patio (todo local de cada jugador; nada viaja por la red):
//   · la Ventana: las siluetas hacen señas desde su cristal; si te acercas a su puerta (entornada) se giran hacia ti y se oyen
//     golpes contra el cristal. Si entras en el cuarto, te atrapa (muerte; en multijugador el anfitrión te derriba como siempre).
//   · los Periféricos: una sombra quieta que solo existe en el rabillo del ojo. Si la miras, ya no está. Inofensivos.
//   · las cortinas: en las habitaciones que se abren, mantener clic izquierdo descorre la cortina… y desapareces de ese cuarto.
//   · puertas lejanas que se cierran en otras plantas. Sin siseos de aire ni crujidos de madera.
public class W188Events : MonoBehaviour
{
    public static W188Events Active { get; private set; }
    public static bool DisableForTests, NoGrabForTests;
    public const string GrabCause = "ventana", GrabText = "LA VENTANA TE HA ATRAPADO";
    public const float GrabDepth = W188Plan.RoomEnd - 0.55f;   // cruzar más de ~0,55 m dentro del cuarto
    public const float CurtainHold = 1.0f;

    BackroomsGenerator gen; LevelConfig cfg; Transform player; Camera cam; FirstPersonController fpc;
    W188World w;
    System.Random rng;
    AudioSource knockSrc, farSrc, stingSrc; AudioClip knockClip, doorClip, stingClip;
    float farT, knockT;
    // periféricos
    Transform peri; float periT, periLife; bool periOn;
    public int Peripherals { get; private set; }
    public int PeripheralsVanishedByLook { get; private set; }
    public int Knocks { get; private set; }
    public int FarDoors { get; private set; }
    // la Ventana
    public bool Grabbing { get; private set; }
    public int Grabs { get; private set; }
    float grabT; W188World.Figure grabFig; bool warned;
    // cortinas
    public string Prompt { get; private set; }
    public float CurtainHold01 { get; private set; }
    public int CurtainVanishes { get; private set; }
    public bool debugHold;
    float holdT; W188World.Curtain holdC; float vanishFx;
    public Vector3 LastVanishTo { get; private set; }
    Text promptText; GameObject hud;

    public static W188Events Create(Transform parent, BackroomsGenerator gen, LevelConfig cfg, Transform player, Camera cam)
    {
        GameObject g = new GameObject("W188Events"); g.transform.SetParent(parent, false);
        W188Events e = g.AddComponent<W188Events>();
        e.gen = gen; e.cfg = cfg; e.player = player; e.cam = cam; e.w = W188World.Active;
        e.fpc = player != null ? player.GetComponent<FirstPersonController>() : null;
        e.rng = new System.Random((gen != null ? gen.UsedSeed : 0) * 613 + (int)(System.DateTime.Now.Ticks & 0xffff));
        e.knockClip = CorridorEvents.CreateNoise(2); e.knockClip.name = "W188_GlassKnock";
        e.doorClip = CorridorEvents.CreateNoise(1); e.doorClip.name = "W188_FarDoor";
        e.stingClip = CreateSting(); e.stingClip.name = "W188_Grab";
        e.knockSrc = e.MakeSrc("Knock", 2f, 14f, 2400f);
        e.farSrc = e.MakeSrc("FarDoor", 8f, 45f, 1500f);
        e.stingSrc = e.MakeSrc("Sting", 1f, 10f, 6000f); e.stingSrc.spatialBlend = 0f;
        e.farT = 25f + (float)e.rng.NextDouble() * 20f; e.periT = 30f + (float)e.rng.NextDouble() * 25f;
        e.peri = new GameObject("Peripheral").transform; e.peri.SetParent(g.transform, false);
        if (e.w != null) W188World.BuildSilhouette(e.peri, e.w.FigureMat, false);
        e.peri.gameObject.SetActive(false);
        e.BuildHud();
        Active = e;
        return e;
    }

    AudioSource MakeSrc(string n, float min, float max, float cutoff)
    {
        GameObject s = new GameObject(n); s.transform.SetParent(transform, false);
        AudioSource a = s.AddComponent<AudioSource>();
        a.playOnAwake = false; a.loop = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = min; a.maxDistance = max; a.dopplerLevel = 0f; a.spread = 0f;
        AudioLowPassFilter lp = s.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = cutoff;
        AudioReverbFilter rv = s.AddComponent<AudioReverbFilter>(); rv.reverbPreset = AudioReverbPreset.Hallway;
        return a;
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        foreach (AudioClip c in new[] { knockClip, doorClip, stingClip }) if (c != null) ProceduralAssets.SafeDestroy(c);
        if (hud != null) Destroy(hud);
    }

    // cielo nublado del patio (el mismo HDRI CC0 del campo, más apagado) y luz ambiente inicial (W188World la ajusta)
    public static void ApplySky(LevelConfig cfg, System.Collections.Generic.List<Object> owned)
    {
        Shader sh = Shader.Find("Backrooms/FieldSky");
        Texture2D hdr = Resources.Load<Texture2D>("Field/sky_overcast");
        if (sh != null)
        {
            Material sky = new Material(sh) { name = "W188Sky" };
            if (hdr != null) sky.SetTexture("_Tex", hdr);
            sky.SetColor("_HazeCol", cfg.fog);
            sky.SetFloat("_Exposure", 0.78f);
            if (owned != null) owned.Add(sky);
            RenderSettings.skybox = sky;
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = W188World.SkyAmb; RenderSettings.ambientEquatorColor = W188World.EqAmb; RenderSettings.ambientGroundColor = W188World.GroundAmb;
        RenderSettings.sun = null;
        Shader.SetGlobalFloat("_GradeWarm", 0.75f);
        Shader.SetGlobalFloat("_GradeContrast", 1.2f);
        Shader.SetGlobalFloat("_GradeExposure", 0.95f);
    }

    void BuildHud()
    {
        hud = new GameObject("W188Hud");
        Canvas c = hud.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 40;
        CanvasScaler cs = hud.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920f, 1080f); cs.matchWidthOrHeight = 0.5f;
        GameObject g = new GameObject("Text"); g.transform.SetParent(hud.transform, false);
        RectTransform rt = g.AddComponent<RectTransform>();
        promptText = g.AddComponent<Text>(); promptText.font = ProceduralAssets.GetDefaultFont(); promptText.fontSize = 30;
        promptText.color = new Color(0.95f, 0.95f, 0.92f, 0.95f); promptText.raycastTarget = false; promptText.alignment = TextAnchor.LowerCenter;
        promptText.horizontalOverflow = HorizontalWrapMode.Wrap; promptText.verticalOverflow = VerticalWrapMode.Truncate;
        Outline o = g.AddComponent<Outline>(); o.effectColor = new Color(0f, 0f, 0f, 0.7f); o.effectDistance = new Vector2(1.5f, -1.5f);
        HudBottom.Place(rt, HudBottom.AboveToast, HudBottom.PromptMaxWidth, HudBottom.PromptHeight);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (w == null) w = W188World.Active;
        GameManager gm = GameManager.Instance;
        bool playing = gm == null || gm.State == GameManager.GameState.Playing;
        bool downed = NetSession.Active && NetSession.Instance != null && NetSession.Instance.LocalDowned;
        Prompt = null;
        AnimateFigures(dt);
        AnimateCurtains(dt);
        if (w != null && player != null && playing && !downed)
        {
            TickGrab(dt, gm);
            if (!Grabbing) TickCurtain(dt);
            if (!DisableForTests) { TickKnocks(dt); TickFarDoors(dt); TickPeripheral(dt); }
            else if (periOn) HidePeri();
        }
        else { holdT = 0f; CurtainHold01 = 0f; if (periOn) HidePeri(); }
        if (vanishFx > 0f) { vanishFx -= dt; if (VhsEffect.Active != null) VhsEffect.Active.Spike(Mathf.Clamp01(vanishFx)); }
        bool hide = UIManager.InputBlocked || !playing;
        if (promptText != null) promptText.text = hide ? "" : (Prompt ?? "");
    }

    // ---------------- la Ventana ----------------
    void AnimateFigures(float dt)
    {
        if (w == null) return;
        Vector3 pp = player != null ? player.position : Vector3.zero;
        float t = Time.time;
        foreach (W188World.Figure f in w.Figures)
        {
            if (f.root == null) continue;
            // brazo: señas lentas («ven»)
            float k = Mathf.Sin(t * 2.1f + f.phase);
            if (f.arm != null) f.arm.localRotation = Quaternion.Euler(-120f - 35f * Mathf.Max(0f, k), 0f, 18f + 10f * k);
            if (Grabbing && f == grabFig) continue;
            // si alguien está en su puerta (misma planta), se gira despacio hacia ella; si no, mira al patio
            Vector3 dd = pp - f.doorWorld; float dy = pp.y - f.doorWorld.y; dd.y = 0f;
            bool near = player != null && dy > -1f && dy < 1.6f && dd.magnitude < 4.5f;
            float faceCourt = W188World.YawToCourt(f.s), faceDoor = W188World.YawFromCourt(f.s);
            float cur = f.root.localEulerAngles.y;
            float tgt = near ? faceDoor : faceCourt;
            f.root.localRotation = Quaternion.Euler(0f, Mathf.MoveTowardsAngle(cur, tgt, dt * (near ? 55f : 25f)), 0f);
        }
    }

    // ¿está el jugador dentro de un cuarto de la Ventana (más allá del umbral)?
    public W188World.Figure EntityRoomAt(Vector3 world, out float depthIn)
    {
        depthIn = 0f;
        if (w == null) return null;
        Vector3 lp = w.levelRoot.InverseTransformPoint(world);
        int pf = W188Plan.FloorOf(lp.y);
        foreach (W188World.Figure f in w.Figures)
        {
            if (f.f != pf) continue;
            Vector3 q = W188Plan.FromLevel(f.s, lp);
            float u0 = f.i * W188Plan.M;
            if (q.x < u0 || q.x > u0 + W188Plan.M || q.z > W188Plan.RoomEnd + 0.02f || q.z < 0f) continue;
            depthIn = W188Plan.RoomEnd - q.z;
            return f;
        }
        return null;
    }

    void TickGrab(float dt, GameManager gm)
    {
        if (Grabbing)
        {
            grabT += dt;
            if (VhsEffect.Active != null) VhsEffect.Active.Spike(0.6f + 0.4f * Mathf.Clamp01(grabT / 0.6f));
            if (grabFig != null && cam != null)
            {   // la silueta se te echa encima
                Vector3 f = cam.transform.forward; f.y = 0f; f.Normalize();
                Vector3 target = cam.transform.position + f * 0.55f; target.y = player.position.y;
                grabFig.root.position = Vector3.Lerp(grabFig.root.position, target, dt * 10f);
                grabFig.root.rotation = Quaternion.LookRotation(-f, Vector3.up);
            }
            if (grabT > 0.75f)
            {
                Grabbing = false;
                Health h = Health.Instance;
                if (NetSession.Active || gm == null) { if (h != null) h.Damage(1000f, GrabCause); }
                else gm.PlayerKilled(GrabText);
                ResetFigure(grabFig); grabFig = null;
            }
            return;
        }
        float depth;
        W188World.Figure fig = EntityRoomAt(player.position, out depth);
        if (fig == null) return;
        if (!warned && depth < 0.3f && gm != null) { warned = true; gm.ShowToast("Dentro no hay luz. Desde la ventana, algo te hace señas."); }
        if (depth > W188Plan.RoomEnd - GrabDepth && !NoGrabForTests && !Health.DisableForTests) StartGrab(fig);
    }

    public void StartGrab(W188World.Figure fig)
    {
        if (Grabbing || fig == null) return;
        Grabbing = true; grabT = 0f; grabFig = fig; Grabs++;
        if (stingSrc != null && stingClip != null) { stingSrc.clip = stingClip; stingSrc.volume = AudioMix.Event(1f); stingSrc.Play(); }
        Adrenaline.Scare(1f);
        AudioEventLog.Note("W188_Grab");
    }

    void ResetFigure(W188World.Figure f)
    {
        if (f == null || f.root == null || w == null) return;
        f.root.localPosition = W188Plan.ToLevel(f.s, W188Plan.UC(f.i), f.f * W188Plan.FH, 1.05f);
        f.root.localRotation = Quaternion.Euler(0f, W188Plan.SideYaw(f.s), 0f);
    }

    void TickKnocks(float dt)
    {
        knockT -= dt;
        if (knockT > 0f || knockSrc.isPlaying) return;
        Vector3 pp = player.position;
        W188World.Figure best = null; float bd = 9f;
        foreach (W188World.Figure f in w.Figures)
        {
            float dy = pp.y - f.doorWorld.y; if (dy < -1f || dy > 1.6f) continue;
            Vector3 d = pp - f.doorWorld; d.y = 0f;
            if (d.magnitude < bd) { bd = d.magnitude; best = f; }
        }
        if (best == null) { knockT = 1f; return; }
        knockT = 6.5f + (float)rng.NextDouble() * 4f;
        knockSrc.transform.position = w.levelRoot.TransformPoint(W188Plan.ToLevel(best.s, W188Plan.UC(best.i), best.f * W188Plan.FH + 1.4f, 0.4f));
        knockSrc.pitch = 1.25f + (float)rng.NextDouble() * 0.1f;   // nudillos contra cristal: más agudo que en una puerta
        knockSrc.clip = knockClip; knockSrc.volume = AudioMix.Event(0.9f); knockSrc.Play();
        Knocks++; AudioEventLog.Note(knockClip.name);
    }

    void TickFarDoors(float dt)
    {
        farT -= dt;
        if (farT > 0f) return;
        farT = 35f + (float)rng.NextDouble() * 40f;
        int s = rng.Next(4), f = rng.Next(W188Plan.Floors), i = rng.Next(W188Plan.Len(s));
        Vector3 p = w.DoorWorld(f, s, i, W188Plan.CorrIn + 0.3f) + Vector3.up * 1f;
        if ((p - player.position).magnitude < 12f) { farT = 5f; return; }
        farSrc.transform.position = p; farSrc.pitch = 0.95f + (float)rng.NextDouble() * 0.1f;
        farSrc.clip = doorClip; farSrc.volume = AudioMix.Event(0.85f); farSrc.Play();
        FarDoors++; AudioEventLog.Note(doorClip.name);
    }

    // ---------------- Periféricos ----------------
    void TickPeripheral(float dt)
    {
        if (cam == null) return;
        Vector3 eye = cam.transform.position;
        Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
        if (periOn)
        {
            periLife -= dt;
            Vector3 to = peri.position + Vector3.up * 1.3f - eye; Vector3 toF = to; toF.y = 0f;
            float ang = Vector3.Angle(fwd, toF);
            if (ang < 30f) { PeripheralsVanishedByLook++; HidePeri(); return; }   // la miras: ya no está
            if (Sanity.Instance != null) Sanity.Instance.Add(-0.35f * dt);
            if (periLife <= 0f) HidePeri();
            return;
        }
        periT -= dt;
        if (periT > 0f) return;
        periT = 4f;   // si no hay sitio, se vuelve a intentar enseguida
        if (Grabbing) return;
        float side = rng.Next(2) == 0 ? -1f : 1f, a = side * (58f + (float)rng.NextDouble() * 20f);
        Vector3 dir = Quaternion.Euler(0f, a, 0f) * fwd;
        float dist = 5.5f + (float)rng.NextDouble() * 6f;
        RaycastHit hit;
        if (Physics.Raycast(eye, dir, out hit, dist, ~0, QueryTriggerInteraction.Ignore)) dist = hit.distance - 0.6f;
        if (dist < 3.2f) return;
        Vector3 p = eye + dir * dist; p.y = player.position.y;
        peri.position = p; peri.rotation = Quaternion.LookRotation(-dir, Vector3.up);
        peri.gameObject.SetActive(true); periOn = true; periLife = 3.5f + (float)rng.NextDouble() * 2f;
        Peripherals++;
        periT = 28f + (float)rng.NextDouble() * 30f;
    }
    void HidePeri() { periOn = false; if (peri != null) peri.gameObject.SetActive(false); }
    public bool PeripheralVisible { get { return periOn; } }
    public void ForcePeripheral() { periT = 0f; }
    public Vector3 PeripheralPos { get { return peri != null ? peri.position : Vector3.zero; } }

    // ---------------- cortinas ----------------
    W188World.Curtain LookedCurtain()
    {
        if (cam == null) return null;
        Vector3 eye = cam.transform.position, f = cam.transform.forward;
        W188World.Curtain best = null; float bd = 1.9f;
        foreach (W188World.Curtain c in w.Curtains)
        {
            if (c.busy) continue;
            Vector3 to = c.center - eye;
            if (Mathf.Abs(to.y) > 1.6f) continue;
            float d = to.magnitude; if (d > bd) continue;
            if (Vector3.Dot(to.normalized, f) < 0.72f) continue;
            if (Vector3.Dot(-to, c.inward) < 0.05f) continue;   // desde dentro del cuarto
            best = c; bd = d;
        }
        return best;
    }

    void TickCurtain(float dt)
    {
        W188World.Curtain c = LookedCurtain();
        if (c == null) { holdT = 0f; CurtainHold01 = 0f; holdC = null; return; }
        if (c != holdC) { holdT = 0f; holdC = c; }
        bool hold = debugHold || (!UIManager.InputBlocked && Input.GetMouseButton(0));
        holdT = hold ? holdT + dt : 0f;
        CurtainHold01 = Mathf.Clamp01(holdT / CurtainHold);
        c.open = Mathf.Max(c.open, CurtainHold01 * 0.35f);
        Prompt = "Mantén clic izquierdo: descorrer la cortina" + (CurtainHold01 > 0.01f ? "  " + Mathf.RoundToInt(CurtainHold01 * 100f) + "%" : "");
        if (CurtainHold01 >= 1f) { holdT = 0f; CurtainHold01 = 0f; Vanish(c); }
    }

    // descorres la cortina y el cuarto «te traga»: apareces en un pasillo de otra planta
    void Vanish(W188World.Curtain c)
    {
        c.busy = true; c.open = 1f;
        int f; int s; float u; int guard = 0;
        do { f = rng.Next(W188Plan.Floors); s = rng.Next(4); guard++; } while (f == c.f && guard < 20);
        u = 1.0f + (float)rng.NextDouble() * (W188Plan.Len(s) * W188Plan.M - 2f);
        Vector3 p = w.levelRoot.TransformPoint(W188Plan.ToLevel(s, u, f * W188Plan.FH + 0.1f, (W188Plan.CorrIn + W188Plan.CorrOut) * 0.5f));
        Vector3 along = w.levelRoot.TransformDirection(W188World.LevelDir(s, rng.Next(2) == 0 ? 1f : -1f, 0f));
        if (fpc != null) fpc.Teleport(p, Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg);
        else player.position = p;
        LastVanishTo = p; CurtainVanishes++;
        vanishFx = 1.2f;
        if (Sanity.Instance != null) Sanity.Instance.Add(-6f);
        Adrenaline.Scare(0.5f);
        GameManager gm = GameManager.Instance;
        if (gm != null) gm.ShowToast(CurtainVanishes == 1 ? "Descorres la cortina… y ya no estás en ese cuarto." : "Otra vez en otra parte del edificio.");
        AudioEventLog.Note("W188_Vanish");
        StartCoroutine(CloseLater(c, 4f));
    }
    System.Collections.IEnumerator CloseLater(W188World.Curtain c, float t) { yield return new WaitForSeconds(t); c.busy = false; c.open = 0f; }

    void AnimateCurtains(float dt)
    {
        if (w == null) return;
        foreach (W188World.Curtain c in w.Curtains)
        {
            if (!c.busy && c != holdC) c.open = Mathf.MoveTowards(c.open, 0f, dt * 0.8f);
            float sx = 1f - 0.72f * Mathf.SmoothStep(0f, 1f, c.busy ? 1f : c.open);
            if (c.left != null && Mathf.Abs(c.left.localScale.x - sx) > 0.001f) { c.left.localScale = new Vector3(sx, 1f, 1f); c.right.localScale = new Vector3(sx, 1f, 1f); }
        }
    }

    // golpe grave + chirrido tonal de vidrio frotado, corto (sin ruido: ni siseo de aire ni crujidos)
    public static AudioClip CreateSting()
    {
        const int SR = 22050; int n = SR * 1; float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * 3.2f);
            float thud = Mathf.Sin(2f * Mathf.PI * (55f + 30f * Mathf.Exp(-t * 12f)) * t) * Mathf.Exp(-t * 7f);
            float squeal = Mathf.Sin(2f * Mathf.PI * (620f + 90f * Mathf.Sin(t * 23f)) * t) * 0.22f * Mathf.Clamp01(t * 8f) * env;
            d[i] = (thud * 0.9f + squeal) * 0.8f;
        }
        AudioClip c = AudioClip.Create("W188_Grab", n, 1, SR, false); c.SetData(d, 0);
        return c;
    }
}
