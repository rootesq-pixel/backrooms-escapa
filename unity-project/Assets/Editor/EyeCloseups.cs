using System.IO;
using UnityEditor;
using UnityEngine;

// v7.0.2: primeros planos de la cabeza (frente y perfil) de cada monstruo con ojos, antes (sin encajar) y después.
//   xvfb-run -a Unity -batchmode -quit -projectPath <p> -executeMethod EyeCloseups.Export -force-glcore  -> /workspace/v702shots/eyes_*.png
public static class EyeCloseups
{
    public static float WideK = 1f;
    public static void Wide() { WideK = 3f; Export(); }
    public const string OutDir = "/workspace/v702shots";
    public static void Export()
    {
        Directory.CreateDirectory(OutDir);
        foreach (MonsterProfile p in MonsterProfile.All)
        {
            if (!p.eyes) continue;
            foreach (bool before in new[] { true, false })
            {
                MonsterVisual.SkipSeatForCaptures = before;
                Shot(p, before ? "before" : "after");
            }
        }
        MonsterVisual.SkipSeatForCaptures = false;
        Debug.Log("[EyeCloseups] OK");
    }

    private static void Shot(MonsterProfile p, string tag)
    {
        GameObject prefab = Resources.Load<GameObject>("Monsters/" + p.id);
        if (prefab == null) return;
        GameObject inst = (GameObject)Object.Instantiate(prefab);
        inst.transform.position = Vector3.zero;
        Animator an = inst.GetComponentInChildren<Animator>();
        if (an != null) { an.Rebind(); an.Play("Idle", 0, 0.3f); an.Update(0f); }
        Transform model = inst.transform.Find("Model");
        Transform root = model != null ? model.parent : inst.transform;
        Transform old = root.Find("Eyes"); if (old != null) Object.DestroyImmediate(old.gameObject);
        Transform eyes = MonsterVisual.AttachEyes(root, p);
        Vector3 head = eyes != null ? eyes.position : Vector3.up * p.size * 0.9f;
        float dist = Mathf.Max(0.45f, p.size * 0.22f) * WideK;
        SkinnedMeshRenderer skd = inst.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skd != null) foreach (Transform b in skd.bones) if (b != null && b.name.ToLower().Contains("head")) Debug.Log("[EyeDiag] " + p.id + " bone " + b.name + " " + b.position.ToString("F3") + " eyes " + head.ToString("F3") + " fwd " + root.forward);
        GameObject lg = new GameObject("L"); Light l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.1f; lg.transform.rotation = Quaternion.Euler(25f, 160f, 0f);
        GameObject cg = new GameObject("C"); Camera cam = cg.AddComponent<Camera>(); cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.16f, 0.17f); cam.fieldOfView = 30f; cam.nearClipPlane = 0.02f;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f);
        RenderTexture rt = new RenderTexture(640, 640, 24);
        cam.targetTexture = rt;
        string[] names = { "front", "side" };
        Vector3[] dirs = { root.forward, root.right };
        Texture2D outTex = new Texture2D(1280, 640, TextureFormat.RGB24, false);
        for (int i = 0; i < 2; i++)
        {
            cam.transform.position = head + dirs[i] * dist + Vector3.up * 0.02f;
            cam.transform.LookAt(head);
            cam.Render();
            RenderTexture.active = rt;
            Texture2D t = new Texture2D(640, 640, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); t.Apply();
            outTex.SetPixels(i * 640, 0, 640, 640, t.GetPixels());
            Object.DestroyImmediate(t);
        }
        outTex.Apply();
        File.WriteAllBytes(OutDir + "/eyes_" + p.id + "_" + tag + ".png", outTex.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(outTex); Object.DestroyImmediate(cg); Object.DestroyImmediate(lg); Object.DestroyImmediate(inst);
    }
}
