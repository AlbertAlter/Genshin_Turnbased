using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>元素栏公共轨道；按历史快照播放，每个目标独立排队，不改战斗数据。</summary>
[AddComponentMenu("Battle UI/Element Auras")]
public sealed class BattlefieldElementAuraController : BattleSkillTrackBehaviour,
    IBattleSkillReactionIconTrack, IBattlePresentationSequenceView, IBattleEnemyExitBarrier
{
    public BattleUiLayout layout;
    [Tooltip("A component implementing IBattleUiAuraSource (combat or preview).")]
    public MonoBehaviour dataSource;
    public bool Paused { get; set; }
    public float PlaybackSpeed { get; set; } = 1;
    private IBattleUiLayout boundLayout;
    private IBattleUiAuraSource boundSource;
    private ElementAuraMotion.Style style = new ElementAuraMotion.Style();
    private ElementColorPalette palette;
    private float clock;
    private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    private sealed class Track
    {
        public BattleEntity Owner;
        public BattleUiCardBinding Card;
        public string[] Settled, Planned;
        public float BlockingEnd;
        public readonly List<Scheduled> Requests = new List<Scheduled>();
    }
    private sealed class Scheduled
    {
        public long Sequence, Playback;
        public Track Track;
        public ElementAuraMotion.Clip Clip;
        public BattlePresentationCue Cue;
        public bool Triggered;
        public float Start, End;
    }
    private readonly Dictionary<(BattleSide, int), Track> tracks = new Dictionary<(BattleSide, int), Track>();
    private readonly Dictionary<(BattleSide, int), string[]> displayed = new Dictionary<(BattleSide, int), string[]>();
    private readonly Dictionary<BattlePresentationCue, List<Scheduled>> requests = new Dictionary<BattlePresentationCue, List<Scheduled>>();
    private readonly List<ElementAuraMotion.Icon> frame = new List<ElementAuraMotion.Icon>();

    public void Bind(IBattleUiLayout sceneLayout, IBattleUiAuraSource source)
    {
        Cancel(-1); ClearBars(); displayed.Clear(); boundLayout = sceneLayout; boundSource = source;
        if (!IsAlive(boundLayout)) return;
        foreach (var card in boundLayout.Cards) if (card?.auraBar != null) card.auraBar.Initialize();
    }
    private void Awake() => Bind(layout, dataSource as IBattleUiAuraSource);
    protected override void OnEnable() { base.OnEnable(); BattleEventHub.SessionReset += ResetSession; }
    protected override void Start() { base.Start(); StartCoroutine(LoadAssets()); }
    private IEnumerator LoadAssets()
    {
        try { palette = ElementColorPalette.Load(); style = ElementAuraMotionTable.ReadPublished(); }
        catch (Exception error) { Debug.LogError("元素栏配置读取失败：" + error.Message, this); yield break; }
        foreach (string element in ElementQuery.Elements)
        {
            if (!palette.TryGetAuraIconPath(element, out string path)) continue;
            yield return StreamingAssetSpriteLoader.Load(path, sprite => sprites.Add(element, sprite),
                error => Debug.LogError("元素栏图标加载失败：" + error, this));
        }
    }
    public void PrepareSequence(BattlePresentationSequence sequence, IReadOnlyList<BattlePresentationCue> cues)
    {
        if (!IsAlive(boundLayout)) return;
        var moments = BattleElementAuraTimeline.Build(cues);
        foreach (var moment in moments)
        {
            var identity = moment.Identity;
            if (moment.Target == null || !identity.TargetSide.HasValue ||
                !boundLayout.TryGetCard(identity.TargetSide.Value, identity.TargetSlot, out var card) || card.auraBar == null) continue;
            var key = (identity.TargetSide.Value, identity.TargetSlot);
            if (tracks.TryGetValue(key, out var prior) && !ReferenceEquals(prior.Owner, moment.Target)) Drop(key, prior);
            if (!tracks.TryGetValue(key, out var track))
            {
                displayed.TryGetValue(key, out var initial);
                // 没有净变化时不一定声明AuraChanged，序列中首个Before仍可还原更早的栏位。
                foreach (var next in moments)
                    if (ReferenceEquals(next.Target, moment.Target) && next.Change?.Data is AuraChangedEventData first)
                    { initial = ElementAuraMotion.Order(initial, first.Before); break; }
                if (initial == null) initial = ElementAuraMotion.Copy(IsAlive(boundSource) ? boundSource.GetAuras(key.Item1, key.Item2) : null);
                track = new Track { Owner = moment.Target, Card = card, Settled = initial, Planned = initial };
                tracks.Add(key, track);
            }
            var change = moment.Change?.Data as AuraChangedEventData;
            var before = change == null ? track.Planned : ElementAuraMotion.Order(track.Planned, change.Before);
            var after = change == null ? before : ElementAuraMotion.Order(before, change.After, change.RefreshedPools);
            var contact = moment.Contact?.Data as ElementContactEventData;
            var involved = new List<string>();
            foreach (var reaction in moment.Reactions)
                foreach (string e in ((ReactionEventData)reaction.Data).Elements) if (!involved.Contains(e)) involved.Add(e);
            bool refresh = contact != null && contact.CanApplyAura && change != null && involved.Count == 0 &&
                Array.IndexOf(before, contact.Element) >= 0 && SameMembers(before, after);
            var request = new Scheduled { Sequence = sequence.ID, Cue = moment.Anchor, Track = track,
                Clip = new ElementAuraMotion.Clip(before, after, contact?.Element, involved, refresh, contact != null) };
            track.Planned = after; track.Requests.Add(request);
            if (!requests.TryGetValue(moment.Anchor, out var list)) requests.Add(moment.Anchor, list = new List<Scheduled>());
            list.Add(request);
        }
    }
    private static bool SameMembers(string[] a, string[] b)
    { if (a.Length != b.Length) return false; foreach (string e in a) if (Array.IndexOf(b, e) < 0) return false; return true; }
    public override void Present(long playbackID, BattlePresentationCue cue, Action<Exception> completed)
    {
        if (requests.TryGetValue(cue, out var list))
        {
            requests.Remove(cue);
            foreach (var request in list)
            {
                request.Triggered = true; request.Playback = playbackID;
                request.Start = Mathf.Max(clock, request.Track.BlockingEnd);
                request.End = request.Start + request.Clip.Duration(style);
                if (!request.Clip.IsRefresh) request.Track.BlockingEnd = request.End;
            }
        }
        // 不用全局完成信号阻塞群体目标或下一次真实接触。
        completed(null);
    }
    public void ReleaseSequence(long sequenceID)
    {
        foreach (var pair in new List<KeyValuePair<BattlePresentationCue, List<Scheduled>>>(requests))
        {
            pair.Value.RemoveAll(r => {
                if (r.Sequence != sequenceID) return false;
                r.Track.Requests.Remove(r); return true;
            });
            if (pair.Value.Count == 0) requests.Remove(pair.Key);
        }
        // 未派发序列被替换/取消时，不把它的Planned状态带入下一份准备快照。
        foreach (var pair in new List<KeyValuePair<(BattleSide, int), Track>>(tracks))
            if (pair.Value.Requests.Count == 0) tracks.Remove(pair.Key);
            else pair.Value.Planned = pair.Value.Requests[pair.Value.Requests.Count - 1].Clip.After;
    }
    private void LateUpdate()
    {
        if (!IsAlive(boundLayout)) return;
        if (!Paused) clock += Mathf.Max(0, BattlePause.DeltaTime) * Mathf.Max(0, PlaybackSpeed);
        var source = IsAlive(boundSource) ? boundSource : null;
        foreach (var card in boundLayout.Cards)
        {
            if (card?.auraBar == null) continue;
            var key = (card.side, card.slot);
            if (tracks.TryGetValue(key, out var track))
            {
                bool replaced = source is IBattleUiAuraIdentitySource identity &&
                    !ReferenceEquals(identity.GetAuraOwner(card.side, card.slot), track.Owner);
                if (replaced) Drop(key, track);
                else
                {
                    Draw(track);
                    if (track.Requests.Count > 0) continue;
                    tracks.Remove(key);
                }
            }
            var auras = source?.GetAuras(card.side, card.slot);
            if (!displayed.TryGetValue(key, out var previous) || !SameOrder(previous, auras))
                displayed[key] = ElementAuraMotion.Copy(auras);
            card.auraBar.Present(auras, sprites, palette);
        }
    }
    private static bool SameOrder(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        int count = b?.Count ?? 0; if (a.Count != count) return false;
        for (int i = 0; i < count; i++) if (a[i] != b[i]) return false;
        return true;
    }
    private void Draw(Track track)
    {
        for (int i = 0; i < track.Requests.Count;)
        {
            var r = track.Requests[i];
            if (!r.Triggered || clock < r.End) { i++; continue; }
            if (!r.Clip.IsRefresh) track.Settled = r.Clip.After;
            track.Requests.RemoveAt(i);
        }
        float step = track.Card.auraBar.IconStep, collisionY = 0, flash = 0;
        string flashElement = null;
        ElementAuraMotion.Still(track.Settled, step, frame);
        foreach (var r in track.Requests)
            if (r.Triggered && !r.Clip.IsRefresh && clock >= r.Start)
            {
                flash = ElementAuraMotion.Sample(r.Clip, clock - r.Start, style, step, frame, out collisionY);
                flashElement = r.Clip.Incoming ?? (r.Clip.Involved.Length > 0 ? r.Clip.Involved[0] : null); break;
            }
        foreach (var r in track.Requests)
            if (r.Triggered && r.Clip.IsRefresh && clock >= r.Start) ElementAuraMotion.OverlayRefresh(r.Clip, clock - r.Start, style, frame);
        track.Card.auraBar.PresentMotion(frame, sprites, palette, flash, collisionY, flashElement, style.Brightness);
    }
    public bool HasPendingHits(BattlePresentationCue exit)
    {
        if (!isActiveAndEnabled) return false;
        var result = exit.EnemyExitGroup ?? exit.Result;
        var h = result.Header;
        foreach (var track in tracks.Values)
            foreach (var pending in track.Requests)
            {
                if (!pending.Triggered || pending.End <= clock) continue;
                var r = pending.Cue.Result; var p = r.Header;
                if (h.SessionId != p.SessionId) continue;
                // 敌人退场会按行动/效果分组；不能只等组内第一张卡的元素动画。
                bool same = h.ActionExecutionId != 0 ? h.ActionExecutionId == p.ActionExecutionId :
                    h.EffectExecutionId != 0 ? h.EffectExecutionId == p.EffectExecutionId :
                    h.HitId != 0 ? h.HitId == p.HitId : result.TargetSide == r.TargetSide && result.TargetSlot == r.TargetSlot;
                if (same) return true;
            }
        return false;
    }
    public override void Cancel(long playbackID)
    {
        if (playbackID < 0) { tracks.Clear(); requests.Clear(); ClearBars(); return; }
        foreach (var track in tracks.Values)
        {
            foreach (var r in track.Requests)
                if (r.Triggered && r.Playback == playbackID && !r.Clip.IsRefresh) track.Settled = r.Clip.After;
            track.Requests.RemoveAll(r => r.Triggered && r.Playback == playbackID);
            track.BlockingEnd = clock;
            foreach (var r in track.Requests) if (r.Triggered && !r.Clip.IsRefresh) track.BlockingEnd = Mathf.Max(track.BlockingEnd, r.End);
        }
    }
    private void Drop((BattleSide, int) key, Track track)
    {
        foreach (var r in track.Requests)
            if (requests.TryGetValue(r.Cue, out var list)) { list.Remove(r); if (list.Count == 0) requests.Remove(r.Cue); }
        tracks.Remove(key); track.Card.auraBar.Clear();
    }
    private void ResetSession() { Cancel(-1); displayed.Clear(); clock = 0; }
    private void ClearBars()
    {
        if (!IsAlive(boundLayout)) return;
        foreach (var card in boundLayout.Cards) if (card?.auraBar != null) card.auraBar.Clear();
    }
    private static bool IsAlive(object value)
        => value != null && (!(value is UnityEngine.Object unityObject) || unityObject != null);
    protected override void OnDisable()
    { BattleEventHub.SessionReset -= ResetSession; base.OnDisable(); displayed.Clear(); }
    private void OnDestroy()
    {
        foreach (var sprite in sprites.Values) if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
        sprites.Clear();
    }
}
