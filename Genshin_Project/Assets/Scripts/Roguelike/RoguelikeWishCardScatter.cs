using System;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

/// <summary>Renders the card back and its scattering fragments in the same UGUI mesh.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RoguelikeWishCardScatter : MaskableGraphic
{
    private const int Columns = 20;
    private const int Rows = 34;
    private const float FragmentLifetime = .30f;
    private const float SparkLifetime = .42f;
    private const float GoldSparkLifetime = .64f;
    private const float GoldHaloLifetime = 1.12f;
    private const int GoldHaloCount = 24;

    private static readonly Color32[] Blue = { new Color32(163, 220, 255, 255), new Color32(225, 246, 255, 255) };
    private static readonly Color32[] Purple = { new Color32(198, 146, 247, 255), new Color32(242, 209, 255, 255) };
    private static readonly Color32[] Gold = { new Color32(255, 172, 33, 255), new Color32(255, 222, 98, 255) };

    private readonly Fragment[] fragments = new Fragment[Columns * Rows];
    private Sprite cover;
    private Action onFinished;
    private float elapsed, duration;
    private bool gold;
    private Color32[] palette;
    private uint seed;
    private static uint playbackSequence;

    private struct Fragment
    {
        public float Birth;
        public Vector2 Drift;
        public float Spin;
        public float SparkSize;
    }

    public override Texture mainTexture => cover != null ? cover.texture : Texture2D.whiteTexture;
    internal bool IsPlaying { get; private set; }

    internal void SetCover(Sprite sprite)
    {
        cover = sprite;
        color = Color.white;
        SetAllDirty();
    }

    internal bool Play(int stars, Action completed)
    {
        if (!isActiveAndEnabled) return false;
        gold = stars >= 5;
        palette = gold ? Gold : stars == 4 ? Purple : Blue;
        float sweep = gold ? .52f : .41f;
        duration = gold ? Mathf.Max(GoldHaloLifetime, sweep + GoldSparkLifetime) : sweep + SparkLifetime;
        elapsed = 0;
        seed = GoldenDissolveMath.Mix(unchecked(++playbackSequence ^ (uint)GetInstanceID()));

        for (int i = 0; i < fragments.Length; i++)
        {
            uint key = GoldenDissolveMath.Mix(seed + (uint)i);
            float x = (i % Columns + .5f) / Columns;
            float y = (i / Columns + .5f) / Rows;
            float chance = Unit(key);
            fragments[i] = new Fragment
            {
                Birth = sweep * (.51f * x + .34f * y + .15f * Unit(key ^ 0x9E37u)),
                Drift = new Vector2(.05f + .12f * Unit(key ^ 0xA511u), .06f + .14f * Unit(key ^ 0x1B56u)),
                Spin = (Unit(key ^ 0x63D8u) - .5f) * 2.5f,
                SparkSize = chance < (gold ? .24f : .14f)
                    ? (gold ? .008f : .004f) + .009f * Unit(key ^ 0xB529u) : 0f
            };
        }

        onFinished = completed;
        IsPlaying = true;
        SetVerticesDirty();
        return true;
    }

    private void Update()
    {
        if (!IsPlaying) return;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < duration)
        {
            SetVerticesDirty();
            return;
        }

        IsPlaying = false;
        var complete = onFinished;
        onFinished = null;
        complete?.Invoke();
    }

    protected override void OnDisable()
    {
        IsPlaying = false;
        onFinished = null;
        base.OnDisable();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect bounds = GetPixelAdjustedRect();
        Vector4 uv = cover != null ? DataUtility.GetOuterUV(cover) : new Vector4(0, 0, 1, 1);
        if (!IsPlaying)
        {
            AddQuad(mesh, bounds.center, bounds.size * .5f, uv, color);
            return;
        }

        Vector2 cell = new Vector2(bounds.width / Columns, bounds.height / Rows);
        for (int i = 0; i < fragments.Length; i++)
        {
            var fragment = fragments[i];
            float age = elapsed - fragment.Birth;
            if (age >= FragmentLifetime) continue;
            int x = i % Columns, y = i / Columns;
            Vector2 origin = bounds.min + Vector2.Scale(cell, new Vector2(x + .5f, y + .5f));
            float travel = Mathf.Max(0, age / FragmentLifetime);
            var tint = color;
            tint.a *= 1 - travel;
            if (gold) tint = Color.Lerp(tint, new Color(1, .72f, .33f, tint.a), travel * .42f);
            var cellUv = new Vector4(
                Mathf.Lerp(uv.x, uv.z, x / (float)Columns), Mathf.Lerp(uv.y, uv.w, y / (float)Rows),
                Mathf.Lerp(uv.x, uv.z, (x + 1f) / Columns), Mathf.Lerp(uv.y, uv.w, (y + 1f) / Rows));
            AddQuad(mesh, origin + fragment.Drift * (bounds.height * travel), cell * (.5f - .38f * travel),
                cellUv, tint, fragment.Spin * travel);
        }

        float sparkLifetime = gold ? GoldSparkLifetime : SparkLifetime;
        for (int i = 0; i < fragments.Length; i++)
        {
            var fragment = fragments[i];
            float age = (elapsed - fragment.Birth) / sparkLifetime;
            if (fragment.SparkSize <= 0 || age <= 0 || age >= 1) continue;
            Vector2 origin = bounds.min + Vector2.Scale(cell, new Vector2(i % Columns + .5f, i / Columns + .5f));
            Vector2 position = origin + fragment.Drift * (bounds.height * age * 1.28f);
            float alpha = Mathf.Min(1, age * 12f) * (1 - age);
            AddSpark(mesh, position, bounds.height * fragment.SparkSize * (1 - .6f * age), i, alpha);
        }

        if (gold) AddGoldHalo(mesh, bounds);
    }

    private void AddGoldHalo(VertexHelper mesh, Rect bounds)
    {
        for (int i = 0; i < GoldHaloCount; i++)
        {
            uint key = seed + (uint)i;
            float delay = .18f + .22f * Unit(key ^ 0x71A9u);
            float age = (elapsed - delay) / (GoldHaloLifetime - delay);
            if (age <= 0 || age >= 1) continue;
            float angle = (i + .7f * Unit(key)) * Mathf.PI * 2f / GoldHaloCount + age * .38f;
            float radius = .27f + age * .19f + .07f * Unit(key ^ 0x31D1u);
            var position = bounds.center + Vector2.Scale(bounds.size, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))) * radius;
            float size = bounds.height * (.014f + .014f * Unit(key ^ 0xF1C3u)) * (1 - .55f * age);
            float opacity = Mathf.Min(1, age * 10) * Mathf.Min(1, (1 - age) * 2);
            AddSpark(mesh, position, size * 1.45f, i, opacity * .82f);
            AddSpark(mesh, position, size * .72f, i, opacity, new Color(1f, .93f, .59f));
        }
    }

    private void AddSpark(VertexHelper mesh, Vector2 center, float radius, int index, float opacity, Color? sparkColor = null)
    {
        Color tint = sparkColor ?? palette[index % palette.Length];
        tint.a *= opacity * color.a;
        // Negative U selects an untextured light diamond in the shared UI shader.
        AddQuad(mesh, center, new Vector2(radius, radius * 1.6f), new Vector4(-3, -1, -1, 1), tint);
    }

    private static void AddQuad(VertexHelper mesh, Vector2 center, Vector2 half, Vector4 uv, Color32 tint, float rotation = 0)
    {
        int first = mesh.currentVertCount;
        float cosine = Mathf.Cos(rotation), sine = Mathf.Sin(rotation);
        for (int i = 0; i < 4; i++)
        {
            bool right = i >= 2, top = i == 1 || i == 2;
            float x = right ? half.x : -half.x;
            float y = top ? half.y : -half.y;
            var point = center + new Vector2(x * cosine - y * sine, x * sine + y * cosine);
            mesh.AddVert(point, tint, new Vector2(right ? uv.z : uv.x, top ? uv.w : uv.y));
        }
        mesh.AddTriangle(first, first + 1, first + 2);
        mesh.AddTriangle(first + 2, first + 3, first);
    }

    private static float Unit(uint value) => (GoldenDissolveMath.Mix(value) >> 8) / 16777216f;
}
