using System.Collections;
using UnityEngine;

// v7.1.3: agua y playa de la Poolrooms ajustadas a los renders de Cycles (pool-render/pool_1..3): parámetros globales
// (PoolLook), cáusticas del fondo solo con la cámara bajo el agua, reflejo planar con mips en la playa y banda oscura del vaso.
public static class V713Checks
{
    public static void AssetChecks(SmokeTest.Report r)
    {
        const string T = "[v7.1.3] ";
        Shader pb = Shader.Find("Backrooms/PoolBaked"), w = Shader.Find("Hidden/BackroomsWater"), c = Shader.Find("Hidden/BackroomsCaustics");
        r.Check(pb != null && pb.isSupported || pb != null && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null, T + "shader PoolBaked disponible");
        r.Check(pb != null && pb.FindPropertyIndex("_PlanarK") >= 0 && pb.FindPropertyIndex("_WaterBand") >= 0 && pb.FindPropertyIndex("_LMTint") >= 0, T + "PoolBaked con _PlanarK, _WaterBand y _LMTint");
        r.Check(w != null && c != null, T + "shaders de agua y cáusticas disponibles");
        string ws = System.IO.File.ReadAllText("Assets/Resources/Water/BackroomsWater.shader"), cs = System.IO.File.ReadAllText("Assets/Resources/Water/BackroomsCaustics.shader");
        r.Check(ws.Contains("_PoolWaterTint") && ws.Contains("_PoolWaveP") && ws.Contains("sqrt(9.81 * k)"), T + "agua: tinte, absorción y oleaje analítico de la sala en el shader");
        r.Check(cs.Contains("_PoolCausticK"), T + "cáusticas: atenuación de la sala fuera del agua en el shader");
        r.Check(PoolLook.WaterAbs.w > 0.5f && PoolLook.CausticK.w > 0.5f && PoolLook.CausticK.x < 0.5f, T + "PoolLook: modo v7.1.3 activo y cáusticas del fondo desde fuera <0,5 (" + PoolLook.CausticK.x + ")");
        r.Check(PoolLook.WaterTint.z > PoolLook.WaterTint.x && PoolLook.WaterAbs.x > PoolLook.WaterAbs.z, T + "PoolLook: agua azul (tinte B > R, absorción R > B)");
    }

