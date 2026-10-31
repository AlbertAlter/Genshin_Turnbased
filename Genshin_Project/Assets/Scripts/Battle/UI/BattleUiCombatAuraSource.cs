using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Only this adapter knows combat entities/events; the aura display consumes IBattleUiAuraSource.</summary>
[AddComponentMenu("Battle UI/Data/Combat Auras")]
public sealed class BattleUiCombatAuraSource : MonoBehaviour, IBattleUiAuraSource
{
    private readonly Dictionary<BattleEntity, ElementAuraDisplayOrder> states = new Dictionary<BattleEntity, ElementAuraDisplayOrder>();
    private IDisposable subscription;
    private void OnEnable() { Subscribe(); BattleEventHub.SessionReset += ResetSession; }
    private void Subscribe()
    {
        subscription?.Dispose();
        subscription = BattleEventHub.Subscribe<AuraChangedEventData>(BattleEventKind.AuraChanged,
            (_, fact) => OnAuraChanged(fact), long.MaxValue, () => isActiveAndEnabled);
    }
    private static bool IsAura(ElementPoolSnapshot pool)
        => (pool.Kind == ReactionPoolKind.NormalAura || pool.Kind == ReactionPoolKind.Intrinsic) && (pool.IsIntrinsic || pool.Amount > 0);
    private void OnAuraChanged(AuraChangedEventData fact)
    {
        if (fact.Subject == null) return;
        if (!states.TryGetValue(fact.Subject, out var state)) states.Add(fact.Subject, state = new ElementAuraDisplayOrder());
        state.Synchronize(fact.Before.Where(IsAura).Select(pool => pool.Element));
        var after = fact.After.Where(IsAura).ToList();
        var ordered = new List<string>();
        foreach (string refreshed in fact.RefreshedPools)
            foreach (var pool in after)
                if (refreshed == pool.Kind + ":" + pool.Element) ordered.Add(pool.Element);
        ordered.AddRange(after.Select(pool => pool.Element));
        state.Synchronize(ordered);
    }
    public IReadOnlyList<string> GetAuras(BattleSide side, int slot)
    {
        if (!isActiveAndEnabled) return null;
        var battle = BattleManager.Instance;
        var entity = battle != null ? battle.GetEntityByPosition(side, slot) : null;
        if (entity == null || !entity.IsAlive) return null;
        if (states.TryGetValue(entity, out var state)) return state.Elements;
        state = new ElementAuraDisplayOrder(); states.Add(entity, state);
        var initial = new List<string>();
        initial.AddRange(ElementQuery.Capture(entity).Where(pool => pool.IsIntrinsic)
            .OrderBy(pool => pool.PoolApplyOrder).Select(pool => pool.Element));
        foreach (var aura in entity.ElementalAuras)
            if (ElementQuery.HasNormalAura(entity, aura.Element)) initial.Add(aura.Element);
        state.Synchronize(initial);
        return state.Elements;
    }
    private void ResetSession() { states.Clear(); Subscribe(); }
    private void OnDisable()
    {
        subscription?.Dispose(); subscription = null;
        BattleEventHub.SessionReset -= ResetSession; states.Clear();
    }
}
