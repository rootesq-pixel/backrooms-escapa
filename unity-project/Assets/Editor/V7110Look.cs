using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.11.0: vista previa fuera de Play del cuerpo CC5 con las animaciones retargeteadas, junto al traje anterior (HazmatHD.fbx
// con sus clips originales) en las mismas fases. Necesita gráficos: V7110_OUT=<dir> xvfb-run -a Unity -batchmode -quit
// -projectPath <p> -executeMethod V7110Look.Poses
public static class V7110Look
{
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V7110_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v7110/tmp/poses" : o; } }
    public static void Poses()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.48f);
        GameObject lg = new GameObject("Sun"); Light l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.1f; l.shadows = LightShadows.Soft;
        lg.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = Vector3.one * 3f;
        floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.35f, 0.35f, 0.35f) };
        GameObject cg = new GameObject("Cam"); Camera cam = cg.AddComponent<Camera>(); cam.fieldOfView = 30f; cam.backgroundColor = new Color(0.25f, 0.27f, 0.3f); cam.clearFlags = CameraClearFlags.SolidColor;

        GameObject np = (GameObject)Object.Instantiate(Resources.Load<GameObject>(PlayerBody.PrefabResource));
        np.transform.position = new Vector3(0.6f, 0f, 0f);
        Animator na = np.GetComponentInChildren<Animator>();
        GameObject op = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Monsters/Models/HazmatHD.fbx"));
        Transform oh = PlayerRig.Find(op.transform, "Head");
        float s = 1.6f / Mathf.Max(0.2f, op.transform.InverseTransformPoint(oh.position).y);
        op.transform.localScale = Vector3.one * s; op.transform.position = new Vector3(-0.6f, 0f, 0f);
        Object[] oa = AssetDatabase.LoadAllAssetsAtPath("Assets/Monsters/Models/HazmatHD.fbx");
        string rep = "";
        foreach (Renderer r in np.GetComponentsInChildren<Renderer>()) rep += "renderer " + r.name + " tris " + (r is SkinnedMeshRenderer ? ((SkinnedMeshRenderer)r).sharedMesh.triangles.Length / 3 : 0) + " mats " + string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m != null ? m.name : "-")) + "\n";
        foreach (AnimationClip nc in ((UnityEditor.Animations.AnimatorController)na.runtimeAnimatorController).animationClips)
        {
            AnimationClip oc = null;
            foreach (Object o in oa) { AnimationClip c = o as AnimationClip; if (c != null && !c.name.StartsWith("__preview__") && MonsterModelPostprocessor.Clean(c.name) == nc.name) oc = c; }
            for (int ph = 0; ph < 4; ph++)
            {
                float t = nc.length * ph / 4f;
                nc.SampleAnimation(na.gameObject, t);
                if (oc != null) oc.SampleAnimation(op, oc.length * ph / 4f);
                Bounds b = new Bounds(np.transform.position + Vector3.up, Vector3.zero);
                foreach (Renderer r in np.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                if (ph == 0) rep += nc.name + " altura " + b.max.y.ToString("F3") + " ancho " + b.size.x.ToString("F2") + "\n";
                foreach (string v in new[] { "frente", "lado" })
                {
                    Vector3 c = new Vector3(0f, 0.95f, 0f);
                    Vector3 e = v == "frente" ? new Vector3(0f, 1.1f, 6.2f) : new Vector3(6.2f, 1.1f, 0.3f);
                    cam.transform.position = e; cam.transform.rotation = Quaternion.LookRotation(c - e);
                    Shot(cam, nc.name + "_" + ph + "_" + v);
                }
            }
        }
        // detalle: capucha y manos (Idle 0)
        foreach (AnimationClip nc in ((UnityEditor.Animations.AnimatorController)na.runtimeAnimatorController).animationClips) if (nc.name == "Idle") nc.SampleAnimation(na.gameObject, 0f);
        Transform hd = PlayerRig.Find(np.transform, "Head"), hr = PlayerRig.Find(np.transform, "Hand.R");
        { Vector3 e = hd.position + new Vector3(0.5f, 0.15f, 1.3f); cam.transform.position = e; cam.transform.rotation = Quaternion.LookRotation(hd.position - e); Shot(cam, "det_capucha"); }
        { Vector3 e = hr.position + new Vector3(-0.2f, 0.1f, 0.6f); cam.transform.position = e; cam.transform.rotation = Quaternion.LookRotation(hr.position - e); Shot(cam, "det_mano"); }
        File.WriteAllText(Out + "/report.txt", rep);
        EditorApplication.Exit(0);
    }
    // fuera de Play la piel no se recalcula entre dos Render() del mismo fotograma: se hornea cada malla animada a una malla fija
    static void Shot(Camera cam, string name)
    {
        System.Collections.Generic.List<GameObject> tmp = new System.Collections.Generic.List<GameObject>();
        System.Collections.Generic.List<SkinnedMeshRenderer> off = new System.Collections.Generic.List<SkinnedMeshRenderer>();
        foreach (SkinnedMeshRenderer r in Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            Mesh m = new Mesh(); r.BakeMesh(m, true);
            GameObject g = new GameObject("bake"); g.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
            Vector3 sb = m.bounds.size; float wantH = r.transform.root.name.StartsWith("HazmatHD") ? 1.75f : 0f;
            if (wantH > 0f && sb.y > 1e-4f) { g.transform.localScale = Vector3.one * (wantH / Mathf.Max(sb.x, Mathf.Max(sb.y, sb.z))); }
            g.AddComponent<MeshFilter>().sharedMesh = m; MeshRenderer mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterials = r.sharedMaterials;
            mr.shadowCastingMode = r.shadowCastingMode; tmp.Add(g); off.Add(r); r.enabled = false;
        }
        Shot0(cam, name);
        foreach (GameObject g in tmp) { Object.DestroyImmediate(g.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(g); }
        foreach (SkinnedMeshRenderer r in off) r.enabled = true;
    }
    static void Shot0(Camera cam, string name)
    {
        RenderTexture rt = RenderTexture.GetTemporary(640, 720, 24); cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; Texture2D t = new Texture2D(640, 720, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 640, 720), 0, 0); t.Apply();
        File.WriteAllBytes(Out + "/" + name + ".png", t.EncodeToPNG()); RenderTexture.active = null; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(t);
    }
}
