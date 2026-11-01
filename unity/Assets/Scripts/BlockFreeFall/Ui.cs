using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BlockFreeFall
{
    /// <summary>テーマ(ライト/ダーク)が変わったとき、自分の色を塗り直す印。</summary>
    public class Themed : MonoBehaviour
    {
        public Graphic G;
        public Func<Color> Get;

        void OnEnable() { Theme.Changed += Apply; Apply(); }
        void OnDisable() { Theme.Changed -= Apply; }

        public void Apply()
        {
            if (G != null && Get != null) G.color = Get();
        }
    }

    /// <summary>
    /// 画面の表示と、タップできる部品(コードだけで組み立てる。シーンの設定は不要)。
    /// 見た目は、Web版のCSS(index.html)に合わせてある。大きさは、Webの400px幅を1080に引き伸ばした値(約2.7倍)。
    /// </summary>
    public class Ui : MonoBehaviour
    {
        public const float BarFrac = 0.084f;      // 下の操作バーの高さ(画面に対する割合)
        const float Raised = 8f;
        const float SliderBottom = 0.137f, SliderTop = 0.871f;   // Webの、画面の高さに対するスライダーの範囲                 // 白いボタンの下の厚み(Webの 0 3px 0)

        public Action OnRetry, OnMute, OnRevive, OnShare, OnSearch;

        /// <summary>広告ウィンドウの中の、広告を出す場所。広告SDKが表示先を求めるときに渡す。</summary>
        public RectTransform AdArea { get; private set; }

        Font font;
        Text hud, msg, overScore, overCount, overBest, overComment, thumbText, handText;
        Image muteIcon, themeIcon;
        Text reviveText, adBackText, hSliderText;
        Image warnBg;
        CanvasGroup warnGroup;
        RectTransform sliderArea, thumb, dropRect, overPanel, adWin, howto, hSlider;
        readonly List<RectTransform> chips = new();
        readonly Image[] lifeDots = new Image[Cfg.Lives];
        Button adBackBtn;
        Action adDone;
        Coroutine adRoutine;
        Text toast;
        Canvas canvas;
        RawImage resultBg;               // 結果画面の背景に薄く出す、最高到達時の塔の画像
        AspectRatioFitter resultBgFit;
        readonly List<GameObject> hiddenNow = new();
        float msgT, warnT, dropT, toastT;
        int livesNow;
        bool sliderLeft;
        RectTransform frame;
        AspectRatioFitter fit;
        CanvasScaler scaler;
        bool lastWide;

        /// <summary>タップをゲームの操作として扱わない画面(結果、広告、操作説明)が出ているか。</summary>
        public bool OverlayVisible =>
            (overPanel != null && overPanel.gameObject.activeSelf) ||
            (adWin != null && adWin.gameObject.activeSelf) ||
            (howto != null && howto.gameObject.activeSelf);

        void OnDestroy() { Theme.Changed -= OnThemeChanged; }

        // ============ 組み立て ============
        public void Build()
        {
            font = Resources.Load<Font>("Fonts/MPLUSRounded1c-Bold");
            if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic", "Noto Sans CJK JP", "Arial" }, 40);

            if (FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));   // GraphicRaycasterがないと、ボタンが押せない
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;   // 部品をピクセルの区切りに合わせる(文字のにじみ対策)
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

            BuildBar(root);
            BuildTop(root);
            BuildSlider(root);
            BuildWarning(root);
            msg = Label(Rect("Msg", root, new Vector2(0, 0.52f), new Vector2(1, 0.62f)), "", 64, TextAnchor.MiddleCenter, Theme.Miss);
            // この文字は、時間とともに透明になって消える。テーマ切り替えの塗り直し(不透明に戻る)の対象から外す
            Destroy(msg.GetComponent<Themed>());
            var msgColor = Theme.Miss; msgColor.a = 0f;
            msg.color = msgColor;

            BuildOver(root);
            BuildAdWindow(root);
            BuildHowto(root);

            // 画面の下に、一言を出す場所(「画像を保存しました」など)。結果画面より手前。時間で消える
            toast = Label(Rect("Toast", root, new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.1f)), "", 38, TextAnchor.MiddleCenter, Theme.Text);
            Destroy(toast.GetComponent<Themed>());
            var toastColor = Theme.Text; toastColor.a = 0f;
            toast.color = toastColor;

            SetHand(PlayerPrefs.GetString("bff-hand", "R") == "L");
            Theme.Changed += OnThemeChanged;
            OnThemeChanged();
        }

        // 下の操作バー: ベージュの帯に、白い大きなボタン(下に厚み)
        void BuildBar(RectTransform root)
        {
            var bar = Rect("Bar", root, new Vector2(0, 0), new Vector2(1, BarFrac));
            Img(bar, () => Theme.Panel);
            dropRect = RaisedButton(bar, "Drop", new Vector2(0.015f, 0.0625f), new Vector2(0.985f, 0.875f), Raised,
                () => Theme.Btn, () => Theme.BtnShadow, 3.6f, "", 90, () => Theme.BtnText, null);
            var arrow = Rect("arrow", dropRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            arrow.sizeDelta = new Vector2(80, 80);
            IconImg(arrow, "down", () => Theme.BtnText);
        }

        // 上の表示: 高さ・個数、ミス残りの丸、小さなボタン
        void BuildTop(RectTransform root)
        {
            hud = Label(Rect("Hud", root, new Vector2(0.03f, 0.94f), new Vector2(0.41f, 0.995f)), "", 38, TextAnchor.UpperLeft, () => Theme.Text);

            // ミス残り: 「残り ●●●」。左から濃い丸が残りの数。減ると、右の丸から薄くなる
            var livesBox = Rect("Lives", root, new Vector2(0.62f, 0.955f), new Vector2(0.97f, 0.99f));
            for (int i = 0; i < lifeDots.Length; i++)
            {
                int idx = i;
                var dot = Rect("life" + i, livesBox, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                dot.pivot = new Vector2(1f, 0.5f);
                dot.sizeDelta = new Vector2(32, 32);
                dot.anchoredPosition = new Vector2(-i * 44f, 0f);
                lifeDots[i] = RoundImg(dot, () => idx >= lifeDots.Length - livesNow ? Theme.Text : new Color(0f, 0f, 0f, 0.15f), 1.5f);
            }
            var livesLabel = Rect("LivesLabel", livesBox, new Vector2(0f, 0f), new Vector2(1f, 1f));
            livesLabel.offsetMax = new Vector2(-(lifeDots.Length * 44f + 6f), 0f);
            Label(livesLabel, "残り", 38, TextAnchor.MiddleRight, () => Theme.Text);

            // 小さなボタン(上の中央に1列): 回転の左右 / 音 / ダーク / ヘルプ。Webと同じ、薄い灰色の小さな角丸
            handText = TextChip("Hand", 0.415f, 0.523f, () => SetHand(!sliderLeft));
            muteIcon = IconChip("Mute", 0.533f, 0.591f, () => OnMute?.Invoke());
            themeIcon = IconChip("Theme", 0.601f, 0.659f, () => Theme.Set(!Theme.Dark));
            TextChip("Help", 0.669f, 0.727f, () => ShowHowto(true)).text = "?";
            SetMuteLabel(Sfx.IsMuted);
        }

        RectTransform ChipBase(string name, float x0, float x1, Action onClick)
        {
            var rt = Rect(name, frame, new Vector2(x0, 0.965f), new Vector2(x1, 0.992f));
            RoundImg(rt, () => Theme.Chip, 5f);
            rt.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick());
            chips.Add(rt);
            return rt;
        }

        Text TextChip(string name, float x0, float x1, Action onClick)
        {
            var rt = ChipBase(name, x0, x1, onClick);
            return Label(Rect("t", rt, Vector2.zero, Vector2.one), "", 30, TextAnchor.MiddleCenter, () => Theme.Text);
        }

        Image IconChip(string name, float x0, float x1, Action onClick)
        {
            var rt = ChipBase(name, x0, x1, onClick);
            var icon = Rect("icon", rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            icon.sizeDelta = new Vector2(40, 40);
            return IconImg(icon, "speaker", () => Theme.Text);
        }

        static Image IconImg(RectTransform rt, string kind, Func<Color> color)
        {
            var i = Img(rt, color);
            i.sprite = Shapes.Icon(kind);
            i.preserveAspect = true;
            i.raycastTarget = false;
            return i;
        }

        // 回転スライダー(縦): 細い溝+中央の目印+濃い色の小さなつまみ(白い文字)。上=-180度、中央=0度、下=+180度
        void BuildSlider(RectTransform root)
        {
            sliderArea = Rect("Slider", root, new Vector2(0.88f, SliderBottom), new Vector2(0.988f, SliderTop));
            var track = Rect("Track", sliderArea, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f));
            track.offsetMin = new Vector2(-11, 0); track.offsetMax = new Vector2(11, 0);   // Webの10px
            RoundImg(track, () => Theme.Track, 4.4f);
            thumb = Rect("Thumb", sliderArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            thumb.sizeDelta = new Vector2(112, 76);   // Webの 52x36
            var shadow = Rect("shadow", thumb, Vector2.zero, Vector2.one);
            shadow.offsetMin = new Vector2(0, -5); shadow.offsetMax = new Vector2(0, -5);
            RoundImg(shadow, new Color(0, 0, 0, 0.3f), 2.6f);
            RoundImg(Rect("face", thumb, Vector2.zero, Vector2.one), () => Theme.Strong, 2.6f);
            thumbText = Label(Rect("t", thumb, Vector2.zero, Vector2.one), "0°", 38, TextAnchor.MiddleCenter, () => Theme.StrongText);
        }

        // 注意の帯: 黄黒の縞模様に、黒い札(黄色の文字)。点滅する
        void BuildWarning(RectTransform root)
        {
            var warnRect = Rect("Warn", root, new Vector2(0, 0.64f), new Vector2(1, 0.74f));
            warnGroup = warnRect.gameObject.AddComponent<CanvasGroup>();
            warnBg = warnRect.gameObject.AddComponent<Image>();
            warnBg.sprite = Shapes.Stripes();
            warnBg.type = Image.Type.Tiled;
            warnBg.raycastTarget = false;
            var label = Rect("label", warnRect, new Vector2(0.12f, 0.2f), new Vector2(0.88f, 0.8f));
            Img(label, Theme.Hex("222222"));
            Label(Rect("t", label, Vector2.zero, Vector2.one), "注意!  何かが来る", 66, TextAnchor.MiddleCenter, Theme.Warn);
            warnRect.gameObject.SetActive(false);
        }

        // 結果画面: 全面を薄く覆い、中央に文字とボタンを縦に並べる(背景に、最高到達時の塔が透ける)
        void BuildOver(RectTransform root)
        {
            overPanel = Rect("Over", root, Vector2.zero, Vector2.one);
            Img(overPanel, () => Theme.Overlay);
            // 広告ボタン(黄色)より上の範囲に、縦横比を保ったまま収めて、中央に敷く
            var bgBox = Rect("ResultBgBox", overPanel, new Vector2(0, 0.44f), new Vector2(1, 1));
            var bgRect = Rect("ResultBg", bgBox, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            resultBgFit = bgRect.gameObject.AddComponent<AspectRatioFitter>();
            resultBgFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            resultBgFit.aspectRatio = 0.64f;
            resultBg = bgRect.gameObject.AddComponent<RawImage>();
            resultBg.color = new Color(1f, 1f, 1f, 0.4f);
            resultBg.raycastTarget = false;

            Label(Rect("title", overPanel, new Vector2(0, 0.79f), new Vector2(1, 0.87f)), "ブロック自由落下", 81, TextAnchor.MiddleCenter, () => Theme.Text);
            Label(Rect("cap", overPanel, new Vector2(0, 0.735f), new Vector2(1, 0.785f)), "最高到達点", 43, TextAnchor.MiddleCenter, () => Theme.Text);
            overScore = Label(Rect("score", overPanel, new Vector2(0, 0.62f), new Vector2(1, 0.735f)), "", 130, TextAnchor.MiddleCenter, () => Theme.Text);
            overCount = Label(Rect("count", overPanel, new Vector2(0, 0.575f), new Vector2(1, 0.62f)), "", 43, TextAnchor.MiddleCenter, () => Theme.Text);
            overBest = Label(Rect("best", overPanel, new Vector2(0, 0.53f), new Vector2(1, 0.575f)), "", 43, TextAnchor.MiddleCenter, () => Theme.Text);
            overComment = Label(Rect("comment", overPanel, new Vector2(0.05f, 0.465f), new Vector2(0.95f, 0.53f)), "", 43, TextAnchor.MiddleCenter, () => Theme.Text);

            // 直前に戻る(黄色)
            var revive = RaisedButton(overPanel, "Revive", new Vector2(0.14f, 0.37f), new Vector2(0.86f, 0.435f), Raised,
                () => Theme.Warn, () => Theme.Hex("aa8800"), 3.6f, "", 40, () => Theme.Hex("222222"), () => OnRevive?.Invoke());
            reviveText = revive.GetComponentInChildren<Text>();
            // もう一回(濃い色)
            RaisedButton(overPanel, "Retry", new Vector2(0.14f, 0.29f), new Vector2(0.86f, 0.355f), Raised,
                () => Theme.Strong, () => new Color(0, 0, 0, 0.3f), 3.6f, "もう一回", 54, () => Theme.StrongText, () => OnRetry?.Invoke());
            // シェア / みんなの記録(白いボタン)
            RaisedButton(overPanel, "Share", new Vector2(0.14f, 0.21f), new Vector2(0.86f, 0.275f), 5f,
                () => Theme.Btn, () => Theme.BtnShadow, 3.6f, "シェアする", 50, () => Theme.BtnText, () => OnShare?.Invoke());
            RaisedButton(overPanel, "Search", new Vector2(0.14f, 0.13f), new Vector2(0.86f, 0.195f), 5f,
                () => Theme.Btn, () => Theme.BtnShadow, 3.6f, "みんなの記録を見る", 50, () => Theme.BtnText, () => OnSearch?.Invoke());
            overPanel.gameObject.SetActive(false);
        }

        // 広告ウィンドウ: 薄暗い背景に箱。枠の中に広告を出し、数秒たつと「戻る」が押せる
        void BuildAdWindow(RectTransform root)
        {
            adWin = Rect("AdWindow", root, Vector2.zero, Vector2.one);
            Img(adWin, new Color(0f, 0f, 0f, 0.75f));
            var box = Rect("Box", adWin, new Vector2(0.05f, 0.31f), new Vector2(0.95f, 0.69f));
            RoundImg(box, () => Theme.Box, 2.5f);
            Label(Rect("cap", box, new Vector2(0.04f, 0.9f), new Vector2(0.6f, 0.98f)), "広告", 32, TextAnchor.MiddleLeft, () => Theme.Text);
            AdArea = Rect("AdArea", box, new Vector2(0.035f, 0.24f), new Vector2(0.965f, 0.89f));
            RoundImg(AdArea, () => Theme.Chip, 4f);
            Label(Rect("t", AdArea, Vector2.zero, Vector2.one), "(ここに広告が入ります・ダミー)", 34, TextAnchor.MiddleCenter, () => Theme.Text);
            var back = RaisedButton(box, "Back", new Vector2(0.035f, 0.04f), new Vector2(0.965f, 0.19f), 0f,
                () => Theme.Strong, () => Color.clear, 3.6f, "", 48, () => Theme.StrongText, OnAdBack);
            adBackBtn = back.GetComponent<Button>();
            adBackText = back.GetComponentInChildren<Text>();
            adWin.gameObject.SetActive(false);
        }

        // 操作説明: 黒い半透明の背景に、白い文字(強調は黄色)。どこをタップしても閉じる
        void BuildHowto(RectTransform root)
        {
            howto = Rect("Howto", root, Vector2.zero, Vector2.one);
            Img(howto, new Color(0f, 0f, 0f, 0.72f));
            howto.gameObject.AddComponent<Button>().onClick.AddListener(() => ShowHowto(false));
            Label(Rect("move", howto, new Vector2(0.06f, 0.6f), new Vector2(0.94f, 0.74f)),
                "<color=#ffcc00>画面を押している間</color>、\nピースが指についてくる", 52, TextAnchor.MiddleCenter, Color.white);
            hSlider = Rect("slider", howto, new Vector2(0.05f, 0.36f), new Vector2(0.8f, 0.5f));
            hSliderText = Label(hSlider, "", 46, TextAnchor.MiddleRight, Color.white);
            Label(Rect("drop", howto, new Vector2(0.06f, 0.14f), new Vector2(0.94f, 0.27f)),
                "離したい位置で\n下の「<color=#ffcc00>↓</color>」を押して落とす", 52, TextAnchor.MiddleCenter, Color.white);
            Label(Rect("start", howto, new Vector2(0, 0.5f), new Vector2(1, 0.56f)), "タップで始める", 38, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.8f));
            howto.gameObject.SetActive(false);
        }

        // ============ 外から呼ぶもの ============
        public void SetHud(float score, float peakScore, int count, int peakCount, int livesLeft)
        {
            // 主役(高さ、個数)は大きく、「最高」「最多」は小さく。桁が増えても、枠に収まる(文字の大きさは固定)
            hud.text = $"高さ {score:0.0}m <size=28>(最高 {peakScore:0.0}m)</size>\n個数 {count} <size=28>(最多 {peakCount})</size>";
            livesNow = Mathf.Clamp(livesLeft, 0, lifeDots.Length);
            foreach (var d in lifeDots) d.GetComponent<Themed>().Apply();
        }

        public void SetRot(int deg)
        {
            float f = (deg + 180f) / 360f;
            thumb.anchorMin = thumb.anchorMax = new Vector2(0.5f, 1f - f);
            thumb.anchoredPosition = Vector2.zero;
            thumbText.text = deg + "°";
        }

        public void SetMuteLabel(bool muted) { if (muteIcon != null) muteIcon.sprite = Shapes.Icon(muted ? "mute" : "speaker"); }

        /// <summary>「落とす」ボタンを、押した感じに一瞬へこませる。</summary>
        public void PunchDrop() { dropT = 0.14f; }

        public void Flash(string text) { msg.text = text; msgT = 0.9f; }

        public void ShowWarning(float seconds)
        {
            warnT = seconds;
            warnBg.rectTransform.gameObject.SetActive(true);
        }

        /// <summary>画面の部品を、まるごと隠す/出す(部品なしの絵を撮るため)。</summary>
        public void SetCanvasVisible(bool visible) => canvas.enabled = visible;

        public void ShowGameOver(float peakScore, int peakCount, float best, string comment, bool canRevive, Texture2D preview)
        {
            resultBg.texture = preview;
            resultBg.gameObject.SetActive(preview != null);
            if (preview != null) resultBgFit.aspectRatio = preview.width / (float)preview.height;
            overScore.text = $"{peakScore:0.0}m";
            overCount.text = $"乗せた個数(最多) {peakCount}個";
            overBest.text = $"ベスト {best:0.0}m";
            overComment.text = comment;
            reviveText.text = "直前に戻る(1回だけ)";
            reviveText.transform.parent.gameObject.SetActive(canRevive);
            var reviveShadow = overPanel.Find("ReviveShadow");   // 黄色いボタンの、下の厚み
            if (reviveShadow != null) reviveShadow.gameObject.SetActive(canRevive);
            overPanel.gameObject.SetActive(true);
        }

        public void HideGameOver() => overPanel.gameObject.SetActive(false);

        /// <summary>画像にするとき、結果画面のボタンを一時的に隠す(戻すときは、隠す前の状態に戻す)。</summary>
        public void HideResultButtons(bool hide)
        {
            if (hide)
            {
                hiddenNow.Clear();
                foreach (Transform c in overPanel)
                {
                    string n = c.name;
                    bool button = n.StartsWith("Revive") || n.StartsWith("Retry") || n.StartsWith("Share") || n.StartsWith("Search");
                    if (button && c.gameObject.activeSelf) { hiddenNow.Add(c.gameObject); c.gameObject.SetActive(false); }
                }
            }
            else
            {
                foreach (var g in hiddenNow) if (g != null) g.SetActive(true);
                hiddenNow.Clear();
            }
        }

        /// <summary>縦長の枠の、画面上のピクセルの範囲。</summary>
        public Rect FrameRect()
        {
            var c = new Vector3[4];
            frame.GetWorldCorners(c);
            return new Rect(c[0].x, c[0].y, c[2].x - c[0].x, c[2].y - c[0].y);
        }

        /// <summary>画面の下に、一言を出す。数秒で消える。</summary>
        public void Toast(string text)
        {
            toast.text = text;
            toast.color = Theme.Text;
            toastT = 3.5f;
        }

        public void ShowHowto(bool show)
        {
            howto.gameObject.SetActive(show);
            if (!show) PlayerPrefs.SetInt("bff-howto", 1);
        }

        public bool HowtoSeen => PlayerPrefs.GetInt("bff-howto", 0) == 1;

        /// <summary>広告ウィンドウを開く。数秒たつと、doneLabel のボタンが押せるようになり、押すと onDone が呼ばれる。</summary>
        public void OpenAdWindow(string doneLabel, Action onDone)
        {
            if (adRoutine != null) StopCoroutine(adRoutine);
            adRoutine = StartCoroutine(AdRoutine(doneLabel, onDone));
        }

        IEnumerator AdRoutine(string doneLabel, Action onDone)
        {
            adDone = null;
            adWin.gameObject.SetActive(true);
            adBackBtn.interactable = false;
            for (int s = Cfg.AdWaitSeconds; s > 0; s--)
            {
                adBackText.text = $"あと{s}秒";
                yield return new WaitForSecondsRealtime(1f);
            }
            adBackText.text = doneLabel;
            adBackBtn.interactable = true;
            adDone = onDone;
        }

        void OnAdBack()
        {
            if (adDone == null) return;
            var d = adDone;
            adDone = null;
            adWin.gameObject.SetActive(false);
            d();
        }

        public bool InSlider(Vector2 screen) => RectTransformUtility.RectangleContainsScreenPoint(sliderArea, screen);
        public bool InDrop(Vector2 screen) => RectTransformUtility.RectangleContainsScreenPoint(dropRect, screen);

        /// <summary>画面上部の小さなボタンの上か(ピースを動かす操作として扱わないため)。</summary>
        public bool InButtons(Vector2 screen)
        {
            foreach (var c in chips) if (RectTransformUtility.RectangleContainsScreenPoint(c, screen)) return true;
            return false;
        }

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
            if (dropT > 0f)
            {
                dropT = Mathf.Max(0f, dropT - Time.deltaTime);
                float s = 1f - 0.04f * (dropT / 0.14f);
                dropRect.localScale = new Vector3(s, s, 1f);
            }
            if (toastT > 0f)
            {
                toastT -= Time.deltaTime;
                var tc = toast.color; tc.a = Mathf.Clamp01(toastT / 0.5f); toast.color = tc;
            }
            if (msgT > 0f)
            {
                msgT -= Time.deltaTime;
                var c = msg.color; c.a = Mathf.Clamp01(msgT / 0.3f); msg.color = c;
            }
            if (warnT > 0f)
            {
                warnT -= Time.deltaTime;
                warnGroup.alpha = 0.35f + 0.65f * Mathf.PingPong(Time.time * 6f, 1f);   // Webと同じ点滅
                if (warnT <= 0f) warnBg.rectTransform.gameObject.SetActive(false);
            }
        }

        // ============ スライダーの左右(利き手)、テーマ ============
        void SetHand(bool left)
        {
            sliderLeft = left;
            sliderArea.anchorMin = new Vector2(left ? 0.012f : 0.88f, SliderBottom);
            sliderArea.anchorMax = new Vector2(left ? 0.12f : 0.988f, SliderTop);
            sliderArea.offsetMin = sliderArea.offsetMax = Vector2.zero;
            handText.text = "回転:" + (left ? "左" : "右");
            PlayerPrefs.SetString("bff-hand", left ? "L" : "R");

            // 操作説明の、スライダーを指す文も向きを合わせる
            if (hSlider != null)
            {
                hSlider.anchorMin = new Vector2(left ? 0.2f : 0.05f, 0.36f);
                hSlider.anchorMax = new Vector2(left ? 0.95f : 0.8f, 0.5f);
                hSliderText.alignment = left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
                hSliderText.text = left
                    ? "← <color=#ffcc00>スライダー</color>で回転\n(上下に動かす)"
                    : "<color=#ffcc00>スライダー</color>で回転 →\n(上下に動かす)";
            }
        }

        void OnThemeChanged()
        {
            if (themeIcon != null) themeIcon.sprite = Shapes.Icon(Theme.Dark ? "sun" : "moon");   // 押すと変わる先を表す(Webと同じ)
        }

        // ============ 部品を作る道具 ============
        static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>
        /// Webの「白いボタン」: 角丸の面と、その下に厚み(濃い色)。onClick が null なら、押下の判定は外(Game)で行う。
        /// 厚み(thick)が0なら、影なし。
        /// </summary>
        RectTransform RaisedButton(RectTransform parent, string name, Vector2 a, Vector2 b, float thick,
            Func<Color> face, Func<Color> under, float radius, string label, int size, Func<Color> textColor, Action onClick)
        {
            if (thick > 0f)
            {
                var shadow = Rect(name + "Shadow", parent, a, b);
                shadow.offsetMin = new Vector2(0, -thick); shadow.offsetMax = new Vector2(0, -thick);
                RoundImg(shadow, under, radius);
            }
            var rt = Rect(name, parent, a, b);
            RoundImg(rt, face, radius);
            if (onClick != null) rt.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick());
            Label(Rect("t", rt, Vector2.zero, Vector2.one), label, size, TextAnchor.MiddleCenter, textColor);
            return rt;
        }

        static T Bind<T>(T g, Func<Color> color) where T : Graphic
        {
            var t = g.gameObject.AddComponent<Themed>();
            t.G = g; t.Get = color;
            t.Apply();
            return g;
        }

        static Image Img(RectTransform rt, Func<Color> color) => Bind(rt.gameObject.AddComponent<Image>(), color);
        static Image Img(RectTransform rt, Color color) => Img(rt, () => color);

        /// <summary>角丸の四角。scale が小さいほど角が大きく丸くなる。</summary>
        static Image RoundImg(RectTransform rt, Func<Color> color, float scale)
        {
            var i = Img(rt, color);
            i.sprite = Shapes.RoundRect();
            i.type = Image.Type.Sliced;
            i.pixelsPerUnitMultiplier = scale;
            return i;
        }

        static Image RoundImg(RectTransform rt, Color color, float scale) => RoundImg(rt, () => color, scale);

        Text Label(RectTransform rt, string s, int size, TextAnchor anchor, Func<Color> color)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.text = s; t.fontSize = size; t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            Bind(t, color);
            return t;
        }

        Text Label(RectTransform rt, string s, int size, TextAnchor anchor, Color color) => Label(rt, s, size, anchor, () => color);
    }
}
