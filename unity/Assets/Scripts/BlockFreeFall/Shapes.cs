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

        /// <summary>true: ガラス風(グラデーション+つや)。false: フラットでモダン(色の面だけ)。</summary>
        public const bool Glossy = false;

        /// <summary>四角の角の丸み(短い辺に対する割合)。0なら直角。</summary>
        public const float CornerRatio = 0.05f;
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

        /// <summary>
        /// 小さなアイコン(白。色は掛けて使う)。kind: down=太い下向き矢印 / speaker / mute / moon / sun
        /// 絵文字が使えないUnityで、Web版の 🔊🌙☀⬇ の代わりにする。
        /// </summary>
        public static Sprite Icon(string kind)
        {
            string key = "icon_" + kind;
            if (cache.TryGetValue(key, out var s)) return s;
            const int n = 96;
            Func<float, float, bool> shape = kind switch
            {
                "down" => IconDown,
                "speaker" => (u, v) => IconSpeaker(u, v, false),
                "mute" => (u, v) => IconSpeaker(u, v, true),
                "moon" => IconMoon,
                "sun" => IconSun,
                _ => (u, v) => false
            };
            s = Make(n, n, new Vector2(0.5f, 0.5f), (x, y) => shape(x / n, y / n), false);
            cache[key] = s;
            return s;
        }

        static bool IconDown(float u, float v)
        {
            bool shaft = u >= 0.39f && u <= 0.61f && v >= 0.38f && v <= 0.88f;
            bool head = v >= 0.1f && v <= 0.5f && Mathf.Abs(u - 0.5f) <= (v - 0.1f) / 0.4f * 0.34f;
            return shaft || head;
        }

        static bool IconSpeaker(float u, float v, bool muted)
        {
            bool body = u >= 0.1f && u <= 0.26f && v >= 0.38f && v <= 0.62f;
            bool cone = u > 0.26f && u <= 0.48f && Mathf.Abs(v - 0.5f) <= 0.12f + (u - 0.26f) / 0.22f * 0.2f;
            if (body || cone) return true;
            if (!muted)
            {
                float dx = u - 0.48f, dy = v - 0.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                bool right = dx > 0f && Mathf.Abs(Mathf.Atan2(dy, dx)) < 0.85f;
                return right && (Mathf.Abs(d - 0.22f) < 0.032f || Mathf.Abs(d - 0.34f) < 0.032f);
            }
            float x = u - 0.74f, y = v - 0.5f;
            bool box = Mathf.Abs(x) < 0.15f && Mathf.Abs(y) < 0.15f;
            return box && (Mathf.Abs(x - y) < 0.05f || Mathf.Abs(x + y) < 0.05f);
        }

        static bool IconMoon(float u, float v)
        {
            float d1 = Mathf.Sqrt((u - 0.46f) * (u - 0.46f) + (v - 0.5f) * (v - 0.5f));
            float d2 = Mathf.Sqrt((u - 0.62f) * (u - 0.62f) + (v - 0.58f) * (v - 0.58f));
            return d1 < 0.34f && d2 > 0.28f;
        }

        static bool IconSun(float u, float v)
        {
            float x = u - 0.5f, y = v - 0.5f;
            if (x * x + y * y < 0.17f * 0.17f) return true;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f, dx = Mathf.Cos(a), dy = Mathf.Sin(a);
                float along = x * dx + y * dy, across = Mathf.Abs(-x * dy + y * dx);
                if (along >= 0.26f && along <= 0.4f && across < 0.032f) return true;
            }
            return false;
        }

        /// <summary>
        /// お椀の形(白)。中央が y=0 で最も低く、縁が rise だけ高い楔形(下は y=0 の線)。grow だけ外へ広げる(縁取り用)。
        /// 絵の基準の位置は、中央の面(原点)。まわりに透明な余白を足して、縁をやわらかく透けさせる。
        /// </summary>
        public static Sprite BowlWedge(float halfWidth, float rise, float grow)
        {
            string key = $"bowl{halfWidth:F2}_{rise:F2}_{grow:F3}";
            if (cache.TryGetValue(key, out var s)) return s;
            const float ppu = 64f;
            const int pad = 2;
            float x0 = -(halfWidth + grow), x1 = halfWidth + grow, y0 = -grow, y1 = rise + grow;
            int w = Mathf.CeilToInt((x1 - x0) * ppu), h = Mathf.CeilToInt((y1 - y0) * ppu);
            int W = w + 2 * pad, H = h + 2 * pad;
            var px = new Color32[W * H];
            for (int ty = 0; ty < H; ty++)
            {
                for (int tx = 0; tx < W; tx++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 4; sy++)
                    {
                        float wy = y0 + (ty - pad + (sy + 0.5f) / 4f) / ppu;
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float wx = x0 + (tx - pad + (sx + 0.5f) / 4f) / ppu;
                            if (wx < x0 || wx > x1 || wy < y0) continue;
                            float u = Mathf.Clamp(wx / halfWidth, -1f, 1f);
                            if (wy <= rise * u * u + grow) hit++;
                        }
                    }
                    px[ty * W + tx] = hit == 0 ? new Color32(0, 0, 0, 0) : new Color32(255, 255, 255, (byte)(255f * hit / 16f));
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            var pivot = new Vector2((-x0 * ppu + pad) / W, (-y0 * ppu + pad) / H);
            s = Sprite.Create(tex, new Rect(0, 0, W, H), pivot, ppu, 0, SpriteMeshType.FullRect);
            cache[key] = s;
            return s;
        }

        /// <summary>注意の帯の縞模様(黄と黒、斜め)。つなげて並べても、継ぎ目が出ない。</summary>
        public static Sprite Stripes()
        {
            const string key = "stripes";
            if (cache.TryGetValue(key, out var s)) return s;
            const int n = 240;
            var px = new Color32[n * n];
            var yellow = new Color32(255, 204, 0, 255);
            var dark = new Color32(34, 34, 34, 255);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = ((x + y) % n) < n / 2 ? yellow : dark;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
            cache[key] = s;
            return s;
        }

        /// <summary>丸い点の1周期(縦方向)。SpriteRendererのTiledで縦に並べて、点線にする。</summary>
        public static Sprite Dot()
        {
            const string key = "dot";
            if (cache.TryGetValue(key, out var s)) return s;
            s = Make(8, 12, new Vector2(0.5f, 0.5f), (x, y) => (x - 4f) * (x - 4f) + (y - 6f) * (y - 6f) <= 1.7f * 1.7f, false);
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

        /// <summary>
        /// ガラス風のブロック用の絵。kind: 'b'=四角(a=幅,b=高さ) 'c'=丸(a=半径) 't'=三角(a=半径)。
        /// gloss=false: 上が明るく下が深い色のグラデーション+ふちの光(色を掛けて使う)。
        /// gloss=true : 上側のつや+左上の光の点(白い半透明。上に重ねる)。
        /// </summary>
        public static Sprite Glass(char kind, float a, float b, bool gloss)
        {
            string key = $"g{kind}{a:F3}_{b:F3}_{gloss}";
            if (cache.TryGetValue(key, out var s)) return s;
            int pw, ph;
            var pivot = new Vector2(0.5f, 0.5f);
            Func<float, float, bool> inside;
            if (kind == 'c')
            {
                pw = ph = Mathf.Max(4, Mathf.RoundToInt(2f * a * Ppu));
                float rr = pw / 2f;
                inside = (x, y) => (x - rr) * (x - rr) + (y - rr) * (y - rr) <= rr * rr;
            }
            else if (kind == 't')
            {
                pw = Mathf.CeilToInt(2f * 0.8660254f * a * Ppu); ph = Mathf.CeilToInt(1.5f * a * Ppu);
                pivot = new Vector2(0.5f, 1f / 3f);
                var va = new Vector2(pw / 2f, ph); var vb = new Vector2(pw, 0f); var vc = new Vector2(0f, 0f);
                inside = (x, y) =>
                {
                    var p = new Vector2(x, y);
                    float d1 = Cross(p, va, vb), d2 = Cross(p, vb, vc), d3 = Cross(p, vc, va);
                    return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
                };
            }
            else
            {
                pw = Mathf.Max(2, Mathf.RoundToInt(a * Ppu)); ph = Mathf.Max(2, Mathf.RoundToInt(b * Ppu));
                float cr = Mathf.Min(pw, ph) * CornerRatio;      // 角の丸み(0なら直角)
                int w0 = pw, h0 = ph;
                inside = (x, y) =>
                {
                    float cx = Mathf.Clamp(x, cr, w0 - cr), cy = Mathf.Clamp(y, cr, h0 - cr);
                    float dx = x - cx, dy = y - cy;
                    return dx * dx + dy * dy <= cr * cr;
                };
            }
            s = MakeGlass(pw, ph, pivot, inside, gloss);
            cache[key] = s;
            return s;
        }

        /// <summary>砂ぼこり用の、ふちがぼやけた丸(白。色は掛けて使う)。</summary>
        public static Sprite Puff()
        {
            const string key = "puff";
            if (cache.TryGetValue(key, out var s)) return s;
            const int n = 48;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f - n / 2f) / (n / 2f), dy = (y + 0.5f - n / 2f) / (n / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255f * a * a * (3f - 2f * a)));
                }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 48f, 0, SpriteMeshType.FullRect);
            cache[key] = s;
            return s;
        }

        static float Soft(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>
        /// ブロックの絵を作る。まわりに透明な余白(Pad画素)を足し、縁をやわらかく透けさせる。
        /// 余白は透明なので、見た目の大きさは変わらない。回転した斜めの縁が、MSAAなしでもなめらかになる。
        /// </summary>
        static Sprite MakeGlass(int w, int h, Vector2 pivot, Func<float, float, bool> inside, bool gloss)
        {
            const int pad = 2;
            int W = w + 2 * pad, H = h + 2 * pad;

            var mask = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    mask[y * w + x] = inside(x + 0.5f, y + 0.5f);

            var px = new Color32[W * H];
            float spotR = Mathf.Min(w, h) * 0.16f;
            for (int ty = 0; ty < H; ty++)
            {
                for (int tx = 0; tx < W; tx++)
                {
                    int x = tx - pad, y = ty - pad;           // 余白を除いた、もとの絵での位置
                    int hit = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (inside(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f)) hit++;
                    int i = ty * W + tx;
                    if (hit == 0) { px[i] = new Color32(0, 0, 0, 0); continue; }
                    float cov = hit / 16f;

                    // 縁からの距離(上下左右に、外れるまでの画素数。最大4)
                    int edge = 4;
                    for (int k = 1; k <= 4; k++)
                    {
                        if (!At(mask, w, h, x + k, y) || !At(mask, w, h, x - k, y)
                            || !At(mask, w, h, x, y + k) || !At(mask, w, h, x, y - k)) { edge = k; break; }
                    }
                    float v = Mathf.Clamp01((y + 0.5f) / h);

                    if (!gloss)
                    {
                        float b = 1f;
                        if (Glossy)
                        {
                            b = Mathf.Lerp(0.70f, 1.0f, v);            // 下が深く、上が明るい
                            if (edge <= 2) b = Mathf.Min(1f, b + 0.2f); // ふちの光
                        }
                        byte g = (byte)(255f * b);
                        px[i] = new Color32(g, g, g, (byte)(255f * cov));
                    }
                    else
                    {
                        float a = 0.32f * Soft((v - 0.45f) / 0.5f);     // 上側のつや
                        if (edge <= 1 && v > 0.5f) a += 0.22f;          // 上のふちの光
                        float dx = x + 0.5f - 0.24f * w, dy = y + 0.5f - 0.78f * h;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / spotR;
                        a += 0.8f * Mathf.Exp(-d * d);                  // 左上の光の点
                        px[i] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(a) * cov));
                    }
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixels32(px);
            tex.Apply();
            // 余白を足した分だけ、基準の位置(中心)をずらす
            var pv = new Vector2((pivot.x * w + pad) / W, (pivot.y * h + pad) / H);
            return Sprite.Create(tex, new Rect(0, 0, W, H), pv, Ppu, 0, SpriteMeshType.FullRect);
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
                        for (int sy = 0; sy < 4; sy++)
                            for (int sx = 0; sx < 4; sx++)
                                if (inside(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f)) hit++;
                        px[i] = hit == 0 ? clear : new Color32(255, 255, 255, (byte)(255 * hit / 16));
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
