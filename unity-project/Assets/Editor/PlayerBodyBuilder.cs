using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Genera Assets/Resources/Player/PlayerHazmat.prefab a partir de Assets/Monsters/Models/Hazmat.fbx
//   Unity -batchmode -nographics -quit -projectPath <p> -executeMethod PlayerBodyBuilder.Build
public static class PlayerBodyBuilder
{
    private const string Fbx = "Assets/Monsters/Models/HazmatHD.fbx";   // v7.3.0: subdividido y con UV (Tools/blender/suit_bake.py)
    private const string Tex = "Assets/Monsters/Textures/Hazmat/";
    private const string Dir = "Assets/Resources/Player";

    public static void Build()
    {
        System.IO.Directory.CreateDirectory(Dir);
        System.IO.Directory.CreateDirectory("Assets/Monsters/Materials");
        System.IO.Directory.CreateDirectory("Assets/Monsters/Controllers");
        AssetDatabase.ImportAsset(Fbx, ImportAssetOptions.ForceUpdate);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        if (model == null) throw new System.Exception("Falta " + Fbx);
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(Fbx);

        // v7.3.0: traje realista: todos los materiales usan las texturas horneadas (albedo, normales, máscara metal/AO/suavidad)
        Material suit = Mat("Hazmat_Suit", Color.white, 0.5f, "HazmatBody");
        Material rubber = Mat("Hazmat_Rubber", Color.white, 0.5f, "HazmatBody");
        Material grey = Mat("Hazmat_Grey", Color.white, 0.5f, "HazmatBody");
        Material suitH = Mat("Hazmat_SuitHead", Color.white, 0.5f, "HazmatHead");
        Material visor = Mat("Hazmat_Visor", Color.white, 0.5f, "HazmatHead");

        GameObject root = new GameObject("PlayerHazmat");
        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        inst.name = "Model";
        inst.transform.SetParent(root.transform, false);
        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>())
        {
            Material[] src = r.sharedMaterials, dst = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                string n = src[i] != null ? src[i].name : "";
                bool head = r.name.Contains("Head");
                if (head) dst[i] = n.StartsWith("Visor") || n.StartsWith("Black") ? visor : suitH;
                else dst[i] = n.StartsWith("Hazmat_Main") ? suit : n.StartsWith("Black") ? rubber : grey;
            }
            r.sharedMaterials = dst;
        }

        string cpath = "Assets/Monsters/Controllers/PlayerHazmat.controller";
        AssetDatabase.DeleteAsset(cpath);
        AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(cpath);
        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
        string[] names = { "Idle", "Walk", "Run", "Duck" };
        AnimatorState first = null;
        foreach (string n in names)
        {
            AnimationClip c = Clip(assets, n);
            if (c == null) Debug.LogWarning("[PlayerBodyBuilder] sin clip " + n);
            AnimatorState st = sm.AddState(n);
            st.motion = c;
            if (first == null) first = st;
        }
        sm.defaultState = first;
        Animator anim = inst.GetComponent<Animator>();
        if (anim == null) anim = inst.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        foreach (Object o in assets) { Avatar av = o as Avatar; if (av != null && av.isValid) { anim.avatar = av; break; } }
        anim.applyRootMotion = false;
        bool hasAvatar = anim.avatar != null;
        PrefabUtility.SaveAsPrefabAsset(root, Dir + "/PlayerHazmat.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Debug.Log("[PlayerBodyBuilder] prefab listo (avatar=" + hasAvatar + ")");
    }

    private static AnimationClip Clip(Object[] assets, string key)
    {
        foreach (Object o in assets)
        {
            AnimationClip c = o as AnimationClip;
            if (c == null || c.name.StartsWith("__preview__")) continue;
            if (MonsterModelPostprocessor.Clean(c.name) == key) return c;
        }
        return null;
    }

    private static Material Mat(string name, Color c, float gloss, string tex = null)
    {
        string p = "Assets/Monsters/Materials/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, p); }
        m.color = c;
        m.SetFloat("_Glossiness", gloss);
        m.SetFloat("_Metallic", 0f);
        if (tex != null)
        {
            Texture2D a = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + tex + "_a.png"), n = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + tex + "_n.png"), k = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + tex + "_m.png");
            if (a == null || n == null || k == null) throw new System.Exception("Faltan texturas " + tex);
            m.mainTexture = a;
            m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_MetallicGlossMap", k); m.SetFloat("_GlossMapScale", 1f); m.EnableKeyword("_METALLICGLOSSMAP");
            m.SetTexture("_OcclusionMap", k); m.SetFloat("_OcclusionStrength", 0.7f);
        }
        EditorUtility.SetDirty(m);
        return m;
    }
}
