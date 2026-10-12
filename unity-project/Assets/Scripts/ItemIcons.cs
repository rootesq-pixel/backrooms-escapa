using System;
using System.Collections.Generic;
using UnityEngine;

// Iconos del inventario generados por código (rasterizador de distancias con suavizado), 96x96 px.
public static class ItemIcons
{
    public const int Size = 96;
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    public static Sprite Get(ItemType t) { return GetNamed(t.ToString(), t); }
    // Icono dibujado por código (respaldo si falta el render de Blender)
    public static Sprite Procedural(ItemType t)
    {
        string key = "proc_" + t;
        Sprite s;
        if (cache.TryGetValue(key, out s) && s != null) return s;
        Texture2D tex = Draw(t.ToString(), t);
        tex.name = "Icon_" + key;
        s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        s.name = "Icon_" + key;
        cache[key] = s;
        return s;
    }
    public static Sprite Note() { return GetNamed("Note", ItemType.None); }
    public static Sprite Lock() { return GetNamed("Lock", ItemType.None); }

    private static Sprite GetNamed(string key, ItemType t)
    {
        Sprite s;
        if (cache.TryGetValue(key, out s) && s != null) return s;
        // Iconos renderizados en Blender (Tools/blender/item_icons.py) si existen
        Texture2D rendered = Resources.Load<Texture2D>("Icons/" + key);
        if (rendered != null)
        {
            s = Sprite.Create(rendered, new Rect(0, 0, rendered.width, rendered.height), new Vector2(0.5f, 0.5f), 100f);
            s.name = "Icon_" + key;
            cache[key] = s;
            return s;
        }
        Texture2D tex = Draw(key, t);
        tex.name = "Icon_" + key;
        s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
        s.name = "Icon_" + key;
        cache[key] = s;
        return s;
    }

    public static void ReleaseAll()
    {
        foreach (KeyValuePair<string, Sprite> kv in cache)
        {
            if (kv.Value == null) continue;
            Texture2D tex = kv.Value.texture;
            ProceduralAssets.SafeDestroy(kv.Value);
            if (tex != null && tex.name.StartsWith("Icon_")) ProceduralAssets.SafeDestroy(tex);   // no destruir texturas de Resources
        }
        cache.Clear();
    }

    // ------------------------------------------------------------------
    // Pequeño lienzo con formas por distancia con signo
    // ------------------------------------------------------------------
    private class Canvas2D
    {
        public readonly Color[] px = new Color[Size * Size];

        public void Blend(int x, int y, Color c, float cov)
        {
            if (cov <= 0f) return;
            cov = Mathf.Clamp01(cov) * c.a;
            Color d = px[y * Size + x];
            float a = cov + d.a * (1f - cov);
            Color o = a > 0.0001f ? (c * cov + d * d.a * (1f - cov)) / a : Color.clear;
            o.a = a;
            px[y * Size + x] = o;
        }

        public void Fill(Func<float, float, float> sd, Color c)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    Blend(x, y, c, 0.5f - sd(x + 0.5f, y + 0.5f));
        }

