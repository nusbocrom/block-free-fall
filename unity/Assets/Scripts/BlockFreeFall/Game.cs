using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace BlockFreeFall
{
    /// <summary>シーンの設定なしで、再生すると自動で始まる。</summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Object.FindFirstObjectByType<Game>() != null) return;
            new GameObject("Game", typeof(Game));
        }
    }

    public class Game : MonoBehaviour
    {
        enum State { Play, Over }

        struct Ptr
        {
            public int id; public Vector2 pos; public bool began, ended;
        }

        /// <summary>落とす直前の1ピース分の状態(直前に戻る用)。</summary>
        struct PieceState
        {
            public Piece piece; public Vector2 pos; public float rot; public Vector2 vel; public float angVel;
        }

        /// <summary>落とす直前の全体の状態。広告を見て、ここへ戻る。</summary>
        class Snapshot
        {
            public readonly List<PieceState> items = new();
            public Piece dropped;
            public float x;
            public int rotDeg, lives;
            public bool bossPending;
            public float nextBoss;
        }

        /// <summary>最高到達時の、1ピース分の記録(結果画面の背景に、同じ塔を作り直すため)。</summary>
        struct Ghost
        {
            public char kind; public int boss; public Vector2 pos; public float rot;
        }

        const string SiteUrl = "https://nusbocrom.github.io/block-free-fall/";
        static readonly string[] MissMsg = { "あっ", "ツルッ", "さようなら", "重力は正義", "そこから？", "滑った！", "ありがとう物理" };
        static readonly string[] EndMsg = { "摩擦は友達ではなかった", "積んだ気になってた？", "物理が本気を出した", "もう少しで…ではなかった", "高さ？ そんなものは幻想" };
        const int None = -99;

        Camera cam, bgCam;
        Ui ui;
        readonly List<Piece> pieces = new();
        readonly List<Piece> graveyard = new();        // 落ちて消えたピース。直前に戻るときに復活させるため、捨てずに隠しておく
        readonly List<Ghost> peakRecord = new();
        readonly List<Piece> ghosts = new();
        readonly List<Ptr> ptrs = new();
        Piece held, current;
        State state;
        GameObject guideL, guideR, beam;
        Snapshot snap;
        Texture2D thumb;                 // 結果画面に出す、最高到達時の塔の小さな画像
        bool usedRevive;

        int lives, count, peakCount, rotDeg;
        float rotTarget;                 // 持っているピースの、目標の向き(度)。見た目は、これへなめらかに追いかける
        float score, peakScore, best;
        bool bossPending;
        float nextBoss, spawnDelay, settleTimer, droppedAt;
        int sliderId = None, areaId = None;
        readonly float[] keyT = new float[4];

        // ============ 起動 ============
        void Awake()
        {
            Theme.Load();
            Application.targetFrameRate = 60;
            Screen.orientation = ScreenOrientation.Portrait;   // 実機用(エディタでは無視される)
            Physics2D.gravity = new Vector2(0f, -Cfg.Gravity);

            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.transform.position = new Vector3(0f, 0f, -10f);

            // 枠の外側(横長の画面の両脇)を背景色で塗るカメラ
            var bgGo = new GameObject("Backdrop Camera");
            bgCam = bgGo.AddComponent<Camera>();
            bgCam.orthographic = true; bgCam.clearFlags = CameraClearFlags.SolidColor;
            bgCam.cullingMask = 0; bgCam.depth = -100;

            Bowl.Build();
            guideL = MakeGuide();
            guideR = MakeGuide();
            beam = MakeBeam();

            ui = new GameObject("Ui").AddComponent<Ui>();
            ui.Build();
            ui.OnRetry = () => Ads.BeforeRetry(ResetGame);
            ui.OnMute = () => ui.SetMuteLabel(Sfx.ToggleMute());
            ui.OnRevive = RequestRevive;
            ui.OnShare = Share;
            ui.OnSearch = () => Application.OpenURL("https://x.com/search?q=" + Uri.EscapeDataString("#ブロック自由落下") + "&src=typed_query&f=top");
            if (Object.FindFirstObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();

            // 広告: いまはダミーの広告ウィンドウ。本番では、広告SDKに差し替える
            Ads.RewardedProvider = (ok, no) => ui.OpenAdWindow("戻る(直前に復活)", ok);
            Ads.InterstitialProvider = done => ui.OpenAdWindow("ゲームに戻る", done);

            Theme.Changed += ApplyTheme;
            ApplyTheme();
            Canvas.ForceUpdateCanvases();
            FitCamera();

            best = PlayerPrefs.GetFloat("bff-best", 0f);
            ResetGame();

            // 初回(タッチ操作の端末)だけ、操作説明を出す
            if (!ui.HowtoSeen && Touchscreen.current != null) ui.ShowHowto(true);
        }

        void OnDestroy() { Theme.Changed -= ApplyTheme; }

        void ApplyTheme()
        {
            cam.backgroundColor = Theme.Bg;
            bgCam.backgroundColor = Theme.Bg;
            foreach (var g in new[] { guideL, guideR }) g.GetComponent<SpriteRenderer>().color = Theme.Guide;
            beam.GetComponent<SpriteRenderer>().color = Theme.Beam;
        }

        GameObject MakeGuide()
        {
            var g = new GameObject("guide");
            var sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = Shapes.Dot();
            sr.drawMode = SpriteDrawMode.Tiled;          // 丸い点を縦に並べて、点線にする
            sr.sortingOrder = 0;
            return g;
        }

        /// <summary>2本の点線のあいだに、落ちる範囲をうっすら示す帯。</summary>
        GameObject MakeBeam()
        {
            var g = new GameObject("beam");
            var sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = Shapes.Box(1f, 1f, false);
            sr.sortingOrder = -2;
            return g;
        }

        void ResetGame()
        {
            StopAllCoroutines();
            ClearGhosts();
            foreach (var p in pieces) if (p != null) Destroy(p.gameObject);
            foreach (var p in graveyard) if (p != null) Destroy(p.gameObject);
            pieces.Clear(); graveyard.Clear(); peakRecord.Clear();
            if (held != null) Destroy(held.gameObject);
            held = current = null;
            snap = null; usedRevive = false;

            lives = Cfg.Lives; score = peakScore = 0f; count = peakCount = 0;
            bossPending = false; nextBoss = Cfg.BossHeight; spawnDelay = 0f;
            sliderId = areaId = None;
            cam.orthographicSize = Mathf.Max(Cfg.WorldWidth / 2f / cam.aspect, 7f);
            state = State.Play;
            ui.HideGameOver();
            RefreshHud();
            Spawn();
        }

        // ============ 毎フレーム ============
        void Update()
        {
            ui.Tick();
            FitCamera();
            if (state == State.Play && !ui.OverlayVisible)
            {
                Poll();
                HandlePointers();
                HandleKeys();
                Logic();
            }
            UpdateCamera();
            if (held != null)
            {
                var p = held.transform.position;
                held.transform.position = new Vector3(p.x, SpawnY(), 0f);
                float z = held.transform.eulerAngles.z;
                if (Mathf.Abs(Mathf.DeltaAngle(z, rotTarget)) > 0.05f)
                {
                    float nz = Mathf.LerpAngle(z, rotTarget, 1f - Mathf.Exp(-28f * Time.deltaTime));
                    if (Mathf.Abs(Mathf.DeltaAngle(nz, rotTarget)) < 0.3f) nz = rotTarget;
                    held.transform.rotation = Quaternion.Euler(0f, 0f, nz);
                    ClampHeld();
                }
                Physics2D.SyncTransforms();
            }
            UpdateGuides();
        }

        /// <summary>ゲームの絵を、縦長の枠の中(下の操作バーの上)に映す。</summary>
        void FitCamera()
        {
            var r = ui.GameRect();
            if (r.width > 1f && r.height > 1f) cam.pixelRect = r;
        }

        void FixedUpdate()
        {
            // 落下速度に上限をかけて、着地の衝撃を抑える
            foreach (var p in pieces)
            {
                if (p == null || !p.gameObject.activeSelf) continue;
                var v = p.Rb.linearVelocity;
                if (v.y < -Cfg.MaxFallSpeed) p.Rb.linearVelocity = new Vector2(v.x, -Cfg.MaxFallSpeed);
            }
        }

        // ============ ルール ============
        void Logic()
        {
            // 落下判定。消さずに隠しておく(直前に戻るときに復活させる)
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                var p = pieces[i];
                if (p.transform.position.y > Cfg.FallY) continue;
                bool wasCurrent = p == current;
                pieces.RemoveAt(i);
                p.gameObject.SetActive(false);
                graveyard.Add(p);
                if (wasCurrent) current = null;
                Miss();
                if (state != State.Play) return;
                if (wasCurrent) Spawn();
            }

            // 高さと個数
            Stats(out float h, out int n);
            if (!Mathf.Approximately(h, score) || n != count)
            {
                bool newPeak = h > peakScore;
                score = h; count = n;
                peakScore = Mathf.Max(peakScore, score);
                peakCount = Mathf.Max(peakCount, count);
                if (newPeak) RecordPeak();
                RefreshHud();
            }

            // 次のピースを出す判定
            if (current != null)
            {
                settleTimer = current.Settled() ? settleTimer + Time.deltaTime : 0f;
                if (settleTimer > Cfg.SettleSeconds || Time.time - droppedAt > Cfg.MaxWaitSeconds)
                {
                    current = null;
                    Sfx.Stack(count);
                    if (!bossPending && score >= nextBoss)
                    {
                        bossPending = true;
                        ui.ShowWarning(Cfg.WarnSeconds);
                        Sfx.Warning();
                        spawnDelay = Cfg.WarnSeconds;
                    }
                    else Spawn();
                }
            }

            // 注意の帯を見せてから変な物体を出す
            if (held == null && current == null && spawnDelay > 0f)
            {
                spawnDelay -= Time.deltaTime;
                if (spawnDelay <= 0f) Spawn();
            }
        }

        /// <summary>塔の高さ(m)と、お椀の上にあるピースの数。</summary>
        void Stats(out float h, out int n)
        {
            float top = 0f; n = 0;
            foreach (var p in pieces)
            {
                var b = p.GetBounds();
                if (p.Settled() && p.transform.position.y > -5f) top = Mathf.Max(top, b.max.y);
                if (b.max.x >= -Cfg.BowlHalfWidth && b.min.x <= Cfg.BowlHalfWidth && b.max.y >= -2f) n++;
            }
            h = Mathf.Max(0f, Mathf.Round(top * 0.5f * 10f) / 10f);   // 1マス=0.5m
        }

        void Spawn()
        {
            Piece p;
            if (bossPending)
            {
                p = PieceFactory.CreateBoss(Random.Range(0, PieceFactory.BossKinds));
                bossPending = false;
                nextBoss = score + Cfg.BossInterval;
                ui.Flash("なんか来た");
                Sfx.Boss();
            }
            else
            {
                p = PieceFactory.Create(PieceFactory.Kinds[Random.Range(0, PieceFactory.Kinds.Length)]);
            }
            held = p;
            p.SetAlpha(0.85f);
            p.transform.position = new Vector3(0f, SpawnY(), 0f);
            SetRot(0, true);
        }

        void Drop()
        {
            if (state != State.Play || held == null) return;
            TakeSnapshot();
            var p = held;
            p.transform.rotation = Quaternion.Euler(0f, 0f, rotTarget);   // 追いかけている途中でも、スライダーの値の向きで落とす
            Physics2D.SyncTransforms();
            p.Rb.bodyType = RigidbodyType2D.Dynamic;
            p.SetAlpha(1f);
            p.ApplyDensity();
            Sfx.Drop();
            ui.PunchDrop();
            p.Rb.linearVelocity = new Vector2(0f, -Cfg.DropSpeed);
            p.Rb.angularVelocity = 0f;
            pieces.Add(p);
            current = p; held = null;
            settleTimer = 0f; droppedAt = Time.time;
        }

        void Miss()
        {
            lives--;
            Sfx.Miss();
            ui.Flash(MissMsg[Random.Range(0, MissMsg.Length)]);
            RefreshHud();
            if (lives <= 0) GameOver();
        }

        void GameOver()
        {
            state = State.Over;
            Sfx.Over();
            if (peakScore > best) { best = peakScore; PlayerPrefs.SetFloat("bff-best", best); PlayerPrefs.Save(); }
            StartCoroutine(ShowOverLater());
        }

        IEnumerator ShowOverLater()
        {
            yield return new WaitForSeconds(0.7f);
            // 崩れた塔は隠して、最高到達時の塔を作り直して、結果画面の背景にする
            foreach (var p in pieces) if (p != null) p.gameObject.SetActive(false);
            if (held != null) held.gameObject.SetActive(false);
            BuildGhosts();

            // 最高到達時の塔が収まる大きさに、すぐ合わせて、画面の部品を隠して1枚撮る(結果画面の小さな画像)
            SnapCamera(peakScore);
            ui.SetCanvasVisible(false);
            yield return new WaitForEndOfFrame();
            if (thumb != null) Destroy(thumb);
            thumb = CaptureGameArea();
            ClearGhosts();
            ui.SetCanvasVisible(true);

            ui.ShowGameOver(peakScore, peakCount, best, EndMsg[Random.Range(0, EndMsg.Length)], !usedRevive && snap != null, thumb);
        }

        /// <summary>ゲームの絵を映している範囲だけを、画像にする。撮れなければ null。</summary>
        Texture2D CaptureGameArea()
        {
            try
            {
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                var r = ui.GameRect();
                int x = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, shot.width - 1), y = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, shot.height - 1);
                int w = Mathf.Min(Mathf.RoundToInt(r.width), shot.width - x), h = Mathf.Min(Mathf.RoundToInt(r.height), shot.height - y);
                var crop = new Texture2D(w, h, TextureFormat.RGB24, false);
                crop.SetPixels(shot.GetPixels(x, y, w, h));
                crop.Apply();
                Destroy(shot);
                return crop;
            }
            catch (Exception e) { Debug.LogWarning(e); return null; }
        }

        void RefreshHud() => ui.SetHud(score, peakScore, count, peakCount, lives);

        // ============ 最高到達時の塔(結果画面の背景) ============
        void RecordPeak()
        {
            peakRecord.Clear();
            foreach (var p in pieces)
            {
                if (p.transform.position.y < -5f) continue;
                peakRecord.Add(new Ghost { kind = p.Kind, boss = p.BossIndex, pos = p.transform.position, rot = p.transform.eulerAngles.z });
            }
        }

        void BuildGhosts()
        {
            ClearGhosts();
            foreach (var r in peakRecord)
            {
                var g = r.boss >= 0 ? PieceFactory.CreateBoss(r.boss) : PieceFactory.Create(r.kind);
                foreach (var c in g.Cols) c.enabled = false;     // 見た目だけ。当たり判定も、砂ぼこりも出さない
                g.transform.SetPositionAndRotation(new Vector3(r.pos.x, r.pos.y, 0f), Quaternion.Euler(0f, 0f, r.rot));
                ghosts.Add(g);
            }
        }

        void ClearGhosts()
        {
            foreach (var g in ghosts) if (g != null) Destroy(g.gameObject);
            ghosts.Clear();
        }

        // ============ 直前に戻る(広告を見て、1回だけ) ============
        void TakeSnapshot()
        {
            snap = new Snapshot { dropped = held, x = held.transform.position.x, rotDeg = rotDeg, lives = lives, bossPending = bossPending, nextBoss = nextBoss };
            foreach (var p in pieces)
                snap.items.Add(new PieceState { piece = p, pos = p.transform.position, rot = p.Rb.rotation, vel = p.Rb.linearVelocity, angVel = p.Rb.angularVelocity });
        }

        void RequestRevive()
        {
            if (usedRevive || snap == null) return;
            Ads.Rewarded(RestoreSnapshot, ui.ReviveNoAd);
        }

        void RestoreSnapshot()
        {
            if (snap == null) return;
            ClearGhosts();
            var keep = new HashSet<Piece> { snap.dropped };
            foreach (var it in snap.items) keep.Add(it.piece);

            // 戻る時点より後に増えたものは、全部捨てる
            foreach (var p in pieces) if (p != null && !keep.Contains(p)) Destroy(p.gameObject);
            foreach (var p in graveyard) if (p != null && !keep.Contains(p)) Destroy(p.gameObject);
            if (held != null && !keep.Contains(held)) Destroy(held.gameObject);
            pieces.Clear(); graveyard.Clear();

            foreach (var it in snap.items)
            {
                var p = it.piece;
                p.gameObject.SetActive(true);
                p.transform.SetPositionAndRotation(new Vector3(it.pos.x, it.pos.y, 0f), Quaternion.Euler(0f, 0f, it.rot));
                p.Rb.linearVelocity = it.vel;
                p.Rb.angularVelocity = it.angVel;
                pieces.Add(p);
            }

            // 落とす直前だったピースを、元の位置・向きで持たせ直す
            var d = snap.dropped;
            d.gameObject.SetActive(true);
            d.Rb.bodyType = RigidbodyType2D.Kinematic;
            d.SetAlpha(0.85f);
            d.Rb.linearVelocity = Vector2.zero;
            d.Rb.angularVelocity = 0f;
            held = d;
            current = null; spawnDelay = 0f; settleTimer = 0f;
            SetHeldX(snap.x);
            SetRot(snap.rotDeg, true);

            lives = Mathf.Min(Cfg.Lives, snap.lives + 1);
            bossPending = snap.bossPending; nextBoss = snap.nextBoss;
            usedRevive = true;
            state = State.Play;
            ui.HideGameOver();
            RefreshHud();
            ui.Flash("もう一回！");
            Sfx.Revive();
        }

        // ============ シェア(最高到達時の塔の画像つき) ============
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int BffShare(byte[] data, int size, string name, string text, string url);
#endif
        bool sharing;

        void Share()
        {
            if (!sharing) StartCoroutine(ShareRoutine());
        }

        IEnumerator ShareRoutine()
        {
            sharing = true;
            string text = $"【ブロック自由落下】 {peakScore:0.0}m・{peakCount}個 積んだ！ 物理がつらい… #ブロック自由落下 #クソゲー作ります";

            // 結果画面(最高到達時の塔、ゲーム名、記録)を、ボタンを隠して撮る
            ui.HideResultButtons(true);
            yield return new WaitForEndOfFrame();
            byte[] png = null;
            try
            {
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                var r = ui.FrameRect();
                int x = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, shot.width - 1), y = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, shot.height - 1);
                int w = Mathf.Min(Mathf.RoundToInt(r.width), shot.width - x), h = Mathf.Min(Mathf.RoundToInt(r.height), shot.height - y);
                var crop = new Texture2D(w, h, TextureFormat.RGB24, false);
                crop.SetPixels(shot.GetPixels(x, y, w, h));
                crop.Apply();
                png = crop.EncodeToPNG();
                Destroy(shot);
                Destroy(crop);
            }
            catch (Exception e) { Debug.LogWarning(e); }
            ui.HideResultButtons(false);

            ShareContent(png, text);
            sharing = false;
        }

        void ShareContent(byte[] png, string text)
        {
            if (png == null) { ShareText(text); return; }
#if UNITY_WEBGL && !UNITY_EDITOR
            // 端末の共有画面が使えれば、画像つきで渡す。使えなければ、画像を保存して、Xの投稿画面を開く(Web版と同じ)
            if (BffShare(png, png.Length, "block-free-fall.png", text, SiteUrl) == 0)
            {
                ui.Toast("画像を保存しました。投稿に添付してね");
                OpenTweet(text);
            }
#elif UNITY_ANDROID && !UNITY_EDITOR
            ShareText(text);   // 画像つきの共有は、Android側のファイル共有の設定が要るので、あとで
#else
            // パソコン: 画像を、ピクチャフォルダ(なければ保存用の場所)に保存して、Xの投稿画面を開く
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Application.persistentDataPath;
            string path = Path.Combine(dir, "block-free-fall.png");
            File.WriteAllBytes(path, png);
            ui.Toast("画像を保存しました。投稿に添付してね");
            OpenTweet(text);
#endif
        }

        void OpenTweet(string text) =>
            Application.OpenURL("https://twitter.com/intent/tweet?text=" + Uri.EscapeDataString(text) + "&url=" + Uri.EscapeDataString(SiteUrl));

        /// <summary>文字だけのシェア。Androidは共有画面、ほかはXの投稿画面。</summary>
        void ShareText(string text)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var intentClass = new AndroidJavaClass("android.content.Intent");
                using var intent = new AndroidJavaObject("android.content.Intent");
                intent.Call<AndroidJavaObject>("setAction", "android.intent.action.SEND");
                intent.Call<AndroidJavaObject>("setType", "text/plain");
                intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TEXT", text + "\n" + SiteUrl);
                using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
                using var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "シェア");
                activity.Call("startActivity", chooser);
                return;
            }
            catch (Exception e) { Debug.LogWarning(e); }
