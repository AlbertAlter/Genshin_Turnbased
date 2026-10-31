using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional textured hit/flight module. Accepts resolved visual commands, never performs damage.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Hit Effects")]
public sealed class BattleUiHitEffects : MonoBehaviour, IBattleUiHitEffects
{
    public BattleUiLayout layout;
    public bool loadOnStart = true;
    public bool automaticClock = true;
    public bool paused;
    public bool Ready { get; private set; }
    public string Error { get; private set; }
    private UIHitEffectStyle effects;
    private HitEffectTextureStyle textures;
    private ElementColorPalette palette;
    private HitTextureLibrary library, pendingLibrary;
    private float clock;
    private bool loading;
    private bool started;
    private sealed class Impact { public UIHitTextureEffect View; public float Start; }
    private sealed class Flight { public UIHitProjectileEffect View; public float Start; public (long, long)? CueID; }
    private sealed class Pending { public Action Spawn; public float Time; }
    private readonly List<Impact> impacts = new List<Impact>();
    private readonly List<Flight> flights = new List<Flight>();
    private readonly List<Pending> pending = new List<Pending>();

    private IEnumerator Start() { started = true; if (loadOnStart) yield return Reload(); }
    private void OnEnable() { if (started && loadOnStart && !Ready && !loading) StartCoroutine(Reload()); }
    public IEnumerator Reload()
    {
        if (loading) yield break;
        loading = true; Error = null;
        UIHitEffectStyle nextEffects = null; HitEffectTextureStyle nextTextures = null; ElementColorPalette nextPalette = null;
        try
        {
            if (layout == null || layout.Canvas == null) throw new InvalidOperationException("命中特效需要显式绑定带Canvas的 BattleUiLayout。");
            nextEffects = UIHitEffectStyle.Load(); nextTextures = HitEffectTextureStyle.Load(); nextPalette = ElementColorPalette.Load();
        }
        catch (Exception error) { Error = error.Message; }
        if (Error != null) { Debug.LogError(Error, this); loading = false; yield break; }
        var loadingLibrary = new HitTextureLibrary(); pendingLibrary = loadingLibrary;
        yield return loadingLibrary.Load(nextTextures, message => Error = message);
        // A caller can yield Reload from its own coroutine; cancellation must also invalidate that request.
        if (!isActiveAndEnabled || pendingLibrary != loadingLibrary) { loadingLibrary.Dispose(); yield break; }
        if (Error != null) { Debug.LogError(Error, this); pendingLibrary.Dispose(); pendingLibrary = null; loading = false; yield break; }
        Clear(); library?.Dispose(); library = pendingLibrary; pendingLibrary = null;
        effects = nextEffects; textures = nextTextures; palette = nextPalette; Ready = true; loading = false;
    }

    public HitEffectTextureStyle.PlaybackMode GetPlaybackMode(HitEffectWeapon weapon, bool heavy)
        => textures != null ? textures.Get(weapon, heavy).Mode : HitEffectTextureStyle.PlaybackMode.PerTarget;

    /// <summary>Call once for one total hit with its complete target set. Different hits stay independent.</summary>
    public bool PlayHit(HitEffectWeapon weapon, bool heavy, string element, BattleUiCardBinding source,
        IReadOnlyList<BattleUiCardBinding> targets, int hit, int hitCount, uint seed,
        HitEffectTextureStyle.PlaybackMode? modeOverride = null)
        => Play(weapon, heavy, element, source, targets, hit, hitCount, seed, modeOverride, null, 0, null);

    // A template launches only on anticipation, and draws impact immediately on the hit marker.
    public bool PlayScheduledHit(HitEffectWeapon weapon, bool heavy, string element, BattleUiCardBinding source,
        IReadOnlyList<BattleUiCardBinding> targets, int hit, int hitCount, uint seed, bool launchOnly, float flightSeconds,
        long sessionID, long eventID)
        => Play(weapon, heavy, element, source, targets, hit, hitCount, seed, null, launchOnly, flightSeconds, (sessionID, eventID));

