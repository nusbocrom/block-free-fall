using UnityEngine;

namespace BlockFreeFall
{
    /// <summary>
    /// 隠し絵の広告。ブロックと重なった部分だけダミーの映像が見え、範囲が一定以上埋まるか、落ちて未完成に終わると、
    /// Ad-Virtuaのモニターを全部見える形で15秒流す。隠れている間は、モニターを止めておく。
    /// </summary>
    public class AdDemo : MonoBehaviour
    {
        const int TexW = 240, TexH = 135;
        const int TextRows = 3;             // ダミーの文字の段数
        const float Width = 5f, Height = Width * TexH / TexW;
        const int GridX = 16, GridY = 9;
        const bool FollowRotation = true;   // true: ブロックと一緒に回る / false: 位置だけ追従し、映像は常にまっすぐ

        const float ScrollPixelsPerSecond = 40f;   // ダミーの文字が横に流れる速さ

        static AdDemo currentAd;
        static AdDemo crawlingAd;           // 這い上がって落ちる途中の広告。次の広告と同時に存在できる
        static GameObject monitor;         // Ad-Virtuaのモニター。Unit IDごとに1つだけなので、使い回す
        static readonly Collider2D[] hits = new Collider2D[8];

        Texture2D tex;
        Color32[] px;
        Color32[] textPx;                   // 流す文字の画像(「完成するとここに広告が再生されるよ！」)
        int textW, textH;
        SpriteRenderer video;
        Transform bar;
        Transform anchor;                   // 追従するブロック
        Vector3 localOffset;                // ブロックから見た、範囲の中心の位置
        float rotOffset;                    // ブロックから見た、範囲の向き
        float frameT, checkT, showT;
        bool completed;

        // 未完成の演出で全体を見せるときの大きさ。画面の横幅に対する割合で決める(9:16の画面で、面積の約18%)
        const float RevealWidthRatio = 0.75f;
        const float MonitorBackZ = 1f, MonitorFrontZ = -1f;   // ブロックの奥 / 手前
        const int MonitorBackOrder = -10, MonitorFrontOrder = 10;
        float monitorZ;

        // 未完成のまま落ちたときの演出(さっと上がり、よじ登り、力尽きて落ちる。合計15秒)
        const float RiseSeconds = 0.6f, ClimbSeconds = 12.4f, HangSeconds = 0.5f, FallSeconds = 1.5f;
        const int ClimbSteps = 12;          // 何回に分けて、よじ登るか
        bool crawling;
        float crawlT, crawlX;

        /// <summary>ブロックの下辺の中央に合わせて、広告の範囲を置く。以後、そのブロックに追従する。前の広告が未完成なら、這い上がらせる。</summary>
        public static void Spawn(Piece piece)
        {
            var b = piece.GetBounds();
            if (currentAd != null)
            {
                if (currentAd.completed) currentAd.Finish();
                else currentAd.StartCrawl();
            }
            var go = new GameObject("AdDemo");
            currentAd = go.AddComponent<AdDemo>();
            float x = Mathf.Clamp(b.center.x - Width / 2f, -Cfg.BowlHalfWidth, Cfg.BowlHalfWidth - Width);
            currentAd.Setup(piece.transform, new Vector2(x, b.min.y));
        }

        /// <summary>未完成の広告が、這い上がって落ちる演出の途中か。</summary>
        public static bool IsCrawling => crawlingAd != null;

        public static void Clear()
        {
            if (currentAd != null) Destroy(currentAd.gameObject);
            if (crawlingAd != null) Destroy(crawlingAd.gameObject);
            currentAd = null;
            crawlingAd = null;
            if (monitor != null) monitor.SetActive(false);
        }

        // この広告だけを終わらせる。ほかの広告がモニターを使っていれば、止めない
        void Finish()
        {
            if (currentAd == this) currentAd = null;
            if (crawlingAd == this) crawlingAd = null;
            Destroy(gameObject);
            bool inUse = crawlingAd != null || (currentAd != null && currentAd.completed);
            if (monitor != null && !inUse) monitor.SetActive(false);
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
            if (completed) { UpdateShow(); return; }
            if (anchor == null) { Finish(); return; }
            if (!anchor.gameObject.activeInHierarchy) { StartCrawl(); return; }
            transform.position = anchor.TransformPoint(localOffset);
            transform.rotation = FollowRotation ? Quaternion.Euler(0f, 0f, anchor.eulerAngles.z + rotOffset) : Quaternion.identity;
        }

