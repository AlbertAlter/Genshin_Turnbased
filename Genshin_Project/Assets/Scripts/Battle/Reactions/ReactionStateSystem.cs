using System;
using System.Collections.Generic;

/// <summary>持续反应创建时保存的来源快照。</summary>
[Serializable]
public sealed class ReactionSourceSnapshot
{
    public BattleEntity SourceEntity;
    public int SourceEntityID;
    public BattleSide SourceSide;
    public ReactionSourceKind SourceKind;
    public int Level;
    public float LevelCoefficient;
    public float TotalEM;
    public float DMGBonus;
    public float BaseDMGBonusFlat;
    public string SourceSkillID;
    public string SourceEffectID;

    public ReactionSourceSnapshot Clone()
    {
        return (ReactionSourceSnapshot)MemberwiseClone();
    }

    public static ReactionSourceSnapshot Capture(ReactionContext context)
    {
        if (context != null && context.SourceSnapshotOverride != null)
            return context.SourceSnapshotOverride.Clone();

        BattleEntity source = context != null ? context.SourceEntity : null;
        return new ReactionSourceSnapshot
        {
            SourceEntity = source,
            SourceEntityID = source != null ? source.EntityID : 0,
            SourceSide = source != null ? source.Side : BattleSide.Ally,
            SourceKind = context != null ? context.SourceKind : ReactionSourceKind.DerivedReaction,
            Level = source != null ? source.Level : 0,
            TotalEM = source != null ? source.TotalEM : 0f,
            DMGBonus = source != null ? source.DMGBonus : 0f,
            BaseDMGBonusFlat = source != null ? source.BaseDMGBonusFlat : 0f,
            SourceSkillID = context != null ? context.SourceSkillID : string.Empty,
            SourceEffectID = context != null ? context.SourceEffectID : string.Empty
        };
    }

    public static ReactionSourceSnapshot Capture(
        ReactionContext context,
        ReactionType reactionType,
        string damageElement)
    {
        if (context != null && context.SourceSnapshotOverride != null)
            return context.SourceSnapshotOverride.Clone();

        ReactionSourceSnapshot snapshot = Capture(context);
        BattleEntity source = context != null ? context.SourceEntity : null;
        ReactionBuffTotals bonuses = ReactionDamageCalculator.CollectReactionBonuses(
            source,
            reactionType,
            damageElement);
        snapshot.DMGBonus = bonuses.DMGBonus;
        snapshot.BaseDMGBonusFlat = bonuses.BaseDMGBonusFlat;
        return snapshot;
    }
}

[Serializable]
public sealed class ReactionStateInstance
{
    public ReactionType Type;
    public BattleEntity Target;
    public int RemainingRounds;
    public int OriginPhase;
    public long ApplyOrder;
    public ReactionSourceSnapshot SourceSnapshot;
    public bool UsesRoundDuration = true;
    /// <summary>冻元素、燃元素等随反应状态保存的精确元素量。</summary>
    public float ReactionElementAmount;
    /// <summary>在回合结束反应结算中刚替换的状态，跳过紧随其后的同阶段计时。</summary>
    public bool SkipNextDurationTick;
}

/// <summary>
/// 将脚本控制的反应运行时状态同步为 StatusData 显示状态的适配接口。
/// 显示状态不拥有反应生命周期，也不得被技能主动调用。
/// </summary>
public interface IReactionStateDisplayAdapter
{
    void ApplyOrRefresh(ReactionStateInstance state);
    void Remove(BattleEntity target, ReactionType type);
}

public sealed class NullReactionStateDisplayAdapter : IReactionStateDisplayAdapter
{
    public void ApplyOrRefresh(ReactionStateInstance state) { }
    public void Remove(BattleEntity target, ReactionType type) { }
}

/// <summary>
/// 不依赖 StatusData 的反应运行时状态容器。
/// 同一目标的同类型状态只保留一个；后施加者整体覆盖旧实例和旧快照。
/// </summary>
public static class ReactionStateSystem
{
    private static readonly List<ReactionStateInstance> States = new List<ReactionStateInstance>();
    private static IReactionStateDisplayAdapter _displayAdapter = new NullReactionStateDisplayAdapter();

    public static IReadOnlyList<ReactionStateInstance> ActiveStates => States;

    public static void SetDisplayAdapter(IReactionStateDisplayAdapter adapter)
    {
        _displayAdapter = adapter ?? new NullReactionStateDisplayAdapter();
    }

    public static ReactionStateInstance SetEntityState(
        BattleEntity target,
        ReactionType type,
        int duration,
        ReactionSourceSnapshot snapshot,
        int originPhase = -1,
        bool usesRoundDuration = true,
        float reactionElementAmount = 0f,
        bool skipNextDurationTick = false)
    {
        if (target == null || type == ReactionType.None || duration <= 0) return null;

        int oldIndex = States.FindIndex(x => ReferenceEquals(x.Target, target) && x.Type == type);
        if (oldIndex >= 0) States.RemoveAt(oldIndex);
        var state = new ReactionStateInstance
        {
            Type = type,
            Target = target,
            RemainingRounds = duration,
            OriginPhase = originPhase > 0
                ? originPhase
                : (BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : -1),
            ApplyOrder = ++BattleEntity._applyOrderCounter,
            SourceSnapshot = snapshot,
            UsesRoundDuration = usesRoundDuration,
            ReactionElementAmount = Math.Max(0f, reactionElementAmount),
            SkipNextDurationTick = skipNextDurationTick
        };
        States.Add(state);
        _displayAdapter.ApplyOrRefresh(state);
        return state;
    }

    public static bool TryGetEntityState(BattleEntity target, ReactionType type, out ReactionStateInstance state)
    {
        state = States.Find(x => ReferenceEquals(x.Target, target) && x.Type == type);
        return state != null;
    }

    public static bool RemoveEntityState(BattleEntity target, ReactionType type)
    {
        int index = States.FindIndex(x => ReferenceEquals(x.Target, target) && x.Type == type);
        if (index < 0) return false;
        States.RemoveAt(index);
        _displayAdapter.Remove(target, type);
        return true;
    }

    public static void RefreshDisplay(ReactionStateInstance state)
    {
        if (state != null) _displayAdapter.ApplyOrRefresh(state);
    }

    /// <summary>反应状态使用施加阶段作为计时原点；下一次进入同阶段时扣减一回合。</summary>
    public static void TickPhase(int phase)
    {
        var expired = new List<ReactionStateInstance>();
        foreach (ReactionStateInstance state in States)
        {
            if (state == null || !state.UsesRoundDuration || state.OriginPhase != phase) continue;
            if (state.SkipNextDurationTick)
            {
                state.SkipNextDurationTick = false;
                continue;
            }
            if (state.ReactionElementAmount > 0f)
            {
                state.ReactionElementAmount = Math.Max(0f, state.ReactionElementAmount - 1f);
                state.RemainingRounds = (int)Math.Ceiling(state.ReactionElementAmount);
            }
            else
            {
                state.RemainingRounds--;
            }
            if (state.RemainingRounds <= 0) expired.Add(state);
            else _displayAdapter.ApplyOrRefresh(state);
        }

        foreach (ReactionStateInstance state in expired)
            RemoveEntityState(state.Target, state.Type);
    }

    public static void ClearAll()
    {
        foreach (ReactionStateInstance state in States)
            _displayAdapter.Remove(state.Target, state.Type);
        States.Clear();
    }
}
