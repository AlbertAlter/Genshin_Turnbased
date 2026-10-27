using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Reusable per-hit impulses. A shared hit can shake multiple cards but shakes the screen only once.</summary>
public sealed class UIHitShakeController : MonoBehaviour
{
    public bool AutomaticClock = true;
    private sealed class Impulse { public long Playback; public uint Seed; public float Age; public HitShakeStyle.Profile Profile; }
    private sealed class Channel
    {
        public RectTransform Root;
        public readonly List<Impulse> Impulses = new List<Impulse>();
        public Matrix4x4 Delta = Matrix4x4.identity;
    }
    private readonly Dictionary<RectTransform, Channel> cards = new Dictionary<RectTransform, Channel>();
    private readonly Dictionary<Graphic, UIShakeMeshOffset> graphics = new Dictionary<Graphic, UIShakeMeshOffset>();
    private readonly Dictionary<Graphic, Channel> owners = new Dictionary<Graphic, Channel>();
    private readonly HashSet<(long, long, long, string, int, string)> screenHits = new HashSet<(long, long, long, string, int, string)>();
    private readonly Channel screen = new Channel();
    public int ActiveScreenImpulses => screen.Impulses.Count;
    public void BindScreen(RectTransform canvas) => screen.Root = canvas;
    public void RegisterCard(RectTransform root)
    {
        if (root != null && !cards.ContainsKey(root)) cards.Add(root, new Channel { Root = root });
    }
    public void PulseCard(long playback, RectTransform root, HitShakeStyle.Profile profile, uint seed)
    {
        if (root == null || profile == null || !root.gameObject.activeInHierarchy) return;
        RegisterCard(root); Add(cards[root], playback, profile, seed);
    }
    public void PulseScreen(long playback, long session, long execution, string effect, int hit, HitShakeStyle.Profile profile, uint seed)
    {
        if (profile == null || screen.Root == null || !screenHits.Add((playback, session, execution, effect ?? "", hit, profile.ID))) return;
        Add(screen, playback, profile, seed);
    }
    private static void Add(Channel channel, long playback, HitShakeStyle.Profile profile, uint seed)
    {
        // Bound work for unusually dense effects. The oldest visual impulse may be replaced, never a damage result.
        if (channel.Impulses.Count >= 64) channel.Impulses.RemoveAt(0);
        channel.Impulses.Add(new Impulse { Playback = playback, Profile = profile, Seed = seed });
    }
    private static void Tick(Channel channel, float delta)
    {
        channel.Delta = Matrix4x4.identity;
        if (channel.Root == null) { channel.Impulses.Clear(); return; }
        Vector2 size = channel.Root.rect.size;
        var pose = new HitShakePose(); float maxMove = 0, maxRotate = 0;
        for (int i = channel.Impulses.Count - 1; i >= 0; i--)
        {
            var impulse = channel.Impulses[i]; impulse.Age += delta;
            if (impulse.Age >= impulse.Profile.Duration) { channel.Impulses.RemoveAt(i); continue; }
            var sample = HitShakeMath.Sample(impulse.Profile, size, impulse.Seed, impulse.Age);
            pose.Offset += sample.Offset; pose.Rotation += sample.Rotation;
            maxMove = Mathf.Max(maxMove, impulse.Profile.MaxTranslation); maxRotate = Mathf.Max(maxRotate, impulse.Profile.MaxRotation);
        }
        if (channel.Impulses.Count == 0) return;
        pose = HitShakeMath.Clamp(pose, size, maxMove, maxRotate);
        Vector3 center = channel.Root.TransformPoint(channel.Root.rect.center);
        Vector3 offset = channel.Root.TransformVector(pose.Offset);
        channel.Delta = Matrix4x4.Translate(center + offset) * Matrix4x4.Rotate(Quaternion.AngleAxis(pose.Rotation, channel.Root.forward)) * Matrix4x4.Translate(-center);
    }
    private void Collect(Channel channel)
    {
        if (channel.Root == null || channel.Impulses.Count == 0) return;
        foreach (var graphic in channel.Root.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic is TMP_SubMeshUI) continue; // Parent TMP handles all material submeshes together.
            if (!graphics.ContainsKey(graphic))
                graphics.Add(graphic, graphic.GetComponent<UIShakeMeshOffset>() ?? graphic.gameObject.AddComponent<UIShakeMeshOffset>());
            if (channel != screen) owners[graphic] = channel;
        }
    }
    public void Advance(float delta)
    {
        Tick(screen, Mathf.Max(0, delta)); Collect(screen);
        foreach (var channel in cards.Values) { Tick(channel, Mathf.Max(0, delta)); Collect(channel); }
        foreach (var entry in new List<KeyValuePair<Graphic, UIShakeMeshOffset>>(graphics))
        {
            if (entry.Key == null || entry.Value == null) { graphics.Remove(entry.Key); owners.Remove(entry.Key); continue; }
            Matrix4x4 card = owners.TryGetValue(entry.Key, out var owner) ? owner.Delta : Matrix4x4.identity;
            entry.Value.SetWorldDelta(screen.Delta * card);
        }
    }
    public void Cancel(long playback)
    {
        screen.Impulses.RemoveAll(item => playback < 0 || item.Playback == playback);
        foreach (var channel in cards.Values) channel.Impulses.RemoveAll(item => playback < 0 || item.Playback == playback);
        screenHits.RemoveWhere(key => playback < 0 || key.Item1 == playback);
        Advance(0);
    }
    public void ForgetScreenHits(long playback) => screenHits.RemoveWhere(key => key.Item1 == playback);
    private void LateUpdate() { if (AutomaticClock) Advance(Time.unscaledDeltaTime); }
    private void OnDisable() => Cancel(-1);
}
