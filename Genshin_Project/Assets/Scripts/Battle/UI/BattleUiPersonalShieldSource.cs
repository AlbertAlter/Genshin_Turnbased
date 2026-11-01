using UnityEngine;

/// <summary>把实际占位者身上的个人盾转交给卡面护盾。盾条和场地盾由各自模块显示。</summary>
[DefaultExecutionOrder(260)]
public sealed class BattleUiPersonalShieldSource : MonoBehaviour
{
    public BattleManager battle;
    public BattleUiShields view;

    private void LateUpdate()
    {
        if (view == null || !view.Ready || view.layout == null) return;
        foreach (var card in view.layout.Cards)
        {
            if (card == null) continue;
            var entity = battle != null && battle.IsBattleRunning && !battle.IsBattleOver
                ? battle.GetEntityByPosition(card.side, card.slot) : null;
            Shield shield = null;
            if (entity != null && entity.IsAlive && entity.IsOnField)
            {
                foreach (var s in entity.Shields)
                {
                    if (s == null || s.Value <= 0 || s.SourceStatus?.IsActive == false) continue;
                    if (s.Kind != ShieldKind.Skill && s.Kind != ShieldKind.Crystallize) continue;
                    if (card.side == BattleSide.Enemy && string.Equals(s.SourceStatus?.MainData?.StatusType,
                        "Shield", System.StringComparison.OrdinalIgnoreCase)) continue;
                    shield = s;
                    break;
                }
            }
            view.SetOccupantShield(card, entity, shield != null, shield?.Element ?? "None", shield);
        }
    }

    private void OnDisable()
    {
        if (view == null || view.layout == null) return;
        foreach (var card in view.layout.Cards)
            if (card != null) view.SetShield(card.side, card.slot, false);
    }
}
