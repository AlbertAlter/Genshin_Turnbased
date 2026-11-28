using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene-bound native VFX track. Consumes committed targets/status identities, never gameplay hooks.</summary>
[DisallowMultipleComponent]
public sealed partial class BattleUiNativeEffects : BattleSkillTrackBehaviour, IBattleSkillHitEffectTrack, IBattleAttackAnimationView,
    IBattlePresentationSequenceView, IBattlePresentationCueBarrier, IBattleEnemyExitBarrier
{
    public BattleUiLayout layout;
    public BattleManager battle;
    public string catalogResourcePath = BattleNativeEffectCatalog.ResourcePath;
    private BattleNativeEffectCatalog catalog;
    private bool retained;
    private float nextScan;
    private BattleUiAnimatedCards cardViews;
    private BattleField observedField;
    private sealed class Targets
    {
        public readonly List<RectTransform> Cards = new List<RectTransform>();
        public readonly Dictionary<long, RectTransform> Instances = new Dictionary<long, RectTransform>();
        public RectTransform Caster;
        public BattleEffectKind Kind = BattleEffectKind.AmberBurningArea;
        public int IcicleCount = 3;
        public long VentiBurstInstance;
        public string VentiElement;
    }
    private sealed class Fx
    {
        public string ResourceKey;
        public bool Sustained;
        public bool BlocksHits=true;
        public float TableContact;
        public BattleSide? ScopeSide;
        public BattleEffectKind Kind;
        public UIBattleEffectPlayer View;
        public bool Active, Loop;
        public long Playback;
        public long Batch, Lane;
        public long VentiBurstInstance;
        public (long, long, string) Execution;
        public (long, long, string, long) Hit;
        public Action<Exception> Finish;
        public readonly Dictionary<long, RectTransform> Instances = new Dictionary<long, RectTransform>();
        public readonly Dictionary<long,int> Counts = new Dictionary<long,int>();
    }
    private readonly Dictionary<long, Dictionary<(long,long,string), Targets>> prepared = new Dictionary<long, Dictionary<(long,long,string), Targets>>();
    private readonly Dictionary<long, Dictionary<(long,long,string,long), Targets>> preparedHits = new Dictionary<long, Dictionary<(long,long,string,long), Targets>>();
    private readonly List<Fx> effects = new List<Fx>();
    private readonly HashSet<(long,long,long,string,long,string)> started = new HashSet<(long,long,long,string,long,string)>();
    private static (long,long,string) Execution(BattlePresentationCue cue)
        => cue.StatusBatchLane != 0 && !string.IsNullOrEmpty(cue.StatusBatchEffectKey)
            ? (cue.Result.Header.SessionId, -cue.Result.StatusBatchID, cue.StatusBatchEffectKey)
            : BattleAttackAnimationTimeline.ExecutionKey(cue.Result);
    protected override void OnEnable()
    {
        cardViews = GetComponent<BattleUiAnimatedCards>();
        try { BattleNativeEffectHooks.Retain(); retained = true; }
        catch (Exception error) { Debug.LogException(error, this); enabled = false; return; }
        catalog = Resources.Load<BattleNativeEffectCatalog>(catalogResourcePath);
        base.OnEnable();
    }
    protected override void Start()
    {
        base.Start();
        catalog = Resources.Load<BattleNativeEffectCatalog>(catalogResourcePath);
    }
    private RectTransform Card(BattleSide? side, int slot)
    {
        if (!side.HasValue || layout == null || !layout.TryGetCard(side.Value, slot, out var binding)) return null;
        if (cardViews != null && cardViews.isActiveAndEnabled && cardViews.TryGetCard(binding, null, out var card) && card.Ready)
            return card.Appearance.Root;
        return binding.root;
    }
    public void PrepareSequence(BattlePresentationSequence sequence, IReadOnlyList<BattlePresentationCue> cues)
    {
        PrepareReactions(sequence);
        var groups = new Dictionary<(long,long,string), Targets>();
        var hits = new Dictionary<(long,long,string,long), Targets>();
        foreach (var cue in cues)
        {
            if (cue.IsAnticipation || cue.IsAttackStart || cue.IsEnemyExit) continue;
            var result = cue.Result;
            if (!(result.Data is DamageEventData) && !(result.Data is HealEventData) && result.Data.Kind != BattleEventKind.StatusAdded && result.Data.Kind != BattleEventKind.Summoned && result.Data.Kind != BattleEventKind.Withdrawn) continue;
            var key = Execution(cue);
            if (!groups.TryGetValue(key, out var group)) groups.Add(key, group = new Targets { Caster = Card(result.SourceSide, result.SourceSlot) });
            var card = Card(result.TargetSide, result.TargetSlot);
            if (card == null) continue;
            if (!group.Cards.Contains(card)) group.Cards.Add(card);
            if (result.Data is StatusEventData status) group.Instances[status.InstanceId] = card;
            if (result.Data is DamageEventData damage && damage.HitLanded)
            {
                var hit = BattleAttackAnimationTimeline.PresentationHitKey(cue);
                if (!hits.TryGetValue(hit, out var hitGroup)) hits.Add(hit, hitGroup = new Targets { Caster = group.Caster });
                if (!hitGroup.Cards.Contains(card)) hitGroup.Cards.Add(card);
            }
        }
        prepared[sequence.ID] = groups;
        preparedHits[sequence.ID] = hits;
    }
    public void ReleaseSequence(long sequenceID)
    {
        ReleaseReactions(sequenceID);
        if (prepared.TryGetValue(sequenceID, out var groups))
            started.RemoveWhere(key => groups.ContainsKey((key.Item2, key.Item3, key.Item4)));
        prepared.Remove(sequenceID);
        preparedHits.Remove(sequenceID);
    }
    private Targets ForHit(BattlePresentationCue cue)
    {
        var key = BattleAttackAnimationTimeline.PresentationHitKey(cue);
        foreach (var groups in preparedHits.Values) if (groups.TryGetValue(key, out var group)) return group;
        var targets = new Targets { Caster = Card(cue.Result.SourceSide, cue.Result.SourceSlot) };
        void Add(BattlePresentationResult result)
        {
            if (!(result.Data is DamageEventData damage) || !damage.HitLanded) return;
            var card = Card(result.TargetSide, result.TargetSlot);
            if (card != null && !targets.Cards.Contains(card)) targets.Cards.Add(card);
        }
        if (cue.HitResults != null) foreach (var result in cue.HitResults) Add(result); else Add(cue.Result);
        return targets;
    }
    private Targets For(BattlePresentationCue cue)
    {
        var key = Execution(cue);
        foreach (var groups in prepared.Values) if (groups.TryGetValue(key, out var group)) return group;
        var fallback = new Targets { Caster = Card(cue.Result.SourceSide, cue.Result.SourceSlot) };
        void Add(BattlePresentationResult result)
        {
            var card = Card(result.TargetSide, result.TargetSlot);
            if (card != null && !fallback.Cards.Contains(card)) fallback.Cards.Add(card);
            if (card != null && result.Data is StatusEventData status) fallback.Instances[status.InstanceId] = card;
        }
        if (cue.HitResults != null) foreach (var result in cue.HitResults) Add(result); else Add(cue.Result);
        return fallback;
    }
    private Fx Play(BattleEffectKind kind, Targets targets, long playback, BattlePresentationCue cue, Action<Exception> finish)
    {
        if (catalog == null || layout?.Canvas == null || targets.Cards.Count == 0)
            throw new InvalidOperationException("原生特效未就绪或没有实际目标卡位：" + kind);
        Fx fx = null;
        foreach (var candidate in effects) if (!candidate.Active && candidate.ResourceKey == null && candidate.Kind == kind) { fx = candidate; break; }
        if (fx == null)
        {
            var host = new GameObject("NativeEffect/" + kind, typeof(UIBattleEffectPlayer)); host.transform.SetParent(transform, false);
            fx = new Fx { Kind = kind, View = host.GetComponent<UIBattleEffectPlayer>() }; effects.Add(fx);
            if (!fx.View.Bind(layout.Canvas.transform as RectTransform, layout.GroupHitLayer(true), layout.GroupHitLayer(false), catalog.shader, catalog.Assets(kind)))
            {
                string error = fx.View.Error; effects.Remove(fx); Destroy(host);
                throw new InvalidOperationException(error);
            }
        }
        var settings = BattleNativeEffectHooks.Parameters(kind);
        if (kind == BattleEffectKind.KaeyaGlacialWaltz)
        {
            var values = settings.Values(); values["IcicleCount"] = targets.IcicleCount;
            settings = BattleEffectParameters.Parse(kind, values);
        }
        fx.View.automaticClock = true; fx.View.Paused = false;
        bool loop = BattleEffectParameters.IsSustained(kind);
        bool sliced = cue != null && cue.Result.Data is DamageEventData &&
            (kind == BattleEffectKind.AmberArrowRain || kind == BattleEffectKind.LisaVioletArc || kind == BattleEffectKind.KaeyaIcicle || kind == BattleEffectKind.VentiStormPulse || kind == BattleEffectKind.VentiUpdraft);
        float leadOffset = sliced ? (cue.IsAnticipation ? (float)cue.ProjectileSeconds : 0) - BattleNativeEffectHooks.Lead(settings) : 0;
        var slice = sliced ? new BattleEffectHitSlice(cue.VisualHitIndex, cue.VisualHitCount) : null;
        int visualSeed = unchecked((int)(cue?.Result.Header.EventId ?? 12345));
        if (kind == BattleEffectKind.DandelionWindOrbit)
            foreach (long instance in targets.Instances.Keys) { visualSeed = unchecked((int)(instance ^ (instance >> 32))); break; }
        if (!fx.View.TryPlay(settings, targets.Cards, targets.Caster, visualSeed, loop, Mathf.Max(0, leadOffset), slice))
            throw new InvalidOperationException(fx.View.Error);
        if (kind == BattleEffectKind.KaeyaIcicle) BindKaeyaDeparture(fx, targets, cue, Mathf.Max(0, leadOffset));
        fx.VentiBurstInstance = 0;
        if (kind == BattleEffectKind.VentiStormEye || kind == BattleEffectKind.VentiStormPulse) BindVentiAppearance(fx, targets, cue);
        if (kind == BattleEffectKind.VentiStormPulse) BindVentiPhase(fx, targets);
        if (leadOffset < 0) fx.View.Seek(-leadOffset);
        fx.Active = true; fx.Loop = loop; fx.Sustained = loop; fx.Playback = playback; fx.Finish = finish;
        fx.Execution = cue != null ? Execution(cue) : default;
        fx.Batch = cue?.Result.StatusBatchID ?? 0; fx.Lane = cue?.StatusBatchLane ?? 0;
        fx.Hit = cue?.Result.Data is DamageEventData ? NativeHitKey(cue) : default;
        fx.Instances.Clear(); foreach (var pair in targets.Instances) fx.Instances.Add(pair.Key, pair.Value);
        return fx;
    }
    public override void Present(long playbackID, BattlePresentationCue cue, Action<Exception> completed)
    {
        try
        {
            PresentTableEvents(playbackID,cue);
            if (cue.Result.Data is StatusEventData removed && removed.Kind == BattleEventKind.StatusRemoved)
            { PresentTableCountExit(removed,playbackID,cue); Remove(removed.InstanceId); completed(null); return; }
            if (isActiveAndEnabled && (cue.SkillTracks & BattleSkillTracks.HitEffects) != 0) PresentVentiConversion(cue);
            if (cue.Result.Data is StatusEventData state && state.Host is FieldPosition position) observedField = position.OwnerField;
            if (TryPresentVentiFlip(playbackID, cue, completed)) return;
            if (TryPresentTableState(playbackID,cue,completed)) return;
            if (TryPresentMappedState(playbackID, cue, completed)) return;
            string name = cue.AttackAnimation?.NativeEffect;
            if (TryPresentTableEffect(playbackID,cue,name,completed)) return;
            if (cue.Result.Data is StatusEventData addedStatus && addedStatus.Kind == BattleEventKind.StatusAdded &&
                BattleNativeEffectHooks.TryStatusRule(addedStatus.StatusNumericId, out var mappedStatus))
                name = mappedStatus.HasValue ? mappedStatus.Value.ToString() : "";
            if (!isActiveAndEnabled || (cue.SkillTracks & BattleSkillTracks.HitEffects) == 0 || string.IsNullOrEmpty(name) || cue.IsEnemyExit)
            { completed(null); return; }
            if (!Enum.TryParse(name, out BattleEffectKind kind)) throw new InvalidOperationException("未知原生特效：" + name);
            if (TryPresentReaction(playbackID, cue, kind, completed)) return;
            var execution = Execution(cue);
            long hitID = cue.Result.Data is DamageEventData ? BattleAttackAnimationTimeline.PresentationHitKey(cue).Item4 : 0;
            var once = (playbackID, execution.Item1, execution.Item2, execution.Item3, hitID, name);
            if (BattleEffectParameters.IsSustained(kind))
            {
                if (cue.Result.Data.Kind == BattleEventKind.StatusAdded && started.Add(once))
                {
                    var group = For(cue);
                    if (kind == BattleEffectKind.KaeyaGlacialWaltz && cue.Result.Data is StatusEventData added)
                        ConfigureKaeyaState(group, added);
                    if (kind == BattleEffectKind.VentiStormEye && cue.Result.Data is StatusEventData eye)
                        ConfigureVentiState(group, eye);
                    Play(kind, group, playbackID, cue, null);
                }
                completed(null); return;
            }
            bool volley = kind == BattleEffectKind.AmberArrowRain || kind == BattleEffectKind.LisaVioletArc || kind == BattleEffectKind.KaeyaIcicle || kind == BattleEffectKind.VentiStormPulse || kind == BattleEffectKind.VentiUpdraft;
            if (volley)
            {
                if (cue.IsAttackStart || !(cue.Result.Data is DamageEventData landed) || !landed.HitLanded || (cue.HitResults != null && !cue.LeadsHit))
                { completed(null); return; }
                var hit = BattleAttackAnimationTimeline.PresentationHitKey(cue);
                Fx effect = null;
                foreach (var candidate in effects)
                    if (candidate.Active && candidate.Playback == playbackID && candidate.Kind == kind && candidate.Hit == hit) { effect = candidate; break; }
                if (effect == null && started.Add(once)) effect = Play(kind, ForHit(cue), playbackID, cue, null);
                if (effect == null || cue.IsAnticipation) { completed(null); return; }
                effect.View.ConfirmContact();
                // Earlier hits can cross their own causal blocks; only the final hit waits for the VFX tail.
                if (kind != BattleEffectKind.VentiUpdraft && cue.VisualHitIndex + 1 == cue.VisualHitCount && effect.Finish == null) effect.Finish = completed;
                else completed(null);
                return;
            }
            if (cue.IsAttackStart || cue.IsAnticipation || !(cue.Result.Data is DamageEventData hitDamage) || !hitDamage.HitLanded || (cue.HitResults != null && !cue.LeadsHit))
            { completed(null); return; }
            var targets = new Targets { Caster = Card(cue.Result.SourceSide, cue.Result.SourceSlot) };
            void Add(BattlePresentationResult result)
            {
                if (!(result.Data is DamageEventData damage) || !damage.HitLanded) return;
                var card = Card(result.TargetSide, result.TargetSlot);
                if (card != null && !targets.Cards.Contains(card)) targets.Cards.Add(card);
            }
            if (cue.HitResults != null) foreach (var result in cue.HitResults) Add(result); else Add(cue.Result);
            Play(kind, targets, playbackID, cue, completed);
        }
        catch (Exception error) { Debug.LogWarning("原生特效未播放：" + error.Message, this); completed(null); }
    }
    public bool CanPresent(long playbackID, BattlePresentationCue cue)
    {
        if (!cue.IsAttackStart && !(cue.Result.Data is DamageEventData damage && damage.HitLanded)) return true;
        var execution = Execution(cue);
        foreach (var fx in effects)
        {
            if (!fx.Active || fx.Sustained || !fx.BlocksHits || !fx.View.Playing) continue;
            if (cue.StatusBatchLane != 0 && fx.Batch == cue.Result.StatusBatchID && fx.Lane != cue.StatusBatchLane) continue;
            if (fx.Execution != execution) return false;
            if ((fx.ResourceKey!=null||BattleEffectParameters.IsExplosion(fx.Kind)) && !cue.IsAttackStart && fx.Hit != NativeHitKey(cue)) return false;
        }
        return true;
    }
    public bool HasPendingHits(BattlePresentationCue exit)
    {
        if (exit.EnemyExitGroup == null) return false;
        var execution = BattleAttackAnimationTimeline.ExecutionKey(exit.EnemyExitGroup);
        foreach (var fx in effects) if (fx.Active && !fx.Sustained && fx.View.Playing &&
            (fx.Execution == execution || (exit.EnemyExitGroup.StatusBatchID != 0 && fx.Batch == exit.EnemyExitGroup.StatusBatchID))) return true;
        return false;
    }
    private void Remove(long instance)
    {
        foreach (var fx in effects)
        {
            if (!fx.Active || !fx.Loop || !fx.Instances.Remove(instance)) continue;
            if (fx.Instances.Count == 0) { fx.View.Stop(); fx.Loop = false; }
            else fx.View.SetTargets(new List<RectTransform>(new HashSet<RectTransform>(fx.Instances.Values)));
        }
    }
    private void Update()
    {
        if (catalog == null) catalog = Resources.Load<BattleNativeEffectCatalog>(catalogResourcePath);
        if (battle == null) battle = GetComponent<BattleManager>();
        if (cardViews == null) cardViews = GetComponent<BattleUiAnimatedCards>();
        foreach (var fx in effects.ToArray())
        {
            if (!fx.Active || fx.View.Playing) continue;
            fx.Active = false; fx.Instances.Clear();
            fx.View.ReleaseIdleTableResources();
            var finish = fx.Finish; fx.Finish = null;
            if (fx.View.Error != null)
            {if(fx.ResourceKey!=null)failedTableResources.Add(fx.ResourceKey);Debug.LogWarning("原生特效已停止：" + fx.View.Error, this);}
            finish?.Invoke(null);
        }
        var fieldState = battle?.Field ?? observedField;
        if (catalog == null || fieldState == null || player == null || player.HasPendingResults || BattlePause.UnscaledTime < nextScan) return;
        nextScan = BattlePause.UnscaledTime + .1f;
        if (player.skillTimeline != null && (player.skillTimeline.tracks & BattleSkillTracks.HitEffects) == 0) { Cancel(-1); return; }
        var live = new HashSet<long>(); var missing = new Dictionary<(long,int,BattleSide), Targets>();
        CollectTableStates(fieldState,live);
        void Visit(FieldPosition field)
        {
            foreach (var status in field.StatusList)
            {
                if (!BattleNativeEffectHooks.TryResolveStatus(status.MainData?.StatusID ?? 0, out var mappedKind) || mappedKind != BattleEffectKind.AmberBurningArea) continue;
                live.Add(status.ApplyOrder);
                bool shown = false;
                foreach (var fx in effects) if (fx.Active && fx.Loop && fx.Instances.ContainsKey(status.ApplyOrder)) { shown = true; break; }
                if (shown) continue;
                var key = (status.SourceEffectExecutionId != 0 ? status.SourceEffectExecutionId : -status.ApplyOrder, status.SourceEntityID, field.Side);
                if (!missing.TryGetValue(key, out var group)) missing.Add(key, group = new Targets { Caster = Card(status.Caster?.Side, status.Caster?.SlotPosition ?? 0) });
                var card = Card(field.Side, field.SlotIndex);
                if (card != null) { group.Instances[status.ApplyOrder] = card; if (!group.Cards.Contains(card)) group.Cards.Add(card); }
            }
        }
        foreach (var field in fieldState.AllySlots) { Visit(field); CollectKaeyaState(field, live, missing); CollectVentiState(field, live, missing); CollectMappedStates(field, live, missing); }
        foreach (var field in fieldState.EnemySlots) { Visit(field); CollectKaeyaState(field, live, missing); CollectVentiState(field, live, missing); CollectMappedStates(field, live, missing); }
        var removed = new List<long>();
        foreach (var fx in effects) if (fx.Active && fx.Loop) foreach (long instance in fx.Instances.Keys) if (!live.Contains(instance)) removed.Add(instance);
        foreach (long instance in removed) Remove(instance);
        foreach (var group in missing.Values) if (group.Cards.Count > 0)
            try { Play(group.Kind, group, -1, null, null); } catch (Exception error) { Debug.LogException(error, this); nextScan = float.PositiveInfinity; }
    }
    public override void Cancel(long playbackID)
    {
        ventiFlip?.Cancel(playbackID);
        foreach (var fx in effects.ToArray())
            if (fx.Active && (playbackID < 0 || (!fx.Loop && fx.Playback == playbackID)))
            {
                fx.View.Stop(true); fx.Active = false; fx.Instances.Clear();
                var finish = fx.Finish; fx.Finish = null; finish?.Invoke(null);
            }
        started.RemoveWhere(key => playbackID < 0 || key.Item1 == playbackID);
    }
    protected override void OnDisable()
    {
        base.OnDisable();
        prepared.Clear(); preparedHits.Clear(); reactionTargets.Clear();
        foreach (var fx in effects) if (fx.View != null) Destroy(fx.View.gameObject);
        effects.Clear(); catalog = null; observedField = null; nextScan = 0; ventiPalette = null;
        failedTableResources.Clear();
        if (retained) { retained = false; BattleNativeEffectHooks.Release(); }
    }
}
