using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.0.2: capturas antes/después de los objetos recogibles (primitivas de antes vs modelos realistas), sobre una mesa de madera.
//   Unity -batchmode -quit -executeMethod ItemShots.Export -force-glcore  -> /workspace/v702shots/items_{before,after}.png
public static class ItemShots
{
    public static void Export()
    {
        Directory.CreateDirectory("/workspace/v702shots");
        foreach (bool after in new[] { false, true }) Shot(after);
        ItemPickup.UseModels = true;
        Debug.Log("[ItemShots] OK");
    }

    private static void Shot(bool after)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        ItemPickup.UseModels = after;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.32f, 0.31f, 0.3f);
        Material wood = new Material(Shader.Find("Standard"));
        Texture2D wt = Resources.Load<Texture2D>("Field/house_floor_a"); if (wt != null) { wood.mainTexture = wt; wood.mainTextureScale = new Vector2(2f, 1f); }
        wood.color = new Color(0.55f, 0.45f, 0.36f); wood.SetFloat("_Glossiness", 0.25f);
        GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube); table.transform.position = new Vector3(0f, -0.025f, 0f); table.transform.localScale = new Vector3(1.9f, 0.05f, 0.8f);
        table.GetComponent<Renderer>().sharedMaterial = wood;
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0f, 0.5f, 0.45f); wall.transform.localScale = new Vector3(3f, 1.2f, 0.05f);
        Material wm = new Material(Shader.Find("Standard")); wm.color = new Color(0.42f, 0.4f, 0.34f); wall.GetComponent<Renderer>().sharedMaterial = wm;
        ItemType[] ts = { ItemType.Battery, ItemType.AlmondWater, ItemType.Glowstick, ItemType.VhsTape, ItemType.None, ItemType.Flashlight };
        float[] yaw = { 20f, -10f, 25f, -12f, 8f, -18f };
        for (int i = 0; i < ts.Length; i++)
        {
            GameObject g = new GameObject("it" + i);
            g.transform.position = new Vector3(-0.75f + i * 0.3f, after ? 0f : 0.12f, i % 2 == 0 ? -0.05f : 0.08f);
            g.transform.rotation = Quaternion.Euler(0f, yaw[i], 0f);
            ItemPickup.BuildVisual(ts[i], g.transform);
            ItemHighlight hl = g.GetComponent<ItemHighlight>(); if (hl != null) Object.DestroyImmediate(hl);
        }
        GameObject lg = new GameObject("Key"); Light l = lg.AddComponent<Light>(); l.type = LightType.Spot; l.spotAngle = 70f; l.range = 6f; l.intensity = 2.2f; l.color = new Color(1f, 0.93f, 0.8f);
        lg.transform.position = new Vector3(-0.6f, 1.3f, -0.9f); lg.transform.LookAt(Vector3.zero); l.shadows = LightShadows.Soft;
        GameObject fg = new GameObject("Fill"); Light f = fg.AddComponent<Light>(); f.type = LightType.Point; f.range = 4f; f.intensity = 0.5f; f.color = new Color(0.7f, 0.8f, 1f); fg.transform.position = new Vector3(1f, 0.6f, -0.6f);
        GameObject cg = new GameObject("Cam"); Camera cam = cg.AddComponent<Camera>(); cam.fieldOfView = 34f; cam.nearClipPlane = 0.02f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.08f, 0.08f, 0.08f);
        cg.transform.position = new Vector3(0f, 0.55f, -1.35f); cg.transform.LookAt(new Vector3(0f, 0.04f, 0f));
        RenderTexture rt = new RenderTexture(1600, 640, 24); rt.antiAliasing = 4;
        cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        Texture2D t = new Texture2D(1600, 640, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 1600, 640), 0, 0); t.Apply();
        File.WriteAllBytes("/workspace/v702shots/items_" + (after ? "after" : "before") + ".png", t.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = null;
    }
}
