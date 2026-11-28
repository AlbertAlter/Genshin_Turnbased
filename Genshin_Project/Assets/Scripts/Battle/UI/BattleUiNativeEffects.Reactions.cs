using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class BattleUiNativeEffects
{
    private readonly Dictionary<long, Dictionary<(long Session, long Hit, ReactionType Reaction, string Causes), List<BattlePresentationResult>>> reactionTargets
        = new Dictionary<long, Dictionary<(long, long, ReactionType, string), List<BattlePresentationResult>>>();
    private static string ReactionGroupName((long Session, long Hit, ReactionType Reaction, string Causes) key)
        => "Reaction." + key.Reaction + "/" + key.Causes;
    private void PrepareReactions(BattlePresentationSequence sequence)
        => reactionTargets[sequence.ID] = BattleReactionEffectPlan.Group(sequence.Results);
    private void ReleaseReactions(long sequenceID)
    {
        if (!reactionTargets.TryGetValue(sequenceID, out var groups)) return;
        foreach (var key in groups.Keys)
            started.RemoveWhere(x => x.Item2 == key.Session && x.Item3 == 0 && x.Item4 == ReactionGroupName(key) && x.Item5 == key.Hit);
        reactionTargets.Remove(sequenceID);
    }
    private static (long, long, string, long) NativeHitKey(BattlePresentationCue cue)
    {
        if (!BattleReactionEffectPlan.TryGetReaction(cue.Result, out _)) return BattleAttackAnimationTimeline.PresentationHitKey(cue);
        var key = BattleReactionEffectPlan.Key(cue.Result);
        return (key.Session, 0, ReactionGroupName(key), key.Hit);
    }
    private bool TryPresentReaction(long playbackID, BattlePresentationCue cue, BattleEffectKind kind, Action<Exception> completed)
    {
        if (!BattleReactionEffectPlan.TryGetReaction(cue.Result, out _)) return false;
        if (cue.IsAttackStart || cue.IsAnticipation || !((DamageEventData)cue.Result.Data).HitLanded)
        { completed(null); return true; }
        var key = BattleReactionEffectPlan.Key(cue.Result);
        var once = (playbackID, key.Session, 0L, ReactionGroupName(key), key.Hit, kind.ToString());
        if (!started.Add(once)) { completed(null); return true; }
        IReadOnlyList<BattlePresentationResult> records = null;
        foreach (var sequence in reactionTargets.Values) if (sequence.TryGetValue(key, out var group)) { records = group; break; }
        var targets = new Targets { Caster = Card(cue.Result.SourceSide, cue.Result.SourceSlot) };
        void Add(BattlePresentationResult result)
        {
            if (!BattleReactionEffectPlan.TryGetReaction(result, out _) || BattleReactionEffectPlan.Key(result) != key ||
                !((DamageEventData)result.Data).HitLanded) return;
            var card = Card(result.TargetSide, result.TargetSlot);
            if (card != null && !targets.Cards.Contains(card)) targets.Cards.Add(card);
        }
        if (records != null) foreach (var result in records) Add(result);
        else if (cue.HitResults != null) foreach (var result in cue.HitResults) Add(result);
        else Add(cue.Result);
        if (targets.Cards.Count == 0) { completed(null); return true; }
        Play(kind, targets, playbackID, cue, completed);
        return true;
    }
}
