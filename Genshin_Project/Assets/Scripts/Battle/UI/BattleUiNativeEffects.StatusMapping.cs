using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class BattleUiNativeEffects
{
    // Existing Amber/Kaeya/Venti adapters retain their grouping and trigger semantics.
    private static bool IsMappedHolderEffect(int statusID, out BattleEffectKind kind)
        => BattleNativeEffectHooks.TryResolveStatus(statusID, out kind) && kind == BattleEffectKind.DandelionWindOrbit;
    private RectTransform StatusHostCard(object host, BattleSide? side, int slot)
    {
        if (host is BattleEntity entity) return entity.IsOnField ? Card(side, slot) : null;
        if (host is FieldPosition && side.HasValue && layout != null && layout.TryGetCard(side.Value, slot, out var binding)) return binding.root;
        return null; // A team host has no individual card; never substitute the caster.
    }
    private bool HasMappedState(long instance, RectTransform currentCard)
    {
        foreach (var fx in effects)
        {
            if (!fx.Active || !fx.Loop || !fx.Instances.TryGetValue(instance, out var oldCard)) continue;
            if (oldCard != currentCard)
            {
                fx.Instances[instance] = currentCard;
                fx.View.SetTargets(new[] { currentCard });
            }
            return true;
        }
        return false;
    }
    private bool TryPresentMappedState(long playbackID, BattlePresentationCue cue, Action<Exception> completed)
    {
        if (!isActiveAndEnabled || (cue.SkillTracks & BattleSkillTracks.HitEffects) == 0 || cue.IsAnticipation || cue.IsAttackStart || cue.IsEnemyExit ||
            !(cue.Result.Data is StatusEventData status) || status.Kind != BattleEventKind.StatusAdded ||
            !IsMappedHolderEffect(status.StatusNumericId, out var kind)) return false;
        if (status.Host is BattleEntity entity && entity.Position != null) observedField = entity.Position.OwnerField;
        var card = StatusHostCard(status.Host, status.HostSide, status.HostSlot);
        if (card != null && !HasMappedState(status.InstanceId, card))
        {
            var group = new Targets { Kind = kind };
            group.Cards.Add(card); group.Instances.Add(status.InstanceId, card);
            Play(kind, group, playbackID, cue, null);
        }
        completed(null); return true;
    }
    private void CollectMappedStates(FieldPosition field, HashSet<long> live, Dictionary<(long,int,BattleSide), Targets> missing)
    {
        void Collect(StatusInstance status, object host)
        {
            if (!status.IsActive || !IsMappedHolderEffect(status.MainData?.StatusID ?? 0, out var kind)) return;
            var card = StatusHostCard(host, field.Side, field.SlotIndex);
            if (card == null) return;
            live.Add(status.ApplyOrder);
            if (HasMappedState(status.ApplyOrder, card)) return;
            var group = new Targets { Kind = kind };
            group.Cards.Add(card); group.Instances.Add(status.ApplyOrder, card);
            missing.Add((-status.ApplyOrder, status.SourceEntityID, field.Side), group);
        }
        foreach (var status in field.StatusList) Collect(status, field);
        if (field.Occupant?.IsOnField == true)
            foreach (var status in field.Occupant.StatusDict.Values) Collect(status, field.Occupant);
    }
}
