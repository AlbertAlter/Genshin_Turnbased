using System.Collections.Generic;

// 场地位置：场地上的一个格子
// 位置可以挂状态（EnemyField 目标），也可以站单位（占位）
public class FieldPosition
{
    public BattleField OwnerField;
    public BattleSide Side;          // 所属阵营
    public int SlotIndex;            // 位置编号（我方从右往左1-4，敌方从右往左1-5）
    public BattleEntity Occupant;    // 当前站着的单位（可空）
    public List<StatusInstance> StatusList = new List<StatusInstance>(); // 挂在该位置上的状态

    public FieldPosition(BattleField ownerField, BattleSide side, int slot)
    {
        OwnerField = ownerField;
        Side = side;
        SlotIndex = slot;
    }

    public bool IsOccupied => Occupant != null && !Occupant.IsDead;

    public override string ToString()
    {
        return $"{Side}:{SlotIndex}({(IsOccupied ? Occupant.EntityID.ToString() : "空")})";
    }
}
