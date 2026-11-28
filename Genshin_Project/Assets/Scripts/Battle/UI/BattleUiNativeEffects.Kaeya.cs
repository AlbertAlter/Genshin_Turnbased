using System.Collections.Generic;
using UnityEngine;

/// <summary>Actual Kaeya status identities own sustained visuals; real trigger hits own flights.</summary>
public sealed partial class BattleUiNativeEffects
{
    private static bool IsKaeyaOrbitStatus(string id) => id == "ST_Burst_Kaeya" || id == "ST_Kaeya_C6";
    private void ConfigureKaeyaState(Targets group, StatusEventData added)
    {
        if (!IsKaeyaOrbitStatus(added.StatusId)) throw new System.InvalidOperationException("凯亚环绕钩子只能绑定凛冽轮舞的实际状态实例。");
        var card = Card(added.HostSide, added.HostSlot);
        group.Cards.Clear(); group.Instances.Clear();
        if (card != null) { group.Cards.Add(card); group.Instances.Add(added.InstanceId, card); }
        group.Caster = card; group.Kind = BattleEffectKind.KaeyaGlacialWaltz;
        group.IcicleCount = added.StatusId == "ST_Kaeya_C6" ? 4 : 3;
        if (added.Host is BattleEntity host && host.Position != null) observedField = host.Position.OwnerField;
    }
    private void BindKaeyaDeparture(Fx shot, Targets targets, BattlePresentationCue cue, float launchDelaySeconds)
    {
        long source = (cue?.Result.Data as DamageEventData)?.Source?.SourceStatusApplyOrder ?? 0;
        foreach (var state in effects)
        {
            if (!state.Active || !state.Loop || state.Kind != BattleEffectKind.KaeyaGlacialWaltz) continue;
            bool matches = source != 0 ? state.Instances.ContainsKey(source) : state.Instances.ContainsValue(targets.Caster);
            if (!matches || targets.Cards.Count != 1) continue;
            if (state.View.TryCaptureKaeyaDeparture(targets.Cards[0], out var departure, launchDelaySeconds)) shot.View.SetKaeyaDeparture(departure);
            return;
        }
    }
    private void CollectKaeyaState(FieldPosition field, HashSet<long> live, Dictionary<(long,int,BattleSide), Targets> missing)
    {
        var owner = field.Occupant;
        if (owner?.IsOnField != true) return;
        foreach (var status in owner.StatusDict.Values)
        {
            if (!status.IsActive || !IsKaeyaOrbitStatus(status.StatusID2) ||
                !BattleNativeEffectHooks.TryResolveStatus(status.MainData?.StatusID ?? 0, out var kind) || kind != BattleEffectKind.KaeyaGlacialWaltz) continue;
            live.Add(status.ApplyOrder);
            bool shown = false;
            foreach (var effect in effects)
                if (effect.Active && effect.Loop && effect.Instances.ContainsKey(status.ApplyOrder)) { shown = true; break; }
            if (shown) continue;
            var card = Card(field.Side, field.SlotIndex);
            if (card == null) continue;
            var group = new Targets { Caster = card, Kind = BattleEffectKind.KaeyaGlacialWaltz,
                IcicleCount = status.StatusID2 == "ST_Kaeya_C6" ? 4 : 3 };
            group.Cards.Add(card); group.Instances.Add(status.ApplyOrder, card);
            missing.Add((-status.ApplyOrder, status.SourceEntityID, field.Side), group);
        }
    }
}
