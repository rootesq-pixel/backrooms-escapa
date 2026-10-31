using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// v7.11.0: genera Assets/Resources/Player/PlayerHazmat.prefab con el personaje de Character Creator 5
// (Assets/Player/CC5/BackroomsHazmat.fbx: traje hazmat amarillo, esqueleto CC_Base_*).
//   Unity -batchmode -nographics -quit -projectPath <p> -executeMethod PlayerBodyBuilder.Build
//  - Solo se usa la malla del traje (capucha, mono, guantes, botas): el cuerpo, la cara, los ojos, los dientes, la lengua, las
//    cejas, las pestañas y los calzoncillos de CC quedan siempre ocultos dentro del traje (la cara de la capucha es negra
//    opaca), así que no entran en el prefab (101 k → 49 k triángulos por jugador).
//  - El traje se parte en dos mallas: «HazmatSuit_Head» (capucha y túnel negro de la cara, pesada a la cabeza/cuello; en
//    primera persona solo proyecta sombra, como la cabeza del traje anterior) y «HazmatSuit_Body» (el resto).
//  - Las animaciones Idle/Walk/Run/Duck del traje anterior (Quaternius, HazmatHD.fbx, rig genérico) se retargetean aquí al
//    esqueleto CC: para cada hueso emparejado se copia su giro respecto a la pose de reposo en el espacio del modelo, con la
//    pose de reposo CC (brazos en A) alineada antes a la del traje anterior (brazos en T) hueso a hueso, y la cadera se
//    desplaza como el «Body» del original escalado por el largo de las piernas. Resultado: clips genéricos (.anim) con las
//    rutas del esqueleto CC, que usa el mismo Animator (estados Idle/Walk/Run/Duck) que antes.
public static class PlayerBodyBuilder
{
    private const string Fbx = "Assets/Player/CC5/BackroomsHazmat.fbx";
    private const string Tex = "Assets/Player/CC5/Textures/";
    private const string Gen = "Assets/Player/CC5/Generated";
    private const string MatDir = "Assets/Player/CC5/Materials";
    private const string AnimSrc = "Assets/Monsters/Models/HazmatHD.fbx";
    private const string Dir = "Assets/Resources/Player";
    public const string Controller = "Assets/Player/CC5/Generated/PlayerCC.controller";
    private static readonly string[] HeadBones = { "CC_Base_Head" };   // v7.11.0: el cuello del traje queda en el cuerpo (en 1.ª persona tapa la abertura)

    public static void Build()
    {
        Directory.CreateDirectory(Dir); Directory.CreateDirectory(Gen); Directory.CreateDirectory(MatDir);
        ConfigureTextures();
        ModelImporter mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
        if (mi == null) throw new System.Exception("Falta " + Fbx);
        mi.globalScale = 1f; mi.useFileScale = true;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        mi.importAnimation = false; mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false;
        mi.isReadable = true; mi.meshCompression = ModelImporterMeshCompression.Off; mi.optimizeGameObjects = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.SaveAndReimport();
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);

