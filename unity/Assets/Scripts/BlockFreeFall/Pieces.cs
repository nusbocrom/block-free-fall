using System.Collections.Generic;
using UnityEngine;

namespace BlockFreeFall
{
    /// <summary>落ちてくる1つの物体(ピース、または変な物体)。</summary>
    public class Piece : MonoBehaviour
    {
        public Rigidbody2D Rb;
        public Collider2D[] Cols;
        public bool IsBoss;

        public Bounds GetBounds()
        {
            var b = new Bounds(Cols[0].bounds.center, Vector3.zero);
            foreach (var c in Cols) b.Encapsulate(c.bounds);
            return b;
        }

        /// <summary>
        /// 密度は、動く物体(Dynamic)になってからでないと設定できない。落とす直前に、作るときに決めた値を反映する。
        /// </summary>
        public void ApplyDensity()
        {
            foreach (var t in GetComponentsInChildren<DensityTag>())
            {
                var c = t.GetComponent<Collider2D>();
                if (c != null) c.density = t.Value;
            }
        }

        public bool Settled() =>
            Rb.linearVelocity.magnitude < Cfg.SettleSpeed && Mathf.Abs(Rb.angularVelocity) < Cfg.SettleAngularSpeed;
    }

    /// <summary>ピースの各部分の密度を覚えておくための印。</summary>
    public class DensityTag : MonoBehaviour
    {
        public float Value = 1f;
    }

    public static class PieceFactory
    {
        public const string Kinds = "IOTSZJL";
        public const int BossKinds = 4;

        // マスの中心(Web版と同じ。y は下向きが正)。重心は作るときに引く
        static readonly Dictionary<char, float[]> Cells = new()
        {
            ['I'] = new[] { -1.5f, 0, -0.5f, 0, 0.5f, 0, 1.5f, 0 },
            ['O'] = new[] { -0.5f, -0.5f, 0.5f, -0.5f, -0.5f, 0.5f, 0.5f, 0.5f },
            ['T'] = new[] { -1f, 0, 0, 0, 1f, 0, 0, -1f },
            ['S'] = new[] { -1f, 0, 0, 0, 0, -1f, 1f, -1f },
            ['Z'] = new[] { -1f, -1f, 0, -1f, 0, 0, 1f, 0 },
            ['J'] = new[] { -1f, -1f, -1f, 0, 0, 0, 1f, 0 },
            ['L'] = new[] { 1f, -1f, -1f, 0, 0, 0, 1f, 0 },
        };

        static readonly Dictionary<char, Color> Colors = new()
        {
            ['I'] = Theme.Hex("44aacc"), ['O'] = Theme.Hex("eebb33"), ['T'] = Theme.Hex("aa66cc"),
            ['S'] = Theme.Hex("55bb66"), ['Z'] = Theme.Hex("ee5555"), ['J'] = Theme.Hex("5588dd"),
            ['L'] = Theme.Hex("ee9922"),
        };

        static PhysicsMaterial2D mat;
        public static PhysicsMaterial2D Mat
        {
            get
            {
                if (mat == null) mat = new PhysicsMaterial2D("bff") { friction = Cfg.Friction, bounciness = Cfg.Bounciness };
                return mat;
            }
        }

        public static Piece Create(char kind)
        {
            var cells = Cells[kind];
            int n = cells.Length / 2;
            float cx = 0, cy = 0;
            for (int i = 0; i < n; i++) { cx += cells[2 * i]; cy += cells[2 * i + 1]; }
            cx /= n; cy /= n;

            var p = Begin("Piece_" + kind, false);
            for (int i = 0; i < n; i++)
                AddBox(p.transform, new Vector2(cells[2 * i] - cx, -(cells[2 * i + 1] - cy)), Vector2.one, 0f, Colors[kind], 1f);
            return Finish(p);
        }

