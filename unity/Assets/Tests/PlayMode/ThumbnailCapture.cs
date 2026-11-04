using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BlockFreeFall.Tests
{
    /// <summary>
    /// サムネイル用の GIF を撮る。普段のテストでは動かさず、名前を指定したときだけ動く(Explicit)。
    ///   -runTests -testPlatform PlayMode -testFilter BlockFreeFall.Tests.ThumbnailCapture
    /// ブロックを決まった位置と向きで自動で落とし、お椀に積み上がる様子を正方形で撮って、上にタイトルと同じロゴを重ねる。
    /// 書き出し先: unity/Captures/thumbnail.gif。物理はゲームのまま動かすので、毎回少しずつ違う絵になることがある。
    /// </summary>
    public class ThumbnailCapture
    {
        const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        const string Output = "Captures/thumbnail.gif";   // unity フォルダからの場所

        const int Size = 480;              // GIF の一辺(ドット)
        const int CaptureFps = 30;         // 1コマの間隔(1/30 秒)
        const float Speed = 2f;            // ゲームの時間の進み方。1コマでゲームは 1/15 秒進む
        const int GifDelay = 5;            // GIF の1コマの長さ(1/100 秒)。ゲームの約1.3倍の速さで見える
        const float ViewHalf = 6.8f;       // 撮る範囲(正方形)の半分の大きさ(ユニット)。お椀の幅が収まる
        const float ViewBottom = -2f;      // 撮る範囲の下端(ユニット)
        const int Seed = 7;                // ピースの種類の並びを、毎回同じにする
        const int HoldFrames = 6;          // ピースが出てから落とすまで
        const int EndFrames = 45;          // 最後のピースを落としてから、撮り終えるまで
        const int MaxFrames = 260;

        // 落とす位置(x)と向き(度)。真ん中に積み、だんだん左右にはみ出させる
        static readonly (float x, int rot)[] Drops =
        {
            (0f, 0), (-1.5f, 90), (1.5f, 0), (0f, 90), (-0.5f, 0), (1f, -90),
        };

        [UnityTest, Explicit("サムネイルの GIF を撮るときだけ、名前を指定して実行する")]
        public IEnumerator サムネイルGIFを撮る()
        {
            yield return null;
            yield return null;
            var game = Object.FindFirstObjectByType<Game>();
            Assert.IsNotNull(game, "Gameが自動で始まっていない");
            Object.FindFirstObjectByType<Ui>().HideTitle();
            var t = typeof(Game);
            var held = t.GetField("held", Any);
            var guides = new[] { (GameObject)t.GetField("guideL", Any).GetValue(game), (GameObject)t.GetField("guideR", Any).GetValue(game) };

            // ピースの並びを決めて、最初からやり直す
            Random.InitState(Seed);
            t.GetMethod("ResetGame", Any).Invoke(game, null);
            Time.captureFramerate = CaptureFps;
            Time.timeScale = Speed;

            // 撮影用のカメラ: ゲームのカメラと同じ設定で、正方形の範囲だけを映す
            var main = Camera.main;
            var target = new RenderTexture(Size, Size, 24) { antiAliasing = 8 };
            var cam = new GameObject("Gif Camera").AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.orthographicSize = ViewHalf;
            cam.transform.position = new Vector3(0f, ViewBottom + ViewHalf, -10f);
            cam.targetTexture = target;
            cam.depth = main.depth - 1;
            cam.enabled = false;   // コマごとに Render で描かせる(エディタを開かずに動かすと、描き終わりを待てないため)
            var logoCanvas = BuildLogoCanvas(cam);

            // 撮影用のカメラでは背景の塗りが効かないので、ゲームと同じ色の板を一番奥に敷く
            var backing = new GameObject("Gif Backing").AddComponent<SpriteRenderer>();
            backing.sprite = Shapes.Box(1f, 1f, false);
            backing.color = Theme.Bg;
            backing.sortingOrder = -100;
            backing.transform.position = new Vector3(0f, ViewBottom + ViewHalf, 5f);
            backing.transform.localScale = new Vector3(ViewHalf * 4f, ViewHalf * 4f, 1f);

            var frames = new List<Color32[]>();
            var read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            int next = 0, heldFrames = 0, lastDropAt = -1;
            for (int f = 0; f < MaxFrames; f++)
            {
                yield return null;
                // 広告ブロックと落とす位置の点線は、撮らない
                AdDemo.Clear();
                foreach (var g in guides) g.SetActive(false);

                if (next < Drops.Length && held.GetValue(game) != null && ++heldFrames >= HoldFrames)
                {
                    t.GetMethod("SetRot", Any).Invoke(game, new object[] { Drops[next].rot, true });
                    t.GetMethod("SetHeldX", Any).Invoke(game, new object[] { Drops[next].x });
                    t.GetMethod("Drop", Any).Invoke(game, null);
                    next++; heldFrames = 0; lastDropAt = f;
                }

                if (lastDropAt < 0) continue;   // 最初のピースを落とすまでは撮らない
                Canvas.ForceUpdateCanvases();
                cam.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = previous;
                frames.Add(read.GetPixels32());
                if (next >= Drops.Length && f - lastDropAt >= EndFrames) break;
            }
            Time.captureFramerate = 0;
            Time.timeScale = 1f;

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", Output));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            GifWriter.Write(path, frames, Size, Size, 1, GifDelay);
            Debug.Log($"サムネイルの GIF を書き出しました: {path}({new FileInfo(path).Length / 1024} KB、{frames.Count} コマ、落とした数 {next})");

            cam.targetTexture = null;
            Object.Destroy(logoCanvas);
            Object.Destroy(backing.gameObject);
            Object.Destroy(cam.gameObject);
            target.Release();
            Assert.IsTrue(File.Exists(path));
        }

        /// <summary>撮影用のカメラだけに映る画面に、タイトルと同じロゴを置く(上の方の真ん中)。</summary>
        static GameObject BuildLogoCanvas(Camera cam)
        {
            var go = new GameObject("Gif Logo", typeof(Canvas), typeof(CanvasScaler));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1080);   // タイトル画面と同じ大きさの単位
            scaler.matchWidthOrHeight = 0.5f;

            var logo = new GameObject("Logo", typeof(RectTransform)).GetComponent<RectTransform>();
            logo.SetParent(go.transform, false);
            logo.anchorMin = logo.anchorMax = new Vector2(0.5f, 0.78f);
            logo.sizeDelta = Vector2.zero;
            logo.localScale = Vector3.one * 0.6f;
            var font = Resources.Load<Font>("Fonts/MPLUSRounded1c-Bold");
            Ui.BuildLogo(logo, font);
            return go;
        }
    }
}
