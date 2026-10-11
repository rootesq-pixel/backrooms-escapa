using System.IO;
using UnityEditor;
using UnityEngine;

// Renderiza PNG de cada monstruo (vista 3/4 + frontal) con una cámara temporal. Requiere gráficos (no usar -nographics):
//   xvfb-run -a Unity -batchmode -quit -projectPath <p> -executeMethod MonsterPreviewExporter.Export -force-glcore
public static class MonsterPreviewExporter
{
    public const string OutDir = "/workspace/monster-previews";

    public static void Export()
    {
        Directory.CreateDirectory(OutDir);
        foreach (MonsterProfile p in MonsterProfile.All) Render(p, "Run", 0.3f, "_run");
        foreach (MonsterProfile p in MonsterProfile.All) Render(p, "Idle", 0.2f, "_idle");
        Debug.Log("[MonsterPreviewExporter] OK");
    }

    public static Bounds LiveBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds();
        bool first = true;
        foreach (Renderer r in rs)
        {
            if (r.GetComponent<MeshFilter>() != null && r.name.StartsWith("Eye")) continue;
            if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static void Render(MonsterProfile p, string clipName, float t, string suffix)
    {
        GameObject prefab = Resources.Load<GameObject>("Monsters/" + p.id);
        if (prefab == null) { Debug.LogError("Prefab no encontrado " + p.id); return; }
        GameObject inst = (GameObject)Object.Instantiate(prefab);
        inst.transform.position = Vector3.zero;

        string path = "Assets/Monsters/Models/" + (string.IsNullOrEmpty(p.modelFile) ? p.id : p.modelFile) + ".fbx";
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            AnimationClip c = o as AnimationClip;
            if (c != null && MonsterModelPostprocessor.Clean(c.name) == clipName)
            {
                c.SampleAnimation(inst.transform.Find("Model").gameObject, c.length * t);
                break;
            }
        }

        // Plano de suelo + fondo oscuro con niebla de nivel
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.localScale = new Vector3(2, 1, 2);
        floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.25f, 0.22f, 0.12f) };

        GameObject camGo = new GameObject("cam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.065f, 0.045f);
        Bounds bb = LiveBounds(inst); float h = Mathf.Max(bb.size.y, Mathf.Max(bb.size.x, bb.size.z) * 0.8f, 1.6f);
        Vector3 target = new Vector3(0f, bb.center.y, 0f);
        cam.fieldOfView = 35f;
        Vector3[] views = { new Vector3(0.55f, 0.1f, 1f), new Vector3(0f, 0.05f, 1f) };

        GameObject l1 = new GameObject("key"); Light key = l1.AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.1f; key.color = new Color(1f, 0.93f, 0.75f);
        l1.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
        GameObject l2 = new GameObject("rim"); Light rim = l2.AddComponent<Light>(); rim.type = LightType.Directional; rim.intensity = 0.5f; rim.color = new Color(0.6f, 0.7f, 1f);
        l2.transform.rotation = Quaternion.Euler(20f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.28f, 0.26f, 0.2f);

        // Ojos procedurales (si los hay) para la vista previa
        MonsterVisual.AttachEyes(inst.transform, p);

        int w = 640, hh = 800;
        RenderTexture rt = new RenderTexture(w, hh, 24);
        cam.targetTexture = rt;
        Texture2D tex = new Texture2D(w * 2, hh, TextureFormat.RGB24, false);
        for (int v = 0; v < 2; v++)
        {
            cam.transform.position = target + views[v].normalized * h * 2.3f;
            cam.transform.LookAt(target);
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, hh), v * w, 0);
            RenderTexture.active = null;
        }
        tex.Apply();
        Debug.Log("[MonsterPreviewExporter] " + p.id + suffix + " bounds=" + bb.size + " center=" + bb.center); File.WriteAllBytes(OutDir + "/" + p.id + suffix + ".png", tex.EncodeToPNG());

        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(inst);
        Object.DestroyImmediate(floor);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(l1);
        Object.DestroyImmediate(l2);
    }
}
