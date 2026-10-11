using System.Collections.Generic;
using UnityEngine;

// Parte visual/sonora del monstruo: carga el modelo (Resources/Monsters/<id>), maneja el Animator
// (Idle/Walk/Run/Attack), ojos procedurales, temblor, "teleport-stutter" cerca del jugador, pasos, gruñidos y el grito
// del susto. Si el modelo no carga, se usa el monstruo procedural de primitivas (fallback).
public class MonsterVisual : MonoBehaviour
{
    public enum Anim { Idle, Walk, Run, Attack }

    public MonsterProfile profile;
    public Animator animator;
    public Transform model;           // hijo "Model" del prefab (lleva el Animator)
    public bool IsFallback;
    public Anim CurrentAnim { get; private set; }
    public Renderer[] Renderers { get; private set; }

    private AudioSource stepSrc, voiceSrc;
    private AudioClip stepClip, growlClip, screamClip;
    private Light eyeLight;
    private Material eyeMat;
    private float eyeLightBase;
    private Vector3 modelBasePos;
    private float stepAccum, growlTimer, stutterTimer, stutterOffset, stutterCooldown;
    private Transform player;
    private Flashlight playerFlash;
    private MonsterAI ai;
    private readonly List<Object> owned = new List<Object>();
    private bool chasing;

    // ------------------------------------------------------------------
    // Creación
    // ------------------------------------------------------------------

    // Carga el modelo del perfil bajo 'monsterGo'. Si falla, crea el cuerpo procedural. Nunca devuelve null.
    public static MonsterVisual Create(GameObject monsterGo, MonsterProfile p, Transform playerTransform)
    {
        MonsterVisual v = monsterGo.GetComponent<MonsterVisual>();
        if (v == null) v = monsterGo.AddComponent<MonsterVisual>();
        v.profile = p;
        v.player = playerTransform;
        if (playerTransform != null) v.playerFlash = playerTransform.GetComponentInChildren<Flashlight>();
        v.ai = monsterGo.GetComponent<MonsterAI>();

        GameObject prefab = null;
        try { prefab = Resources.Load<GameObject>("Monsters/" + p.id); }
        catch (System.Exception e) { Debug.LogWarning("[MonsterVisual] Error cargando " + p.id + ": " + e.Message); }

        bool ok = false;
        if (prefab != null)
        {
            try
            {
                GameObject inst = Instantiate(prefab);
                inst.name = "Visual_" + p.id;
                inst.transform.SetParent(monsterGo.transform, false);
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;
                v.model = inst.transform.Find("Model");
                v.animator = inst.GetComponentInChildren<Animator>();
                v.Renderers = inst.GetComponentsInChildren<Renderer>();
                ok = v.model != null && v.animator != null && v.Renderers.Length > 0 && v.animator.runtimeAnimatorController != null;
                if (!ok) Destroy(inst);
                else v.modelBasePos = v.model.localPosition;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MonsterVisual] Error instanciando " + p.id + ": " + e.Message);
                ok = false;
            }
        }

        if (!ok)
        {
            Debug.LogWarning("[MonsterVisual] Modelo '" + p.id + "' no disponible: se usa el monstruo procedural.");
            v.IsFallback = true;
            v.BuildProceduralBody();
        }
        else
        {
            v.animator.Play("Idle", 0, Random.value);
            v.animator.Update(0f);
            v.AttachEyesTo(v.model.parent);
        }

