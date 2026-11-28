using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class BattleUiNativeEffects
{
    private UICardAirborneFlip ventiFlip;
    private ElementColorPalette ventiPalette;
    private Color? VentiElementColor(string element)
        => string.IsNullOrEmpty(element) ? (Color?)null : (ventiPalette ??= ElementColorPalette.Load()).GetColor(element);
    private void BindVentiAppearance(Fx effect, Targets targets, BattlePresentationCue cue)
    {
        var visual = (cue?.Result.Data as DamageEventData)?.VentiBurstVisual;
        effect.VentiBurstInstance = visual?.InstanceId ?? targets.VentiBurstInstance;
        effect.View.SetVentiAbsorbedColor(VentiElementColor(visual != null ? visual.Element : targets.VentiElement));
    }
    private void PresentVentiConversion(BattlePresentationCue cue)
    {
        if (cue.IsAnticipation || cue.IsAttackStart) return;
        var visual = (cue.Result.Data as DamageEventData)?.VentiBurstVisual;
        if (visual?.Element == null) return;
        Color? color = VentiElementColor(visual.Element);
        foreach (var effect in effects)
            if (effect.Active && effect.Loop && effect.Kind == BattleEffectKind.VentiStormEye
                && effect.VentiBurstInstance == visual.InstanceId)
                effect.View.SetVentiAbsorbedColor(color);
    }
    private void ConfigureVentiState(Targets group, StatusEventData added)
    {
        if (added.StatusId != VentiScriptHooks.BurstID) throw new InvalidOperationException("暴风之眼需要绑定实际爆发状态。");
        var card = Card(added.HostSide, added.HostSlot);
        group.Cards.Clear(); group.Instances.Clear(); group.Caster = card; group.Kind = BattleEffectKind.VentiStormEye;
        group.VentiBurstInstance = added.InstanceId; group.VentiElement = null;
        if (card != null) { group.Cards.Add(card); group.Instances.Add(added.InstanceId, card); }
        if (added.Host is BattleEntity owner && owner.Position != null) observedField = owner.Position.OwnerField;
    }
    private void BindVentiPhase(Fx pulse, Targets targets)
    {
        foreach (var state in effects)
            if (state.Active && state.Loop && state.Kind == BattleEffectKind.VentiStormEye
                && (pulse.VentiBurstInstance != 0 ? state.Instances.ContainsKey(pulse.VentiBurstInstance) : state.Instances.ContainsValue(targets.Caster)))
            {
                float eyePeriod = BattleNativeEffectHooks.Parameters(BattleEffectKind.VentiStormEye)["OrbitSeconds"];
                float pulsePeriod = BattleNativeEffectHooks.Parameters(BattleEffectKind.VentiStormPulse)["OrbitSeconds"];
                pulse.View.SetVentiPhase(state.View.Elapsed * pulsePeriod / eyePeriod); return;
            }
        pulse.View.SetVentiPhase(0);
    }
    private bool TryPresentVentiFlip(long playbackID, BattlePresentationCue cue, Action<Exception> completed)
    {
        if (!IsVentiAirborneCue(cue)) return false;
        var added = (StatusEventData)cue.Result.Data;
        var root = Card(added.HostSide, added.HostSlot);
        var view = root != null ? root.GetComponent<UICardView>() : null;
        if (view == null) { Debug.LogWarning("击飞目标缺少公共完整卡牌，未播放翻面。", this); completed(null); return true; }
        if (ventiFlip == null) ventiFlip = GetComponent<UICardAirborneFlip>() ?? gameObject.AddComponent<UICardAirborneFlip>();
        var parameters = BattleNativeEffectHooks.Parameters(BattleEffectKind.VentiUpdraft);
        ventiFlip.Play(playbackID, view, parameters["FlipSeconds"], parameters["LiftHeightRatio"], completed);
        return true;
    }
    public static bool IsVentiAirborneCue(BattlePresentationCue cue)
    {
        if (cue == null) return false;
        if (cue.IsAttackStart || cue.IsAnticipation || (cue.SkillTracks & BattleSkillTracks.HitEffects) == 0 ||
            cue.AttackAnimation?.AnimationHook != "Venti.AirborneFlip" ||
            !(cue.Result.Data is StatusEventData added) || added.Kind != BattleEventKind.StatusAdded ||
            added.StatusId != AirborneStatusHandler.EnemyStatusID) return false;
        return true;
    }
    private void CollectVentiState(FieldPosition field, HashSet<long> live, Dictionary<(long,int,BattleSide), Targets> missing)
    {
        var owner = field.Occupant;
        if (owner?.IsOnField != true) return;
        foreach (var status in owner.StatusDict.Values)
        {
            if (!status.IsActive || status.StatusID2 != VentiScriptHooks.BurstID ||
                !BattleNativeEffectHooks.TryResolveStatus(status.MainData?.StatusID ?? 0, out var kind) || kind != BattleEffectKind.VentiStormEye) continue;
            live.Add(status.ApplyOrder);
            bool shown = false;
            foreach (var effect in effects)
                if (effect.Active && effect.Loop && effect.Kind == BattleEffectKind.VentiStormEye && effect.Instances.ContainsKey(status.ApplyOrder))
                {
                    effect.View.SetVentiAbsorbedColor(VentiElementColor(VentiScriptHooks.GetAbsorbedElement(status)));
                    shown = true; break;
                }
            if (shown) continue;
            var card = Card(field.Side, field.SlotIndex); if (card == null) continue;
            var group = new Targets { Caster = card, Kind = BattleEffectKind.VentiStormEye,
                VentiBurstInstance = status.ApplyOrder, VentiElement = VentiScriptHooks.GetAbsorbedElement(status) };
            group.Cards.Add(card); group.Instances.Add(status.ApplyOrder, card);
            missing.Add((-status.ApplyOrder, status.SourceEntityID, field.Side), group);
        }
    }
}
