using System;
using UnityEngine;

namespace BlockFreeFall
{
    /// <summary>
    /// 調整用の数値。Web版(Matter.js)の値を、1マス=1ユニットに直して置いてある。
    /// Box2DはMatter.jsと挙動が違うので、遊んで合わせ直すこと。
    /// </summary>
    public static class Cfg
    {
        // ---- 物理 ----
        public const float Gravity = 30f;           // 重力(ユニット/秒^2)。Web版の0.9相当
        public const float MaxFallSpeed = 18f;      // 落下速度の上限。着地の衝撃を抑える
        public const float Friction = 0.6f;         // 摩擦(小さいほど滑る。大きいと引っかかって転がる)
        public const float Bounciness = 0.1f;       // 反発
        public const float DropSpeed = 2f;          // 落とした直後の初速

        // ---- お椀 ----
        public const float BowlHalfWidth = 5f;      // 半幅(5マス)
        public const float BowlRise = 1f;           // 縁の高さ(1マス)
        public const int BowlSegments = 20;
        public const float BowlThickness = 1f;

        // ---- ルール ----
        public const float WorldWidth = 400f / 30f; // 画面に必ず収める横幅(Web版の400px)
        public const int Lives = 3;                 // ミスしていい回数
        public const float SettleSeconds = 0.75f;   // この間ずっと止まっていたら「落ち着いた」
        public const float MaxWaitSeconds = 7f;     // 落ち着かなくても次を出すまでの時間
        public const float SettleSpeed = 0.5f;      // 止まっているとみなす速さ
        public const float SettleAngularSpeed = 69f;// 止まっているとみなす回転(度/秒)
        public const float FallY = -11.7f;          // これより下に落ちたらミス
        public const int RotStep = 10;              // 回転スライダーの刻み(度)

        // ---- 変な物体 ----
        public const float BossHeight = 5f;         // この高さ(m)に達すると来る
        public const float BossInterval = 4f;       // 以降はこの高さごと
        public const float BossWeight = 1.5f;       // 重い部分の密度
        public const float WarnSeconds = 1.83f;     // 注意の帯を出してから物体が出るまで

        // ---- 広告 ----
        public const int AdFirst = 2;               // 最初に隠し絵の広告を出すピース(何個目か)
        public const int AdEvery = 8;               // 2回目以降の、隠し絵の広告を出す間隔(何個落とすごとか)
        public const float AdCompleteRatio = 0.6f;  // 範囲がこの割合まで埋まると完成
        public const float AdCompleteSeconds = 15f; // 完成したあと、全体を流す秒数
        public const int AdWaitSeconds = 5;         // 広告ウィンドウで「戻る」が押せるようになるまで
        public const int InterstitialEvery = 10;    // ゲーム間の広告を挟む頻度(何回に1回)
    }

    /// <summary>
    /// 色。ライト/ダークで変わる。切り替えると Changed が呼ばれ、色を持つ部品は自分で塗り直す。
    /// </summary>
    public static class Theme
    {
        public static bool Dark { get; private set; }
        public static event Action Changed;

        public static void Load() { Dark = PlayerPrefs.GetInt("bff-dark", 0) == 1; }   // 初期値はライト

        public static void Set(bool dark)
        {
            if (Dark == dark) return;
            Dark = dark;
            PlayerPrefs.SetInt("bff-dark", dark ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        static Color P(string light, string dark) => Hex(Dark ? dark : light);

        public static Color Bg => P("f4efe6", "15171c");
        public static Color Text => P("333333", "e8e8ea");
        public static Color Strong => P("333333", "e8e8ea");
        public static Color StrongText => P("ffffff", "15171c");
        // ---- WebのCSS変数と同じ値 ----
        public static Color Panel => P("ddd5c5", "222630");           // 下の操作バー
        public static Color Btn => P("ffffff", "363c4a");             // 白いボタン
        public static Color BtnText => P("333333", "e8e8ea");
        public static Color BtnShadow => P("aa9999", "0c0d10");       // ボタンの下の厚み
        public static Color Card => P("ffffff", "22262f");
        public static Color Overlay => Dark ? new Color(0.082f, 0.09f, 0.11f, 0.94f) : new Color(0.957f, 0.937f, 0.902f, 0.94f);   // 結果画面(Webと同じ濃さ)
        public static Color Box => Dark ? new Color(0.082f, 0.09f, 0.11f, 0.97f) : new Color(0.957f, 0.937f, 0.902f, 0.97f);       // 広告ウィンドウの箱
        public static Color Chip => Dark ? new Color(1f, 1f, 1f, 0.14f) : new Color(0f, 0f, 0f, 0.12f);                          // 小さなボタン(Webの --subtle)
        public static Color Soft => Chip;
        public static Color Track => Dark ? new Color(1f, 1f, 1f, 0.28f) : new Color(0f, 0f, 0f, 0.16f);
        public static Color Tick => Dark ? new Color(1f, 1f, 1f, 0.3f) : new Color(0f, 0f, 0f, 0.2f);
        public static Color Bowl => P("555555", "8b92a3");
        public static Color Line => P("222222", "c4c8d2");
        public static Color Abyss => P("e8dfd0", "0e1014");
        public static Color Boss => P("2a2d38", "3a3f52");
        public static Color Guide => Dark ? new Color(1f, 1f, 1f, 0.4f) : new Color(0.2f, 0.2f, 0.2f, 0.4f);
        public static Color Beam => Dark ? new Color(1f, 1f, 1f, 0.012f) : new Color(0f, 0f, 0f, 0.045f);
        public static Color Warn => Hex("ffcc00");
        public static Color Miss => Hex("dd3333");

        /// <summary>ブロックの縁の色。Webと同じく、ブロックの色によらず、ライトは黒・ダークは明るい灰色。</summary>
        public static Color PieceEdge(Color c) => Line;

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
