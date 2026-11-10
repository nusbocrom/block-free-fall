using System.Reflection;
using UnityEngine;
using unityroom.Api;

namespace BlockFreeFall
{
    /// <summary>
    /// unityroom のスコアランキングに、最高到達点(m)を送る。unityroom 公式のライブラリ(UnityroomApiClient)を使う。
    /// 送るのは unityroom 用のビルド(UNITYROOM)だけ。エディタでは、送ったつもりのログが出るだけ(ライブラリの仕様)。
    /// HMAC 認証用キーは Git に入れない: unity/unityroom-hmac.txt に置き、ビルドの間だけ Resources にコピーされる(BuildScript)。
    /// </summary>
    public static class Ranking
    {
        public const int BoardNo = 1;      // unityroom のスコアボード一覧の「ボードNo」
        public const string KeyResource = "UnityroomHmac";
        static bool ready;

        public static void SendHeight(float meters)
        {
#if UNITYROOM || UNITY_EDITOR
            if (!Prepare()) return;
            float score = Mathf.Round(meters * 10f) / 10f;   // 画面と同じ 0.1m 単位にそろえる
            UnityroomApiClient.Instance.SendScore(BoardNo, score, ScoreboardWriteMode.HighScoreDesc);
#endif
        }

        /// <summary>
        /// 送る係を作る。シーンは全部コードで作っているので、ライブラリのプレハブは置かず、
        /// 止めたまま部品を付けてキーを入れてから動かす(動き出すとき、キーが空だとエラーが出るため)。
        /// </summary>
        static bool Prepare()
        {
            if (ready) return true;
            var asset = Resources.Load<TextAsset>(KeyResource);
            string key = asset != null ? asset.text.Trim() : "";
#if UNITY_EDITOR
            if (key == "") key = "editor";   // エディタでは実際には送らないので、仮のキーでよい
#endif
            if (key == "") { Debug.LogWarning("[Ranking] HMAC 認証用キーがビルドに入っていないので、スコアを送らない"); return false; }

            var go = new GameObject("UnityroomApiClient");
            go.SetActive(false);
            var client = go.AddComponent<UnityroomApiClient>();
            typeof(UnityroomApiClient).GetField("HmacKey", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(client, key);
            go.SetActive(true);
            ready = true;
            return true;
        }
    }
}
