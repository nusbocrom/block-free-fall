#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BlockFreeFall.Tests
{
    /// <summary>
    /// 動く GIF を書き出す(サムネイル用)。色は全部のコマで共通の 255 色まで(多ければ減らす)。
    /// 2コマ目からは、前のコマと変わった四角だけを書き、変わらないドットは透明にして小さくする。
    /// ドット絵が崩れないよう、拡大は整数倍(scale)で行う。
    /// </summary>
    public static class GifWriter
    {
        const int Transparent = 255;

        /// <param name="frames">各コマの色(下の行から。Texture2D と同じ並び)。</param>
        /// <param name="delayCentiseconds">1コマの長さ(1/100 秒)。</param>
        public static void Write(string path, List<Color32[]> frames, int width, int height, int scale, int delayCentiseconds)
        {
            var palette = MakePalette(frames);
            var lookup = new Dictionary<int, byte>();
            int W = width * scale, H = height * scale;
            var indexed = new List<byte[]>();
            foreach (var f in frames)
            {
                var idx = new byte[width * height];
                for (int i = 0; i < f.Length; i++) idx[i] = Nearest(palette, lookup, f[i]);
                indexed.Add(idx);
            }

            using var s = new BinaryWriter(File.Create(path));
            s.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
            s.Write((ushort)W); s.Write((ushort)H);
            s.Write((byte)0xF7);   // 全体の色表あり、256 色
            s.Write((byte)0); s.Write((byte)0);
            for (int i = 0; i < 256; i++)
            {
                var c = i < palette.Count ? palette[i] : new Color32(0, 0, 0, 255);
                s.Write(c.r); s.Write(c.g); s.Write(c.b);
            }
            // くり返し再生(NETSCAPE2.0)
            s.Write(new byte[] { 0x21, 0xFF, 0x0B });
            s.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            s.Write(new byte[] { 3, 1, 0, 0, 0 });

            for (int n = 0; n < indexed.Count; n++)
            {
                var cur = indexed[n];
                var prev = n > 0 ? indexed[n - 1] : null;
                // 前のコマと変わった範囲(元の大きさのドットで)
                int x0 = 0, y0 = 0, x1 = width - 1, y1 = height - 1;
                if (prev != null)
                {
                    x0 = width; y0 = height; x1 = -1; y1 = -1;
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                            if (cur[y * width + x] != prev[y * width + x])
                            {
                                x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                                y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                            }
                    if (x1 < 0) { x0 = y0 = x1 = y1 = 0; }   // 変化なし: 1ドットだけ書く
                }
                // コマの設定: 前のコマを残し、透明の色を使う
                s.Write(new byte[] { 0x21, 0xF9, 4, (byte)(prev != null ? 0x05 : 0x04) });
                s.Write((ushort)delayCentiseconds);
                s.Write((byte)Transparent);
                s.Write((byte)0);
                // GIF は上の行から。Texture2D の並びは下の行からなので、上下を入れ替える
                int fw = (x1 - x0 + 1) * scale, fh = (y1 - y0 + 1) * scale;
                int top = (height - 1 - y1) * scale, left = x0 * scale;
                s.Write((byte)0x2C);
                s.Write((ushort)left); s.Write((ushort)top); s.Write((ushort)fw); s.Write((ushort)fh);
                s.Write((byte)0);
                var pixels = new byte[fw * fh];
                for (int y = 0; y < fh; y++)
                {
                    int sy = y1 - y / scale;
                    for (int x = 0; x < fw; x++)
                    {
                        int sx = x0 + x / scale, k = sy * width + sx;
                        pixels[y * fw + x] = prev != null && cur[k] == prev[k] ? (byte)Transparent : cur[k];
                    }
                }
                WriteLzw(s, pixels);
            }
            s.Write((byte)0x3B);
        }

        /// <summary>全部のコマの色から、255 色までの色表を作る。多ければ、色の箱を半分に分けていく方法(メディアンカット)で減らす。</summary>
        static List<Color32> MakePalette(List<Color32[]> frames)
        {
            var counts = new Dictionary<int, int>();
            foreach (var f in frames)
                foreach (var c in f)
                {
                    int key = c.r << 16 | c.g << 8 | c.b;
                    counts.TryGetValue(key, out int n);
                    counts[key] = n + 1;
                }
            var colors = new List<int>(counts.Keys);
            if (colors.Count <= 255) return colors.ConvertAll(Unpack);

            var boxes = new List<List<int>> { colors };
            while (boxes.Count < 255)
            {
                // いちばん幅の広い箱を、その向きの真ん中で2つに分ける
                int best = -1, bestRange = 0, bestChannel = 0;
                for (int b = 0; b < boxes.Count; b++)
                {
                    if (boxes[b].Count < 2) continue;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        int lo = 255, hi = 0;
                        foreach (int c in boxes[b]) { int v = c >> (16 - ch * 8) & 255; lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
                        if (hi - lo > bestRange) { bestRange = hi - lo; best = b; bestChannel = ch; }
                    }
                }
                if (best < 0) break;
                var box = boxes[best];
                int shift = 16 - bestChannel * 8;
                box.Sort((p, q) => (p >> shift & 255).CompareTo(q >> shift & 255));
                boxes[best] = box.GetRange(0, box.Count / 2);
                boxes.Add(box.GetRange(box.Count / 2, box.Count - box.Count / 2));
            }
            var palette = new List<Color32>();
            foreach (var box in boxes)
            {
                // 箱の中の色を、使われた回数で重みを付けて平均する
                long r = 0, g = 0, bl = 0, total = 0;
                foreach (int c in box) { int n = counts[c]; r += (c >> 16 & 255) * n; g += (c >> 8 & 255) * n; bl += (c & 255) * n; total += n; }
                palette.Add(new Color32((byte)(r / total), (byte)(g / total), (byte)(bl / total), 255));
            }
            return palette;
        }

        static Color32 Unpack(int c) => new Color32((byte)(c >> 16), (byte)(c >> 8), (byte)c, 255);

        static byte Nearest(List<Color32> palette, Dictionary<int, byte> lookup, Color32 c)
        {
            int key = c.r << 16 | c.g << 8 | c.b;
            if (lookup.TryGetValue(key, out byte found)) return found;
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                int dr = palette[i].r - c.r, dg = palette[i].g - c.g, db = palette[i].b - c.b;
                int d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = i; }
            }
            lookup[key] = (byte)best;
            return (byte)best;
        }

        /// <summary>色の番号の並びを、GIF の LZW で圧縮して書く(最初の符号の長さは 8 ビット)。</summary>
        static void WriteLzw(BinaryWriter s, byte[] pixels)
        {
            const int MinCodeSize = 8, Clear = 256, End = 257;
            s.Write((byte)MinCodeSize);
            var output = new List<byte>();
            int bitBuffer = 0, bitCount = 0, codeSize = MinCodeSize + 1;
            void Emit(int code)
            {
                bitBuffer |= code << bitCount;
                bitCount += codeSize;
                while (bitCount >= 8) { output.Add((byte)bitBuffer); bitBuffer >>= 8; bitCount -= 8; }
            }
            var table = new Dictionary<int, int>();
            int next = End + 1;
            Emit(Clear);
            int prefix = pixels.Length > 0 ? pixels[0] : 0;
            for (int i = 1; i < pixels.Length; i++)
            {
                int key = prefix << 8 | pixels[i];
                if (table.TryGetValue(key, out int code)) { prefix = code; continue; }
                Emit(prefix);
                if (next < 4096)
                {
                    table[key] = next++;
                    if (next > 1 << codeSize && codeSize < 12) codeSize++;
                }
                else
                {
                    // 表がいっぱいになったら作り直す
                    Emit(Clear);
                    table.Clear();
                    next = End + 1;
                    codeSize = MinCodeSize + 1;
                }
                prefix = pixels[i];
            }
            Emit(prefix);
            Emit(End);
            if (bitCount > 0) output.Add((byte)bitBuffer);
            // 255 バイトずつのかたまりで書く
            for (int i = 0; i < output.Count; i += 255)
            {
                int n = Mathf.Min(255, output.Count - i);
                s.Write((byte)n);
                for (int k = 0; k < n; k++) s.Write(output[i + k]);
            }
            s.Write((byte)0);
        }
    }
}
#endif
