using UnityEngine;

namespace BlockFreeFall
{
    /// <summary>
    /// 広告ブロック。広告が付いたブロックは塗りが虹色に変わり続ける。あとから落としたブロックが当たるか、
    /// 広告ブロックが落ちるか、発動しないまま次の広告ブロックが来ると、Ad-Virtuaのモニターが這い上がってくる(15秒)。
    /// 発動するまでは、モニターを止めておく。
    /// </summary>
    public class AdDemo : MonoBehaviour
    {
        static AdDemo currentAd;
        static AdDemo crawlingAd;           // 這い上がって落ちる途中の広告。次の広告ブロックと同時に存在できる
        static GameObject monitor;          // Ad-Virtuaのモニター。Unit IDごとに1つだけなので、使い回す

        Piece piece;                        // 広告ブロック
        SpriteRenderer[] fills;             // ブロックの色の面
        Color[] baseColors;                 // 虹色にする前の色

        // 這い上がるときの大きさ。画面の横幅に対する割合で決める(9:16の画面で、面積の約18%)
        const float RevealWidthRatio = 0.75f;
        const float MonitorZ = 1f;          // ブロックの奥
        const int MonitorOrder = -10;

        // 這い上がる演出(さっと上がり、よじ登り、力尽きて落ちる。合計15秒)
        const float RiseSeconds = 0.6f, ClimbSeconds = 12.4f, HangSeconds = 0.5f, FallSeconds = 1.5f;
        const int ClimbSteps = 12;          // 何回に分けて、よじ登るか
        bool crawling;
        float crawlT, crawlX;

        /// <summary>ピースを広告ブロックにする。前の広告ブロックが発動していなければ、そこで這い上がらせる。</summary>
        public static void Spawn(Piece p)
        {
            if (currentAd != null) currentAd.StartCrawl();
            var go = new GameObject("AdBlock");
            currentAd = go.AddComponent<AdDemo>();
            currentAd.Setup(p);
        }

        /// <summary>ピースどうしがぶつかった。広告ブロックに、あとから落としたブロックが当たったら発動する。</summary>
        public static void OnPiecesHit(Piece a, Piece b)
        {
            var ad = currentAd;
            if (ad == null || ad.piece == null) return;
            var other = a == ad.piece ? b : b == ad.piece ? a : null;
            if (other == null || ad.piece.DropOrder == 0 || other.DropOrder <= ad.piece.DropOrder) return;
            Sfx.Revive();
            ad.StartCrawl();
        }

        /// <summary>広告が這い上がって落ちる演出の途中か。</summary>
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
            if (monitor != null && crawlingAd == null) monitor.SetActive(false);
        }

        void Setup(Piece p)
        {
            piece = p;
            var list = new System.Collections.Generic.List<SpriteRenderer>();
            foreach (var sr in p.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.gameObject.name == "f") list.Add(sr);
            fills = list.ToArray();
            baseColors = new Color[fills.Length];
            for (int i = 0; i < fills.Length; i++) baseColors[i] = fills[i].color;
        }

        // 塗りを、虹色に変わり続けるようにする。濃さ(持っている間の透け具合)はそのまま
        void Update()
        {
            if (crawling || fills == null) return;
            for (int i = 0; i < fills.Length; i++)
            {
                if (fills[i] == null) continue;
                var c = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.5f + i * 0.08f, 1f), 0.7f, 0.95f);
                c.a = fills[i].color.a;
                fills[i].color = c;
            }
        }

        void RestoreColors()
        {
            if (fills == null) return;
            for (int i = 0; i < fills.Length; i++)
            {
                if (fills[i] == null) continue;
                var c = baseColors[i]; c.a = fills[i].color.a; fills[i].color = c;
            }
            fills = null;
        }

        // 広告ブロックが落ちて消えたら、這い上がらせる
        void LateUpdate()
        {
            if (crawling) { UpdateCrawl(); return; }
            if (piece == null) { Finish(); return; }
            if (!piece.gameObject.activeInHierarchy) StartCrawl();
        }

        void StartCrawl()
        {
            if (crawling) return;
            if (crawlingAd != null) crawlingAd.Finish();   // モニターは1つなので、前の演出は打ち切る
            if (currentAd == this) currentAd = null;
            crawlingAd = this;
            crawling = true;
            crawlX = piece != null ? piece.transform.position.x : 0f;
            RestoreColors();
            Reveal();
            UpdateCrawl();
        }

        // モニターを出す。プレハブがなければ(テストなど)、見た目なしで時間だけ進める
        static void Reveal()
        {
            if (monitor == null)
            {
                var prefab = Resources.Load<GameObject>("AdVirtuaMonitor");
                if (prefab == null) return;
                monitor = Instantiate(prefab);
                foreach (var r in monitor.GetComponentsInChildren<Renderer>(true)) r.sortingOrder = MonitorOrder;   // ブロックと土台の奥
            }
            monitor.SetActive(true);
        }

        static float RevealWidthNow(Camera cam) => 2f * cam.orthographicSize * cam.aspect * RevealWidthRatio;

        static void SetMonitorPose(Vector3 pos, float rot, float width)
        {
            if (monitor == null) return;
            monitor.transform.SetPositionAndRotation(new Vector3(pos.x, pos.y, MonitorZ), Quaternion.Euler(0f, 0f, rot));
            monitor.transform.localScale = new Vector3(width, width * 9f / 16f, 1f);
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
            SetMonitorPose(new Vector3(x, y, 0f), rot, w);
        }

        // 直前に戻るなどで途中で消えても、ブロックの色は元に戻す
        void OnDestroy() => RestoreColors();
    }
}
