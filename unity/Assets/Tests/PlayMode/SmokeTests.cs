using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlockFreeFall.Tests
{
    /// <summary>再生して、エラーなく遊べるかを自動で確かめる。</summary>
    public class SmokeTests
    {
        const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        [UnityTest]
        public IEnumerator ピースを落として積める()
        {
            var errors = new List<string>();
            Application.LogCallback cb = (msg, stack, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error) errors.Add(type + ": " + msg + "\n" + stack);
            };
            Application.logMessageReceived += cb;

            yield return null;
            yield return null;
            var game = Object.FindFirstObjectByType<Game>();
            Assert.IsNotNull(game, "Gameが自動で始まっていない");

            var drop = typeof(Game).GetMethod("Drop", Any);
            var held = typeof(Game).GetField("held", Any);
            for (int i = 0; i < 12; i++)
            {
                float t = 0f;
                while (held.GetValue(game) == null && t < 10f) { t += Time.deltaTime; yield return null; }
                drop.Invoke(game, null);
                yield return new WaitForSeconds(1.2f);
            }

            var peak = (float)typeof(Game).GetField("peakScore", Any).GetValue(game);
            var pieces = (IList)typeof(Game).GetField("pieces", Any).GetValue(game);
            Debug.Log($"[smoke] 最高={peak}m 残りピース={pieces.Count}");
            Application.logMessageReceived -= cb;

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.Greater(pieces.Count + peak, 0f);
        }

        [UnityTest]
        public IEnumerator 全部の形が作れる()
        {
            yield return null;
            foreach (var k in PieceFactory.Kinds)
            {
                var p = PieceFactory.Create(k);
                Assert.Greater(p.GetBounds().size.x, 0.5f, "形が空: " + k);
                Object.Destroy(p.gameObject);
            }
            for (int i = 0; i < PieceFactory.BossKinds; i++)
            {
                var p = PieceFactory.CreateBoss(i);
                Assert.Greater(p.GetBounds().size.x, 0.5f, "変な物体が空: " + i);
                Object.Destroy(p.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator 直前に戻れる()
        {
            var errors = new List<string>();
            Application.LogCallback cb = (msg, stack, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error) errors.Add(type + ": " + msg + "\n" + stack);
            };
            Application.logMessageReceived += cb;

            yield return null;
            yield return null;
            var game = Object.FindFirstObjectByType<Game>();
            var t = typeof(Game);
            var drop = t.GetMethod("Drop", Any);
            var held = t.GetField("held", Any);
            for (int i = 0; i < 4; i++)
            {
                float w = 0f;
                while (held.GetValue(game) == null && w < 10f) { w += Time.deltaTime; yield return null; }
                drop.Invoke(game, null);
                yield return new WaitForSeconds(1.2f);
            }

            // 強制的にゲームオーバーにして、結果画面を出し、直前に戻る
            t.GetMethod("GameOver", Any).Invoke(game, null);
            yield return new WaitForSeconds(1.2f);
            t.GetMethod("RestoreSnapshot", Any).Invoke(game, null);
            yield return null;

            Application.logMessageReceived -= cb;
            Assert.IsNotNull(held.GetValue(game), "戻ったあと、持っているピースがない");
            Assert.AreEqual("Play", t.GetField("state", Any).GetValue(game).ToString());
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }
    }
}
