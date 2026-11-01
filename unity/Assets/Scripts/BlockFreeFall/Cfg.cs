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
    }

    /// <summary>色。ダークモードは、あとで足す。</summary>
    public static class Theme
    {
        public static readonly Color Bg = Hex("f4efe6");
        public static readonly Color Text = Hex("333333");
        public static readonly Color Panel = Hex("ddd5c5");
        public static readonly Color Strong = Hex("333333");
        public static readonly Color StrongText = Color.white;
        public static readonly Color Bowl = Hex("555555");
        public static readonly Color Line = Hex("222222");
        public static readonly Color Abyss = Hex("e8dfd0");
        public static readonly Color Shadow = Hex("aa9999");
        public static readonly Color Boss = Hex("2a2d38");
        public static readonly Color Warn = Hex("ffcc00");
        public static readonly Color Miss = Hex("dd3333");

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
