using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public struct CardAttackPose
{
    public Vector2 Offset;
    public Vector3 Rotation;
    public float Scale;
    public static CardAttackPose Rest => new CardAttackPose { Scale = 1 };
}

/// <summary>No self action or one of four self motions; three hit choices. No battle RNG or scene coordinates.</summary>
public static class CardAttackMotionMath
{
    public static readonly HitShakeStyle.Profile LightShake = new HitShakeStyle.Profile { ID = "LightHit", Space = "Card", Duration = 0.14f,
        X = 0.012f, Y = 0.006f, Rotation = 0.45f, Frequency = 28, Decay = 2, MaxTranslation = 0.03f, MaxRotation = 1.2f };
    public static readonly HitShakeStyle.Profile HeavyShake = new HitShakeStyle.Profile { ID = "HeavyHit", Space = "Card", Duration = 0.2f,
        X = 0.02f, Y = 0.009f, Rotation = 0.8f, Frequency = 24, Decay = 2, MaxTranslation = 0.05f, MaxRotation = 1.8f };
    public static float HitDuration(int kind) => kind == 3 ? 0.36f : kind == 2 ? 0.14f : 0;
    private static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }
    private static float Between(float t, float a, float b, float x, float y) => Mathf.Lerp(x, y, Ease((t - a) / (b - a)));
    public static CardAttackPose Self(BattleAttackAnimationSpec spec, Vector2 size, float progress, float direction, float? sampledStrength = null)
    {
        if (spec.SelfAnimation == 1) return CardAttackPose.Rest;
        if (spec.SelfAnimation == 5)
        {
            float v = CastLift(progress), strength = sampledStrength ?? spec.StrengthAt(progress);
            return new CardAttackPose { Offset = new Vector2(0, size.y * 0.065f * v * strength),
                Rotation = new Vector3(4 * v * strength, 0, 0), Scale = 1 + 0.045f * v * strength };
        }
        float t = Mathf.Clamp01(progress), c = spec.ContactRatio, recovery = Mathf.Min(0.95f, c + 0.2f), p;
        if (t <= c * 0.52f) p = spec.SelfAnimation == 3 ? 0 : Between(t, 0, c * 0.52f, 0, -0.2f);
        else if (t <= c) p = Between(t, c * 0.52f, c, spec.SelfAnimation == 3 ? 0 : -0.2f, 1);
        else if (t <= recovery) p = Between(t, c, recovery, 1, 0.12f);
        else p = Between(t, recovery, 1, 0.12f, 0);
        bool heavy = spec.SelfAnimation == 3, lunge = spec.SelfAnimation == 4;
        float s = sampledStrength ?? spec.StrengthAt(progress), forward = Mathf.Max(0, p);
        return new CardAttackPose {
            Offset = new Vector2(direction * size.x * (lunge ? 0.72f : heavy ? 0.11f : 0.065f) * p * s,
                size.y * (lunge ? 0.045f : 0.012f) * forward * s),
            Rotation = new Vector3(2.5f * Mathf.Abs(p) * s, -direction * 3 * p * s, -direction * spec.TiltDegrees * p * (heavy ? 1.45f : 1) * s),
            Scale = 1 + forward * 0.025f * s
        };
    }
    public static float CastLift(float progress)
    {
        float t = Mathf.Clamp01(progress);
        return t <= 0.3f ? Between(t, 0, 0.3f, 0, 1) : t <= 0.65f ? Between(t, 0.3f, 0.65f, 1, 0.8f) : Between(t, 0.65f, 1, 0.8f, 0);
    }
    public static float CastHaloOpacity(float progress, float strength)
        => progress <= 0 || progress >= 1 ? 0 : Mathf.Sin(progress * Mathf.PI) * 0.65f * Mathf.Clamp(strength, 0, 1.5f);
    public static CardAttackPose Hit(int kind, Vector2 size, float age, float direction, uint seed, float strength = 1)
    {
        if (kind == 1 || age < 0 || age >= HitDuration(kind)) return CardAttackPose.Rest;
        var shake = HitShakeMath.Sample(kind == 3 ? HeavyShake : LightShake, size, seed, age);
        float recoil = kind != 3 ? 0 : age <= 0.065f ? Between(age, 0, 0.065f, 0, 1)
            : age <= 0.15f ? Between(age, 0.065f, 0.15f, 1, 0.6f) : Between(age, 0.15f, 0.36f, 0.6f, 0);
        return new CardAttackPose { Offset = (shake.Offset + new Vector2(direction * size.x * 0.09f * recoil, -size.y * 0.007f * recoil)) * strength,
            Rotation = new Vector3(0, 0, -shake.Rotation - direction * 4 * recoil) * strength, Scale = 1 };
    }
}