        Material fabric = Mat("HZ_Fabric", "HZ_Fabric", 0.35f);
        Material rubber = Mat("HZ_Rubber", "HZ_Rubber", 0.6f);
        rubber.SetFloat("_GlossMapScale", RubberGloss);   // v7.11.0: la goma negra reflejaba el cielo (guantes azulados en el 4x4)
        rubber.SetFloat("_GlossyReflections", 0f); rubber.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); EditorUtility.SetDirty(rubber);
        Material visor = Mat("HZ_Visor", null, 0.05f);
        visor.color = new Color(0.008f, 0.008f, 0.008f);
        visor.SetFloat("_SpecularHighlights", 0f); visor.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        visor.SetFloat("_GlossyReflections", 0f); visor.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        EditorUtility.SetDirty(visor);

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.name = "Model";
        SkinnedMeshRenderer suit = null; int dropped = 0, droppedTris = 0;
        foreach (SkinnedMeshRenderer r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r.name == "HazmatSuit") { suit = r; continue; }
            if (r.sharedMesh != null) droppedTris += r.sharedMesh.triangles.Length / 3;
            dropped++; Object.DestroyImmediate(r.gameObject);
        }
        if (suit == null) throw new System.Exception("sin malla HazmatSuit");
        string[] mn = System.Array.ConvertAll(suit.sharedMaterials, m => m != null ? m.name : "");
        Debug.Log("[PlayerBodyBuilder] traje: submallas " + string.Join(",", mn) + "; quitadas " + dropped + " mallas (" + droppedTris + " triángulos)");

        // --- partir el traje: capucha (cabeza) / cuerpo
        Mesh src = suit.sharedMesh;
        Transform[] bones = suit.bones;
        HashSet<int> hb = new HashSet<int>();
        for (int i = 0; i < bones.Length; i++) if (bones[i] != null && System.Array.IndexOf(HeadBones, bones[i].name) >= 0) hb.Add(i);
        BoneWeight[] bw = src.boneWeights;
        float[] hw = new float[bw.Length];
        for (int v = 0; v < bw.Length; v++)
        {
            BoneWeight b = bw[v]; float s = 0f;
            if (hb.Contains(b.boneIndex0)) s += b.weight0; if (hb.Contains(b.boneIndex1)) s += b.weight1;
            if (hb.Contains(b.boneIndex2)) s += b.weight2; if (hb.Contains(b.boneIndex3)) s += b.weight3;
            hw[v] = s;
        }
        List<int[]> bodyT = new List<int[]>(), headT = new List<int[]>();
        List<Material> bodyM = new List<Material>(), headM = new List<Material>();
        int nb = 0, nh = 0;
        for (int sm = 0; sm < src.subMeshCount; sm++)
        {
            int[] t = src.GetTriangles(sm);
            List<int> a = new List<int>(), h = new List<int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                float w = (hw[t[i]] + hw[t[i + 1]] + hw[t[i + 2]]) / 3f;
                List<int> dst = w >= 0.5f ? h : a;
                dst.Add(t[i]); dst.Add(t[i + 1]); dst.Add(t[i + 2]);
            }
            string n = sm < mn.Length ? mn[sm] : "";
            Material m = n.StartsWith("Visor") ? visor : n.StartsWith("Rubber") ? rubber : fabric;
            if (a.Count > 0) { bodyT.Add(a.ToArray()); bodyM.Add(m); nb += a.Count / 3; }
            if (h.Count > 0) { headT.Add(h.ToArray()); headM.Add(m); nh += h.Count / 3; }
        }
        Mesh bodyMesh = Compact(src, bodyT, "HazmatSuit_Body"), headMesh = Compact(src, headT, "HazmatSuit_Head");
        string ip = MatDir + "/HZ_FabricInner.mat";
        Material inner = AssetDatabase.LoadAssetAtPath<Material>(ip);
        Shader lining = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Player/CC5/SuitLining.shader");
        if (inner == null) { inner = new Material(lining); AssetDatabase.CreateAsset(inner, ip); }
        inner.shader = lining; inner.shaderKeywords = new string[0];
        inner.mainTexture = Load("HZ_Fabric_a"); inner.color = new Color(0.22f, 0.22f, 0.22f);   // forro: interior de la tela, más oscuro
        EditorUtility.SetDirty(inner);
        int ni = AddInner(bodyMesh, bodyM.IndexOf(fabric), bones);
        if (ni > 0) bodyM.Add(inner);
        Debug.Log("[PlayerBodyBuilder] forro interior de mangas/hombros: " + ni + " tris");
        SaveMesh(bodyMesh); SaveMesh(headMesh);
        suit.name = "HazmatSuit_Body";
        suit.sharedMesh = bodyMesh; suit.sharedMaterials = bodyM.ToArray();
        GameObject hgo = new GameObject("HazmatSuit_Head");
        hgo.transform.SetParent(suit.transform.parent, false);
        hgo.transform.localPosition = suit.transform.localPosition; hgo.transform.localRotation = suit.transform.localRotation; hgo.transform.localScale = suit.transform.localScale;
        SkinnedMeshRenderer head = hgo.AddComponent<SkinnedMeshRenderer>();
        head.sharedMesh = headMesh; head.bones = bones; head.rootBone = suit.rootBone; head.sharedMaterials = headM.ToArray();
        head.localBounds = suit.localBounds; head.quality = SkinQuality.Bone4;
        suit.quality = SkinQuality.Bone4;
        Debug.Log("[PlayerBodyBuilder] cuerpo " + nb + " tris / " + bodyMesh.vertexCount + " vért., capucha " + nh + " tris / " + headMesh.vertexCount + " vért.");

        // --- orientación: el modelo mira a +Z del prefab
        Vector3 dstF = Fwd(inst.transform, "CC_Base_L_ToeBase", "CC_Base_L_Foot", "CC_Base_R_ToeBase", "CC_Base_R_Foot");
        Quaternion face = Quaternion.FromToRotation(dstF.normalized, Vector3.forward);
        Debug.Log("[PlayerBodyBuilder] delante CC " + dstF.normalized.ToString("F2"));

        // --- animación
        Animator anim = inst.GetComponent<Animator>(); if (anim == null) anim = inst.AddComponent<Animator>();
        AnimationClip[] clips = Retarget(inst);
        AssetDatabase.DeleteAsset(Controller);
        AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(Controller);
        AnimatorStateMachine smc = ctrl.layers[0].stateMachine;
        AnimatorState first = null;
        foreach (AnimationClip c in clips) { AnimatorState st = smc.AddState(c.name); st.motion = c; if (first == null) first = st; }
        smc.defaultState = first;
        Avatar av = AvatarBuilder.BuildGenericAvatar(inst, "");
        av.name = "PlayerCC_Avatar";
        string avp = Gen + "/PlayerCC_Avatar.asset"; AssetDatabase.DeleteAsset(avp); AssetDatabase.CreateAsset(av, avp);
        anim.avatar = av; anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        GameObject root = new GameObject("PlayerHazmat");
        inst.transform.SetParent(root.transform, false);
        inst.transform.localRotation = face;
        PrefabUtility.SaveAsPrefabAsset(root, Dir + "/PlayerHazmat.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[PlayerBodyBuilder] prefab CC5 listo: " + clips.Length + " clips, avatar " + av.isValid);
    }

    // ------------------------------------------------------------------ texturas y materiales
    private static void ConfigureTextures()
    {
        foreach (string n in new[] { "HZ_Fabric_a.png", "HZ_Fabric_n.jpg", "HZ_Fabric_m.png", "HZ_Rubber_a.png", "HZ_Rubber_n.png", "HZ_Rubber_m.png" })
        {
            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(Tex + n);
            if (ti == null) throw new System.Exception("Falta " + Tex + n);
            bool normal = n.Contains("_n."), mask = n.Contains("_m.");
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !(normal || mask);
            ti.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.mipmapEnabled = true; ti.anisoLevel = 4;
            ti.maxTextureSize = MaxSize(n);
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SaveAndReimport();
        }
    }
    // tela: albedo y normales a 4K (detalle del tejido); máscaras y goma a 2K
    public const float RubberGloss = 0.45f;
    public static int MaxSize(string n) { return n.StartsWith("HZ_Fabric_a") || n.StartsWith("HZ_Fabric_n") ? 4096 : 2048; }

    private static Material Mat(string name, string tex, float gloss)
    {
        string p = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find("Standard");
        m.color = Color.white;
        m.SetFloat("_Glossiness", gloss); m.SetFloat("_Metallic", 0f);
        if (tex != null)
        {
            Texture2D a = Load(tex + "_a"), n = Load(tex + "_n"), k = Load(tex + "_m");
            m.mainTexture = a;
            m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP");
            // máscara: R metal (0), G oclusión (AO), A suavidad (1 − roughness)
            m.SetTexture("_MetallicGlossMap", k); m.SetFloat("_GlossMapScale", 1f); m.EnableKeyword("_METALLICGLOSSMAP");
            m.SetTexture("_OcclusionMap", k); m.SetFloat("_OcclusionStrength", 1f);
        }
        EditorUtility.SetDirty(m);
        return m;
    }
    private static Texture2D Load(string n)
    {
        foreach (string ext in new[] { ".png", ".jpg" }) { Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + n + ext); if (t != null) return t; }
        throw new System.Exception("Falta textura " + n);
    }

    // ------------------------------------------------------------------ mallas
    private static Mesh Compact(Mesh src, List<int[]> subs, string name)
    {
        Vector3[] v = src.vertices, n = src.normals; Vector4[] t = src.tangents; Vector2[] uv = src.uv, uv2 = src.uv2; BoneWeight[] bw = src.boneWeights;
        Dictionary<int, int> map = new Dictionary<int, int>();
        List<int> order = new List<int>();
        List<int[]> outSubs = new List<int[]>();
        foreach (int[] s in subs)
        {
            int[] o = new int[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                int k;
                if (!map.TryGetValue(s[i], out k)) { k = order.Count; map[s[i]] = k; order.Add(s[i]); }
                o[i] = k;
            }
            outSubs.Add(o);
        }
        int c = order.Count;
        Vector3[] v2 = new Vector3[c], n2 = new Vector3[c]; Vector4[] t2 = new Vector4[c]; Vector2[] u2 = new Vector2[c]; BoneWeight[] b2 = new BoneWeight[c];
        bool hasT = t != null && t.Length == v.Length, hasUv = uv != null && uv.Length == v.Length;
        for (int i = 0; i < c; i++)
        {
            int s = order[i]; v2[i] = v[s]; n2[i] = n[s]; b2[i] = bw[s];
            if (hasT) t2[i] = t[s]; if (hasUv) u2[i] = uv[s];
        }
        Mesh m = new Mesh { name = name };
        m.indexFormat = c > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.vertices = v2; m.normals = n2; if (hasT) m.tangents = t2; if (hasUv) m.uv = u2;
        m.boneWeights = b2; m.bindposes = src.bindposes;
        m.subMeshCount = outSubs.Count;
        for (int i = 0; i < outSubs.Count; i++) m.SetTriangles(outSubs[i], i);
        m.RecalculateBounds();
        return m;
    }
    // v7.11.0: en 1.ª persona el plano cercano corta mangas y hombros y se veían tubos huecos; se añade una
    // submalla con las mismas caras de tela de brazos/hombros vueltas del revés (normales invertidas) como forro.
    private static bool ArmBone(string n) { return n.Contains("Clavicle") || n.Contains("Upperarm") || n.Contains("Forearm") || n.Contains("Elbow") || n.Contains("Shoulder") || n.Contains("Spine02") || n.Contains("NeckTwist") || n.Contains("Spine01"); }
    private static int AddInner(Mesh m, int sub, Transform[] bones)
    {
        if (sub < 0) return 0;
        BoneWeight[] bw = m.boneWeights;
        System.Func<int, float> aw = v =>
        {
            BoneWeight b = bw[v]; float s = 0f;
            if (bones[b.boneIndex0] != null && ArmBone(bones[b.boneIndex0].name)) s += b.weight0;
            if (bones[b.boneIndex1] != null && ArmBone(bones[b.boneIndex1].name)) s += b.weight1;
            if (bones[b.boneIndex2] != null && ArmBone(bones[b.boneIndex2].name)) s += b.weight2;
            if (bones[b.boneIndex3] != null && ArmBone(bones[b.boneIndex3].name)) s += b.weight3;
            return s;
        };
        int[] t = m.GetTriangles(sub);
        List<Vector3> v = new List<Vector3>(m.vertices), n = new List<Vector3>(m.normals);
        List<Vector4> tg = new List<Vector4>(m.tangents); List<Vector2> uv = new List<Vector2>(m.uv); List<BoneWeight> b2 = new List<BoneWeight>(bw);
        Dictionary<int, int> dup = new Dictionary<int, int>(); List<int> o = new List<int>();
        for (int i = 0; i < t.Length; i += 3)
        {
            if ((aw(t[i]) + aw(t[i + 1]) + aw(t[i + 2])) / 3f < 0.02f) continue;   // cualquier peso: sin bordes en dientes de sierra
            for (int j = 2; j >= 0; j--)
            {
                int s = t[i + j], k;
                if (!dup.TryGetValue(s, out k))
                {
                    k = v.Count; dup[s] = k; v.Add(v[s]); n.Add(-n[s]);
                    Vector4 q = tg.Count > s ? tg[s] : new Vector4(1, 0, 0, 1); tg.Add(new Vector4(q.x, q.y, q.z, -q.w));
                    uv.Add(uv.Count > s ? uv[s] : Vector2.zero); b2.Add(bw[s]);
                }
                o.Add(k);
            }
        }
        if (o.Count == 0) return 0;
        int[][] subs = new int[m.subMeshCount][]; for (int i = 0; i < subs.Length; i++) subs[i] = m.GetTriangles(i);
        Matrix4x4[] bp = m.bindposes;
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v); m.SetNormals(n); m.SetTangents(tg); m.SetUVs(0, uv); m.boneWeights = b2.ToArray(); m.bindposes = bp;
        m.subMeshCount = subs.Length + 1;
        for (int i = 0; i < subs.Length; i++) m.SetTriangles(subs[i], i);
        m.SetTriangles(o.ToArray(), subs.Length);
        m.RecalculateBounds();
        return o.Count / 3;
    }
    private static void SaveMesh(Mesh m)
    {
        string p = Gen + "/" + m.name + ".asset";
        AssetDatabase.DeleteAsset(p);
        AssetDatabase.CreateAsset(m, p);
        SerializedObject so = new SerializedObject(m);
        SerializedProperty rp = so.FindProperty("m_IsReadable");
        if (rp != null) { rp.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
    }

    // ------------------------------------------------------------------ retarget de las animaciones del traje anterior
    private struct Pair { public string s, d, sf, st, df, dt; public Pair(string s, string d, string sf, string st, string df, string dt) { this.s = s; this.d = d; this.sf = sf; this.st = st; this.df = df; this.dt = dt; } }

    private static List<Pair> Pairs()
    {
        List<Pair> p = new List<Pair>();
        p.Add(new Pair("Body", "CC_Base_Hip", null, null, null, null));
        p.Add(new Pair("Abdomen", "CC_Base_Waist", "Abdomen", "Torso", "CC_Base_Waist", "CC_Base_Spine02"));
        p.Add(new Pair("Torso", "CC_Base_Spine02", "Torso", "Neck", "CC_Base_Spine02", "CC_Base_NeckTwist01"));
        p.Add(new Pair("Neck", "CC_Base_NeckTwist01", "Neck", "Head", "CC_Base_NeckTwist01", "CC_Base_Head"));
        p.Add(new Pair("Head", "CC_Base_Head", null, null, null, null));
        foreach (string s in new[] { "L", "R" })
        {
            string c = "CC_Base_" + s + "_";
            p.Add(new Pair("Shoulder." + s, c + "Clavicle", "Shoulder." + s, "UpperArm." + s, c + "Clavicle", c + "Upperarm"));
            p.Add(new Pair("UpperArm." + s, c + "Upperarm", "UpperArm." + s, "LowerArm." + s, c + "Upperarm", c + "Forearm"));
            p.Add(new Pair("LowerArm." + s, c + "Forearm", "LowerArm." + s, "Middle1." + s, c + "Forearm", c + "Hand"));
            p.Add(new Pair("LowerArm." + s, c + "Hand", "LowerArm." + s, "Middle1." + s, c + "Hand", c + "Mid1"));
            foreach (string[] f in new[] { new[] { "Index", "Index" }, new[] { "Middle", "Mid" }, new[] { "Middle", "Ring" }, new[] { "Pinky", "Pinky" } })
            {
                p.Add(new Pair(f[0] + "1." + s, c + f[1] + "1", f[0] + "1." + s, f[0] + "2." + s, c + f[1] + "1", c + f[1] + "2"));
                p.Add(new Pair(f[0] + "2." + s, c + f[1] + "2", f[0] + "2." + s, f[0] + "3." + s, c + f[1] + "2", c + f[1] + "3"));
                p.Add(new Pair(f[0] + "3." + s, c + f[1] + "3", f[0] + "2." + s, f[0] + "3." + s, c + f[1] + "2", c + f[1] + "3"));
            }
            // pulgares: el del traje anterior (2 huesos, otra pose de reposo) quedaba de punta hacia arriba; el CC se queda en
            // su pose de reposo (junto al índice) y sigue a la mano
            p.Add(new Pair("UpperLeg." + s, c + "Thigh", "UpperLeg." + s, "LowerLeg." + s, c + "Thigh", c + "Calf"));
            p.Add(new Pair("LowerLeg." + s, c + "Calf", "LowerLeg." + s, "Foot." + s, c + "Calf", c + "Foot"));
            p.Add(new Pair("Foot." + s, c + "Foot", null, null, null, null));
        }
        return p;
    }

    private static Transform F(Transform r, string n) { if (r.name == n) return r; for (int i = 0; i < r.childCount; i++) { Transform t = F(r.GetChild(i), n); if (t != null) return t; } return null; }
    // frente del modelo (espacio del modelo, horizontal): media de los dos lados para que la puntera abierta no lo tuerza
    private static Vector3 Fwd(Transform r, string a1, string b1, string a2, string b2)
    {
        Vector3 v = (r.InverseTransformPoint(F(r, a1).position) - r.InverseTransformPoint(F(r, b1).position)) + (r.InverseTransformPoint(F(r, a2).position) - r.InverseTransformPoint(F(r, b2).position));
        v.y = 0f; return v.normalized;
    }
    public const float ArmAbduction = 0.42f;   // ángulo del brazo respecto a la vertical (en el plano frontal) × este factor
    private static Vector3 Adduct(Vector3 d)
    {
        float m = new Vector2(d.x, d.y).magnitude; if (m < 1e-5f) return d;
        float a = Mathf.Atan2(Mathf.Abs(d.x), -d.y) * ArmAbduction;
        return new Vector3(Mathf.Sign(d.x) * Mathf.Sin(a) * m, -Mathf.Cos(a) * m, d.z);
    }
    private static int Depth(Transform t) { int d = 0; while (t.parent != null) { d++; t = t.parent; } return d; }

    public static AnimationClip[] Retarget(GameObject dst)
    {
        GameObject srcModel = AssetDatabase.LoadAssetAtPath<GameObject>(AnimSrc);
        GameObject src = Object.Instantiate(srcModel);
        Transform sr = src.transform, dr = dst.transform;
        Quaternion sRootInv = Quaternion.Inverse(sr.rotation), dRootInv = Quaternion.Inverse(dr.rotation);
        System.Func<Transform, Quaternion> SR = t => sRootInv * t.rotation, DR = t => dRootInv * t.rotation;
        System.Func<Transform, Vector3> SP = t => sr.InverseTransformPoint(t.position), DP = t => dr.InverseTransformPoint(t.position);

        // reposo del destino (todas las rotaciones/posiciones locales)
        Transform[] dAll = dst.GetComponentsInChildren<Transform>(true);
        Dictionary<Transform, Quaternion> dRestL = new Dictionary<Transform, Quaternion>(); Dictionary<Transform, Vector3> dRestP = new Dictionary<Transform, Vector3>();
        foreach (Transform t in dAll) { dRestL[t] = t.localRotation; dRestP[t] = t.localPosition; }

        // frente: rodilla del original (PoleTarget) vs. puntera del CC
        Vector3 sF = Fwd(sr, "PoleTarget.L", "LowerLeg.L", "PoleTarget.R", "LowerLeg.R");
        Vector3 dF = Fwd(dr, "CC_Base_L_ToeBase", "CC_Base_L_Foot", "CC_Base_R_ToeBase", "CC_Base_R_Foot");
        Quaternion Q = Quaternion.FromToRotation(sF.normalized, dF.normalized), Qi = Quaternion.Inverse(Q);
        float sLeg = Vector3.Distance(SP(F(sr, "UpperLeg.L")), SP(F(sr, "Foot.L"))), dLeg = Vector3.Distance(DP(F(dr, "CC_Base_L_Thigh")), DP(F(dr, "CC_Base_L_Foot")));
        float k = dLeg / Mathf.Max(1e-4f, sLeg);
        Debug.Log("[PlayerBodyBuilder] retarget: frente orig " + sF.normalized.ToString("F2") + " CC " + dF.normalized.ToString("F2") + ", piernas " + sLeg.ToString("F3") + " → " + dLeg.ToString("F3") + " (×" + k.ToString("F3") + ")");

        List<Pair> pairs = Pairs();
        List<Transform> sB = new List<Transform>(), dB = new List<Transform>();
        List<Quaternion> sRest = new List<Quaternion>(), dRestAligned = new List<Quaternion>();
        List<Pair> ok = new List<Pair>();
        foreach (Pair p in pairs)
        {
            Transform s = F(sr, p.s), d = F(dr, p.d);
            if (s == null || d == null) { Debug.LogWarning("[PlayerBodyBuilder] sin pareja " + p.s + " → " + p.d); continue; }
            Quaternion rd = DR(d);
            if (p.sf != null)
            {
                Transform a = F(sr, p.sf), b = F(sr, p.st), c = F(dr, p.df), e = F(dr, p.dt);
                if (a != null && b != null && c != null && e != null)
                {
                    Vector3 sd = Q * (SP(b) - SP(a)), dd = DP(e) - DP(c);
                    if (sd.sqrMagnitude > 1e-8f && dd.sqrMagnitude > 1e-8f) rd = Quaternion.FromToRotation(dd.normalized, sd.normalized) * rd;
                }
            }
            sB.Add(s); dB.Add(d); sRest.Add(SR(s)); dRestAligned.Add(rd); ok.Add(p);
        }
        // orden: padres antes que hijos
        List<int> idx = new List<int>(); for (int i = 0; i < dB.Count; i++) idx.Add(i);
        idx.Sort((x, y) => Depth(dB[x]).CompareTo(Depth(dB[y])));
        Transform sBody = F(sr, "Body"), dHip = F(dr, "CC_Base_Hip");
        Transform uaL = F(dr, "CC_Base_L_Upperarm"), uaR = F(dr, "CC_Base_R_Upperarm"), faL = F(dr, "CC_Base_L_Forearm"), faR = F(dr, "CC_Base_R_Forearm");
        Vector3 sBody0 = SP(sBody), dHip0 = DP(dHip);
        // huesos con curva (sin repetir: Hand y Ring usan el giro de LowerArm/Middle)
        HashSet<Transform> written = new HashSet<Transform>(dB);
        // huesos sin pareja que el código del juego gira cada fotograma (BendTorso → Spine01, SeatedLegs...): el clip los
        // fija en su reposo para que el Animator los reponga y el giro no se acumule de un fotograma al siguiente
        foreach (string n in new[] { "CC_Base_Spine01", "CC_Base_NeckTwist02" }) { Transform t = F(dr, n); if (t != null) written.Add(t); }
        Transform sp01 = F(dr, "CC_Base_Spine01"), nk02 = F(dr, "CC_Base_NeckTwist02");

        AnimationClip[] srcClips = System.Array.FindAll(AssetDatabase.LoadAllAssetsAtPath(AnimSrc), o => o is AnimationClip && !o.name.StartsWith("__preview__")).Length > 0
            ? System.Array.ConvertAll(System.Array.FindAll(AssetDatabase.LoadAllAssetsAtPath(AnimSrc), o => o is AnimationClip && !o.name.StartsWith("__preview__")), o => (AnimationClip)o) : new AnimationClip[0];
        List<AnimationClip> outC = new List<AnimationClip>();
        foreach (string name in new[] { "Idle", "Walk", "Run", "Duck" })
        {
            AnimationClip sc = System.Array.Find(srcClips, c => MonsterModelPostprocessor.Clean(c.name) == name);
            if (sc == null) { Debug.LogWarning("[PlayerBodyBuilder] sin clip " + name); continue; }
            float fps = 30f; int frames = Mathf.Max(2, Mathf.RoundToInt(sc.length * fps));
            Dictionary<Transform, AnimationCurve[]> rc = new Dictionary<Transform, AnimationCurve[]>();
            foreach (Transform t in written) rc[t] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            AnimationCurve[] pc = { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            Dictionary<Transform, Quaternion> prevQ = new Dictionary<Transform, Quaternion>();
            Dictionary<string, float> errMax = new Dictionary<string, float>();
            for (int f = 0; f <= frames; f++)
            {
                float time = f == frames ? sc.length : f / fps;
                sc.SampleAnimation(src, f == frames ? 0f : time);   // el último fotograma = el primero (bucle sin salto)
                foreach (Transform t in dAll) { t.localRotation = dRestL[t]; t.localPosition = dRestP[t]; }
                Vector3 dp = dHip0 + Q * (SP(sBody) - sBody0) * k;
                dHip.position = dr.TransformPoint(dp);
                Quaternion cL = Quaternion.identity, cR = Quaternion.identity;
                foreach (int i in idx)
                {
                    Quaternion delta = Q * SR(sB[i]) * Quaternion.Inverse(sRest[i]) * Qi;
                    Quaternion w = delta * dRestAligned[i];
                    if (dB[i].IsChildOf(uaL) && dB[i] != uaL) w = cL * w;
                    else if (dB[i].IsChildOf(uaR) && dB[i] != uaR) w = cR * w;
                    dB[i].rotation = dr.rotation * w;
                    if (dB[i] == uaL || dB[i] == uaR)
                    {   // el traje anterior era muy abultado y abría los brazos (casi en T): se cierran hacia el cuerpo
                        Vector3 d = DP(dB[i] == uaL ? faL : faR) - DP(dB[i]);
                        Quaternion c = Quaternion.FromToRotation(d, Adduct(d));
                        dB[i].rotation = dr.rotation * (c * w);
                        if (dB[i] == uaL) cL = c; else cR = c;
                    }
                }
                // control: dirección de cada hueso (hacia su hijo) igual en los dos esqueletos
                foreach (int i in idx)
                {
                    Pair pr = ok[i]; if (pr.sf == null || dB[i].IsChildOf(uaL) || dB[i].IsChildOf(uaR)) continue;
                    Transform a1 = F(sr, pr.sf), b1 = F(sr, pr.st), c1 = F(dr, pr.df), e1 = F(dr, pr.dt);
                    float ang = Vector3.Angle(Q * (SP(b1) - SP(a1)), DP(e1) - DP(c1));
                    float o; errMax.TryGetValue(pr.d, out o); if (ang > o) errMax[pr.d] = ang;
                }
                float kt = f == frames ? sc.length : time;
                // respiración muy leve en la columna y el cuello: el Animator no repone las curvas constantes y el código del
                // juego gira estos huesos cada fotograma (BendTorso), así que necesitan una curva que varíe
                float br = Mathf.Sin(2f * Mathf.PI * (f == frames ? 0f : time) / sc.length);
                if (sp01 != null) sp01.localRotation = sp01.localRotation * Quaternion.Euler(0.6f * br, 0f, 0f);
                if (nk02 != null) nk02.localRotation = nk02.localRotation * Quaternion.Euler(-0.4f * br, 0f, 0f);
                foreach (Transform t in written)
                {
                    Quaternion q = t.localRotation;
                    Quaternion pq; if (prevQ.TryGetValue(t, out pq) && Quaternion.Dot(pq, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    prevQ[t] = q;
                    AnimationCurve[] cv = rc[t];
                    cv[0].AddKey(kt, q.x); cv[1].AddKey(kt, q.y); cv[2].AddKey(kt, q.z); cv[3].AddKey(kt, q.w);
                }
                Vector3 lp = dHip.localPosition; pc[0].AddKey(kt, lp.x); pc[1].AddKey(kt, lp.y); pc[2].AddKey(kt, lp.z);
            }
            // el Animator no repone las curvas constantes y el código del juego gira algunos huesos cada fotograma (IK del
            // brazo, columna, piernas sentado): toda curva constante recibe una oscilación de 0,15° (invisible) para que varíe
            string cst = "";
            foreach (KeyValuePair<Transform, AnimationCurve[]> kv in rc)
            {
                float rg = 0f;
                foreach (AnimationCurve cv in kv.Value) { float mn = 9f, mx = -9f; foreach (Keyframe kf in cv.keys) { mn = Mathf.Min(mn, kf.value); mx = Mathf.Max(mx, kf.value); } rg = Mathf.Max(rg, mx - mn); }
                if (rg >= 1e-4f) continue;
                cst += " " + kv.Key.name.Replace("CC_Base_", "");
                AnimationCurve[] cv0 = kv.Value; Keyframe[][] ks = { cv0[0].keys, cv0[1].keys, cv0[2].keys, cv0[3].keys };
                for (int n = 0; n < ks[0].Length; n++)
                {
                    float tk = ks[0][n].time; float ph = n == ks[0].Length - 1 ? 0f : tk / sc.length;
                    Quaternion q = new Quaternion(ks[0][n].value, ks[1][n].value, ks[2][n].value, ks[3][n].value) * Quaternion.Euler(0.15f * Mathf.Sin(2f * Mathf.PI * ph), 0f, 0f);
                    ks[0][n].value = q.x; ks[1][n].value = q.y; ks[2][n].value = q.z; ks[3][n].value = q.w;
                }
                for (int c = 0; c < 4; c++) cv0[c].keys = ks[c];
            }
            Debug.Log("[PlayerBodyBuilder] " + name + ": curvas constantes con oscilación mínima:" + (cst.Length > 0 ? cst : " ninguna"));
            AnimationClip clip = new AnimationClip { name = name, frameRate = fps };
            foreach (KeyValuePair<Transform, AnimationCurve[]> kv in rc)
            {
                string path = AnimationUtility.CalculateTransformPath(kv.Key, dr);
                string[] comp = { "x", "y", "z", "w" };
                for (int c = 0; c < 4; c++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + comp[c]), kv.Value[c]);
            }
            string hp = AnimationUtility.CalculateTransformPath(dHip, dr);
            string[] pcn = { "x", "y", "z" };
            for (int c = 0; c < 3; c++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(hp, typeof(Transform), "m_LocalPosition." + pcn[c]), pc[c]);
            clip.EnsureQuaternionContinuity();
            AnimationClipSettings st = AnimationUtility.GetAnimationClipSettings(clip); st.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, st);
            string cp = Gen + "/" + name + ".anim";   // el nombre del clip = el del archivo (PlayerBody busca «Walk»/«Run»)
            AssetDatabase.DeleteAsset(cp); AssetDatabase.CreateAsset(clip, cp);
            outC.Add(clip);
            string worst = ""; foreach (KeyValuePair<string, float> kv in errMax) if (kv.Value > 3f) worst += " " + kv.Key + " " + kv.Value.ToString("F0") + "°";
            Debug.Log("[PlayerBodyBuilder] " + name + ": huesos con dirección distinta del original (>3°):" + (worst.Length > 0 ? worst : " ninguno"));
            Debug.Log("[PlayerBodyBuilder] clip " + name + " " + sc.length.ToString("F2") + " s, " + (frames + 1) + " claves, " + rc.Count + " huesos");
        }
        foreach (Transform t in dAll) { t.localRotation = dRestL[t]; t.localPosition = dRestP[t]; }
        Object.DestroyImmediate(src);
        return outC.ToArray();
    }
}
