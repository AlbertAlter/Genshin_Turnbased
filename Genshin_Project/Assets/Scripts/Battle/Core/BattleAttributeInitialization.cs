using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// 开战属性的唯一收尾入口：基础构筑/状态挂载不逐项改当前HP，完整队伍就绪后统一提交。
/// 只提交初始资源，不把ATK/DEF/EM等动态属性冻结成快照；实际效果仍走各自GetFinal查询。
/// </summary>
public static class BattleAttributeInitialization
{
    private sealed class Seed
    {
        internal float HP;
        internal bool WasFull, BuildStarted, Complete;
    }
    internal readonly struct OpeningHealth
    {
        internal readonly BattleEntity Entity;
        internal readonly float HP;
        internal readonly bool WasFull;
        internal OpeningHealth(BattleEntity entity, float hp, bool full)
        { Entity = entity; HP = hp; WasFull = full; }
    }
    private static readonly ConditionalWeakTable<BattleEntity, Seed> Seeds = new ConditionalWeakTable<BattleEntity, Seed>();

    public static bool IsComplete(BattleEntity entity)
        => entity != null && Seeds.TryGetValue(entity, out var seed) && seed.Complete;

    /// <summary>Init/Reset或独立工具完成裸面板后记录资源基准；必须在挂载初始效果前调用，不用于战斗内刷新。</summary>
    public static void Prepare(BattleEntity entity)
    {
        if (entity == null) return;
        Seeds.Remove(entity);
        Seeds.Add(entity, new Seed { HP = entity.CurrentHP,
            WasFull = entity.IsAlive && entity.CurrentHP >= entity.TotalHP - .0001f });
    }

    /// <summary>独立构筑器在首次写入天赋/装备前调用；完整开战收尾仍由BattleManager负责。</summary>
    public static void BeginBuild(BattleEntity entity)
    {
        if (entity == null) return;
        if (!Seeds.TryGetValue(entity, out var seed)) { Prepare(entity); seed = Seeds.GetValue(entity, _ => new Seed()); }
        if (seed.BuildStarted || seed.Complete) return;
        // 构筑前调用者可以设置剩余HP；只在第一次修改装备/天赋前保存，不被后续每件装备反复覆盖。
        if (entity.CurrentHP != seed.HP)
            seed.WasFull = entity.IsAlive && entity.CurrentHP >= entity.GetFinalHP() - .0001f;
        seed.HP = entity.CurrentHP;
        seed.BuildStarted = true;
    }

    internal static OpeningHealth Capture(BattleEntity entity)
    {
        if (!Seeds.TryGetValue(entity, out var seed)) { Prepare(entity); seed = Seeds.GetValue(entity, _ => new Seed()); }
        // 初始基准只用于首次构筑；停战后再开始不能把恰好降回裸面板HP的伤残者视为原满血。
        bool full = !seed.Complete && entity.CurrentHP == seed.HP ? seed.WasFull
            : entity.IsAlive && entity.CurrentHP >= entity.GetFinalHP() - .0001f;
        seed.Complete = false;
        return new OpeningHealth(entity, entity.CurrentHP, full);
    }

    internal static void Complete(IReadOnlyList<OpeningHealth> opening)
    {
        // 全部初始化和绑定已完成，先统一读取最终上限，再统一写入；不开治疗/HP变化/消耗声明。
        var maximums = new float[opening.Count];
        for (int i = 0; i < opening.Count; i++) maximums[i] = opening[i].Entity.GetFinalHP();
        for (int i = 0; i < opening.Count; i++)
        {
            var state = opening[i];
            if (state.HP > 0f && state.Entity.IsAlive)
                state.Entity.CurrentHP = state.WasFull ? maximums[i] : Mathf.Min(state.HP, maximums[i]);
        }
        foreach (var state in opening) Seeds.GetValue(state.Entity, _ => new Seed()).Complete = true;
    }
}
