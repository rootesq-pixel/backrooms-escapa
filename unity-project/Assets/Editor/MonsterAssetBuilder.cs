using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Importación de modelos de monstruos y generación de prefabs en Assets/Resources/Monsters.
//   Unity -batchmode -nographics -quit -projectPath <p> -executeMethod MonsterAssetBuilder.BuildAll
public class MonsterModelPostprocessor : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (assetPath.StartsWith("Assets/Resources/Elevator/"))
        {
            // Cabina del ascensor (modelo estático CC0 convertido a FBX): sin animación ni cámaras/luces
            ModelImporter em = (ModelImporter)assetImporter;
            em.animationType = ModelImporterAnimationType.None;
            em.importAnimation = false;
            em.importCameras = false;
            em.importLights = false;
            em.isReadable = assetPath.EndsWith("Elevator740.fbx");   // v7.4.0: el frente del rellano reescribe sus UV en runtime (malla pequeña)
            em.meshCompression = ModelImporterMeshCompression.Off;
            em.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            em.materialLocation = ModelImporterMaterialLocation.InPrefab;
            return;
        }
        if (!assetPath.StartsWith("Assets/Monsters/Models/")) return;
        ModelImporter mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true;
        mi.animationCompression = ModelImporterAnimationCompression.Optimal;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.isReadable = false;
        mi.importCameras = false;
        mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
    }

    private void OnPostprocessModel(GameObject g) { }

    // Bucle en Idle/Walk/Run y limpieza de takes sobrantes
    private void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith("Assets/Monsters/Models/")) return;
        ModelImporter mi = (ModelImporter)assetImporter;
        ModelImporterClipAnimation[] clips = mi.defaultClipAnimations;
        List<ModelImporterClipAnimation> keep = new List<ModelImporterClipAnimation>();
        for (int i = 0; i < clips.Length; i++)
        {
            string n = Clean(clips[i].name);
            if (n.StartsWith("Take 001") || n == "Default Take") continue;
            clips[i].name = n;
            clips[i].loopTime = n.StartsWith("Idle") || n.StartsWith("Walk") || n.StartsWith("Run") || n.StartsWith("Duck");
            clips[i].loopPose = clips[i].loopTime;
            keep.Add(clips[i]);
        }
        if (keep.Count > 0) mi.clipAnimations = keep.ToArray();
    }

    public static string Clean(string name)
    {
        int bar = name.LastIndexOf('|');
        if (bar >= 0) name = name.Substring(bar + 1);
        return name;
    }

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Monsters/Textures/")) return;
        TextureImporter ti = (TextureImporter)assetImporter;
        string n = Path.GetFileNameWithoutExtension(assetPath);
        ti.maxTextureSize = 1024;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 4;
        if (n.EndsWith("_Normal")) ti.textureType = TextureImporterType.NormalMap;
        else if (n.EndsWith("_MetalSmooth") || n.EndsWith("_AO") || n.EndsWith("_Spec")) ti.sRGBTexture = false;
        ti.textureCompression = TextureImporterCompression.Compressed;
    }
}

public static class MonsterAssetBuilder
{
    private const string MatDir = "Assets/Monsters/Materials";
    private const string CtrlDir = "Assets/Monsters/Controllers";
    private const string PrefabDir = "Assets/Resources/Monsters";

