using UnityEngine;

namespace BlockFreeFall
{
    /// <summary>
    /// 隠し絵の広告のデモ。ブロックと重なった部分だけ映像が見え、範囲が一定以上埋まると全体を数秒流す。
    /// 映像は実際の広告ではなく、プログラムで作るダミー。
    /// </summary>
    public class AdDemo : MonoBehaviour
    {
        const int TexW = 160, TexH = 90;
        const float Width = 5f, Height = Width * TexH / TexW;
        const int GridX = 16, GridY = 9;
        const bool FollowRotation = true;   // true: ブロックと一緒に回る / false: 位置だけ追従し、映像は常にまっすぐ

        // 「DEMO AD」の文字(1文字5x7、上の行から)
        static readonly string[] Letters =
        {
            "11110,10001,10001,10001,10001,10001,11110", // D
            "11111,10000,10000,11110,10000,10000,11111", // E
            "10001,11011,10101,10101,10001,10001,10001", // M
            "01110,10001,10001,10001,10001,10001,01110", // O
            null,                                        // 空白
            "01110,10001,10001,11111,10001,10001,10001", // A
            "11110,10001,10001,10001,10001,10001,11110", // D
        };

        static AdDemo currentAd;
        static readonly Collider2D[] hits = new Collider2D[8];

        Texture2D tex;
        Color32[] px;
        bool[] textMask;
        SpriteRenderer video;
        Transform bar;
        Transform anchor;                   // 追従するブロック
        Vector3 localOffset;                // ブロックから見た、範囲の中心の位置
        float rotOffset;                    // ブロックから見た、範囲の向き
        float frameT, checkT, showT;
        bool completed;

        // 未完成のまま落ちたときの演出(完成した映像が下から這い上がり、むなしく落ちる)
        const float ClimbSeconds = 1.6f, HangSeconds = 0.45f;
        const int ClimbSteps = 5;           // 何回に分けて、よじ登るか
        bool crawling;
        float crawlT, crawlX, crawlStartY, crawlTopY, fallSpeed, fallRot;

        /// <summary>ブロックの左下の角を起点に、広告の範囲を置く。以後、そのブロックに追従する。前の広告は消す。</summary>
        public static void Spawn(Piece piece)
        {
            var b = piece.GetBounds();
            Clear();
            var go = new GameObject("AdDemo");
            currentAd = go.AddComponent<AdDemo>();
            float x = Mathf.Clamp(b.min.x, -Cfg.BowlHalfWidth, Cfg.BowlHalfWidth - Width);
            currentAd.Setup(piece.transform, new Vector2(x, b.min.y));
        }

        /// <summary>未完成の広告が、這い上がって落ちる演出の途中か。</summary>
        public static bool IsCrawling => currentAd != null && currentAd.crawling;

        public static void Clear()
        {
            if (currentAd != null) Destroy(currentAd.gameObject);
            currentAd = null;
        }

        void Setup(Transform target, Vector2 lowerLeft)
        {
            anchor = target;
            transform.position = new Vector3(lowerLeft.x + Width / 2f, lowerLeft.y + Height / 2f, 0f);
            localOffset = anchor.InverseTransformPoint(transform.position);
            rotOffset = -anchor.eulerAngles.z;

            tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            px = new Color32[TexW * TexH];
            BuildText();
            DrawFrame();

            video = gameObject.AddComponent<SpriteRenderer>();
            video.sprite = Sprite.Create(tex, new Rect(0, 0, TexW, TexH), new Vector2(0.5f, 0.5f), TexW / Width, 0, SpriteMeshType.FullRect);
            video.sortingOrder = 3;                                  // ブロックの色の面より手前
            video.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;   // ブロックと重なった部分だけ見せる

            // 範囲を示す枠と、進み具合の棒
            var line = new Color(Theme.Text.r, Theme.Text.g, Theme.Text.b, 0.35f);
            Edge(new Vector2(0f, Height / 2f), new Vector2(Width, 0.03f), line);
            Edge(new Vector2(0f, -Height / 2f), new Vector2(Width, 0.03f), line);
            Edge(new Vector2(-Width / 2f, 0f), new Vector2(0.03f, Height), line);
            Edge(new Vector2(Width / 2f, 0f), new Vector2(0.03f, Height), line);
            bar = Edge(new Vector2(-Width / 2f, Height / 2f + 0.12f), new Vector2(0f, 0.1f), Theme.Warn);
        }

        Transform Edge(Vector2 pos, Vector2 size, Color color)
        {
            var g = new GameObject("edge");
            g.transform.SetParent(transform, false);
            g.transform.localPosition = pos;
            g.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = Shapes.Box(1f, 1f, false);
            sr.color = color;
            sr.sortingOrder = 3;
            return g.transform;
        }

        // ブロックの動きのあとで、範囲を合わせる。ブロックが落ちて消えたら、広告も消す
        void LateUpdate()
        {
            if (crawling) { UpdateCrawl(); return; }
            if (anchor == null) { Clear(); return; }
            if (!anchor.gameObject.activeInHierarchy)
            {
                if (completed) Clear(); else StartCrawl();
                return;
            }
            transform.position = anchor.TransformPoint(localOffset);
            transform.rotation = FollowRotation ? Quaternion.Euler(0f, 0f, anchor.eulerAngles.z + rotOffset) : Quaternion.identity;
        }

