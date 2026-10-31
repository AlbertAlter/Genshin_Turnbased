using System;
using System.Collections.Generic;

/// <summary>敌人固有特性只读查询。身份与免疫来自总表，不从名字、元素或等级类别推断。</summary>
public static class EnemyTraits
{
    public static HashSet<string> ParseStatusImmunities(string value)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in (value ?? string.Empty).Split(';'))
        {
            string name = item.Trim();
            if (name.Length > 0) names.Add(name);
        }
        return names;
    }

    private static EnemyMainData Definition(BattleEntity entity)
        => entity?.Type == BattleEntity.EntityType.Enemy && DataManager.Instance != null
            && DataManager.Instance.EnemyMainDict.TryGetValue(entity.EntityID, out var data) ? data : null;

    public static bool HasFamily(BattleEntity entity, string family)
        => !string.IsNullOrWhiteSpace(family) && Definition(entity)?.EnemyFamily == family.Trim();
    public static bool HasType(BattleEntity entity, string type)
        => !string.IsNullOrWhiteSpace(type) && Definition(entity)?.EnemyType == type.Trim();

    public static bool IsStatusImmune(BattleEntity entity, string statusID, StatusMainData main = null)
    {
        var names = Definition(entity)?.StatusImmunities;
        if (names == null || names.Count == 0) return false;
        if (main == null && DataManager.Instance != null && !string.IsNullOrEmpty(statusID))
            DataManager.Instance.StatusMainDict.TryGetValue(statusID, out main);
        return (!string.IsNullOrWhiteSpace(main?.StatusName) && names.Contains(main.StatusName.Trim()))
            || (!string.IsNullOrEmpty(statusID) && names.Contains(statusID))
            || (statusID == "ST_Freeze" && (names.Contains("冻结") || names.Contains("Frozen")));
    }

    public static bool IsReactionStateImmune(BattleEntity entity, ReactionType type)
    {
        var names = Definition(entity)?.StatusImmunities;
        if (names == null || names.Count == 0) return false;
        var main = StatusDataReactionStateDisplayAdapter.GetMainData(type);
        return names.Contains(type.ToString()) || (main != null && IsStatusImmune(entity, main.StatusID2, main));
    }
}
