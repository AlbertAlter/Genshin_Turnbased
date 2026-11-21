using UnityEngine;

/// <summary>把实际占位者身上的个人盾转交给卡面护盾。盾条和场地盾由各自模块显示。</summary>
[DefaultExecutionOrder(260)]
public sealed class BattleUiPersonalShieldSource : MonoBehaviour
{
    public BattleManager battle;
    public BattleUiShields view;
    private System.IDisposable shieldChanges;

    private void OnEnable()
    {
        BattleEventHub.SessionReset += SubscribeShieldChanges;
        SubscribeShieldChanges();
    }

    private void SubscribeShieldChanges()
    {
        shieldChanges?.Dispose();
        shieldChanges = BattleEventHub.Subscribe<ShieldChangedEventData>(BattleEventKind.ShieldChanged,
            (_, change) => OnShieldChanged(change), 0, () => isActiveAndEnabled);
    }

    private void OnShieldChanged(ShieldChangedEventData change)
    {
        if (change.Reason != "Absorption" || view == null || !view.Ready || view.layout == null || battle == null) return;
        var entity = change.Subject;
        if (entity == null || !ReferenceEquals(battle.GetEntityByPosition(entity.Side, entity.SlotPosition), entity)
            || DisplayShield(entity) != null || !view.layout.TryGetCard(entity.Side, entity.SlotPosition, out var card)) return;
        // A depleted shield has already stopped protecting HP in this hit. Remove its membrane
        // with the same committed result; expiry/removal still uses the configured fade.
        view.SetOccupantShield(card, entity, false, "None", immediate: true);
    }

    private static Shield DisplayShield(BattleEntity entity)
    {
        if (entity?.IsAlive != true || !entity.IsOnField) return null;
        foreach (var shield in entity.Shields)
        {
            if (shield == null || shield.Value <= 0 || shield.SourceStatus?.IsActive == false) continue;
            if (shield.Kind != ShieldKind.Skill && shield.Kind != ShieldKind.Crystallize) continue;
            if (entity.Side == BattleSide.Enemy && string.Equals(shield.SourceStatus?.MainData?.StatusType,
                "Shield", System.StringComparison.OrdinalIgnoreCase)) continue;
            return shield;
        }
        return null;
    }

    private void LateUpdate()
    {
        if (view == null || !view.Ready || view.layout == null) return;
        foreach (var card in view.layout.Cards)
        {
            if (card == null) continue;
            var entity = battle != null && battle.IsBattleRunning && !battle.IsBattleOver
                ? battle.GetEntityByPosition(card.side, card.slot) : null;
            Shield shield = DisplayShield(entity);
            view.SetOccupantShield(card, entity, shield != null, shield?.Element ?? "None", shield);
        }
    }

    private void OnDisable()
    {
        BattleEventHub.SessionReset -= SubscribeShieldChanges;
        shieldChanges?.Dispose();
        shieldChanges = null;
        if (view == null || view.layout == null) return;
        foreach (var card in view.layout.Cards)
            if (card != null) view.SetShield(card.side, card.slot, false);
    }
}
