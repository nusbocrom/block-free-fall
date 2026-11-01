using System;

namespace BlockFreeFall
{
    /// <summary>
    /// ゲーム間の広告の受け口。InterstitialProvider(done) は、広告が終わったら done() を呼ぶ。
    /// いまは何もつないでいないので、広告は出ない。
    /// </summary>
    public static class Ads
    {
        public static Action<Action> InterstitialProvider;

        static int games;

        /// <summary>「もう一回」の前。数回に1回だけ、ゲーム間の広告を挟む。</summary>
        public static void BeforeRetry(Action next)
        {
            games++;
            if (InterstitialProvider == null || games % Cfg.InterstitialEvery != 0) { next(); return; }
            Sfx.AdPlaying = true;
            try { InterstitialProvider(() => { Sfx.AdPlaying = false; next(); }); }
            catch (Exception) { Sfx.AdPlaying = false; next(); }
        }
    }
}
