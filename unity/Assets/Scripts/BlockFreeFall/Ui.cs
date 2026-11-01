using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BlockFreeFall
{
    /// <summary>画面の表示と、タップできる部品(コードだけで組み立てる。シーンの設定は不要)。</summary>
    public class Ui : MonoBehaviour
    {
        public const float BarFrac = 0.12f;      // 下の操作バーの高さ(画面に対する割合)

        public Action OnRetry;

        Font font;
        Text hud, lives, msg, warnText, overScore, overCount, overBest, overComment, thumbText, handText;
        Image warnBg;
        RectTransform sliderArea, thumb, dropRect, handRect, overPanel;
        float msgT, warnT;
        bool sliderLeft;
        RectTransform frame;
        AspectRatioFitter fit;
        CanvasScaler scaler;
        bool lastWide;

        public bool OverlayVisible => overPanel != null && overPanel.gameObject.activeSelf;

        public void Build()
        {
            font = Resources.Load<Font>("Fonts/MPLUSRounded1c-Bold");
            if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic", "Noto Sans CJK JP", "Arial" }, 40);

            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            // 横長の画面では、中央に縦長(9:16)の枠を作って、その中に全部を置く。縦長の画面では全面を使う
            var frameGo = new GameObject("Frame", typeof(RectTransform), typeof(AspectRatioFitter));
            frame = (RectTransform)frameGo.transform;
            frame.SetParent(cgo.transform, false);
            frame.anchorMin = Vector2.zero; frame.anchorMax = Vector2.one;
            frame.offsetMin = frame.offsetMax = Vector2.zero;
            fit = frameGo.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = 9f / 16f;
            lastWide = true;
            var root = frame;

            // 下の操作バー(落とすボタン)
            var bar = Rect("Bar", root, new Vector2(0, 0), new Vector2(1, BarFrac));
            Img(bar, Theme.Panel);
            // 「落とす」ボタン: 濃い色の角丸に、白い下向き矢印
            var dropShadow = Rect("DropShadow", bar, new Vector2(0.08f, 0.16f), new Vector2(0.92f, 0.84f));
            dropShadow.offsetMin = new Vector2(0, -8); dropShadow.offsetMax = new Vector2(0, -8);
            RoundImg(dropShadow, new Color(0, 0, 0, 0.22f), 1.7f);
            dropRect = Rect("Drop", bar, new Vector2(0.08f, 0.16f), new Vector2(0.92f, 0.84f));
            RoundImg(dropRect, Theme.Strong, 1.7f);
            Label(Rect("t", dropRect, Vector2.zero, Vector2.one), "↓", 110, TextAnchor.MiddleCenter, Theme.StrongText);

            // 上の表示
            hud = Label(Rect("Hud", root, new Vector2(0.03f, 0.925f), new Vector2(0.66f, 0.995f)), "", 38, TextAnchor.UpperLeft, Theme.Text);
            lives = Label(Rect("Lives", root, new Vector2(0.66f, 0.95f), new Vector2(0.97f, 0.995f)), "", 36, TextAnchor.UpperRight, Theme.Text);
            handRect = Rect("Hand", root, new Vector2(0.72f, 0.905f), new Vector2(0.97f, 0.945f));
            RoundImg(handRect, new Color(0, 0, 0, 0.12f), 0.6f);
            handRect.gameObject.AddComponent<Button>().onClick.AddListener(ToggleHand);
            handText = Label(Rect("t", handRect, Vector2.zero, Vector2.one), "", 32, TextAnchor.MiddleCenter, Theme.Text);

            // 回転スライダー(縦)。上=-180度、中央=0度、下=+180度
            sliderArea = Rect("Slider", root, new Vector2(0.86f, 0.17f), new Vector2(0.98f, 0.82f));
            RoundImg(Rect("Track", sliderArea, new Vector2(0.4f, 0f), new Vector2(0.6f, 1f)), new Color(0, 0, 0, 0.22f), 3.5f);
            var mark = Rect("Mark", sliderArea, new Vector2(0.2f, 0.5f), new Vector2(0.8f, 0.5f));
            mark.offsetMin = new Vector2(0, -2); mark.offsetMax = new Vector2(0, 2);   // 中央(0度)の目印
            Img(mark, new Color(0, 0, 0, 0.35f));
            thumb = Rect("Thumb", sliderArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            thumb.sizeDelta = new Vector2(96, 54);
            RoundImg(thumb, Theme.Strong, 3.2f);
            thumbText = Label(Rect("t", thumb, Vector2.zero, Vector2.one), "0°", 30, TextAnchor.MiddleCenter, Theme.StrongText);
            SetHand(PlayerPrefs.GetString("bff-hand", "R") == "L");

            // メッセージと注意の帯
            msg = Label(Rect("Msg", root, new Vector2(0, 0.52f), new Vector2(1, 0.62f)), "", 80, TextAnchor.MiddleCenter, Theme.Miss);
            var warnRect = Rect("Warn", root, new Vector2(0, 0.64f), new Vector2(1, 0.74f));
            warnBg = Img(warnRect, Theme.Warn);
            warnText = Label(Rect("t", warnRect, Vector2.zero, Vector2.one), "注意!  何かが来る", 72, TextAnchor.MiddleCenter, Color.black);
            warnRect.gameObject.SetActive(false);

            BuildOver(root);
        }

        void BuildOver(RectTransform root)
        {
            overPanel = Rect("Over", root, Vector2.zero, Vector2.one);
            Img(overPanel, new Color(0.96f, 0.94f, 0.9f, 0.96f));
            Label(Rect("title", overPanel, new Vector2(0, 0.78f), new Vector2(1, 0.88f)), "ブロック自由落下", 72, TextAnchor.MiddleCenter, Theme.Text);
            Label(Rect("cap", overPanel, new Vector2(0, 0.70f), new Vector2(1, 0.76f)), "最高到達点", 44, TextAnchor.MiddleCenter, Theme.Text);
            overScore = Label(Rect("score", overPanel, new Vector2(0, 0.58f), new Vector2(1, 0.70f)), "", 150, TextAnchor.MiddleCenter, Theme.Text);
            overCount = Label(Rect("count", overPanel, new Vector2(0, 0.52f), new Vector2(1, 0.58f)), "", 48, TextAnchor.MiddleCenter, Theme.Text);
            overBest = Label(Rect("best", overPanel, new Vector2(0, 0.46f), new Vector2(1, 0.52f)), "", 48, TextAnchor.MiddleCenter, Theme.Text);
            overComment = Label(Rect("comment", overPanel, new Vector2(0.05f, 0.38f), new Vector2(0.95f, 0.45f)), "", 46, TextAnchor.MiddleCenter, Theme.Text);

            var retry = Rect("Retry", overPanel, new Vector2(0.2f, 0.24f), new Vector2(0.8f, 0.33f));
            RoundImg(retry, Theme.Strong, 1.0f);
            retry.gameObject.AddComponent<Button>().onClick.AddListener(() => OnRetry?.Invoke());
            Label(Rect("t", retry, Vector2.zero, Vector2.one), "もう一回", 60, TextAnchor.MiddleCenter, Theme.StrongText);
            overPanel.gameObject.SetActive(false);
        }

        // ---------- 外から呼ぶもの ----------
        public void SetHud(float score, float peakScore, int count, int peakCount, int livesLeft)
        {
            hud.text = $"高さ {score:0.0}m (最高 {peakScore:0.0}m)\n個数 {count} (最多 {peakCount})";
            lives.text = $"ミス残り {Mathf.Max(0, livesLeft)}";
        }

        public void SetRot(int deg)
        {
            float f = (deg + 180f) / 360f;
            thumb.anchorMin = thumb.anchorMax = new Vector2(0.5f, 1f - f);
            thumb.anchoredPosition = Vector2.zero;
            thumbText.text = deg + "°";
        }

        public void Flash(string text) { msg.text = text; msgT = 0.9f; }

        public void ShowWarning(float seconds)
        {
            warnT = seconds;
            warnBg.rectTransform.gameObject.SetActive(true);
        }

        public void ShowGameOver(float peakScore, int peakCount, float best, string comment)
        {
            overScore.text = $"{peakScore:0.0}m";
            overCount.text = $"乗せた個数(最多) {peakCount}個";
            overBest.text = $"ベスト {best:0.0}m";
            overComment.text = comment;
            overPanel.gameObject.SetActive(true);
        }

        public void HideGameOver() => overPanel.gameObject.SetActive(false);

        public bool InSlider(Vector2 screen) => RectTransformUtility.RectangleContainsScreenPoint(sliderArea, screen);
        public bool InDrop(Vector2 screen) => RectTransformUtility.RectangleContainsScreenPoint(dropRect, screen);
        public bool InHand(Vector2 screen) => RectTransformUtility.RectangleContainsScreenPoint(handRect, screen);

        /// <summary>スライダーの上端を0、下端を1とした位置。</summary>
        public float SliderFraction(Vector2 screen)
        {
            var c = new Vector3[4];
            sliderArea.GetWorldCorners(c);
            float top = c[1].y, bottom = c[0].y;
            return Mathf.Clamp01((top - screen.y) / Mathf.Max(1f, top - bottom));
        }

        /// <summary>ゲームの絵を映す範囲(画面のピクセル)。縦長の枠から、下の操作バーを除いたもの。</summary>
        public Rect GameRect()
        {
            var c = new Vector3[4];
            frame.GetWorldCorners(c);
            float h = c[2].y - c[0].y, bar = h * BarFrac;
            return new Rect(c[0].x, c[0].y + bar, c[2].x - c[0].x, h - bar);
        }

        public void Tick()
        {
            bool wide = Screen.width * 16f > Screen.height * 9f;
            // 横長の画面では高さに、縦長の画面では幅に合わせて大きさを決める(枠の幅が常に約1080になる)
            scaler.matchWidthOrHeight = wide ? 1f : 0f;
            if (wide != lastWide)
            {
                lastWide = wide;
                fit.enabled = wide;
                if (!wide)
                {
                    frame.anchorMin = Vector2.zero; frame.anchorMax = Vector2.one;
                    frame.offsetMin = frame.offsetMax = Vector2.zero;
                }
            }
            if (msgT > 0f)
            {
                msgT -= Time.deltaTime;
                var c = msg.color; c.a = Mathf.Clamp01(msgT / 0.3f); msg.color = c;
            }
            if (warnT > 0f)
            {
                warnT -= Time.deltaTime;
                float a = 0.45f + 0.55f * Mathf.PingPong(Time.time * 6f, 1f);
                var bg = warnBg.color; bg.a = a; warnBg.color = bg;
                if (warnT <= 0f) warnBg.rectTransform.gameObject.SetActive(false);
            }
        }

        // ---------- スライダーの左右(利き手) ----------
        void ToggleHand() => SetHand(!sliderLeft);

        void SetHand(bool left)
        {
            sliderLeft = left;
            sliderArea.anchorMin = new Vector2(left ? 0.02f : 0.86f, 0.17f);
            sliderArea.anchorMax = new Vector2(left ? 0.14f : 0.98f, 0.82f);
            sliderArea.offsetMin = sliderArea.offsetMax = Vector2.zero;
            handText.text = "回転:" + (left ? "左" : "右");
            PlayerPrefs.SetString("bff-hand", left ? "L" : "R");
        }

        // ---------- 部品を作る道具 ----------
        static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>角丸の四角。scale が小さいほど角が大きく丸くなる。</summary>
        static Image RoundImg(RectTransform rt, Color c, float scale)
        {
            var i = Img(rt, c);
            i.sprite = Shapes.RoundRect();
            i.type = Image.Type.Sliced;
            i.pixelsPerUnitMultiplier = scale;
            return i;
        }

        static Image Img(RectTransform rt, Color c)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.color = c;
            return i;
        }

        Text Label(RectTransform rt, string s, int size, TextAnchor anchor, Color c)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.text = s; t.fontSize = size; t.alignment = anchor; t.color = c;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}
