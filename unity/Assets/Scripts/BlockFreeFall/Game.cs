using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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

        static readonly string[] MissMsg = { "あっ", "ツルッ", "さようなら", "重力は正義", "そこから？", "滑った！", "ありがとう物理" };
        static readonly string[] EndMsg = { "摩擦は友達ではなかった", "積んだ気になってた？", "物理が本気を出した", "もう少しで…ではなかった", "高さ？ そんなものは幻想" };
        const int None = -99;

        Camera cam;
        Ui ui;
        readonly List<Piece> pieces = new();
        readonly List<Ptr> ptrs = new();
        Piece held, current;
        State state;
        GameObject guideL, guideR;

        int lives, count, peakCount, rotDeg;
        float score, peakScore, best;
        bool bossPending;
        float nextBoss, spawnDelay, settleTimer, droppedAt;
        int sliderId = None, areaId = None;
        readonly float[] keyT = new float[4];

        // ============ 起動 ============
        void Awake()
        {
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
            cam.backgroundColor = Theme.Bg;
            cam.transform.position = new Vector3(0f, 0f, -10f);

            // 枠の外側(横長の画面の両脇)を背景色で塗るカメラ
            var bgGo = new GameObject("Backdrop Camera");
            var bg = bgGo.AddComponent<Camera>();
            bg.orthographic = true; bg.clearFlags = CameraClearFlags.SolidColor; bg.backgroundColor = Theme.Bg;
            bg.cullingMask = 0; bg.depth = -100;

            Bowl.Build();
            guideL = MakeGuide();
            guideR = MakeGuide();

            ui = new GameObject("Ui").AddComponent<Ui>();
            ui.Build();
            ui.OnRetry = ResetGame;
            Canvas.ForceUpdateCanvases();
            FitCamera();

            best = PlayerPrefs.GetFloat("bff-best", 0f);
            ResetGame();
        }

        GameObject MakeGuide()
        {
            var g = new GameObject("guide");
            var sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = Shapes.Dash();
            sr.drawMode = SpriteDrawMode.Tiled;          // 点線の1周期を、縦に並べる
            sr.color = new Color(0f, 0f, 0f, 0.3f);
            sr.sortingOrder = 0;
            return g;
        }

        void ResetGame()
        {
            StopAllCoroutines();
            foreach (var p in pieces) if (p != null) Destroy(p.gameObject);
            pieces.Clear();
            if (held != null) Destroy(held.gameObject);
            held = current = null;

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
                if (p == null) continue;
                var v = p.Rb.linearVelocity;
                if (v.y < -Cfg.MaxFallSpeed) p.Rb.linearVelocity = new Vector2(v.x, -Cfg.MaxFallSpeed);
            }
        }

        // ============ ルール ============
        void Logic()
        {
            // 落下判定
            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                var p = pieces[i];
                if (p.transform.position.y > Cfg.FallY) continue;
                bool wasCurrent = p == current;
                pieces.RemoveAt(i);
                Destroy(p.gameObject);
                if (wasCurrent) current = null;
                Miss();
                if (state != State.Play) return;
                if (wasCurrent) Spawn();
            }

            // 高さと個数
            Stats(out float h, out int n);
            if (!Mathf.Approximately(h, score) || n != count)
            {
                score = h; count = n;
                peakScore = Mathf.Max(peakScore, score);
                peakCount = Mathf.Max(peakCount, count);
                RefreshHud();
            }

            // 次のピースを出す判定
            if (current != null)
            {
                settleTimer = current.Settled() ? settleTimer + Time.deltaTime : 0f;
                if (settleTimer > Cfg.SettleSeconds || Time.time - droppedAt > Cfg.MaxWaitSeconds)
                {
                    current = null;
                    if (!bossPending && score >= nextBoss)
                    {
                        bossPending = true;
                        ui.ShowWarning(Cfg.WarnSeconds);
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
            }
            else
            {
                p = PieceFactory.Create(PieceFactory.Kinds[Random.Range(0, PieceFactory.Kinds.Length)]);
            }
            held = p;
            p.transform.position = new Vector3(0f, SpawnY(), 0f);
            SetRot(0);
        }

        void Drop()
        {
            if (state != State.Play || held == null) return;
            var p = held;
            p.Rb.bodyType = RigidbodyType2D.Dynamic;
            p.ApplyDensity();
            p.Rb.linearVelocity = new Vector2(0f, -Cfg.DropSpeed);
            p.Rb.angularVelocity = 0f;
            pieces.Add(p);
            current = p; held = null;
            settleTimer = 0f; droppedAt = Time.time;
        }

        void Miss()
        {
            lives--;
            ui.Flash(MissMsg[Random.Range(0, MissMsg.Length)]);
            RefreshHud();
            if (lives <= 0) GameOver();
        }

        void GameOver()
        {
            state = State.Over;
            if (peakScore > best) { best = peakScore; PlayerPrefs.SetFloat("bff-best", best); PlayerPrefs.Save(); }
            StartCoroutine(ShowOverLater());
        }

        IEnumerator ShowOverLater()
        {
            yield return new WaitForSeconds(0.7f);
            ui.ShowGameOver(peakScore, peakCount, best, EndMsg[Random.Range(0, EndMsg.Length)]);
        }

        void RefreshHud() => ui.SetHud(score, peakScore, count, peakCount, lives);

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

        void SetRot(int deg)
        {
            rotDeg = deg;
            ui.SetRot(deg);
            if (held == null) return;
            held.transform.rotation = Quaternion.Euler(0f, 0f, -deg);   // 下向きが時計回り(スライダーと同じ)
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
                    else if (!ui.InHand(p.pos) && cam.pixelRect.Contains(p.pos)) areaId = p.id;
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
        float SpawnY() => cam.transform.position.y + cam.orthographicSize - 3.2f;

        void UpdateCamera()
        {
            // 塔が高くなるほど引く。下から1.67ユニットの位置が、お椀の中央
            float needH = Mathf.Max(0f, score * 2f) + 14f;
            float target = Mathf.Max(Cfg.WorldWidth / 2f / cam.aspect, needH / 2f);
            cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, target, 1f - Mathf.Exp(-3f * Time.deltaTime));
            cam.transform.position = new Vector3(0f, cam.orthographicSize - 1.67f, -10f);
        }

        /// <summary>持っているピースの左端・右端から真下に、落下位置の目安の線を2本引く。</summary>
        void UpdateGuides()
        {
            bool on = held != null && state == State.Play;
            guideL.SetActive(on); guideR.SetActive(on);
            if (!on) return;
            var b = held.GetBounds();
            float top = b.min.y, bottom = 0f;
            float len = Mathf.Max(0.1f, top - bottom);
            foreach (var (g, x) in new[] { (guideL, b.min.x), (guideR, b.max.x) })
            {
                g.transform.position = new Vector3(x, (top + bottom) / 2f, 0f);
                g.GetComponent<SpriteRenderer>().size = new Vector2(0.125f, len);
            }
        }
    }
}