    private bool Play(HitEffectWeapon weapon, bool heavy, string element, BattleUiCardBinding source,
        IReadOnlyList<BattleUiCardBinding> targets, int hit, int hitCount, uint seed,
        HitEffectTextureStyle.PlaybackMode? modeOverride, bool? launchOnly, float flightSeconds, (long, long)? cueID)
    {
        // A slow frame can cross both markers. Never leave this hit's projectile flying after impact.
        if (launchOnly == false && cueID.HasValue)
            for (int i = flights.Count - 1; i >= 0; i--)
                if (flights[i].CueID == cueID)
                { if (flights[i].View != null) Destroy(flights[i].View.gameObject); flights.RemoveAt(i); }
        if (!isActiveAndEnabled || !Ready || layout == null || targets == null) return false;
        if (hit < 0 || hitCount < 1 || hit >= hitCount) throw new ArgumentOutOfRangeException(nameof(hit));
        var selected = new List<BattleUiCardBinding>(); var keys = new HashSet<(BattleSide, int)>();
        foreach (var card in targets)
            if (card?.art != null && card.root != null && keys.Add((card.side, card.slot))) selected.Add(card);
        if (selected.Count == 0) return false;
        var profile = effects.Get(weapon, heavy); var style = textures.Get(weapon, heavy);
        if (!profile.Enabled) return false;
        if (string.IsNullOrWhiteSpace(element) || element == "None") element = "Physical";
        Color color = palette.GetColor(element); var particles = effects.GetParticles(element);
        bool grouped = (modeOverride ?? style.Mode) == HitEffectTextureStyle.PlaybackMode.PerHitGroup && selected.Count > 1;
        bool behind = profile.Text("DrawLayer") == "Behind";
        var groupLayer = grouped ? layout.GroupHitLayer(behind) : null;
        Vector2 center = Vector2.zero, span = Vector2.zero;
        if (grouped)
        {
            Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
            var corners = new Vector3[4];
            foreach (var card in selected)
            {
                card.art.GetWorldCorners(corners);
                foreach (var corner in corners)
                { Vector2 point = groupLayer.InverseTransformPoint(corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            }
            center = (min + max) * .5f; span = max - min;
        }
        for (int i = 0; i < (grouped ? 1 : selected.Count); i++)
        {
            var card = selected[i]; uint visualSeed = grouped ? seed : HitEffectMath.Mix(seed ^ (uint)(i + 1));
            var parent = grouped ? groupLayer : layout.HitLayer(card, behind);
            float delay = launchOnly.HasValue ? 0 : style.Number("FlightSeconds"), heading = 0;
            if (source?.art != null && (delay > 0 || flightSeconds > 0))
            {
                var flightLayer = layout.ProjectileLayer();
                Vector2 destination = grouped ? (Vector2)flightLayer.InverseTransformPoint(groupLayer.TransformPoint(center))
                    : UIHitProjectileEffect.Destination(flightLayer, card.art, HitEffectMath.Center(profile, visualSeed));
                heading = UIHitProjectileEffect.Heading(flightLayer, source.art, destination, style);
                float duration = launchOnly == true ? flightSeconds : delay;
                if (duration > 0)
                {
                    var flight = UIHitProjectileEffect.Create(flightLayer, source.art, destination, style, library, color, duration);
                    if (flight != null) flights.Add(new Flight { View = flight, Start = clock, CueID = cueID });
                }
            }
            if (launchOnly == true) continue;
            float start = clock + delay; Vector2 size = card.art.rect.size;
            Vector2? groupCenter = grouped ? (Vector2?)center : null, groupSpan = grouped ? (Vector2?)span : null;
            pending.Add(new Pending { Time = start, Spawn = () => PlayResolved(parent, library, profile, particles,
                style, color, size, visualSeed, hit, hitCount, heading, start, groupCenter, groupSpan) });
        }
        SetTime(clock); return true;
    }

    // Explicit composition entry for a preview with already loaded settings. Borrowed assets remain caller-owned.
    public void PlayResolved(Transform parent, HitTextureLibrary assets, UIHitEffectStyle.Profile profile,
        UIHitEffectStyle.Particles particles, HitEffectTextureStyle.Profile style, Color color, Vector2 cardSize,
        uint seed, int hit, int hitCount, float direction, float startTime, Vector2? center = null, Vector2? span = null)
    {
        if (!isActiveAndEnabled || parent == null) return;
        var effect = UIHitTextureEffect.Create(parent, assets, profile, particles, style, color, cardSize,
            seed, hit, hitCount, direction, center, span);
        impacts.Add(new Impact { View = effect, Start = startTime });
    }
    private void Update() { if (automaticClock && !paused) SetTime(clock + Time.unscaledDeltaTime); }
    public void SetTime(float seconds)
    {
        clock = Mathf.Max(0, seconds);
        for (int i = 0; i < pending.Count;)
        {
            if (pending[i].Time > clock) { i++; continue; }
            var cue = pending[i]; pending.RemoveAt(i); cue.Spawn();
        }
        for (int i = impacts.Count - 1; i >= 0; i--)
        {
            var item = impacts[i];
            if (item.View == null) { impacts.RemoveAt(i); continue; }
            item.View.SetElapsed(clock - item.Start);
            if (item.View.Completed) { Destroy(item.View.gameObject); impacts.RemoveAt(i); }
        }
        for (int i = flights.Count - 1; i >= 0; i--)
        {
            var item = flights[i];
            if (item.View == null) { flights.RemoveAt(i); continue; }
            item.View.SetElapsed(clock - item.Start);
            if (item.View.Completed) { Destroy(item.View.gameObject); flights.RemoveAt(i); }
        }
    }
    public void Clear()
    {
        pending.Clear(); clock = 0;
        foreach (var item in impacts) if (item.View != null) Destroy(item.View.gameObject);
        foreach (var item in flights) if (item.View != null) Destroy(item.View.gameObject);
        impacts.Clear(); flights.Clear();
    }
    private void OnDisable()
    {
        Clear(); StopAllCoroutines(); pendingLibrary?.Dispose(); pendingLibrary = null; loading = false;
    }
    private void OnDestroy() { Clear(); library?.Dispose(); pendingLibrary?.Dispose(); }
}
