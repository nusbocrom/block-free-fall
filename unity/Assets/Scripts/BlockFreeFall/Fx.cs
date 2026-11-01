using UnityEngine;

namespace BlockFreeFall
{
    /// <summary>着地したときに舞う砂ぼこり。小さな丸がふくらみながら、上と横へ散って消える。</summary>
    public class Dust : MonoBehaviour
    {
        const int MaxActive = 80;
        static int active;

        SpriteRenderer sr;
        Vector2 vel;
        float t, life, size0, size1;
        Color color;

        /// <summary>point: ぶつかった場所、strength: ぶつかった強さ(速さ)。強いほど多く、大きく舞う。</summary>
        public static void Burst(Vector2 point, float strength)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(strength / 2.5f), 2, 7);
            for (int i = 0; i < n && active < MaxActive; i++)
            {
                var g = new GameObject("dust");
                g.transform.position = new Vector3(point.x + Random.Range(-0.25f, 0.25f), point.y + Random.Range(0f, 0.1f), 0f);
                var d = g.AddComponent<Dust>();
                d.sr = g.AddComponent<SpriteRenderer>();
                d.sr.sprite = Shapes.Puff();
                d.sr.sortingOrder = 4;                 // ピースより手前
                float k = Mathf.Clamp01(strength / 16f);
                float side = Random.value < 0.5f ? -1f : 1f;
                d.vel = new Vector2(side * Random.Range(0.8f, 2.4f) * (0.6f + k), Random.Range(0.4f, 1.5f) * (0.6f + k));
                d.life = Random.Range(0.35f, 0.65f);
                d.size0 = Random.Range(0.18f, 0.3f);
                d.size1 = d.size0 * Random.Range(2.2f, 3.2f) * (0.7f + k * 0.6f);
                d.color = Theme.Dark ? new Color(0.55f, 0.58f, 0.66f, 0.5f) : new Color(0.72f, 0.67f, 0.6f, 0.55f);
                active++;
            }
        }

        void Update()
        {
            t += Time.deltaTime;
            float p = t / life;
            if (p >= 1f) { Destroy(gameObject); return; }
            transform.position += (Vector3)(vel * Time.deltaTime);
            vel *= 1f - 3f * Time.deltaTime;           // だんだん遅くなる
            float s = Mathf.Lerp(size0, size1, 1f - (1f - p) * (1f - p));
            transform.localScale = new Vector3(s, s, 1f);
            var c = color; c.a *= (1f - p) * (1f - p);
            sr.color = c;
        }

        void OnDestroy() { active--; }
    }
}
