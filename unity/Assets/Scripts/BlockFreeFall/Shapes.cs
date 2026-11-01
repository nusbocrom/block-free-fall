using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockFreeFall
{
    /// <summary>絵を持たずに済むよう、四角・丸・三角のスプライトをプログラムで作る。縁取り付き。</summary>
    public static class Shapes
    {
        const float Ppu = 32f;       // 1ユニットあたりのピクセル数
        const int OutlinePx = 3;
        static readonly Dictionary<string, Sprite> cache = new();

        public static Sprite Box(float w, float h, bool outline = true)
        {
            string key = $"b{w:F3}_{h:F3}_{outline}";
            if (cache.TryGetValue(key, out var s)) return s;
            int pw = Mathf.Max(2, Mathf.RoundToInt(w * Ppu)), ph = Mathf.Max(2, Mathf.RoundToInt(h * Ppu));
            s = Make(pw, ph, new Vector2(0.5f, 0.5f), (x, y) => true, outline);
            cache[key] = s;
            return s;
        }

        /// <summary>点線の1周期(縦方向)。上半分だけ塗る。SpriteRendererのTiledで並べて使う。</summary>
        public static Sprite Dash()
        {
            const string key = "dash";
            if (cache.TryGetValue(key, out var s)) return s;
            s = Make(4, 16, new Vector2(0.5f, 0.5f), (x, y) => y >= 8f, false);
            cache[key] = s;
            return s;
        }

        public static Sprite Circle(float r, bool outline = true)
        {
            string key = $"c{r:F3}_{outline}";
            if (cache.TryGetValue(key, out var s)) return s;
            int d = Mathf.Max(4, Mathf.RoundToInt(2f * r * Ppu));
            float rr = d / 2f;
            s = Make(d, d, new Vector2(0.5f, 0.5f),
                (x, y) => (x - rr) * (x - rr) + (y - rr) * (y - rr) <= rr * rr, outline);
            cache[key] = s;
            return s;
        }

        /// <summary>頂点が上向きの正三角形(外接円の半径=r)。中心(重心)を原点にする。</summary>
        public static Sprite Triangle(float r, bool outline = true)
        {
            string key = $"t{r:F3}_{outline}";
            if (cache.TryGetValue(key, out var s)) return s;
            int pw = Mathf.CeilToInt(2f * 0.8660254f * r * Ppu), ph = Mathf.CeilToInt(1.5f * r * Ppu);
            var a = new Vector2(pw / 2f, ph);
            var b = new Vector2(pw, 0f);
            var c = new Vector2(0f, 0f);
            s = Make(pw, ph, new Vector2(0.5f, 1f / 3f), (x, y) =>
            {
                var p = new Vector2(x, y);
                float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
                bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                return !(neg && pos);
            }, outline);
            cache[key] = s;
            return s;
        }

        /// <summary>角丸の四角(画面のボタンや枠用)。角の部分を固定して引き伸ばせる(9分割)。</summary>
        public static Sprite RoundRect()
        {
            const string key = "rr";
            if (cache.TryGetValue(key, out var s)) return s;
            const int size = 128;      // 引き伸ばしても縁がギザギザにならないよう大きめに作る
            const float r = 48f;
            s = Make(size, size, new Vector2(0.5f, 0.5f), (x, y) =>
            {
                float cx = Mathf.Clamp(x, r, size - r), cy = Mathf.Clamp(y, r, size - r);
                float dx = x - cx, dy = y - cy;
                return dx * dx + dy * dy <= r * r;
            }, false, new Vector4(r, r, r, r));
            cache[key] = s;
            return s;
        }

        static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

        static Sprite Make(int w, int h, Vector2 pivot, Func<float, float, bool> inside, bool outline, Vector4 border = default)
        {
            var mask = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    mask[y * w + x] = inside(x + 0.5f, y + 0.5f);

            var px = new Color32[w * h];
            var clear = new Color32(0, 0, 0, 0);
            var white = new Color32(255, 255, 255, 255);
            var line = new Color32(40, 40, 40, 255);   // 色を掛けると、同じ色相の濃い縁になる
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!outline)
                    {
                        int hit = 0;
                        for (int sy = 0; sy < 3; sy++)
                            for (int sx = 0; sx < 3; sx++)
                                if (inside(x + (sx + 0.5f) / 3f, y + (sy + 0.5f) / 3f)) hit++;
                        px[i] = hit == 0 ? clear : new Color32(255, 255, 255, (byte)(255 * hit / 9));
                        continue;
                    }
                    if (!mask[i]) { px[i] = clear; continue; }
                    bool edge = false;
                    if (outline)
                    {
                        for (int k = 1; k <= OutlinePx && !edge; k++)
                            edge = !At(mask, w, h, x + k, y) || !At(mask, w, h, x - k, y)
                                || !At(mask, w, h, x, y + k) || !At(mask, w, h, x, y - k);
                    }
                    px[i] = edge ? line : white;
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, Ppu, 0, SpriteMeshType.FullRect, border);
        }

        static bool At(bool[] m, int w, int h, int x, int y) => x >= 0 && y >= 0 && x < w && y < h && m[y * w + x];
    }
}
