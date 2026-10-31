using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional preview source. No BattleManager or combat initialization is required.</summary>
[AddComponentMenu("Battle UI/Data/Manual Auras")]
public sealed class BattleUiManualAuraSource : MonoBehaviour, IBattleUiAuraSource
{
    [Serializable] public sealed class Slot
    {
        public BattleSide side;
        public int slot = 1;
        public string[] elements = Array.Empty<string>();
    }
    public Slot[] slots = Array.Empty<Slot>();
    public IReadOnlyList<string> GetAuras(BattleSide side, int slot)
    {
        if (!isActiveAndEnabled) return null;
        foreach (var item in slots) if (item != null && item.side == side && item.slot == slot) return item.elements;
        return null;
    }
}
