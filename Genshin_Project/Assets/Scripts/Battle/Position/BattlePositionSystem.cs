using System.Collections.Generic;

// ============================================================
// 位置系统（2026-08-06）
// 规则：
//   我方 4 个位置，在右侧，从左往右为 1-4
//   敌方 5 个位置，从右往左为 1-5
//   例：安柏初始位置=1（我方最左），丘丘人初始位置=3（敌方中间）
// 用途：
//   状态效果 BindStatus 的 TargetSelect "1,1" 需要计算相邻位置，
//   通过 GetAdjacentPositions 获取原点左右各扩展 range 的位置编号。
// ============================================================

public enum BattleSide
{
    Ally = 0,   // 我方
    Enemy = 1   // 敌方
}

public static class BattlePositionSystem
{
    // 我方最多 4 人
    public const int AllyCount = 4;
    // 敌方最多 5 人
    public const int EnemyCount = 5;

    /// <summary>
    /// 获取该阵营的位置上限
    /// </summary>
    public static int GetSideCount(BattleSide side)
    {
        return side == BattleSide.Ally ? AllyCount : EnemyCount;
    }

    /// <summary>
    /// 位置编号是否合法（1 ~ 阵营上限）
    /// </summary>
    public static bool IsValidPosition(BattleSide side, int position)
    {
        return position >= 1 && position <= GetSideCount(side);
    }

    /// <summary>
    /// 以原点位置为中心，向左右各扩展 range 位，返回该阵营内合法的相邻位置集合（含原点）
    /// 例：敌方 position=3, range=1 → [2,3,4]
    /// </summary>
    public static List<int> GetAdjacentPositions(BattleSide side, int position, int range)
    {
        var result = new List<int>();
        if (!IsValidPosition(side, position)) return result;
        for (int p = position - range; p <= position + range; p++)
        {
            if (IsValidPosition(side, p))
                result.Add(p);
        }
        return result;
    }
}