        /// <summary>変な物体。0=ダンベル 1=星 2=三角 3=鉄球</summary>
        public static Piece CreateBoss(int index)
        {
            var color = Theme.Boss;
            float heavy = Cfg.BossWeight;
            var p = Begin("Boss_" + index, true);
            switch (index)
            {
                case 0: // 片側だけ重いダンベル
                    AddBox(p.transform, Vector2.zero, new Vector2(220f / 30f, 16f / 30f), 0f, color, 1f);
                    AddCircle(p.transform, new Vector2(-110f / 30f, 0f), 30f / 30f, color, heavy);
                    AddCircle(p.transform, new Vector2(110f / 30f, 0f), 16f / 30f, color, 1f);
                    break;
                case 1: // トゲトゲの星
                    foreach (var deg in new[] { 0f, 60f, 120f })
                        AddBox(p.transform, Vector2.zero, new Vector2(170f / 30f, 18f / 30f), deg, color, 1f);
                    break;
                case 2: // 巨大三角
                    AddTriangle(p.transform, 56f / 30f, color, heavy);
                    break;
                default: // 転がる鉄球
                    AddCircle(p.transform, Vector2.zero, 38f / 30f, color, heavy);
                    break;
            }
            return Finish(p);
        }

        static Piece Begin(string name, bool boss)
        {
            var go = new GameObject(name);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;      // 持っている間は動かさない
            rb.useAutoMass = true;                        // 密度から重さを決める
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            var p = go.AddComponent<Piece>();
            p.Rb = rb; p.IsBoss = boss;
            return p;
        }

        static Piece Finish(Piece p)
        {
            p.Cols = p.GetComponentsInChildren<Collider2D>();
            Physics2D.SyncTransforms();
            return p;
        }

        // 縁取りの太さ(ユニット)。暗い下地を少し大きく、色の面を少し小さくして、その差で縁を作る。
        // こうすると、外側もつなぎ目も同じ太さになる(1マスごとに縁を描くと、つなぎ目だけ倍の太さになる)
        const float Edge = 0.07f;

        /// <summary>当たり判定を持つ入れ物。大きさを変えないよう、絵は子として別に作る。</summary>
        static GameObject Holder(Transform parent, Vector2 pos, float angleDeg)
        {
            var g = new GameObject("c");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0, 0, angleDeg);
            return g;
        }

        static void Visual(GameObject g, Sprite s, Vector2 fillScale, Vector2 underScale, Color color)
        {
            var o = new GameObject("o");
            o.transform.SetParent(g.transform, false);
            o.transform.localScale = new Vector3(underScale.x, underScale.y, 1f);
            var osr = o.AddComponent<SpriteRenderer>();
            osr.sprite = s; osr.color = Theme.Line; osr.sortingOrder = 1;   // 下地は、お椀より手前

            var f = new GameObject("f");
            f.transform.SetParent(g.transform, false);
            f.transform.localScale = new Vector3(fillScale.x, fillScale.y, 1f);
            var fsr = f.AddComponent<SpriteRenderer>();
            fsr.sprite = s; fsr.color = color; fsr.sortingOrder = 2;        // 色の面は、全部の下地より手前
        }

        static void AddBox(Transform parent, Vector2 pos, Vector2 size, float angleDeg, Color color, float density)
        {
            var g = Holder(parent, pos, angleDeg);
            Visual(g, Shapes.Box(size.x, size.y, false),
                new Vector2((size.x - Edge) / size.x, (size.y - Edge) / size.y),
                new Vector2((size.x + Edge) / size.x, (size.y + Edge) / size.y), color);
            var c = g.AddComponent<BoxCollider2D>();
            c.size = size; c.sharedMaterial = Mat;
            g.AddComponent<DensityTag>().Value = density;
        }

        static void AddCircle(Transform parent, Vector2 pos, float r, Color color, float density)
        {
            var g = Holder(parent, pos, 0f);
            float f = (r - Edge / 2f) / r, u = (r + Edge / 2f) / r;
            Visual(g, Shapes.Circle(r, false), new Vector2(f, f), new Vector2(u, u), color);
            var c = g.AddComponent<CircleCollider2D>();
            c.radius = r; c.sharedMaterial = Mat;
            g.AddComponent<DensityTag>().Value = density;
        }

