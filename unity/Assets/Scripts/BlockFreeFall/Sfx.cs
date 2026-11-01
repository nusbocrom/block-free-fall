using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlockFreeFall
{
    /// <summary>
    /// 効果音。音のファイルを使わず、波形をプログラムで作る(Web版のWeb Audioと同じ考え方)。
    /// 作った音は覚えておき、同じ音を何度も作り直さない。
    /// </summary>
    public static class Sfx
    {
        const int Rate = 44100;
        const float Master = 0.6f;

        public static bool Muted { get; private set; }
        public static bool AdPlaying;                 // 広告の再生中は鳴らさない

        static bool inited;
        static AudioSource[] pool;
        static int next;
        static float lastHit;
        static readonly Dictionary<string, AudioClip> clips = new();

        static void Init()
        {
            if (inited) return;
            inited = true;
            Muted = PlayerPrefs.GetInt("bff-mute", 0) == 1;
            var go = new GameObject("Sfx");
            Object.DontDestroyOnLoad(go);
            pool = new AudioSource[10];
            for (int i = 0; i < pool.Length; i++)
            {
                pool[i] = go.AddComponent<AudioSource>();
                pool[i].playOnAwake = false;
            }
        }

        public static bool ToggleMute()
        {
            Init();
            Muted = !Muted;
            PlayerPrefs.SetInt("bff-mute", Muted ? 1 : 0);
            PlayerPrefs.Save();
            return Muted;
        }

        public static bool IsMuted { get { Init(); return Muted; } }

        static void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            Init();
            if (Muted || AdPlaying || clip == null) return;
            var s = pool[next];
            next = (next + 1) % pool.Length;
            s.clip = clip;
            s.volume = Mathf.Clamp01(volume * Master);
            s.pitch = pitch;
            s.Play();
        }

        // ============ ゲームから呼ぶ音 ============

        /// <summary>ぶつかった強さ(速さ)で、音の大きさと低さが変わる。</summary>
        public static void Hit(float speed)
        {
            if (speed < 1.2f || Time.unscaledTime - lastHit < 0.04f) return;   // 連続しすぎると耳障りなので間引く
            lastHit = Time.unscaledTime;
            float k = Mathf.Clamp01(speed / 9f);
            Play(Get("hit", () =>
            {
                var b = new Buf(0.25f);
                b.Tone(210f, 0.17f, Wave.Triangle, 0.5f, 70f);
                b.Noise(0.1f, 0.3f, 900f);
                return b;
            }), 0.25f + 0.75f * k, Mathf.Lerp(1.2f, 0.85f, k) * UnityEngine.Random.Range(0.95f, 1.05f));
        }

        public static void Drop() => Play(Get("drop", () =>
        {
            var b = new Buf(0.2f);
            b.Tone(720f, 0.13f, Wave.Sine, 0.14f, 240f);
            b.Noise(0.1f, 0.06f, 3000f);
            return b;
        }));

        /// <summary>積むたびに音が上がる(ペンタトニック)。崩れて個数が減ると、音も下がる。</summary>
        public static void Stack(int count)
        {
            int i = Mathf.Clamp(count - 1, 0, 14);
            int[] deg = { 0, 2, 4, 7, 9 };
            int semi = deg[i % 5] + 12 * (i / 5);
            float f = 261.63f * Mathf.Pow(2f, semi / 12f);
            Play(Get("stack" + i, () =>
            {
                var b = new Buf(0.35f);
                b.Tone(f, 0.28f, Wave.Sine, 0.2f);
                b.Tone(f * 2f, 0.2f, Wave.Triangle, 0.07f);
                return b;
            }));
        }

        public static void Miss() => Play(Get("miss", () =>
        {
            var b = new Buf(0.55f);
            b.Tone(420f, 0.45f, Wave.Square, 0.1f, 90f);
            b.Tone(300f, 0.4f, Wave.Sine, 0.2f, 60f, 0.05f);
            return b;
        }));

        /// <summary>「ぎゅいーん ぎゅいーん」: 低い音から一気に上がる音を3回。</summary>
        public static void Warning() => Play(Get("warning", () =>
        {
            var b = new Buf(1.85f);
            for (int i = 0; i < 3; i++)
                foreach (float detune in new[] { 1f, Mathf.Pow(2f, 14f / 1200f) })
                    b.Sweep(i * 0.58f, 220f * detune, 1100f * detune, 0.52f, 0.065f);
            return b;
        }));

        public static void Boss() => Play(Get("boss", () =>
        {
            var b = new Buf(0.75f);
            b.Tone(80f, 0.7f, Wave.Saw, 0.35f, 38f);
            b.Noise(0.4f, 0.2f, 200f);
            return b;
        }));

        public static void Over() => Play(Get("over", () =>
        {
            var b = new Buf(1f);
            b.Noise(0.5f, 0.25f, 220f);
            b.Tone(220f, 0.9f, Wave.Saw, 0.14f, 50f);
            return b;
        }));

        public static void Revive() => Play(Get("revive", () =>
        {
            var b = new Buf(0.65f);
            b.Tone(200f, 0.35f, Wave.Triangle, 0.18f, 900f);
            b.Tone(1200f, 0.3f, Wave.Sine, 0.14f, 0f, 0.32f);
            return b;
        }));

        // ============ 波形を作る道具 ============

        static AudioClip Get(string name, Func<Buf> make)
        {
            if (clips.TryGetValue(name, out var c) && c != null) return c;
            var buf = make();
            for (int i = 0; i < buf.D.Length; i++) buf.D[i] = Mathf.Clamp(buf.D[i], -1f, 1f);
            c = AudioClip.Create(name, buf.D.Length, 1, Rate, false);
            c.SetData(buf.D, 0);
            clips[name] = c;
            return c;
        }

        enum Wave { Sine, Triangle, Square, Saw }

        sealed class Buf
        {
            public readonly float[] D;
            public Buf(float seconds) { D = new float[Mathf.CeilToInt(seconds * Rate)]; }

            static float Osc(Wave w, float ph)
            {
                switch (w)
                {
                    case Wave.Sine: return Mathf.Sin(2f * Mathf.PI * ph);
                    case Wave.Triangle: return 4f * Mathf.Abs(ph - 0.5f) - 1f;
                    case Wave.Square: return ph < 0.5f ? 1f : -1f;
                    default: return 2f * ph - 1f;
                }
            }

            /// <summary>音を足す。f0からtoへ、なめらかに音程が動く(to=0なら動かない)。大きさは、すぐ立ち上がって、だんだん消える。</summary>
            public void Tone(float f0, float dur, Wave w, float vol, float to = 0f, float delay = 0f)
            {
                int start = Mathf.RoundToInt(delay * Rate), n = Mathf.RoundToInt(dur * Rate);
                int att = Mathf.Max(1, Mathf.RoundToInt(0.006f * Rate));
                double phase = 0;
                for (int i = 0; i < n && start + i < D.Length; i++)
                {
                    float t = i / (float)n;
                    float f = to > 0f ? f0 * Mathf.Pow(to / f0, t) : f0;
                    phase += f / Rate;
                    float ph = (float)(phase - Math.Floor(phase));
                    float env = i < att ? vol * i / att : vol * Mathf.Pow(0.0001f / vol, (i - att) / (float)(n - att));
                    D[start + i] += Osc(w, ph) * env;
                }
            }

            /// <summary>ざらざらした音(ノイズ)を、中心の周波数のまわりだけ通して足す。</summary>
            public void Noise(float dur, float vol, float freq, float delay = 0f)
            {
                int start = Mathf.RoundToInt(delay * Rate), n = Mathf.RoundToInt(dur * Rate);
                float f = 2f * Mathf.Sin(Mathf.PI * freq / Rate), low = 0f, band = 0f;
                var rnd = new System.Random(12345);
                for (int i = 0; i < n && start + i < D.Length; i++)
                {
                    float x = (float)(rnd.NextDouble() * 2.0 - 1.0);
                    low += f * band;
                    float high = x - low - band;
                    band += f * high;
                    float env = vol * Mathf.Pow(0.0001f / vol, i / (float)n);
                    D[start + i] += band * env;
                }
            }

            /// <summary>鋸波の音が、f0からf1へ一気に上がる。立ち上がり→保つ→消える。</summary>
            public void Sweep(float t0, float f0, float f1, float dur, float vol)
            {
                int start = Mathf.RoundToInt(t0 * Rate), n = Mathf.RoundToInt(dur * Rate);
                double phase = 0;
                for (int i = 0; i < n && start + i < D.Length; i++)
                {
                    float t = i / (float)n;
                    float f = f0 * Mathf.Pow(f1 / f0, Mathf.Min(1f, t / 0.87f));
                    phase += f / Rate;
                    float ph = (float)(phase - Math.Floor(phase));
                    float env = t < 0.1f ? t / 0.1f : (t < 0.73f ? 1f : Mathf.Pow(1f - (t - 0.73f) / 0.27f, 2f));
                    D[start + i] += Osc(Wave.Saw, ph) * vol * env;
                }
            }
        }
    }
}