    public static IEnumerator LevelChecks(SmokeTest.Report r, string tag, int lv, LevelConfig cfg, GameManager gm)
    {
        string T = tag + "[v7.1.3 agua] ";
        BackroomsGenerator gen = gm != null ? gm.generator : null;
        bool fixedPool = gen != null && gen.PoolFixed && PoolRoomSetPiece.Root != null && PoolRoomSetPiece.HasBakedAssets;
        // toda el agua del nivel Poolrooms (pasillos y salas inundadas, vaso) con el aspecto nuevo; el resto de niveles, sin tocar
        int waterR = 0, newR = 0; string bad = "";
        foreach (Renderer rr in Object.FindObjectsOfType<Renderer>())
        {
            bool isWater = rr.gameObject.layer == WaterVolume.WaterLayer && !(rr is ParticleSystemRenderer);
            foreach (Material mm in rr.sharedMaterials) if (mm != null && mm.shader != null && mm.shader.name.Contains("Water") && !(rr is ParticleSystemRenderer)) isWater = true;
            if (!isWater) continue;
            waterR++;
            bool ok = rr.sharedMaterials.Length > 0;
            foreach (Material mm in rr.sharedMaterials) if (mm == null || mm.shader == null || mm.shader.name != "Hidden/BackroomsWater") ok = false;
            if (ok) newR++; else if (bad.Length < 200) bad += " " + rr.name;
        }
        if (cfg.index == 2)
        {
            r.Check(waterR > 0 && newR == waterR, T + "todo el agua del nivel usa el shader de agua v7.1.3: " + newR + "/" + waterR + " renderers" + bad);
            r.Check(Shader.GetGlobalFloat("_PoolLookAll") > 0.5f && PoolLook.LevelWide && Shader.GetGlobalVector("_PoolWaterAbs").w > 0.5f, T + "aspecto nuevo en TODO el nivel (_PoolLookAll = 1), no solo en la sala");
            r.Check(PoolPlanarReflection.Instance != null && Mathf.Abs(PoolPlanarReflection.Instance.waterY - WaterVolume.Active.surfaceY) < 0.01f, T + "un reflejo planar para todo el nivel, a la altura del agua");
            int nPl = Object.FindObjectsOfType<PoolPlanarReflection>().Length;
            r.Check(nPl == 1, T + "un solo reflejo planar activo (" + nPl + ")");
        }
        else
            r.Check(Shader.GetGlobalFloat("_PoolLookAll") < 0.5f, T + "fuera de los Poolrooms el agua no usa el aspecto del nivel 2 (" + waterR + " renderers de agua)");
        if (!fixedPool)
        {
            if (cfg.index != 2) r.Check(Shader.GetGlobalVector("_PoolWaterAbs").w == 0f && Shader.GetGlobalVector("_PoolCausticK").w == 0f, T + "sin sala de piscina: parámetros de la sala apagados (agua y cáusticas como antes)");
            yield break;
        }
        r.Check(Shader.GetGlobalVector("_PoolWaterAbs").w > 0.5f, T + "parámetros de la sala activos (_PoolWaterAbs.w)");
        Transform bk = PoolRoomSetPiece.Root.transform.Find("PoolRoomBaked");
        MeshRenderer mr = bk != null ? bk.GetComponent<MeshRenderer>() : null;
        Material[] mats = mr != null ? mr.sharedMaterials : new Material[0];
        r.Check(mats.Length >= 4 && mats[1].GetFloat("_PlanarK") > 0f, T + "playa con reflejo planar (_PlanarK " + (mats.Length > 1 ? mats[1].GetFloat("_PlanarK") : -1f) + ")");
        r.Check(mats.Length >= 4 && mats[2].GetFloat("_WaterBand") > 0.3f, T + "vaso con banda oscura sobre el agua");
        r.Check(mats.Length >= 4 && Mathf.Abs(mats[2].GetTextureScale("_MainTex").x - PoolLook.PoolTileScale) < 1e-4f, T + "baldosa del vaso a escala " + PoolLook.PoolTileScale + " (como en los renders)");
        r.Check(Shader.GetGlobalVector("_PoolEdgeP").x > 0f, T + "línea oscura del agua junto al vaso activa");
        Color lt = mats.Length > 0 ? mats[0].GetColor("_LMTint") : Color.black;
        r.Check(lt.b >= lt.r && lt.g > 0.5f, T + "luz horneada con tinte frío " + lt);
        PoolPlanarReflection pl = PoolPlanarReflection.Instance;
        r.Check(pl != null, T + "reflejo planar presente");
        if (pl == null) yield break;
        GameObject go = new GameObject("V713TestCam"); Camera cam = go.AddComponent<Camera>(); cam.enabled = false;
        try
        {
            cam.transform.position = pl.center + new Vector3(0f, 1.6f, 0f); cam.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            pl.Refresh(cam);
            Vector4 ka = Shader.GetGlobalVector("_PoolCausticK");
            cam.transform.position = new Vector3(pl.center.x, pl.waterY - 0.6f, pl.center.z);
            pl.Refresh(cam);
            Vector4 kb = Shader.GetGlobalVector("_PoolCausticK");
            float nw = Shader.GetGlobalVector("_PoolWaveK").w;
            r.Check(nw == (GameSettings.Quality == 0 ? 4f : 8f), T + "oleaje: " + nw + " ondas (Bajo 4, resto 8)");
            r.Check(ka.z == 0f && kb.z == 1f && ka.x < 0.5f, T + "cáusticas del fondo: atenuadas con la cámara fuera del agua (" + ka.x + "), enteras bajo el agua");
            bool gfx = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            if (gfx && GameSettings.Quality >= 1)
            {
                cam.transform.position = pl.center + new Vector3(0f, 1.6f, 0f);
                pl.Refresh(cam);
                RenderTexture rt = Shader.GetGlobalTexture("_PoolPlanarTex") as RenderTexture;
                r.Check(Shader.GetGlobalFloat("_PoolPlanarOn") > 0.5f && rt != null && rt.useMipMap, T + "reflejo planar activo y con mips (playa borrosa por rugosidad)");
            }
            else r.Info(T + "reflejo planar no comprobado (sin GPU o calidad Bajo)");
        }
        finally { Object.DestroyImmediate(go); }
        yield return null;
        pl.Refresh(Camera.main);
    }
}
