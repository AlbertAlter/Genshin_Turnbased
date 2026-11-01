using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Adapts scheduled real hit results to the independent hit renderer. Never deals damage.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Hit Result Adapter")]
public sealed class BattleUiHitResultView : BattleSkillTrackBehaviour, IBattleSkillHitEffectTrack
{
    public BattleUiLayout layout;
    public BattleUiHitEffects effects;
    private sealed class Pending { public long Id; public Coroutine Routine; public Action<Exception> Finish; }
    private readonly List<Pending> pending = new List<Pending>();

    public override void Present(long playbackID, BattlePresentationCue cue, Action<Exception> completed)
    {
        if (!isActiveAndEnabled || !(cue.Result.Data is DamageEventData damage) || !damage.HitLanded ||
            (cue.Timing.HitTemplate == null && damage.DamageType != "Normal" && damage.DamageType != "Heavy") ||
            effects == null || !effects.isActiveAndEnabled || layout == null)
        { completed(null); return; }
        if (effects.Ready) { Draw(cue, completed); return; }
        var item = new Pending { Id = playbackID, Finish = completed }; pending.Add(item);
        item.Routine = StartCoroutine(WaitForAssets(item, cue));
    }
    private IEnumerator WaitForAssets(Pending item, BattlePresentationCue cue)
    {
        float deadline = Time.realtimeSinceStartup + 20;
        while (effects != null && effects.isActiveAndEnabled && !effects.Ready && effects.Error == null &&
               Time.realtimeSinceStartup < deadline) yield return null;
        pending.Remove(item);
        if (effects != null && effects.isActiveAndEnabled && effects.Ready) Draw(cue, item.Finish);
        else
        {
            Debug.LogWarning("命中特效未就绪，本次不播放：" + effects?.Error, this);
            item.Finish(null);
        }
    }
    private void Draw(BattlePresentationCue cue, Action<Exception> completed)
    {
        try
        {
            var damage = (DamageEventData)cue.Result.Data;
            var source = damage.Source;
            var template = cue.Timing.HitTemplate;
            bool enemy = source.SourceSide == BattleSide.Enemy;
            if (enemy && (source.SourceKind != ReactionSourceKind.EnemySkill || template == null)) { completed(null); return; }
            int weaponValue = enemy ? template.WeaponType : damage.SourceWeaponType;
            if (weaponValue < 0 || weaponValue > 4) { completed(null); return; }
            var weapon = (HitEffectWeapon)weaponValue; bool heavy = (enemy ? template.AttackType : damage.DamageType) == "Heavy";
            bool grouped = effects.GetPlaybackMode(weapon, heavy) == HitEffectTextureStyle.PlaybackMode.PerHitGroup;
            if (grouped && cue.HitResults != null && !cue.LeadsHit) { completed(null); return; }
            var targets = new List<BattleUiCardBinding>();
            void Add(BattlePresentationResult r)
            {
                if (r.TargetSide.HasValue && layout.TryGetCard(r.TargetSide.Value, r.TargetSlot, out var target)) targets.Add(target);
            }
            if (grouped && cue.HitResults != null) foreach (var result in cue.HitResults) Add(result);
            else Add(cue.Result);
            BattleUiCardBinding from = null;
            if (source.SourceEntity != null)
                layout.TryGetCard(source.SourceSide, source.SourceEntity.SlotPosition, out from);
            var header = cue.Result.Header;
            uint seed = HitEffectMath.Mix(unchecked((uint)header.SessionId ^ (uint)header.HitId ^ (uint)header.EventId));
            if (cue.Timing.HitTemplate != null)
                effects.PlayScheduledHit(weapon, heavy, damage.Element, from, targets, cue.VisualHitIndex, cue.VisualHitCount,
                    seed, cue.IsAnticipation, (float)(cue.IsAnticipation ? cue.ProjectileSeconds : cue.Timing.HitTemplate.ProjectileSeconds),
                    header.SessionId, header.EventId);
            else effects.PlayHit(weapon, heavy, damage.Element, from, targets, cue.VisualHitIndex, cue.VisualHitCount, seed);
        }
        catch (Exception error) { completed(error); return; }
        completed(null);
    }
    public override void Cancel(long playbackID)
    {
        foreach (var item in pending.ToArray())
            if (playbackID < 0 || item.Id == playbackID)
            {
                if (item.Routine != null) StopCoroutine(item.Routine);
                pending.Remove(item); item.Finish(null);
            }
        effects?.Clear();
    }
}