        v.SetupAudio();
        return v;
    }

    // Monstruo procedural (primitivas): el original de antes de los modelos
    private void BuildProceduralBody()
    {
        Material dark = ProceduralAssets.CreateMaterial(new Color(0.03f, 0.03f, 0.03f), null, Vector2.one, 0.1f);
        Material eyes = ProceduralAssets.CreateMaterial(Color.black, null, Vector2.one, 0f);
        ProceduralAssets.SetEmission(eyes, new Color(2.5f, 0.1f, 0.05f));
        owned.Add(dark); owned.Add(eyes);
        Transform m = transform;
        GameObject root = new GameObject("FallbackBody");
        root.transform.SetParent(m, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0f, 1.1f, 0f), new Vector3(0.55f, 1.1f, 0.55f), dark, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 2.3f, 0f), new Vector3(0.38f, 0.45f, 0.38f), dark, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Capsule, "ArmL", root.transform, new Vector3(-0.42f, 1.15f, 0f), new Vector3(0.12f, 0.75f, 0.12f), dark, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Capsule, "ArmR", root.transform, new Vector3(0.42f, 1.15f, 0f), new Vector3(0.12f, 0.75f, 0.12f), dark, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Sphere, "EyeL", root.transform, new Vector3(-0.09f, 2.33f, 0.17f), new Vector3(0.07f, 0.07f, 0.07f), eyes, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Sphere, "EyeR", root.transform, new Vector3(0.09f, 2.33f, 0.17f), new Vector3(0.07f, 0.07f, 0.07f), eyes, false);
        GameObject glow = new GameObject("EyeGlow");
        glow.transform.SetParent(root.transform, false);
        glow.transform.localPosition = new Vector3(0f, 2.3f, 0.4f);
        Light l = glow.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.1f, 0.05f); l.range = 4f; l.intensity = 0.7f; l.shadows = LightShadows.None;
        model = root.transform;
        modelBasePos = model.localPosition;
        Renderers = root.GetComponentsInChildren<Renderer>();
    }

    private void SetupAudio()
    {
        int seed = 500 + (profile != null ? profile.id.GetHashCode() & 0xff : 0);
        GrowlKind kind = profile != null ? profile.growl : GrowlKind.LowRumble;
        stepClip = ProceduralAudio.CreateMonsterStep(kind, seed);
        growlClip = ProceduralAudio.CreateGrowl(kind, seed + 1);
        screamClip = ProceduralAudio.CreateScream(kind, seed + 2);
        stepSrc = gameObject.AddComponent<AudioSource>();
        voiceSrc = gameObject.AddComponent<AudioSource>();
        foreach (AudioSource s in new[] { stepSrc, voiceSrc })
        {
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 2.5f;
            s.maxDistance = 30f;
            s.playOnAwake = false;
        }
        growlTimer = Random.Range(4f, 9f);
    }

    private void OnDestroy()
    {
        ProceduralAssets.SafeDestroy(stepClip);
        ProceduralAssets.SafeDestroy(growlClip);
        ProceduralAssets.SafeDestroy(screamClip);
        ProceduralAssets.SafeDestroy(splashClip);
        if (eyeMat != null) ProceduralAssets.SafeDestroy(eyeMat);
        for (int i = 0; i < owned.Count; i++) ProceduralAssets.SafeDestroy(owned[i]);
    }

    // ------------------------------------------------------------------
    // Ojos procedurales (se enganchan al hueso más cercano para seguir la animación)
    // ------------------------------------------------------------------

    // Versión estática para vista previa: devuelve el contenedor de ojos
    public static Transform AttachEyes(Transform root, MonsterProfile p)
    {
        MonsterVisual tmp = root.gameObject.AddComponent<MonsterVisual>();
        tmp.profile = p;
        Transform t = tmp.AttachEyesTo(root);
        if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp);
        return t;
    }

    private Transform AttachEyesTo(Transform visualRoot)
    {
        MonsterProfile p = profile;
        if (p == null || !p.eyes) return null;
        Transform existing = visualRoot.Find("Eyes");
        if (existing != null) { if (Application.isPlaying) Destroy(existing.gameObject); else DestroyImmediate(existing.gameObject); }

        float size = p.size;
        Vector3 mid = visualRoot.TransformPoint(new Vector3(0f, p.eyePos.y * size, p.eyePos.z * size));
        GameObject eyes = new GameObject("Eyes");
        eyes.transform.position = mid;
        eyes.transform.rotation = visualRoot.rotation;
        eyes.transform.SetParent(visualRoot, true);

        Material m = new Material(Shader.Find("Standard"));
        m.color = Color.black;
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", p.eyeColor * 3f);
        eyeMat = m;
        int count = p.eyeSpacing > 0.0001f ? 2 : 1;
        for (int i = 0; i < count; i++)
        {
            GameObject e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            e.name = "Eye" + i;
            if (Application.isPlaying) Destroy(e.GetComponent<Collider>()); else DestroyImmediate(e.GetComponent<Collider>());
            e.transform.SetParent(eyes.transform, false);
            float side = count == 1 ? 0f : (i == 0 ? -1f : 1f);
            e.transform.localPosition = new Vector3(side * p.eyeSpacing * size, 0f, 0f);
            e.transform.localScale = Vector3.one * p.eyeSize * size * 2f;
            Renderer r = e.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        GameObject glow = new GameObject("EyeGlow");
        glow.transform.SetParent(eyes.transform, false);
        glow.transform.localPosition = new Vector3(0f, 0f, 0.25f);
        Light l = glow.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = p.eyeColor;
        l.range = p.eyeLightRange;
        l.intensity = p.eyeLightIntensity;
        l.shadows = LightShadows.None;
        eyeLight = l;
        eyeLightBase = p.eyeLightIntensity;

        // Enganchar al hueso más cercano (posición mundial conservada)
        SkinnedMeshRenderer sk = visualRoot.GetComponentInChildren<SkinnedMeshRenderer>();
        if (sk != null && sk.bones != null && sk.bones.Length > 0)
        {
            Transform best = null; float bd = float.MaxValue;
            foreach (Transform b in sk.bones)
            {
                if (b == null) continue;
                float d = (b.position - mid).sqrMagnitude;
                if (d < bd) { bd = d; best = b; }
            }
            if (best != null) eyes.transform.SetParent(best, true);
        }
        return eyes.transform;
    }

    // ------------------------------------------------------------------
    // Animación y sonido (lo llama MonsterAI)
    // ------------------------------------------------------------------

    private MonsterAI owner;
    private AudioClip splashClip;

    public void SetLocomotion(MonsterAI.State state, float speed)
    {
        chasing = state == MonsterAI.State.Chase;
        Anim want;
        if (state == MonsterAI.State.Kill) want = Anim.Attack;
        else if (speed < 0.25f) want = Anim.Idle;
        else if (state == MonsterAI.State.Chase) want = Anim.Run;
        else want = Anim.Walk;
        if (want != CurrentAnim)
        {
            CurrentAnim = want;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.CrossFadeInFixedTime(want.ToString(), want == Anim.Attack ? 0.08f : 0.22f);
            }
        }

        // Pasos
        if (profile != null && profile.footsteps && speed > 0.3f && stepClip != null && state != MonsterAI.State.Kill)
        {
            stepAccum += speed * Time.deltaTime;
            float stride = chasing ? profile.strideChase : profile.stridePatrol;
            if (stepAccum >= stride)
            {
                stepAccum = 0f;
                stepSrc.pitch = profile.footstepPitch * Random.Range(0.92f, 1.08f);
                stepSrc.PlayOneShot(stepClip, AudioMix.S(profile.footstepVolume * (chasing ? 1.2f : 0.8f)));
                // en el agua: chapoteo audible + onda en la superficie
                if (owner == null) owner = GetComponent<MonsterAI>();
                WaterVolume wv = WaterVolume.Active;
                if (owner != null && wv != null && owner.WaterDepth > 0.04f)
                {
                    if (splashClip == null) splashClip = ProceduralAudio.CreateSplash();
                    stepSrc.pitch = profile.footstepPitch * Random.Range(0.75f, 1.0f);
                    stepSrc.PlayOneShot(splashClip, AudioMix.S(Mathf.Clamp01(0.45f + owner.WaterDepth) * (chasing ? 1f : 0.7f)));
                    wv.Splash(transform.position, chasing ? 0.7f : 0.4f, false);
                }
            }
        }
    }

    public void OnChaseStart()
    {
        PlayGrowl(1f);
        growlTimer = Random.Range(4f, 7f);
    }

    public void PlayGrowl(float volume)
    {
        if (growlClip == null || voiceSrc == null) return;
        voiceSrc.pitch = profile.growlPitch * Random.Range(0.93f, 1.07f);
        voiceSrc.PlayOneShot(growlClip, AudioMix.S(volume));
    }

    // Susto al ser atrapado: grito 2D fuerte
    public void OnKill()
    {
        if (Screamer.Enabled) return;   // el Screamer reproduce su propio grito en capas
        if (screamClip != null && voiceSrc != null)
        {
            voiceSrc.spatialBlend = 0.2f;
            voiceSrc.pitch = profile.growlPitch;
            voiceSrc.PlayOneShot(screamClip, AudioMix.S(1f));
        }
    }

    // ------------------------------------------------------------------
    // Efectos de terror: temblor, stutter, gruñidos aleatorios, ojos
    // ------------------------------------------------------------------
    private void LateUpdate()
    {
        if (profile == null || model == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float distance = player != null ? Vector3.Distance(player.position, transform.position) : 99f;
        bool lit = false;
        if (playerFlash != null && playerFlash.IsOn && player != null && Camera.main != null)
        {
            Vector3 to = transform.position + Vector3.up * 1.0f - Camera.main.transform.position;
            lit = to.magnitude < playerFlash.range && Vector3.Angle(Camera.main.transform.forward, to) < playerFlash.spotAngle * 0.5f;
        }

        // Gruñidos aleatorios
        growlTimer -= dt;
        if (growlTimer <= 0f && ai != null && ai.CurrentState != MonsterAI.State.Kill)
        {
            growlTimer = chasing ? Random.Range(4f, 8f) : Random.Range(9f, 18f);
            PlayGrowl(chasing ? 0.9f : 0.55f);
        }

        Vector3 off = Vector3.zero;
        if (profile.jitter > 0f && (chasing || distance < 10f) && CurrentAnim != Anim.Attack)
        {
            float k = profile.jitter * (chasing ? 1f : 0.5f);
            off += new Vector3(Random.Range(-1f, 1f), Random.Range(-0.3f, 0.3f), Random.Range(-1f, 1f)) * 0.018f * k;
        }

        // "Teleport-stutter": parones breves con salto hacia delante cuando está cerca del jugador
        if (stutterTimer > 0f)
        {
            stutterTimer -= dt;
            off += transform.forward * stutterOffset;
            if (animator != null) animator.speed = 0f;
            if (stutterTimer <= 0f) { if (animator != null) animator.speed = 1f; stutterOffset = 0f; stutterCooldown = Random.Range(0.8f, 2.2f); }
        }
        else
        {
            stutterCooldown -= dt;
            if (profile.stutterChance > 0f && stutterCooldown <= 0f && distance < 14f && distance > 2.2f && CurrentAnim != Anim.Idle && CurrentAnim != Anim.Attack)
            {
                float chance = profile.stutterChance * (lit && profile.lightReactive ? 3f : 1f) * (chasing ? 1.5f : 0.7f);
                if (Random.value < chance * dt)
                {
                    stutterTimer = Random.Range(0.06f, 0.16f);
                    stutterOffset = Random.Range(0.15f, 0.55f);
                }
            }
        }
        // El modelo se desplaza en espacio del monstruo; basePos es el offset de los pies
        model.localPosition = modelBasePos + transform.InverseTransformVector(off);

        // Ojos: parpadeo y reacción a la linterna
        if (eyeMat != null)
        {
            float n = 0.75f + 0.5f * Mathf.PerlinNoise(Time.time * 7f, 3.3f);
            float lightK = (lit && profile.lightReactive) ? 0.35f : 1.2f;
            eyeMat.SetColor("_EmissionColor", profile.eyeColor * 3f * n * lightK);
            if (eyeLight != null) eyeLight.intensity = eyeLightBase * n * lightK;
        }
    }
}
