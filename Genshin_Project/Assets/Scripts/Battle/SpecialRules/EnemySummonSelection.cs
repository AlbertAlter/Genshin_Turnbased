using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

/// <summary>按总表生物种类召唤；一次完整Summon效果的各次重复不重复抽取种类。</summary>
public static class EnemySummonSelection
{
    private static readonly ConditionalWeakTable<BattleResolutionContext.CounterScope, Dictionary<long, HashSet<int>>> Selected =
        new ConditionalWeakTable<BattleResolutionContext.CounterScope, Dictionary<long, HashSet<int>>>();

    public static List<int> FamilyPool(string family)
        => DataManager.Instance?.EnemyMainDict.Values.Where(e => e.EnemyFamily == family)
            .Select(e => e.EnemyID).OrderBy(id => id).ToList() ?? new List<int>();

    public static int Resolve(string reference)
    {
        if (int.TryParse(reference, out int fixedID) && fixedID > 0) return fixedID;
        if (reference == null || !reference.StartsWith("Family(", StringComparison.Ordinal) || !reference.EndsWith(")", StringComparison.Ordinal))
            throw new FormatException("Summon.Param1须为敌人ID或Family(生物种类)。");
        var pool = FamilyPool(reference.Substring(7, reference.Length - 8).Trim());
        var scope = BattleResolutionContext.CurrentEffect;
        if (scope == null) throw new InvalidOperationException("分类召唤必须在正式效果作用域内执行。");
        var records = Selected.GetOrCreateValue(scope);
        long execution = BattleEventHub.CurrentEffectExecutionId;
        if (!records.TryGetValue(execution, out var used)) records.Add(execution, used = new HashSet<int>());
        pool.RemoveAll(used.Contains);
        if (pool.Count == 0) return 0;
        int chosen = pool[BattleRandom.NextInt(0, pool.Count)];
        used.Add(chosen);
        return chosen;
    }
}
