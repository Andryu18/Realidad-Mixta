using UnityEngine;

namespace RallyMixto
{
    // texturas generadas por codigo
    public static class ProcTex
    {
        static Texture2D New(int w, int h, bool mips = true)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, mips) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
        }

        // piso: losas con lineas finas y ruido suave
        public static Texture2D Floor()
        {
            int n = 1024;
            var t = New(n, n);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float noise = Mathf.PerlinNoise(x * 0.02f, y * 0.02f) * 0.5f + Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.5f;
                    float g = 0.16f + noise * 0.05f;
                    int mx = x % 256, my = y % 256;
                    bool major = mx < 3 || my < 3;
                    bool minor = (x % 64 < 1) || (y % 64 < 1);
                    if (major) g += 0.22f;
                    else if (minor) g += 0.06f;
                    px[y * n + x] = new Color(g, g, g, 1);
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        // flechas del turbo
        public static Texture2D Chevrons()
        {
            int w = 256, h = 512;
            var t = New(w, h);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = Mathf.Abs(x - w / 2f) / (w / 2f);
                    float v = (y % 128) / 128f;
                    float d = v - (1 - u) * 0.45f;
                    bool on = d > 0.1f && d < 0.42f && u < 0.85f;
                    float edge = (x < 8 || x > w - 9) ? 1 : 0;
                    float a = Mathf.Max(on ? 1 : 0.06f, edge);
                    px[y * w + x] = new Color(a, a, a, 1);
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        public static Texture2D Checker(int cells = 8)
        {
            int n = 256;
            var t = New(n, n);
            t.filterMode = FilterMode.Bilinear;
            var px = new Color32[n * n];
            int s = n / cells;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool w = ((x / s) + (y / s)) % 2 == 0;
                    px[y * n + x] = w ? new Color32(235, 235, 230, 255) : new Color32(18, 18, 22, 255);
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        // brillo radial (ui y piso)
        public static Texture2D Radial(int n, Color c, float power = 2f)
        {
            var t = New(n, n, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2 - 1, dy = (y + 0.5f) / n * 2 - 1;
                    float a = Mathf.Pow(Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy)), power);
                    px[y * n + x] = new Color(c.r, c.g, c.b, c.a * a);
                }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        // degradado vertical para fondos de pantalla
        public static Texture2D Vertical(Color top, Color bottom, int h = 256)
        {
            var t = new Texture2D(4, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[4 * h];
            for (int y = 0; y < h; y++)
            {
                var c = Color.Lerp(bottom, top, y / (h - 1f));
                for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
            }
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }

        // lineas diagonales sutiles para paneles
        public static Texture2D Hatch(Color c, int n = 64)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = ((x + y) % 16 < 2) ? c : new Color(c.r, c.g, c.b, 0);
            t.SetPixels(px);
            t.Apply(false);
            return t;
        }
    }
}