/// <summary>Public render-only player. Self and hit channels compose; original RectTransforms never move.</summary>
public sealed class UICardAttackMotion : MonoBehaviour
{
    public bool AutomaticClock = true;
    public bool Paused;
    private sealed class Motion
    {
        public long ID, Playback; public RectTransform Root; public BattleAttackAnimationSpec Spec;
        public float Age, Windup, RecoveryAge, Direction; public uint Seed; public int Hit;
        public bool Released, AutoRelease; public Action<Exception> Finish;
    }
    private readonly List<Motion> motions = new List<Motion>();
    private readonly Dictionary<Graphic, UIShakeMeshOffset> graphics = new Dictionary<Graphic, UIShakeMeshOffset>();
    private readonly Dictionary<Graphic, RectTransform> roots = new Dictionary<Graphic, RectTransform>();
    private readonly Dictionary<RectTransform, UICardCastHalo> halos = new Dictionary<RectTransform, UICardCastHalo>();
    private long nextID;
    public int ActiveCount => motions.Count;
    public long PlaySelf(long playback, RectTransform root, BattleAttackAnimationSpec spec, float direction, float windup,
        bool autoRelease, Action<Exception> finished)
    {
        if (!isActiveAndEnabled || root == null || !root.gameObject.activeInHierarchy || spec == null || spec.SelfAnimation == 1) { finished?.Invoke(null); return 0; }
        Remove(m => m.Root == root && m.Hit == 0);
        var item = new Motion { ID = ++nextID, Playback = playback, Root = root, Spec = spec, Direction = direction,
            Windup = Mathf.Max(0, windup), AutoRelease = autoRelease, Finish = finished };
        if (spec.SelfAnimation == 5 && (!halos.TryGetValue(root, out var halo) || halo == null))
        {
            var go = new GameObject("CastingTripleHalo", typeof(RectTransform), typeof(CanvasRenderer), typeof(UICardCastHalo));
            go.transform.SetParent(root, false); halo = go.GetComponent<UICardCastHalo>(); halo.raycastTarget = false; halo.maskable = false;
            var rect = halo.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero; rect.anchoredPosition = Vector2.zero;
            halos[root] = halo;
        }
        motions.Add(item); Collect(root); Apply(); return item.ID;
    }
    public void Impact(long id)
    {
        var item = motions.Find(m => m.ID == id);
        if (item == null || item.Released) return;
        item.Released = true; item.RecoveryAge = 0; Apply();
    }
    public void PlayHit(long playback, RectTransform root, int kind, float direction, uint seed, float strength, Action<Exception> finished = null,
        BattleAttackAnimationSpec resolvedSpec = null)
    {
        if (!isActiveAndEnabled || root == null || !root.gameObject.activeInHierarchy || kind == 1) { finished?.Invoke(null); return; }
        if (kind < 1 || kind > 3) throw new ArgumentOutOfRangeException(nameof(kind));
        motions.Add(new Motion { ID = ++nextID, Playback = playback, Root = root, Hit = kind, Direction = direction, Seed = seed,
            Spec = resolvedSpec ?? new BattleAttackAnimationSpec("Hit", 1, 1, kind, 1, 0.44f, 0, strength), Finish = finished });
        Collect(root); Apply();
    }
    private void Collect(RectTransform root)
    {
        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic is TMP_SubMeshUI || graphic is UICardCastHalo) continue;
            if (!graphics.ContainsKey(graphic)) graphics.Add(graphic, graphic.GetComponent<UIShakeMeshOffset>() ?? graphic.gameObject.AddComponent<UIShakeMeshOffset>());
            roots[graphic] = root;
        }
    }
    public void Advance(float delta)
    {
        if (Paused) return;
        delta = Mathf.Max(0, delta); var ended = new List<Motion>();
        foreach (var m in motions)
        {
            if (m.Root == null || !m.Root.gameObject.activeInHierarchy) { ended.Add(m); continue; }
            m.Age += delta;
            if (m.Hit != 0) { if (m.Age >= CardAttackMotionMath.HitDuration(m.Hit)) ended.Add(m); continue; }
            if (!m.Released && m.AutoRelease && m.Age >= m.Windup)
            { m.Released = true; m.RecoveryAge = Mathf.Max(0, m.Age - m.Windup); }
            else if (m.Released) m.RecoveryAge += delta;
            if (m.Released && m.RecoveryAge >= m.Spec.Duration * (1 - m.Spec.ContactRatio)) ended.Add(m);
        }
        foreach (var m in ended) motions.Remove(m);
        Apply(); foreach (var m in ended) Finish(m);
    }
    private void Apply()
    {
        var deltas = new Dictionary<RectTransform, Matrix4x4>();
        var hitPoses = new Dictionary<RectTransform, CardAttackPose>();
        var casting = new Dictionary<RectTransform, (float progress, float strength)>();
        foreach (var m in motions)
        {
            if (m.Root == null) continue;
            var size = m.Root.rect.size;
            float progress = m.Released ? Mathf.Lerp(m.Spec.ContactRatio, 1, m.RecoveryAge / (m.Spec.Duration * (1 - m.Spec.ContactRatio)))
                : m.Spec.ContactRatio * (m.Windup <= 0 ? 1 : Mathf.Clamp01(m.Age / m.Windup));
            float strength = m.Spec.StrengthAt(m.Hit != 0 ? Mathf.Clamp01(m.Age / CardAttackMotionMath.HitDuration(m.Hit)) : progress);
            var pose = m.Hit != 0 ? CardAttackMotionMath.Hit(m.Hit, size, m.Age, m.Direction, m.Seed, strength)
                : CardAttackMotionMath.Self(m.Spec, size, progress, m.Direction, strength);
            if (m.Hit == 0 && m.Spec.SelfAnimation == 5) casting[m.Root] = (progress, strength);
            if (m.Hit != 0)
            {
                var combined = hitPoses.TryGetValue(m.Root, out var oldHit) ? oldHit : CardAttackPose.Rest;
                combined.Offset += pose.Offset; combined.Rotation += pose.Rotation; hitPoses[m.Root] = combined;
                continue;
            }
            Vector2 localPivot = m.Root.rect.min + new Vector2(size.x * 0.5f, size.y * 0.08f);
            Matrix4x4 local = Matrix4x4.TRS(localPivot + pose.Offset, Quaternion.Euler(pose.Rotation), Vector3.one * pose.Scale) * Matrix4x4.Translate(-localPivot);
            Matrix4x4 world = m.Root.localToWorldMatrix * local * m.Root.worldToLocalMatrix;
            deltas[m.Root] = (deltas.TryGetValue(m.Root, out var old) ? old : Matrix4x4.identity) * world;
        }
        foreach (var entry in hitPoses)
        {
            var root = entry.Key; var pose = entry.Value; var size = root.rect.size; var pivot = root.rect.center;
            pose.Offset = Vector2.ClampMagnitude(pose.Offset, Mathf.Min(size.x, size.y) * 0.15f);
            pose.Rotation.z = Mathf.Clamp(pose.Rotation.z, -8, 8);
            var local = Matrix4x4.TRS(pivot + pose.Offset, Quaternion.Euler(pose.Rotation), Vector3.one) * Matrix4x4.Translate(-pivot);
            var world = root.localToWorldMatrix * local * root.worldToLocalMatrix;
            deltas[root] = (deltas.TryGetValue(root, out var old) ? old : Matrix4x4.identity) * world;
        }
        foreach (var entry in new List<KeyValuePair<Graphic, UIShakeMeshOffset>>(graphics))
        {
            if (entry.Key == null || entry.Value == null) { graphics.Remove(entry.Key); roots.Remove(entry.Key); continue; }
            entry.Value.SetWorldDelta(this, roots.TryGetValue(entry.Key, out var root) && root != null && deltas.TryGetValue(root, out var delta) ? delta : Matrix4x4.identity);
        }
        foreach (var entry in new List<KeyValuePair<RectTransform, UICardCastHalo>>(halos))
        {
            if (entry.Key == null || entry.Value == null) { halos.Remove(entry.Key); continue; }
            if (casting.TryGetValue(entry.Key, out var sample)) entry.Value.Sample(sample.progress, sample.strength);
            else entry.Value.Sample(1, 0);
        }
    }
    private static void Finish(Motion m) { var done = m.Finish; m.Finish = null; done?.Invoke(null); }
    private void Remove(Predicate<Motion> predicate)
    {
        var removed = motions.FindAll(predicate); motions.RemoveAll(predicate); Apply(); foreach (var m in removed) Finish(m);
    }
    public void Cancel(long playback) => Remove(m => playback < 0 || m.Playback == playback);
    private void LateUpdate() { if (AutomaticClock) Advance(Time.unscaledDeltaTime); }
    private void OnDisable()
    {
        Cancel(-1); foreach (var effect in graphics.Values) if (effect != null) effect.ClearWorldDelta(this);
        graphics.Clear(); roots.Clear();
        foreach (var halo in halos.Values) if (halo != null) Destroy(halo.gameObject);
        halos.Clear();
    }
}