        void Update()
        {
            frameT += Time.deltaTime;
            if (frameT >= 1f / 15f && video.enabled) { frameT = 0f; DrawFrame(); }   // 映像の更新は1秒に15回

            if (crawling) return;

            if (completed)
            {
                showT -= Time.deltaTime;
                if (showT <= 0f) Finish();
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
            if (crawlingAd != null) crawlingAd.Finish();   // モニターは1つなので、這い上がりの途中でも完成を優先する
            completed = true;
            showT = Cfg.AdCompleteSeconds;
            Reveal(true);
            Sfx.Revive();
        }

        // 隠し絵をやめて、全体を見せる。モニターがなければ、ダミーの映像のまま見せる
        void Reveal(bool front)
        {
            foreach (Transform child in transform) child.gameObject.SetActive(false);
            if (monitor == null)
            {
                var prefab = Resources.Load<GameObject>("AdVirtuaMonitor");
                if (prefab != null) monitor = Instantiate(prefab);
            }
            if (monitor != null)
            {
                monitorZ = front ? MonitorFrontZ : MonitorBackZ;
                foreach (var r in monitor.GetComponentsInChildren<Renderer>(true)) r.sortingOrder = front ? MonitorFrontOrder : MonitorBackOrder;
                monitor.SetActive(true);
                video.enabled = false;
            }
            else video.maskInteraction = SpriteMaskInteraction.None;
        }

        static float RevealWidthNow(Camera cam) => 2f * cam.orthographicSize * cam.aspect * RevealWidthRatio;

        void SetRevealPose(Vector3 pos, float rot, float width)
        {
            var q = Quaternion.Euler(0f, 0f, rot);
            transform.SetPositionAndRotation(pos, q);
            if (monitor != null)
            {
                monitor.transform.SetPositionAndRotation(new Vector3(pos.x, pos.y, monitorZ), q);
                monitor.transform.localScale = new Vector3(width, width * 9f / 16f, 1f);
            }
            else transform.localScale = Vector3.one * (width / Width);
        }

        // 完成: 隠し絵と同じ大きさのまま、ブロックに追従する。ブロックが落ちたら、その場で最後まで流す
        void UpdateShow()
        {
            var pos = transform.position;
            float rot = transform.eulerAngles.z;
            if (anchor != null && anchor.gameObject.activeInHierarchy)
            {
                pos = anchor.TransformPoint(localOffset);
                if (FollowRotation) rot = anchor.eulerAngles.z + rotOffset;
            }
            SetRevealPose(pos, rot, Width);
        }

        void StartCrawl()
        {
            if (crawlingAd != null) crawlingAd.Finish();
            if (currentAd == this) currentAd = null;
            crawlingAd = this;
            crawling = true;
            crawlX = transform.position.x;
            Reveal(false);
            UpdateCrawl();
        }

        // さっと上がる → よじ登る → 力尽きて震える → 回りながら落ちる
        void UpdateCrawl()
        {
            crawlT += Time.deltaTime;
            var cam = Camera.main;
            float size = cam.orthographicSize, centerY = cam.transform.position.y;
            float w = RevealWidthNow(cam), h = w * 9f / 16f;
            float top = centerY + size, bottom = centerY - size;
            float lowY = bottom + h / 2f + 1f, highY = top - h / 2f - 1.5f;   // ここの間なら、四隅が画面に入る
            float maxX = cam.orthographicSize * cam.aspect - w / 2f - 0.3f;
            float x = Mathf.Clamp(crawlX, -maxX, maxX), y, rot;

            if (crawlT < RiseSeconds)
            {
                y = Mathf.SmoothStep(bottom - h / 2f - 0.2f, lowY, crawlT / RiseSeconds);
                rot = 0f;
            }
            else if (crawlT < RiseSeconds + ClimbSeconds)
            {
                float climbT = crawlT - RiseSeconds;
                float s = climbT / ClimbSeconds * ClimbSteps;
                float step = Mathf.Floor(s), f = s - step;
                float p = (step + f * f * (3f - 2f * f)) / ClimbSteps;   // 少し登っては止まる
                y = Mathf.Lerp(lowY, highY, p);
                rot = Mathf.Sin(climbT * 3f) * 3f;
            }
            else if (crawlT < RiseSeconds + ClimbSeconds + HangSeconds)
            {
                y = highY;
                x += Mathf.Sin(crawlT * 60f) * 0.05f;
                rot = Mathf.Sin(crawlT * 45f) * 2f;
            }
            else
            {
                float f = (crawlT - RiseSeconds - ClimbSeconds - HangSeconds) / FallSeconds;
                if (f >= 1f) { Finish(); return; }
                y = Mathf.Lerp(highY, bottom - w, f * f);
                rot = f * 180f;
            }
            SetRevealPose(new Vector3(x, y, 0f), rot, w);
        }

        // 流す文字の画像を読み込む。右端の余白のあとに、また先頭が続く
        void BuildText()
        {
            var bytes = Resources.Load<TextAsset>("AdDummyText");
            if (bytes == null) return;
            var img = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (img.LoadImage(bytes.bytes))
            {
                textW = img.width;
                textH = img.height;
                textPx = img.GetPixels32();
            }
            Destroy(img);
        }

        // ダミーの動画: 色が流れる背景に、白い文字を3段。段ごとに向きと位置をずらして、入れ違いに流す
        void DrawFrame()
        {
            float t = Time.time;
            int scroll = Mathf.FloorToInt(t * ScrollPixelsPerSecond);
            int rowH = TexH / TextRows;
            for (int y = 0; y < TexH; y++)
            {
                int row = y / rowH;
                int ty = y - row * rowH - (rowH - textH) / 2;
                bool hasText = textPx != null && row < TextRows && ty >= 0 && ty < textH;
                int shift = hasText ? (row % 2 == 0 ? scroll : -scroll) + row * textW / TextRows : 0;
                for (int x = 0; x < TexW; x++)
                {
                    int i = y * TexW + x;
                    float h = Mathf.Repeat((x + y) / 300f + t * 0.25f, 1f);
                    Color32 c = Color.HSVToRGB(h, 0.75f, 0.9f);
                    if (hasText)
                    {
                        int tx = ((x + shift) % textW + textW) % textW;
                        c = Color32.Lerp(c, new Color32(255, 255, 255, 255), textPx[ty * textW + tx].a / 255f);
                    }
                    px[i] = c;
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
