using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Scene adapter for the shared per-effect attacker and per-hit receiver animations. Never settles gameplay.</summary>
[DisallowMultipleComponent]
public sealed class BattlefieldAttackAnimationView : MonoBehaviour, IBattleAttackAnimationView, IBattlePresentationCueBarrier, IBattleVisualCompletion
{
    public BattleUiLayout layout;
    private UICardAttackMotion motion;
    public bool HasPendingVisuals => motion != null && motion.ActiveCount > 0;
    private UIHitShakeController screenShake;
    private BattleUiAnimatedCards cardViews;
    private BattlefieldPresentationPlayer player;
    private readonly Dictionary<(long, long, long, string), long> self = new Dictionary<(long, long, long, string), long>();
    private (long, long, long, string, long)? lastHit;
    private static (long, long, long, string, long) HitKey(long playback, BattlePresentationCue cue)
    {
        var key = BattleAttackAnimationTimeline.PresentationHitKey(cue);
        return (playback,key.Item1,key.Item2,key.Item3,key.Item4);
    }
    public bool CanPresent(long playbackID, BattlePresentationCue cue)
    {
        if (!isActiveAndEnabled || motion == null || cue.AttackAnimation == null) return true;
        if (cue.IsAttackStart) return motion.ActiveCount == 0;
        if (!(cue.Result.Data is DamageEventData damage) || !damage.HitLanded) return true;
        var hit = HitKey(playbackID,cue);
        bool shared = cue.StatusBatchLane != 0 && lastHit == hit;
        if (lastHit != hit && motion.HasActiveHits) return false;
        var r = cue.Result;
        if (!shared && motion.HasHit(Card(r.TargetSide, r.TargetSlot, r.Data.Subject))) return false;
        var spec = cue.AttackAnimation; var execution = BattleAttackAnimationTimeline.ExecutionKey(r);
        return cue.IsAnticipation || !spec.HasScreenShake || screenShake == null || !screenShake.isActiveAndEnabled ||
            screenShake.CanPulseScreen(playbackID, r.Header.SessionId, hit.Item3, hit.Item4,
                cue.StatusBatchLane != 0 ? cue.VisualHitIndex : damage.HitIndex, "AttackScreen/" + spec.EffectID);
    }
    private void OnEnable()
    {
        motion = GetComponent<UICardAttackMotion>() ?? gameObject.AddComponent<UICardAttackMotion>();
        screenShake = GetComponent<UIHitShakeController>() ?? gameObject.AddComponent<UIHitShakeController>();
        if (layout?.Canvas != null) screenShake.BindScreen(layout.Canvas.transform as RectTransform);
        cardViews = GetComponent<BattleUiAnimatedCards>();
        player = GetComponent<BattlefieldPresentationPlayer>(); player?.RegisterView(this);
    }
    private RectTransform Card(BattleSide? side, int slot, BattleEntity expected)
    {
        if (!side.HasValue || layout == null || !layout.TryGetCard(side.Value, slot, out var binding)) return null;
        if (cardViews != null && cardViews.isActiveAndEnabled)
            return cardViews.TryGetCard(binding, expected, out var view) && view.Ready ? view.Appearance.Root : null;
        return binding.root;
    }
    public void Present(long playbackID, BattlePresentationCue cue, Action<Exception> completed)
    {
        try
        {
            var spec = cue.AttackAnimation;
            if (!isActiveAndEnabled || spec == null || motion == null) { completed(null); return; }
            var r = cue.Result; var execution = BattleAttackAnimationTimeline.ExecutionKey(r);
            var key = (playbackID, execution.Item1, execution.Item2, execution.Item3);
            var source = Card(r.SourceSide, r.SourceSlot, r.SourceEntity);
            var target = Card(r.TargetSide, r.TargetSlot, r.Data.Subject);
            float direction = source != null && target != null && source != target
                ? Mathf.Sign(target.position.x - source.position.x) : r.SourceSide == BattleSide.Enemy ? 1 : -1;
            if (direction == 0) direction = r.SourceSide == BattleSide.Enemy ? 1 : -1;
            if (cue.IsAttackStart)
            {
                long handle = motion.PlaySelf(playbackID, source, spec, direction, (float)cue.AttackWindupSeconds,
                    !(r.Data is DamageEventData), error => { self.Remove(key); completed(error); });
                if (handle != 0) self[key] = handle;
                return;
            }
            if (r.Data is DamageEventData damage)
            {
                var hit = HitKey(playbackID,cue);
                bool sharedReceiver = cue.StatusBatchLane != 0 && lastHit == hit && motion.HasHit(target);
                lastHit = hit;
                if (self.TryGetValue(key, out long handle)) motion.Impact(handle);
                if (damage.HitLanded && spec.HasScreenShake && screenShake != null && screenShake.isActiveAndEnabled)
                {
                    var shake = HitShakeStyle.AttackScreen(spec);
                    uint seed = HitEffectMath.CueSeed(unchecked((int)r.Header.SessionId), damage.HitIndex, 0);
                    screenShake.PulseScreen(playbackID, r.Header.SessionId, hit.Item3, hit.Item4,
                        cue.StatusBatchLane != 0 ? cue.VisualHitIndex : damage.HitIndex, shake, seed);
                }
                if (damage.HitLanded && target != null && !sharedReceiver)
                {
                    uint seed = HitEffectMath.CueSeed(unchecked((int)r.Header.SessionId), damage.HitIndex, r.Header.TargetIndex);
                    motion.PlayHit(playbackID, target, spec.HitAnimation, direction, seed, spec.Strength, completed, spec);
                    return;
                }
            }
            completed(null);
        }
        catch (Exception error) { completed(error); }
    }
    public void Cancel(long playbackID)
    {
        if (playbackID < 0 || lastHit?.Item1 == playbackID) lastHit = null;
        motion?.Cancel(playbackID);
        screenShake?.CancelScreen(playbackID);
        foreach (var key in new List<(long, long, long, string)>(self.Keys)) if (playbackID < 0 || key.Item1 == playbackID) self.Remove(key);
    }
    private void OnDisable() { player?.UnregisterView(this); Cancel(-1); }
}
