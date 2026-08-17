using System.Collections.Generic;
using System.Linq;

// 场地：敌方5格 + 我方4格
public class BattleField
{
    public const int ALLY_SLOTS = 4;   // 我方位置数（从左往右1-4）
    public const int ENEMY_SLOTS = 5;  // 敌方位置数（从右往左1-5）

    public List<FieldPosition> AllySlots = new List<FieldPosition>();
    public List<FieldPosition> EnemySlots = new List<FieldPosition>();

    public BattleField()
    {
        for (int i = 1; i <= ALLY_SLOTS; i++) AllySlots.Add(new FieldPosition(BattleSide.Ally, i));
        for (int i = 1; i <= ENEMY_SLOTS; i++) EnemySlots.Add(new FieldPosition(BattleSide.Enemy, i));
    }

    public FieldPosition GetSlot(BattleSide side, int slotIndex)
    {
        var list = side == BattleSide.Ally ? AllySlots : EnemySlots;
        return list.FirstOrDefault(p => p.SlotIndex == slotIndex);
    }

    // 该位置范围内所有有人占位的单位
    public List<BattleEntity> GetOccupants(BattleSide side, IEnumerable<int> positions)
    {
        var result = new List<BattleEntity>();
        foreach (var pos in positions)
        {
            var slot = GetSlot(side, pos);
            if (slot != null && slot.IsOccupied) result.Add(slot.Occupant);
        }
        return result;
    }

    // 该位置范围内所有位置（含空位）——EnemyField 目标用
    public List<FieldPosition> GetSlots(BattleSide side, IEnumerable<int> positions)
    {
        var result = new List<FieldPosition>();
        foreach (var pos in positions)
        {
            var slot = GetSlot(side, pos);
            if (slot != null) result.Add(slot);
        }
        return result;
    }
}