        public void Stroke(Func<float, float, float> sd, float width, Color c)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    Blend(x, y, c, 0.5f - (Mathf.Abs(sd(x + 0.5f, y + 0.5f)) - width * 0.5f));
        }

        // Brillo radial aditivo
        public void Glow(float cx, float cy, float r, Color c, float strength)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) / r;
                    float g = Mathf.Clamp01(1f - d); g *= g;
                    if (g <= 0f) continue;
                    Color o = px[y * Size + x];
                    float a = Mathf.Clamp01(o.a + g * strength * c.a);
                    o.r = Mathf.Clamp01(o.r + c.r * g * strength); o.g = Mathf.Clamp01(o.g + c.g * g * strength); o.b = Mathf.Clamp01(o.b + c.b * g * strength);
                    o.a = a;
                    px[y * Size + x] = o;
                }
        }
    }

    // Distancias con signo (en píxeles)
    private static float Circle(float x, float y, float cx, float cy, float r) { return Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r; }

    private static float Box(float x, float y, float cx, float cy, float hx, float hy, float rad, float angDeg)
    {
        float a = -angDeg * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
        float dx = x - cx, dy = y - cy;
        float px = dx * ca - dy * sa, py = dx * sa + dy * ca;
        float qx = Mathf.Abs(px) - (hx - rad), qy = Mathf.Abs(py) - (hy - rad);
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - rad;
    }

    private static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
    {
        float pax = x - ax, pay = y - ay, bax = bx - ax, bay = by - ay;
        float h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
        float dx = pax - bax * h, dy = pay - bay * h;
        return Mathf.Sqrt(dx * dx + dy * dy) - r;
    }

    // Tronco de cono/cilindro girado (para linterna)
    private static float Trapezoid(float x, float y, float cx, float cy, float halfLen, float rBack, float rFront, float angDeg)
    {
        float a = -angDeg * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
        float dx = x - cx, dy = y - cy;
        float px = dx * ca - dy * sa, py = dx * sa + dy * ca;   // px a lo largo del eje
        float t = Mathf.Clamp01((px + halfLen) / (2f * halfLen));
        float r = Mathf.Lerp(rBack, rFront, t);
        float along = Mathf.Abs(px) - halfLen;
        float across = Mathf.Abs(py) - r;
        return Mathf.Max(along, across);
    }

    private static Color Hex(int r, int g, int b, int a = 255) { return new Color(r / 255f, g / 255f, b / 255f, a / 255f); }

    private static Texture2D Draw(string key, ItemType t)
    {
        Canvas2D c = new Canvas2D();
        float S = Size;
        Color ink = Hex(14, 12, 6);
        switch (key)
        {
            case "Flashlight":
            {
                float ang = 38f, cx = 48f, cy = 48f;
                Func<float, float, float> body = (x, y) => Box(x, y, cx - 6, cy - 6, 27f, 8.5f, 4f, ang);
                Func<float, float, float> head = (x, y) => Trapezoid(x, y, cx + 21, cy + 17, 9f, 9.5f, 14f, ang);
                // sombra
                c.Fill((x, y) => Box(x, y, cx - 3, cy - 10, 27f, 8.5f, 4f, ang), new Color(0, 0, 0, 0.35f));
                c.Fill(body, Hex(52, 52, 56));
                c.Fill((x, y) => Box(x, y, cx - 8, cy - 8, 20f, 3.2f, 1.6f, ang + 90f * 0f), new Color(1, 1, 1, 0.10f));
                // franja naranja y agarre
                c.Fill((x, y) => Box(x, y, cx - 21, cy - 21, 3f, 9f, 0f, ang), Hex(226, 140, 24));
                for (int i = 0; i < 4; i++)
                {
                    float o = -2f + i * 4.2f;
                    float ox = Mathf.Cos(ang * Mathf.Deg2Rad) * o, oy = Mathf.Sin(ang * Mathf.Deg2Rad) * o;
                    c.Fill((x, y) => Box(x, y, cx - 6 + ox, cy - 6 + oy, 1.2f, 8.6f, 0f, ang), Hex(24, 24, 26));
                }
                c.Fill(head, Hex(72, 72, 78));
                c.Stroke(head, 1.2f, Hex(24, 24, 26));
                // lente
                float lx = cx + 21 + Mathf.Cos(ang * Mathf.Deg2Rad) * 9.5f, ly = cy + 17 + Mathf.Sin(ang * Mathf.Deg2Rad) * 9.5f;
                c.Fill((x, y) => Box(x, y, lx, ly, 2.2f, 13f, 1f, ang), Hex(255, 244, 190));
                c.Glow(lx + 6, ly + 5, 30f, Hex(255, 236, 150), 0.55f);
                c.Fill((x, y) => Box(x, y, cx - 27, cy - 27, 2f, 5f, 1f, ang), Hex(220, 220, 220));   // pulsador
                break;
            }
            case "Battery":
            {
                c.Fill((x, y) => Box(x, y, 51, 45, 17f, 32f, 5f, 0f), new Color(0, 0, 0, 0.35f));
                c.Fill((x, y) => Box(x, y, 48, 48, 17f, 32f, 5f, 0f), Hex(34, 34, 36));
                c.Fill((x, y) => Box(x, y, 48, 38, 17f, 17f, 0f, 0f), Hex(70, 150, 60));
                c.Fill((x, y) => Box(x, y, 48, 56, 17f, 15f, 0f, 0f), Hex(214, 168, 40));
                c.Fill((x, y) => Box(x, y, 48, 78, 17f, 4f, 0f, 0f), Hex(190, 190, 195));
                c.Fill((x, y) => Box(x, y, 48, 11, 7f, 5f, 2f, 0f), Hex(205, 205, 210));
                c.Fill((x, y) => Box(x, y, 48, 50, 17f, 32f, 5f, 0f), new Color(0, 0, 0, 0f));
                // signo +
                c.Fill((x, y) => Box(x, y, 48, 32, 8f, 2.2f, 0f, 0f), ink);
                c.Fill((x, y) => Box(x, y, 48, 32, 2.2f, 8f, 0f, 0f), ink);
                c.Fill((x, y) => Box(x, y, 40, 40, 2f, 25f, 0f, 0f), new Color(1, 1, 1, 0.15f));
                c.Stroke((x, y) => Box(x, y, 48, 48, 17f, 32f, 5f, 0f), 1.4f, ink);
                break;
            }
            case "AlmondWater":
            {
                c.Fill((x, y) => Box(x, y, 51, 45, 20f, 33f, 8f, 0f), new Color(0, 0, 0, 0.3f));
                // botella
                c.Fill((x, y) => Box(x, y, 48, 52, 19f, 31f, 9f, 0f), Hex(205, 224, 226, 235));
                c.Fill((x, y) => Box(x, y, 48, 21, 7f, 8f, 2f, 0f), Hex(205, 224, 226, 235));
                // líquido (almendra)
                c.Fill((x, y) => Box(x, y, 48, 62, 16.5f, 20f, 7f, 0f), Hex(236, 214, 164));
                // etiqueta
                c.Fill((x, y) => Box(x, y, 48, 55, 19f, 11f, 0f, 0f), Hex(206, 120, 60));
                c.Fill((x, y) => Circle(x, y, 48, 55, 5.5f), Hex(246, 226, 190));
                c.Fill((x, y) => Capsule(x, y, 48, 51.5f, 48, 58f, 2.4f), Hex(120, 70, 34));
                // tapón
                c.Fill((x, y) => Box(x, y, 48, 11, 9f, 5f, 2f, 0f), Hex(60, 60, 64));
                c.Fill((x, y) => Box(x, y, 38, 55, 2f, 24f, 1f, 0f), new Color(1, 1, 1, 0.35f));
                c.Stroke((x, y) => Box(x, y, 48, 52, 19f, 31f, 9f, 0f), 1.3f, ink);
                break;
            }
            case "Glowstick":
            {
                float ang = 52f;
                Func<float, float, float> g = (x, y) => Capsule(x, y, 26f, 24f, 70f, 72f, 8.5f);
                c.Glow(48, 48, 46f, Hex(110, 255, 90), 0.7f);
                c.Fill((x, y) => Capsule(x, y, 28f, 22f, 72f, 70f, 8.5f), new Color(0, 0, 0, 0.35f));
                c.Fill(g, Hex(70, 210, 60));
                c.Fill((x, y) => Capsule(x, y, 28f, 26f, 68f, 70f, 5f), Hex(190, 255, 140));
                c.Fill((x, y) => Capsule(x, y, 30f, 29f, 60f, 62f, 2f), Hex(240, 255, 220));
                c.Fill((x, y) => Capsule(x, y, 24f, 22f, 28f, 26f, 8.8f), Hex(40, 60, 40));   // tapón
                c.Fill((x, y) => Capsule(x, y, 68f, 70f, 72f, 74f, 8.8f), Hex(40, 60, 40));
                c.Stroke(g, 1.3f, Hex(20, 60, 20));
                break;
            }
            case "VhsTape":
            {
                c.Fill((x, y) => Box(x, y, 51, 45, 38f, 25f, 3f, 0f), new Color(0, 0, 0, 0.35f));
                c.Fill((x, y) => Box(x, y, 48, 48, 38f, 25f, 3f, 0f), Hex(28, 28, 30));
                c.Fill((x, y) => Box(x, y, 48, 38, 30f, 11f, 1f, 0f), Hex(226, 216, 176));          // etiqueta
                c.Fill((x, y) => Box(x, y, 48, 41, 24f, 1.3f, 0f, 0f), Hex(60, 54, 40));
                c.Fill((x, y) => Box(x, y, 48, 36, 18f, 1.3f, 0f, 0f), Hex(60, 54, 40));
                c.Fill((x, y) => Box(x, y, 48, 32, 3f, 3f, 0f, 0f), Hex(190, 40, 30));              // punto REC
                c.Fill((x, y) => Box(x, y, 48, 67, 20f, 8f, 1.5f, 0f), Hex(54, 54, 58));           // ventana inferior
                c.Fill((x, y) => Circle(x, y, 38, 66, 5.5f), Hex(205, 205, 205));
                c.Fill((x, y) => Circle(x, y, 58, 66, 5.5f), Hex(205, 205, 205));
                c.Fill((x, y) => Circle(x, y, 38, 66, 2f), Hex(20, 20, 20));
                c.Fill((x, y) => Circle(x, y, 58, 66, 2f), Hex(20, 20, 20));
                c.Stroke((x, y) => Box(x, y, 48, 48, 38f, 25f, 3f, 0f), 1.4f, ink);
                break;
            }
            case "Note":
            {
                c.Fill((x, y) => Box(x, y, 52, 44, 26f, 34f, 1.5f, -4f), new Color(0, 0, 0, 0.35f));
                c.Fill((x, y) => Box(x, y, 48, 48, 26f, 34f, 1.5f, -4f), Hex(228, 216, 172));
                for (int i = 0; i < 6; i++)
                {
                    float yy = 66f - i * 8.5f;
                    float len = i == 5 ? 10f : 17f;
                    c.Fill((x, y) => Box(x, y, 46f - (17f - len) * 0.5f, yy, len, 1.1f, 0f, -4f), Hex(90, 74, 44));
                }
                c.Fill((x, y) => Box(x, y, 59, 77, 6f, 6f, 0f, 45f), Hex(190, 176, 130));      // esquina doblada
                c.Fill((x, y) => Circle(x, y, 40, 28, 3.2f), Hex(160, 50, 40, 150));            // mancha
                c.Stroke((x, y) => Box(x, y, 48, 48, 26f, 34f, 1.5f, -4f), 1.3f, ink);
                break;
            }
            case "Lock":
            {
                c.Fill((x, y) => Box(x, y, 48, 38, 18f, 14f, 3f, 0f), Hex(60, 56, 40, 200));
                c.Stroke((x, y) => Circle(x, y, 48, 56, 12f), 4f, Hex(60, 56, 40, 200));
                c.Fill((x, y) => Box(x, y, 48, 52, 3f, 7f, 1.5f, 0f), Hex(20, 18, 8));
                break;
            }
        }
        Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        // Battery / AlmondWater / VhsTape / Note están dibujados con y hacia abajo: se voltean
        bool flip = key == "Battery" || key == "AlmondWater" || key == "VhsTape" || key == "Note";
        Color[] outPx = c.px;
        if (flip)
        {
            outPx = new Color[Size * Size];
            for (int y = 0; y < Size; y++) Array.Copy(c.px, y * Size, outPx, (Size - 1 - y) * Size, Size);
        }
        tex.SetPixels(outPx);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply(false, false);
        return tex;
    }
}