#endif
            OpenTweet(text);
        }

        // ============ 持っているピースの操作 ============
        void SetHeldX(float x)
        {
            if (held == null) return;
            held.transform.position = new Vector3(x, SpawnY(), 0f);
            ClampHeld();
        }

        void ClampHeld()
        {
            if (held == null) return;
            Physics2D.SyncTransforms();
            var b = held.GetBounds();
            float hw = Cfg.WorldWidth / 2f, dx = 0f;
            if (b.min.x < -hw) dx = -hw - b.min.x;
            else if (b.max.x > hw) dx = hw - b.max.x;
            if (dx != 0f)
            {
                held.transform.position += new Vector3(dx, 0f, 0f);
                Physics2D.SyncTransforms();
            }
        }

        /// <summary>向きを決める(下向きが時計回り。スライダーと同じ)。instant=false なら、見た目はなめらかに追いかける。</summary>
        void SetRot(int deg, bool instant = false)
        {
            rotDeg = deg;
            rotTarget = Mathf.Repeat(-deg, 360f);
            ui.SetRot(deg);
            if (held == null || !instant) return;
            held.transform.rotation = Quaternion.Euler(0f, 0f, rotTarget);
            ClampHeld();
        }

        void Rotate(int dir)
        {
            int d = rotDeg + dir * Cfg.RotStep;
            if (d > 180) d -= 360;
            if (d < -180) d += 360;
            SetRot(d);
        }

        // ============ 入力 ============
        void Poll()
        {
            ptrs.Clear();
            var ts = Touchscreen.current;
            if (ts != null)
            {
                foreach (var t in ts.touches)
                {
                    var ph = t.phase.ReadValue();
                    if (ph == UnityEngine.InputSystem.TouchPhase.None) continue;
                    ptrs.Add(new Ptr
                    {
                        id = t.touchId.ReadValue(),
                        pos = t.position.ReadValue(),
                        began = ph == UnityEngine.InputSystem.TouchPhase.Began,
                        ended = ph == UnityEngine.InputSystem.TouchPhase.Ended || ph == UnityEngine.InputSystem.TouchPhase.Canceled
                    });
                }
            }
            var m = Mouse.current;
            if (m != null && (m.leftButton.isPressed || m.leftButton.wasReleasedThisFrame))
            {
                ptrs.Add(new Ptr
                {
                    id = -1, pos = m.position.ReadValue(),
                    began = m.leftButton.wasPressedThisFrame, ended = m.leftButton.wasReleasedThisFrame
                });
            }
        }

        void HandlePointers()
        {
            foreach (var p in ptrs)
            {
                if (p.began)
                {
                    if (ui.InSlider(p.pos)) sliderId = p.id;
                    else if (ui.InDrop(p.pos)) Drop();
                    else if (!ui.InButtons(p.pos) && cam.pixelRect.Contains(p.pos)) areaId = p.id;
                }
                if (p.id == sliderId)
                {
                    if (p.ended) sliderId = None;
                    else
                    {
                        float f = ui.SliderFraction(p.pos);
                        SetRot(Mathf.RoundToInt((f * 360f - 180f) / Cfg.RotStep) * Cfg.RotStep);
                    }
                }
                if (p.id == areaId)
                {
                    if (p.ended) areaId = None;
                    else if (held != null)
                    {
                        var w = cam.ScreenToWorldPoint(new Vector3(p.pos.x, p.pos.y, -cam.transform.position.z));
                        SetHeldX(w.x);
                    }
                }
            }
        }

        void HandleKeys()
        {
            var k = Keyboard.current;
            if (k != null)
            {
                if (held != null)
                {
                    float dir = (k.rightArrowKey.isPressed ? 1f : 0f) - (k.leftArrowKey.isPressed ? 1f : 0f);
                    if (dir != 0f) SetHeldX(held.transform.position.x + dir * 10f * Time.deltaTime);
                }
                // 上下は押し続けると10度ずつ連続で回る。上=反時計回り、下=時計回り
                if (Repeat(k.upArrowKey, 0)) Rotate(-1);
                if (Repeat(k.downArrowKey, 1)) Rotate(1);
                if (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame) Drop();
            }
            var m = Mouse.current;
            if (m != null)
            {
                float wheel = m.scroll.ReadValue().y;
                if (wheel > 0f) Rotate(-1); else if (wheel < 0f) Rotate(1);
                if (m.rightButton.wasPressedThisFrame) Drop();
            }
        }

        bool Repeat(KeyControl key, int i)
        {
            if (key.wasPressedThisFrame) { keyT[i] = 0f; return true; }
            if (key.isPressed)
            {
                keyT[i] += Time.deltaTime;
                if (keyT[i] > 0.35f) { keyT[i] -= 0.05f; return true; }
            }
            return false;
        }

        // ============ 見た目 ============
        float SpawnY() => cam.transform.position.y + cam.orthographicSize - 3f;

        /// <summary>高さ s(m)の塔が収まるカメラの大きさ。</summary>
        float TargetSize(float s)
        {
            float needH = Mathf.Max(0f, s * 2f) + 14f;
            return Mathf.Max(Cfg.WorldWidth / 2f / cam.aspect, needH / 2f);
        }

        /// <summary>カメラを、なめらかに追いかけず、すぐ合わせる。</summary>
        void SnapCamera(float s)
        {
            cam.orthographicSize = TargetSize(s);
            cam.transform.position = new Vector3(0f, cam.orthographicSize - 1.67f, -10f);
        }

        void UpdateCamera()
        {
            // 塔が高くなるほど引く。結果画面では、最高到達時の塔が収まるようにする。下から1.67ユニットの位置が、お椀の中央
            float s = state == State.Over ? peakScore : score;
            float target = TargetSize(s);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, target, 1f - Mathf.Exp(-3f * Time.deltaTime));
            cam.transform.position = new Vector3(0f, cam.orthographicSize - 1.67f, -10f);
        }

        /// <summary>持っているピースの左端・右端から真下に、落下位置の目安の点線を2本引く。</summary>
        void UpdateGuides()
        {
            bool on = held != null && state == State.Play;
            guideL.SetActive(on); guideR.SetActive(on); beam.SetActive(false);   // 落下範囲の帯は、Webにないので出さない
            if (!on) return;
            var b = held.GetBounds();
            float top = b.min.y, bottom = 0f;
            float len = Mathf.Max(0.1f, top - bottom);
            foreach (var (g, x) in new[] { (guideL, b.min.x), (guideR, b.max.x) })
            {
                g.transform.position = new Vector3(x, (top + bottom) / 2f, 0f);
                g.GetComponent<SpriteRenderer>().size = new Vector2(0.25f, len);
            }
            beam.transform.position = new Vector3((b.min.x + b.max.x) / 2f, (top + bottom) / 2f, 0f);
            beam.transform.localScale = new Vector3(b.max.x - b.min.x, len, 1f);
        }
    }
}
