using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Exporta las texturas procedurales (albedo + normal) de cada nivel a PNG y una hoja de contacto.
// Uso: Unity -batchmode -nographics -quit -projectPath <proyecto> -executeMethod TexturePreviewExporter.Export
public static class TexturePreviewExporter
{
    private const string OutDir = "/workspace/texture-previews";

    [MenuItem("Backrooms/Exportar previsualización de texturas")]
    public static void Export()
    {
        Directory.CreateDirectory(OutDir);
        Directory.CreateDirectory(OutDir + "/normal");
        bool prevKeep = TextureLibrary.KeepReadable, prevBaked = ProceduralTextures.UseBaked;
        TextureLibrary.KeepReadable = true;
        ProceduralTextures.UseBaked = false;   // este exportador vuelca las texturas generadas por CPU (legibles); las de Blender: TextureV66Preview
        TextureLibrary.ReleaseAll();

        const int tile = 480;
        const int cols = 3;
        const int margin = 10;
        int rows = LevelConfig.Count;
        int sheetW = cols * tile + (cols + 1) * margin;
        int rowH = tile + margin;
        int sheetH = rows * rowH + margin;
        Color32[] sheet = new Color32[sheetW * sheetH];
        for (int i = 0; i < sheet.Length; i++) sheet[i] = new Color32(18, 18, 18, 255);

        StringBuilder labels = new StringBuilder();
        HashSet<string> written = new HashSet<string>();

        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            LevelConfig cfg = LevelConfig.Get(lv);
            SurfaceStyle[] styles = { cfg.wallStyle, cfg.floorStyle, cfg.ceilingStyle };
            string[] roles = { "pared", "suelo", "techo" };
            for (int k = 0; k < 3; k++)
            {
                SurfaceSet set = TextureLibrary.Get(styles[k], cfg.wallHeight);
                string baseName = "L" + lv + "_" + roles[k] + "_" + styles[k];
                if (written.Add(baseName))
                {
                    File.WriteAllBytes(OutDir + "/" + baseName + ".png", set.albedo.EncodeToPNG());
                    File.WriteAllBytes(OutDir + "/normal/" + baseName + "_normal.png", set.normal.EncodeToPNG());
                }
                Blit(set.albedo, sheet, sheetW, margin + k * (tile + margin), margin + (rows - 1 - lv) * rowH, tile);
                // Posición de la etiqueta (x, y desde arriba) para el postproceso
                int topY = margin + lv * rowH;
                labels.AppendLine("Nivel " + lv + " " + roles[k] + ": " + styles[k] + " (" + set.albedo.width + "px)\t" + (margin + k * (tile + margin)) + "\t" + topY);
            }
        }

        Texture2D sheetTex = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false);
        sheetTex.SetPixels32(sheet);
        sheetTex.Apply();
        File.WriteAllBytes(OutDir + "/contact-sheet.png", sheetTex.EncodeToPNG());
        File.WriteAllText(OutDir + "/contact-sheet-labels.tsv", labels.ToString());
        Object.DestroyImmediate(sheetTex);

        // Normal del agua
        SurfaceSet water = TextureLibrary.Get(SurfaceStyle.WaterNormal, 3f);
        File.WriteAllBytes(OutDir + "/normal/water_ripples_normal.png", water.normal.EncodeToPNG());

        TextureLibrary.ReleaseAll();
        TextureLibrary.KeepReadable = prevKeep;
        ProceduralTextures.UseBaked = prevBaked;
        Debug.Log("[TexturePreviewExporter] Exportado a " + OutDir + " (" + written.Count + " texturas, hoja " + sheetW + "x" + sheetH + ")");
    }

    // Copia (reduce con muestreo bilineal) una textura legible a la hoja; y0 = fila inferior en coordenadas de textura
    private static void Blit(Texture2D src, Color32[] dst, int dstW, int x0, int y0, int size)
    {
        Color32[] px = src.GetPixels32();
        int sw = src.width, sh = src.height;
        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size * sh - 0.5f;
            int ya = Mathf.Clamp(Mathf.FloorToInt(v), 0, sh - 1), yb = Mathf.Min(ya + 1, sh - 1);
            float fy = v - ya;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * sw - 0.5f;
                int xa = Mathf.Clamp(Mathf.FloorToInt(u), 0, sw - 1), xb = Mathf.Min(xa + 1, sw - 1);
                float fx = u - xa;
                Color a = Color.Lerp(px[ya * sw + xa], px[ya * sw + xb], fx);
                Color b = Color.Lerp(px[yb * sw + xa], px[yb * sw + xb], fx);
                dst[(y0 + y) * dstW + x0 + x] = Color.Lerp(a, b, fy);
            }
        }
    }
}
