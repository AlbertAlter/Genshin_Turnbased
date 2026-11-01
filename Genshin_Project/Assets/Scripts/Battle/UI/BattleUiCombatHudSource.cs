using System;
using UnityEngine;

/// <summary>The combat boundary for optional HUD views. Does not own or install any visuals.</summary>
[AddComponentMenu("Battle UI/Data/Combat HUD Source")]
public sealed class BattleUiCombatHudSource : MonoBehaviour, IBattleUiHealthSource,
    IBattleUiEnemyShieldSource, IBattleUiApSource, IBattleUiActionSource
{
    public BattleManager battle;
    public BattleInputController input;
    public BattlefieldSwitchController switcher;

    public BattleUiMeterState GetHealth(BattleSide side, int slot)
    {
        var entity = isActiveAndEnabled && battle != null ? battle.GetEntityByPosition(side, slot) : null;
        return entity == null ? default : new BattleUiMeterState {
            Identity = entity, Visible = entity.IsOnField, Current = entity.CurrentHP, Maximum = entity.TotalHP,
            HealthPhases = entity.EnemyCtrl != null ? entity.EnemyCtrl.HealthPhases : default
        };
    }

    public BattleUiMeterState GetEnemyShield(int slot)
    {
        var entity = isActiveAndEnabled && battle != null ? battle.GetEntityByPosition(BattleSide.Enemy, slot) : null;
        if (entity == null || !entity.IsAlive || !entity.IsOnField) return default;
        // Enemy Shield statuses are exclusive. Skill/Crystallize/Overall party shields are not this bar.
        var shield = entity.GetEnemyElementalShield();
        if (shield == null)
            shield = entity.Shields.Find(s => s != null && s.Value > 0 && s.Kind == ShieldKind.Skill
                && s.SourceStatus?.IsActive == true
                && string.Equals(s.SourceStatus.MainData?.StatusType, "Shield", StringComparison.OrdinalIgnoreCase));
        return shield == null ? default : new BattleUiMeterState {
            Identity = shield, Visible = true, Current = shield.Value,
            Maximum = shield.InitialValue, Element = shield.Element
        };
    }

    public BattleUiApState GetAP()
    {
        var ap = isActiveAndEnabled && battle != null ? battle.APManager : null;
        if (ap == null) return default;
        int? cost = GetPreviewCost(battle, input, switcher);
        return new BattleUiApState { Identity = ap, Available = true, Current = ap.CurrentAP,
            Maximum = ap.MaxAP, PreviewDelta = cost.HasValue ? -cost.Value : (int?)null };
    }

    public static int? GetPreviewCost(BattleManager battle, BattleInputController input, BattlefieldSwitchController switcher)
    {
        if (battle == null) return null;
        if (switcher != null && switcher.isActiveAndEnabled && switcher.IsSwitching)
            return battle.CanSwitchActiveAllyTo(switcher.SelectedSlot)
                ? battle.GetSwitchAPCost(switcher.SelectedSlot) : 0;
        if (input == null || !input.IsSelecting || input.PendingSkill < 0) return null;
        var caster = input.PendingAlly != null ? input.PendingAlly : battle.GetActiveAlly();
        // 多处目标仍属于同一次未提交施放，费用预览要一直保留到最终确认。
        if (caster == null || battle.IsResolving) return null;
        return caster.GetBoundSkillAPCost(input.PendingSkill);
    }

    public BattleUiActionState GetAction(int slot, int skillType)
    {
        var ally = isActiveAndEnabled && battle != null
            ? (slot == 0 ? battle.GetActiveAlly() : battle.GetAllyBySlot(slot - 1)) : null;
        if (ally?.Entity?.IsOnField != true) return default;
        return new BattleUiActionState { Identity = ally.Entity, Visible = true,
            Frozen = FrozenReactionHandler.IsFrozen(ally.Entity) || ally.IsButtonFrozen(skillType),
            Cooldown = ally.GetCooldownRemaining(skillType),
            HasAvailableCharge = ally.GetBoundSkillMaxCharge(skillType) > 0
                && ally.GetBoundSkillCurrentCharge(skillType) > 0,
            Icon = ally.GetBoundSkillIcon(skillType) };
    }
}