        static void AddTriangle(Transform parent, float r, Color color, float density)
        {
            var g = Holder(parent, Vector2.zero, 0f);
            float f = 1f - Edge / r, u = 1f + Edge / r;
            Visual(g, Shapes.Triangle(r, false), new Vector2(f, f), new Vector2(u, u), color);
            var c = g.AddComponent<PolygonCollider2D>();
            c.points = new[]
            {
                new Vector2(0f, r),
                new Vector2(0.8660254f * r, -0.5f * r),
                new Vector2(-0.8660254f * r, -0.5f * r),
            };
            c.sharedMaterial = Mat;
            g.AddComponent<DensityTag>().Value = density;
        }
    }

    public static class Bowl
    {
        /// <summary>
        /// なだらかなお椀。中央が y=0 で最も低く、縁が BowlRise だけ高い放物線。
        /// 見た目は、y=0 の線とのあいだを塗った楔形(Web版と同じ)。当たり判定は、短い厚い板の連続。
        /// </summary>
        public static GameObject Build()
        {
            var root = new GameObject("Bowl");
            int n = Cfg.BowlSegments;
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float u = -1f + 2f * i / n;
                pts[i] = new Vector2(u * Cfg.BowlHalfWidth, Cfg.BowlRise * u * u);
            }

            // お椀の下は、暗い帯(奈落)にする
            var abyss = new GameObject("abyss");
            abyss.transform.SetParent(root.transform, false);
            abyss.transform.position = new Vector3(0f, -1f - 50f, 0f);
            abyss.transform.localScale = new Vector3(200f, 100f, 1f);
            var asr = abyss.AddComponent<SpriteRenderer>();
            asr.sprite = Shapes.Box(1f, 1f, false); asr.color = Theme.Abyss; asr.sortingOrder = -5;

            // 見た目(縁取りは、少し大きい暗い楔を後ろに重ねる)
            var mat = DefaultSpriteMaterial();
            const int visualSegments = 64;
            var vpts = new Vector2[visualSegments + 1];
            for (int i = 0; i <= visualSegments; i++)
            {
                float u = -1f + 2f * i / visualSegments;
                vpts[i] = new Vector2(u * Cfg.BowlHalfWidth, Cfg.BowlRise * u * u);
            }
            AddMesh(root.transform, vpts, 0.09f, Theme.Line, -1, mat);
            AddMesh(root.transform, vpts, 0f, Theme.Bowl, 0, mat);

            // 当たり判定(見えない)
            for (int i = 0; i < n; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                Vector2 d = (b - a).normalized;
                Vector2 down = new Vector2(d.y, -d.x);   // 表面の下向きの法線
                Vector2 center = (a + b) / 2f + down * (Cfg.BowlThickness / 2f);
                float len = (b - a).magnitude + 0.2f;    // 継ぎ目の隙間を消すため少し長くする

                var g = new GameObject("seg");
                g.transform.SetParent(root.transform, false);
                g.transform.localPosition = center;
                g.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                var col = g.AddComponent<BoxCollider2D>();
                col.size = new Vector2(len, Cfg.BowlThickness);
                col.sharedMaterial = PieceFactory.Mat;
            }
            return root;
        }

        static Material DefaultSpriteMaterial()
        {
            var tmp = new GameObject("tmp").AddComponent<SpriteRenderer>();
            var m = tmp.sharedMaterial;
            Object.Destroy(tmp.gameObject);
            return m;
        }

        /// <summary>弧の上面と y=0 の線のあいだを、四角形の連なりで塗ったメッシュ。grow だけ外へ広げる。</summary>
        static void AddMesh(Transform parent, Vector2[] pts, float grow, Color color, int order, Material mat)
        {
            int n = pts.Length - 1;
            var v = new Vector3[n * 4];
            var c = new Color[n * 4];
            var t = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                float xl = pts[i].x - (i == 0 ? grow : 0f), xr = pts[i + 1].x + (i == n - 1 ? grow : 0f);
                v[i * 4 + 0] = new Vector3(xl, pts[i].y + grow, 0f);
                v[i * 4 + 1] = new Vector3(xr, pts[i + 1].y + grow, 0f);
                v[i * 4 + 2] = new Vector3(xr, -grow, 0f);
                v[i * 4 + 3] = new Vector3(xl, -grow, 0f);
                for (int k = 0; k < 4; k++) c[i * 4 + k] = color;
                int o = i * 4, q = i * 6;
                t[q] = o; t[q + 1] = o + 1; t[q + 2] = o + 2;
                t[q + 3] = o; t[q + 4] = o + 2; t[q + 5] = o + 3;
            }
            var mesh = new Mesh { vertices = v, colors = c, triangles = t };
            mesh.RecalculateBounds();
            var g = new GameObject(grow > 0f ? "bowl_outline" : "bowl_fill");
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingOrder = order;
        }
    }
}
