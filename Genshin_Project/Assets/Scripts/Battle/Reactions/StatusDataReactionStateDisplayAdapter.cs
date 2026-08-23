/// <summary>
/// 将脚本控制的反应状态放入实体 StatusDict 供 UI 显示。
/// 这些实例不登记到普通状态计时桶，增删和剩余时间完全由 ReactionStateSystem 控制。
/// </summary>
public sealed class StatusDataReactionStateDisplayAdapter : IReactionStateDisplayAdapter
{
    public void ApplyOrRefresh(ReactionStateInstance state)
    {
        if (state == null || state.Target == null) return;
        string statusID = GetStatusID(state.Type);
        if (string.IsNullOrEmpty(statusID)) return;

        StatusMainData mainData = null;
        if (DataManager.Instance != null)
            DataManager.Instance.StatusMainDict.TryGetValue(statusID, out mainData);
        if (mainData == null)
            mainData = CreateFallbackMainData(state.Type, statusID);

        if (state.Target.StatusDict.TryGetValue(statusID, out StatusInstance existing))
        {
            existing.RemainingPhaseCount = state.RemainingRounds;
            existing.MainData = mainData;
            existing.AddInPhase = state.OriginPhase;
            existing.Caster = state.SourceSnapshot != null ? state.SourceSnapshot.SourceEntity : null;
            existing.ApplyOrder = state.ApplyOrder;
            return;
        }

        state.Target.StatusDict[statusID] = new StatusInstance
        {
            StatusID2 = statusID,
            StackCount = 1,
            RemainingPhaseCount = state.RemainingRounds,
            AddInPhase = state.OriginPhase,
            Caster = state.SourceSnapshot != null ? state.SourceSnapshot.SourceEntity : null,
            MainData = mainData,
            IsActive = true,
            ApplyOrder = state.ApplyOrder
        };
    }

    public void Remove(BattleEntity target, ReactionType type)
    {
        if (target == null) return;
        string statusID = GetStatusID(type);
        if (!string.IsNullOrEmpty(statusID))
            target.RemoveStatus(statusID);
    }

    private static string GetStatusID(ReactionType type)
    {
        switch (type)
        {
            case ReactionType.Frozen: return "ST_Freeze";
            case ReactionType.Superconduct: return "ST_SuperConduct";
            case ReactionType.ElectroCharged: return "ST_ElectroCharged";
            case ReactionType.Burning: return "ST_Burning";
            case ReactionType.Quicken: return "ST_Quicken";
            default: return string.Empty;
        }
    }

    private static StatusMainData CreateFallbackMainData(ReactionType type, string statusID)
    {
        if (type == ReactionType.Superconduct)
        {
            return new StatusMainData
            {
                StatusID = 40004,
                StatusID2 = statusID,
                StatusName = "超导",
                StatusType = "Debuff",
                Display = 1,
                Description = "降低40%物理抗性",
                MultiplierPart2 = "ResBonus",
                ApplyElementType = "Physical"
            };
        }

        if (type == ReactionType.ElectroCharged)
        {
            return new StatusMainData
            {
                StatusID = 40005,
                StatusID2 = statusID,
                StatusName = "感电",
                Display = 1,
                Description = "回合结束时触发感电伤害"
            };
        }

        if (type == ReactionType.Burning)
        {
            return new StatusMainData
            {
                StatusID = 40006,
                StatusID2 = statusID,
                StatusName = "燃烧",
                Display = 1,
                Description = "回合结束时触发燃烧伤害"
            };
        }

        if (type == ReactionType.Quicken)
        {
            return new StatusMainData
            {
                StatusID = 40008,
                StatusID2 = statusID,
                StatusName = "原激化",
                Display = 1,
                Description = "受到雷元素和草元素攻击时改变伤害计算公式"
            };
        }

        return new StatusMainData
        {
            StatusID = 40003,
            StatusID2 = statusID,
            StatusName = "冻结",
            Display = 1
        };
    }
}
