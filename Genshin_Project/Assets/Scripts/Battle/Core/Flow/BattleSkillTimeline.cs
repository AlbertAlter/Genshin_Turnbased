using System;
using System.Collections.Generic;

[Flags]
public enum BattleSkillTracks
{
    None = 0, Animation = 1, Voice = 2, HitEffects = 4, ReactionIcons = 8,
    All = Animation | Voice | HitEffects | ReactionIcons
}

public enum BattleVoiceAnchor { CastStart, FirstLaunch, FirstImpact }
public enum BattleSkillMarker { None, CastStart, Launch, Impact, ElementContact, Reaction, AuraChange, CastEnd, ActionPrompt }

// All tracks share the player's Present/Cancel lifecycle. The contracts select which cues reach each module.
public interface IBattleSkillTrack : IBattlePresentationView { }
public interface IBattleSkillAnimationTrack : IBattleSkillTrack { }
public interface IBattleSkillVoiceTrack : IBattleSkillTrack { }
public interface ICharacterVoiceSequenceView : IBattleSkillVoiceTrack
{
    void PrepareVoices(IReadOnlyList<BattlePresentationCue> cues);
}
public interface IBattleSkillHitEffectTrack : IBattleSkillTrack { }
public interface IBattleSkillReactionIconTrack : IBattleSkillTrack { }
public interface IBattleEnemyExitView : IBattlePresentationView { bool CanPresentExit(BattlePresentationCue cue); }
public interface IBattleEnemyExitBarrier { bool HasPendingHits(BattlePresentationCue exit); }

/// <summary>Bind already scheduled results to real casts. No clocks, duration estimates or battle mutations.</summary>
public static class BattleSkillTimeline
{
    private sealed class Cast
    {
        public BattlePresentationCue Start;
        public BattlePresentationCue FirstLaunch;
        public BattlePresentationCue FirstImpact;
    }

    public static IReadOnlyList<BattlePresentationCue> Build(BattlePresentationSequence sequence,
        Func<BattlePresentationResult, BattlePresentationTiming> timing, BattleSkillTracks enabled, BattleVoiceAnchor voice)
    {
        var cues = BattlePresentationTimeline.Build(sequence, timing);
        var casts = new Dictionary<(long, long), Cast>();
        foreach (var cue in cues)
        {
            var result = cue.Result;
            cue.SkillMarker = Marker(cue);
            var key = (result.Header.SessionId, result.Header.ActionExecutionId);
            if (cue.SkillMarker == BattleSkillMarker.CastStart && key.Item2 != 0)
                casts.Add(key, new Cast { Start = cue });

            casts.TryGetValue(key, out var cast);
            cue.CastStart = cast?.Start.Result;
            switch (cue.SkillMarker)
            {
                case BattleSkillMarker.ActionPrompt:
                    cue.SkillTracks = enabled & BattleSkillTracks.Animation;
                    break;
                case BattleSkillMarker.CastStart:
                case BattleSkillMarker.CastEnd:
                    if (cast != null) cue.SkillTracks = enabled & BattleSkillTracks.Animation;
                    break;
                case BattleSkillMarker.Launch:
                case BattleSkillMarker.Impact:
                    cue.SkillTracks = enabled & BattleSkillTracks.HitEffects;
                    if (cast == null || !cue.LeadsHit || !IsCastDamage(cast.Start.Result, result)) break;
                    cue.SkillTracks |= enabled & BattleSkillTracks.Animation;
                    if (cue.IsAnticipation) cast.FirstLaunch ??= cue;
                    else cast.FirstImpact ??= cue;
                    break;
                case BattleSkillMarker.ElementContact:
                case BattleSkillMarker.Reaction:
                case BattleSkillMarker.AuraChange:
                    cue.SkillTracks = enabled & BattleSkillTracks.ReactionIcons;
                    break;
                case BattleSkillMarker.None:
                    // Persistent VFX consume the committed lifecycle, including empty field slots.
                    if (result.Data is StatusEventData) cue.SkillTracks = enabled & BattleSkillTracks.HitEffects;
                    break;
            }
        }
        foreach (var cast in casts.Values)
        {
            var voiceCue = cast.Start;
            if (voice == BattleVoiceAnchor.FirstLaunch) voiceCue = cast.FirstLaunch ?? cast.FirstImpact ?? cast.Start;
            else if (voice == BattleVoiceAnchor.FirstImpact) voiceCue = cast.FirstImpact ?? cast.Start;
            voiceCue.SkillTracks |= enabled & BattleSkillTracks.Voice;
        }
        return ScheduleEnemyExits(cues, casts);
    }

