using System;

namespace BlockFreeFall
{
    /// <summary>
    /// 広告の受け口。ゲーム側は Ads.* だけを呼ぶ。広告会社のSDKに合わせて、Provider を差し替える。
    ///   RewardedProvider(onReward, onNoAd)   見終わったら onReward()、出せなければ onNoAd() を呼ぶ
    ///   InterstitialProvider(done)           ゲーム間の広告。終わったら done() を呼ぶ
    /// いまは、Game が広告ウィンドウ(ダミー)をつないでいる。本番では、AdMobなどのSDKに差し替える。
    /// 広告の再生中は、効果音を止める。
    /// </summary>
    public static class Ads
    {
        public static Action<Action, Action> RewardedProvider;
        public static Action<Action> InterstitialProvider;

        static int games;

        public static void Rewarded(Action onReward, Action onNoAd)
        {
            if (RewardedProvider == null) { onNoAd?.Invoke(); return; }
            Sfx.AdPlaying = true;
            Action Wrap(Action f) => () => { Sfx.AdPlaying = false; f?.Invoke(); };
            try { RewardedProvider(Wrap(onReward), Wrap(onNoAd)); }
            catch (Exception) { Sfx.AdPlaying = false; onNoAd?.Invoke(); }
        }

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