    private static Texture2D Tex(string name)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Monsters/Textures/" + name + ".png");
    }

    private static string ModelPath(MonsterProfile p) { return "Assets/Monsters/Models/" + (string.IsNullOrEmpty(p.modelFile) ? p.id : p.modelFile) + ".fbx"; }

    [MenuItem("Backrooms/Generar prefabs de monstruos")]
    public static void BuildAll()
    {
        Directory.CreateDirectory(MatDir);
        Directory.CreateDirectory(CtrlDir);
        Directory.CreateDirectory(PrefabDir);
        AssetDatabase.Refresh();
        foreach (MonsterProfile p in MonsterProfile.All) AssetDatabase.ImportAsset(ModelPath(p), ImportAssetOptions.ForceUpdate);   // aplica avatar/ajustes del postprocesador
        foreach (MonsterProfile p in MonsterProfile.All)
        {
            try { BuildOne(p); }
            catch (System.Exception e) { Debug.LogError("[MonsterAssetBuilder] " + p.id + ": " + e); }
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MonsterAssetBuilder] OK");
    }

    // v7.1.0: solo un prefab (batchmode: -executeMethod MonsterAssetBuilder.BuildOnlyFromEnv, MONSTER_ID=PitLurker)
    public static void BuildOnlyFromEnv()
    {
        string id = System.Environment.GetEnvironmentVariable("MONSTER_ID");
        MonsterProfile p = MonsterProfile.ById(id);
        if (p == null) { Debug.LogError("[MonsterAssetBuilder] no existe " + id); EditorApplication.Exit(1); return; }
        Directory.CreateDirectory(MatDir); Directory.CreateDirectory(CtrlDir); Directory.CreateDirectory(PrefabDir);
        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(ModelPath(p), ImportAssetOptions.ForceUpdate);
        BuildOne(p);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log("[MonsterAssetBuilder] OK " + id);
        EditorApplication.Exit(0);
    }

    private static Material MakeMat(string name, Texture2D albedo, Texture2D normal, Texture2D metalSmooth, Texture2D ao, Color tint, float smoothness, Color emission)
    {
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = Shader.Find("Standard");
        m.color = tint;
        m.mainTexture = albedo;
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", smoothness >= 0f ? smoothness : 0.3f);
        if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
        if (metalSmooth != null && smoothness < 0f) { m.SetTexture("_MetallicGlossMap", metalSmooth); m.SetFloat("_GlossMapScale", 1f); m.EnableKeyword("_METALLICGLOSSMAP"); }
        if (ao != null) { m.SetTexture("_OcclusionMap", ao); m.SetFloat("_OcclusionStrength", 1f); }
        if (emission.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); }
        EditorUtility.SetDirty(m);
        return m;
    }

    // Material por nombre de slot de la malla
    private static Material MaterialFor(MonsterProfile p, string slotName)
    {
        string s = slotName.ToLowerInvariant();
        switch (p.id)
        {
            case "CityZombie":
                if (s.Contains("outfit"))
                    return MakeMat("CityZombie_Outfit", Tex("City_Outfit_Albedo"), Tex("City_Outfit_Normal"), null, Tex("City_Outfit_AO"), new Color(0.30f, 0.27f, 0.25f), 0.12f, Color.black);
                return MakeMat("CityZombie_Body", Tex("City_Body_Albedo"), Tex("City_Body_Normal"), null, Tex("City_Body_AO"), new Color(0.62f, 0.66f, 0.58f), 0.3f, Color.black);
            case "DrownedZombie":
                if (s.Contains("outfit"))
                    return MakeMat("DrownedZombie_Outfit", Tex("Drowned_Outfit_Albedo"), Tex("City_Outfit_Normal"), null, Tex("City_Outfit_AO"), new Color(0.8f, 0.9f, 0.9f), 0.5f, Color.black);
                return MakeMat("DrownedZombie_Body", Tex("Drowned_Body_Albedo"), Tex("City_Body_Normal"), null, Tex("City_Body_AO"), p.tint, p.smoothness, Color.black);
            case "HorrorStalker":
                return MakeMat("HorrorStalker", Tex("Stalker_Albedo"), Tex("Stalker_Normal"), null, null, p.tint, p.smoothness, Color.black);
            case "FieldWatcher":
                return MakeMat("FieldWatcher", Tex("Watcher_Albedo"), Tex("Stalker_Normal"), null, null, p.tint, p.smoothness, Color.black);
            case "GiantMutant":
                return MakeMat("GiantMutant", Tex("Giant_Albedo"), null, null, null, p.tint, p.smoothness, Color.black);
            case "PitLurker":
                return MakeMat("PitLurker", Tex("Sunk_Albedo"), Tex("Soot_Normal"), null, null, p.tint, p.smoothness, Color.black);
            case "SootHorror":
                return MakeMat("SootHorror", Tex("Soot_Albedo"), Tex("Soot_Normal"), null, null, p.tint, p.smoothness, Color.black);
        }
        return MakeMat(p.id + "_Default", null, null, null, null, p.tint, 0.3f, Color.black);
    }

    private static AnimationClip FindClip(Object[] assets, string key)
    {
        AnimationClip best = null;
        foreach (Object o in assets)
        {
            AnimationClip c = o as AnimationClip;
            if (c == null || c.name.StartsWith("__preview__")) continue;
            string n = MonsterModelPostprocessor.Clean(c.name);
            if (n == key) return c;
            if (best == null && n.StartsWith(key)) best = c;
        }
        return best;
    }

    private static void BuildOne(MonsterProfile p)
    {
        string path = ModelPath(p);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null) throw new System.Exception("Modelo no encontrado: " + path);
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);

        GameObject root = new GameObject(p.id);
        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inst.name = "Model";
        inst.transform.SetParent(root.transform, false);
        inst.transform.localRotation = Quaternion.Euler(0f, p.yaw, 0f);

        // Materiales
        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>())
        {
            Material[] src = r.sharedMaterials;
            Material[] dst = new Material[src.Length];
            for (int i = 0; i < src.Length; i++) dst[i] = MaterialFor(p, src[i] != null ? src[i].name : "");
            r.sharedMaterials = dst;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            SkinnedMeshRenderer sk = r as SkinnedMeshRenderer;
            if (sk != null) sk.updateWhenOffscreen = true;
        }

        // Normaliza la escala y apoya los pies en y = 0, midiendo con la pose de reposo ANIMADA (Idle), no la pose de enlace
        Bounds b = IdleBounds(model, p, assets);
        float measure = p.fit == FitMode.Height ? b.size.y : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        float k = measure > 0.0001f ? p.size / measure : 1f;
        inst.transform.localScale = new Vector3(k * p.stretch.x, k * p.stretch.y, k * p.stretch.z);
        Vector3 center = Quaternion.Euler(0f, p.yaw, 0f) * new Vector3(b.center.x * k * p.stretch.x, 0f, b.center.z * k * p.stretch.z);
        inst.transform.localPosition = new Vector3(-center.x, -b.min.y * k * p.stretch.y + p.hover, -center.z);
        Debug.Log("[MonsterAssetBuilder] " + p.id + " medida(idle)=" + b.size + " centro=" + b.center + " escala=" + k);

        // Animator + controlador
        AnimatorController ctrl = BuildController(p, assets);
        Animator anim = inst.GetComponent<Animator>();
        if (anim == null) anim = inst.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        if (anim.avatar == null)
        {
            foreach (Object o in assets) { Avatar av = o as Avatar; if (av != null && av.isValid && !av.isHuman) { anim.avatar = av; break; } }
        }
        if (anim.avatar == null) Debug.LogWarning("[MonsterAssetBuilder] " + p.id + ": sin Avatar válido en el FBX");
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        string prefabPath = PrefabDir + "/" + p.id + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
    }

    // Límites del modelo (escala 1, rotación 0) con el clip Idle muestreado
    private static Bounds IdleBounds(GameObject model, MonsterProfile p, Object[] assets)
    {
        GameObject tmp = (GameObject)PrefabUtility.InstantiatePrefab(model);
        tmp.transform.position = Vector3.zero;
        tmp.transform.rotation = Quaternion.Euler(0f, p.yaw, 0f);
        tmp.transform.localScale = Vector3.one;
        foreach (SkinnedMeshRenderer sk in tmp.GetComponentsInChildren<SkinnedMeshRenderer>()) sk.updateWhenOffscreen = true;
        AnimationClip idle = FindClip(assets, "Idle");
        if (idle != null) idle.SampleAnimation(tmp, idle.length * 0.3f);
        Bounds b = new Bounds();
        bool first = true;
        foreach (Renderer r in tmp.GetComponentsInChildren<Renderer>())
        {
            Bounds rb = r.bounds;
            if (first) { b = rb; first = false; } else b.Encapsulate(rb);
        }
        Object.DestroyImmediate(tmp);
        return b;
    }

    public static Bounds RenderBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        bool first = true;
        foreach (Renderer r in rs)
        {
            Bounds rb = r.bounds;
            SkinnedMeshRenderer sk = r as SkinnedMeshRenderer;
            if (sk != null && sk.sharedMesh != null)
            {
                // límites de la malla en espacio mundial (más fiable que bounds antes de animar)
                Bounds mb = sk.sharedMesh.bounds;
                Vector3 c = mb.center, e = mb.extents;
                Vector3 mn = Vector3.one * float.MaxValue, mx = Vector3.one * float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    Vector3 w = sk.transform.TransformPoint(corner);
                    mn = Vector3.Min(mn, w); mx = Vector3.Max(mx, w);
                }
                rb = new Bounds((mn + mx) * 0.5f, mx - mn);
            }
            if (first) { b = rb; first = false; } else b.Encapsulate(rb);
        }
        return b;
    }

    private static AnimatorController BuildController(MonsterProfile p, Object[] assets)
    {
        string cpath = CtrlDir + "/" + p.id + ".controller";
        AssetDatabase.DeleteAsset(cpath);
        AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(cpath);
        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

        AnimationClip idle = FindClip(assets, "Idle");
        AnimationClip walk = FindClip(assets, "Walk");
        AnimationClip run = FindClip(assets, "Run");
        AnimationClip attack = FindClip(assets, "Attack");
        if (run == null) run = walk;
        if (walk == null) walk = run;
        if (idle == null) idle = walk;
        if (attack == null) attack = FindClip(assets, "Scream") ?? idle;

        AnimatorState sIdle = sm.AddState("Idle"); sIdle.motion = idle;
        AnimatorState sWalk = sm.AddState("Walk"); sWalk.motion = walk;
        AnimatorState sRun = sm.AddState("Run"); sRun.motion = run;
        AnimatorState sAttack = sm.AddState("Attack"); sAttack.motion = attack;
        bool walkIsRun = FindClip(assets, "Walk") == null;
        sWalk.speed = (walkIsRun ? p.walkRunSlowdown : 1f) * p.walkAnimSpeed;
        sRun.speed = p.runAnimSpeed;
        sm.defaultState = sIdle;
        Debug.Log("[MonsterAssetBuilder] " + p.id + " clips: idle=" + (idle ? idle.name : "-") + " walk=" + (walk ? walk.name : "-") + " run=" + (run ? run.name : "-") + " attack=" + (attack ? attack.name : "-"));
        return ctrl;
    }
}