    // Keep the Death declaration at its original position. Only its card visual gets a later block.
    private static IReadOnlyList<BattlePresentationCue> ScheduleEnemyExits(IReadOnlyList<BattlePresentationCue> cues,
        Dictionary<(long, long), Cast> casts)
    {
        Dictionary<(long, long, long, long), List<BattlePresentationCue>> deaths = null;
        (long, long, long, long) Scope(BattlePresentationCue cue)
        {
            var h = cue.Result.Header;
            if (h.ActionExecutionId != 0) return (h.SessionId, h.ActionExecutionId, 0, 0);
            if (h.EffectExecutionId != 0) return (h.SessionId, 0, h.EffectExecutionId, 0);
            return (h.SessionId, 0, 0, h.HitId != 0 ? h.HitId : -h.EventId);
        }
        foreach (var cue in cues)
        {
            if (!(cue.Result.Data is DeathEventData) || cue.Result.TargetSide != BattleSide.Enemy) continue;
            deaths ??= new Dictionary<(long, long, long, long), List<BattlePresentationCue>>();
            var key = Scope(cue);
            if (!deaths.TryGetValue(key, out var batch)) deaths.Add(key, batch = new List<BattlePresentationCue>());
            batch.Add(cue);
        }
        if (deaths == null) return cues;

        foreach (var cue in cues)
            if (cue.Result.Data is DamageEventData && deaths.TryGetValue(Scope(cue), out var group))
                cue.EnemyExitGroup = group[0].Result;
        var exits = new Dictionary<int, List<BattlePresentationCue>>();
        for (int i = cues.Count - 1; i >= 0 && deaths.Count > 0; i--)
        {
            var cue = cues[i];
            if (!(cue.Result.Data is DamageEventData) && !(cue.Result.Data is DeathEventData)
                && cue.SkillMarker != BattleSkillMarker.CastEnd) continue;
            var key = Scope(cue);
            if (!deaths.TryGetValue(key, out var batch)) continue;
            deaths.Remove(key);
            foreach (var death in batch) death.EnemyExitGroup = batch[0].Result;
            if (!exits.TryGetValue(cue.Block, out var group)) exits.Add(cue.Block, group = new List<BattlePresentationCue>());
            group.AddRange(batch);
        }
        var scheduled = new List<BattlePresentationCue>(cues.Count + exits.Count);
        int addedBlocks = 0;
        for (int i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            int block = cue.Block;
            cue.Block += addedBlocks;
            scheduled.Add(cue);
            if ((i + 1 < cues.Count && cues[i + 1].Block == block) || !exits.TryGetValue(block, out var batch)) continue;
            addedBlocks++;
            foreach (var death in batch)
            {
                // A final blow legitimately omits the gameplay ActionCompleted declaration.
                var h = death.Result.Header;
                double time = casts.TryGetValue((h.SessionId, h.ActionExecutionId), out var cast)
                    ? Math.Max(cue.Time, cast.Start.Time + cast.Start.Timing.AnimationSeconds) : cue.Time;
                scheduled.Add(new BattlePresentationCue(death.Result, time, death.Timing, block + addedBlocks) {
                    IsEnemyExit = true, EnemyExitGroup = death.EnemyExitGroup
                });
            }
        }
        return scheduled;
    }

    private static bool IsCastDamage(BattlePresentationResult start, BattlePresentationResult result)
    {
        var action = (ActionEventData)start.Data;
        var source = ((DamageEventData)result.Data).Source;
        bool fromSkill = source.SourceKind == ReactionSourceKind.CharacterSkill || source.SourceKind == ReactionSourceKind.EnemySkill;
        return fromSkill && source.HasSourceEntity && source.SourceEntityID == action.SubjectId && source.SourceSide == start.TargetSide
            && source.SourceSkillID == action.SkillId;
    }

    private static BattleSkillMarker Marker(BattlePresentationCue cue)
    {
        if (cue.IsAnticipation) return BattleSkillMarker.Launch;
        switch (cue.Result.Data.Kind)
        {
            case BattleEventKind.ActionPrompt: return BattleSkillMarker.ActionPrompt;
            case BattleEventKind.ActionStarted: return BattleSkillMarker.CastStart;
            case BattleEventKind.ActionCompleted: return BattleSkillMarker.CastEnd;
            case BattleEventKind.DamageResolved:
                return ((DamageEventData)cue.Result.Data).HitLanded ? BattleSkillMarker.Impact : BattleSkillMarker.None;
            case BattleEventKind.ElementContactResolved: return BattleSkillMarker.ElementContact;
            case BattleEventKind.ReactionOccurred: return BattleSkillMarker.Reaction;
            case BattleEventKind.AuraChanged: return BattleSkillMarker.AuraChange;
            default: return BattleSkillMarker.None;
        }
    }

    public static bool Receives(IBattlePresentationView view, BattlePresentationCue cue)
    {
        if (cue.IsAttackStart) return view is IBattleAttackAnimationView;
        if (cue.IsEnemyExit || view is IBattleEnemyExitView) return cue.IsEnemyExit && view is IBattleEnemyExitView;
        if (view is ICharacterVoiceSequenceView) return cue.CharacterVoicePath != null;
        if (!(view is IBattleSkillTrack))
            return !cue.IsAnticipation || view is IBattlePresentationAnticipationView;
        var tracks = cue.SkillTracks;
        return (view is IBattleSkillAnimationTrack && (tracks & BattleSkillTracks.Animation) != 0)
            || (view is IBattleSkillVoiceTrack && (tracks & BattleSkillTracks.Voice) != 0)
            || (view is IBattleSkillHitEffectTrack && (tracks & BattleSkillTracks.HitEffects) != 0)
            || (view is IBattleSkillReactionIconTrack && (tracks & BattleSkillTracks.ReactionIcons) != 0);
    }
}
