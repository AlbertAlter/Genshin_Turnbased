using System;
using System.Collections.Generic;

[Flags]
public enum BattleSkillTracks
{
    None = 0, Animation = 1, Voice = 2, HitEffects = 4, ReactionIcons = 8,
    All = Animation | Voice | HitEffects | ReactionIcons
}

public enum BattleVoiceAnchor { CastStart, FirstLaunch, FirstImpact }
public enum BattleSkillMarker { None, CastStart, Launch, Impact, ElementContact, Reaction, AuraChange, CastEnd }

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
            }
        }
        foreach (var cast in casts.Values)
        {
            var voiceCue = cast.Start;
            if (voice == BattleVoiceAnchor.FirstLaunch) voiceCue = cast.FirstLaunch ?? cast.FirstImpact ?? cast.Start;
            else if (voice == BattleVoiceAnchor.FirstImpact) voiceCue = cast.FirstImpact ?? cast.Start;
            voiceCue.SkillTracks |= enabled & BattleSkillTracks.Voice;
        }
        return cues;
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