        void Update()
        {
            frameT += Time.deltaTime;
            if (frameT >= 1f / 15f) { frameT = 0f; DrawFrame(); }   // 映像の更新は1秒に15回

            if (crawling) return;

            if (completed)
            {
                showT -= Time.deltaTime;
                if (showT <= 0f) Clear();
                return;
            }

            checkT += Time.deltaTime;
            if (checkT < 0.25f) return;
            checkT = 0f;
            float coverage = Coverage();
            var s = bar.localScale; s.x = Width * Mathf.Clamp01(coverage / Cfg.AdCompleteRatio); bar.localScale = s;
            bar.localPosition = new Vector3(-Width / 2f + s.x / 2f, bar.localPosition.y, 0f);
            if (coverage >= Cfg.AdCompleteRatio) Complete();
        }

        /// <summary>範囲のうち、動くブロックと重なっている割合(格子状の点で調べる)。</summary>
        float Coverage()
        {
            int hit = 0;
            var filter = new ContactFilter2D().NoFilter();
            for (int y = 0; y < GridY; y++)
            {
                for (int x = 0; x < GridX; x++)
                {
                    // 範囲の傾きに合わせて、調べる点も回す
                    Vector2 p = transform.TransformPoint(new Vector3(((x + 0.5f) / GridX - 0.5f) * Width, ((y + 0.5f) / GridY - 0.5f) * Height, 0f));
                    int n = Physics2D.OverlapPoint(p, filter, hits);
                    for (int i = 0; i < n; i++)
                    {
                        var rb = hits[i].attachedRigidbody;
                        if (rb != null && rb.bodyType == RigidbodyType2D.Dynamic) { hit++; break; }
                    }
                }
            }
            return hit / (float)(GridX * GridY);
        }

        void Complete()
        {
            completed = true;
            showT = Cfg.AdCompleteSeconds;
            video.maskInteraction = SpriteMaskInteraction.None;   // 切り抜きをやめて、全体を見せる
            Sfx.Revive();
        }

        void StartCrawl()
        {
            crawling = true;
            video.maskInteraction = SpriteMaskInteraction.None;   // ここで初めて、全体が見える
            bar.gameObject.SetActive(false);
            var cam = Camera.main;
            float bottom = cam.transform.position.y - cam.orthographicSize;
            float maxX = (Cfg.WorldWidth - Width) / 2f;
            crawlX = Mathf.Clamp(transform.position.x, -maxX, maxX);
            crawlStartY = bottom - Height / 2f - 0.2f;
            crawlTopY = bottom + Height / 2f + 1.2f;
            transform.SetPositionAndRotation(new Vector3(crawlX, crawlStartY, 0f), Quaternion.identity);
        }

        // よじ登る → 力尽きて震える → 回りながら落ちる
        void UpdateCrawl()
        {
            crawlT += Time.deltaTime;
            float x = crawlX, y, rot;
            if (crawlT < ClimbSeconds)
            {
                float s = crawlT / ClimbSeconds * ClimbSteps;
                float step = Mathf.Floor(s), f = s - step;
                float p = (step + f * f * (3f - 2f * f)) / ClimbSteps;   // 少し登っては止まる
                y = Mathf.Lerp(crawlStartY, crawlTopY, p);
                rot = Mathf.Sin(crawlT * 9f) * 5f;
            }
            else if (crawlT < ClimbSeconds + HangSeconds)
            {
                y = crawlTopY;
                x += Mathf.Sin(crawlT * 60f) * 0.05f;
                rot = Mathf.Sin(crawlT * 45f) * 2f;
            }
            else
            {
                fallSpeed += Cfg.Gravity * 0.5f * Time.deltaTime;
                fallRot += 90f * Time.deltaTime;
                y = transform.position.y - fallSpeed * Time.deltaTime;
                rot = fallRot;
                var cam = Camera.main;
                if (y < cam.transform.position.y - cam.orthographicSize - Width) { Clear(); return; }
            }
            transform.SetPositionAndRotation(new Vector3(x, y, 0f), Quaternion.Euler(0f, 0f, rot));
        }

        // 文字の形を、映像の中央に置く
        void BuildText()
        {
            textMask = new bool[TexW * TexH];
            const int scale = 2, charW = 6 * scale, charH = 7 * scale;
            int x0 = (TexW - Letters.Length * charW) / 2, y0 = (TexH - charH) / 2;
            for (int c = 0; c < Letters.Length; c++)
            {
                if (Letters[c] == null) continue;
                var rows = Letters[c].Split(',');
                for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 5; col++)
                    {
                        if (rows[row][col] != '1') continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px0 = x0 + c * charW + col * scale + sx;
                                int py0 = y0 + (6 - row) * scale + sy;
                                textMask[py0 * TexW + px0] = true;
                            }
                    }
            }
        }

        // 動画の代わり: 色が流れる背景に、白い文字
        void DrawFrame()
        {
            float t = Time.time;
            for (int y = 0; y < TexH; y++)
            {
                for (int x = 0; x < TexW; x++)
                {
                    int i = y * TexW + x;
                    if (textMask[i]) { px[i] = new Color32(255, 255, 255, 255); continue; }
                    float h = Mathf.Repeat((x + y) / 200f + t * 0.25f, 1f);
                    px[i] = Color.HSVToRGB(h, 0.75f, 0.9f);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
        }

        void OnDestroy()
        {
            if (video != null && video.sprite != null) Destroy(video.sprite);
            if (tex != null) Destroy(tex);
        }
    }
}
