using System;
using System.Collections.Generic;

/// <summary>
/// 把配表目标字段和当前战场转换为 TargetResolver 的位置请求，并把结果映射回实体/场地。
/// 角色技能、状态效果、敌人技能共用本入口。
/// </summary>
public static class BattleTargetResolver
{
    public static TargetResolutionResult Resolve(
        BattleManager battle,
        BattleEntity source,
        object origin,
        string targetType,
        int targetNumber,
        int targetConsecutive,
        string targetOverride,
        string targetSelect,
        IList<int> forcedPositions,
        TargetResolutionResult previousResult,
        Func<int, float> scoreProvider = null,
        Func<int, int> randomIndex = null,
        bool selfUsesOrigin = false)
    {
        if (battle == null || battle.Field == null)
        {
            return new TargetResolutionResult
            {
                Error = "战场尚未初始化"
            };
        }

        BattleSide sourceSide = source != null ? source.Side : GetOriginSide(origin, BattleSide.Ally);
        DescribeTarget(sourceSide, origin, targetType, out BattleSide targetSide, out BattleTargetDomain domain);
        if (selfUsesOrigin && targetType == "Self")
        {
            targetSide = GetOriginSide(origin, sourceSide);
            domain = origin is FieldPosition ? BattleTargetDomain.Field : BattleTargetDomain.Unit;
        }

        int sourcePosition = source != null ? source.SlotPosition : GetOriginPosition(origin);
        int originPosition = GetOriginPosition(origin);
        if (originPosition <= 0) originPosition = sourcePosition;

        var request = new TargetResolutionRequest
        {
            Side = targetSide,
            Domain = domain,
            MaxPosition = BattlePositionSystem.GetSideCount(targetSide),
            OriginPosition = originPosition,
            TargetNumber = targetNumber,
            TargetConsecutive = targetConsecutive,
            TargetOverride = targetOverride,
            TargetSelect = targetSelect,
            ForcedPositions = forcedPositions != null ? new List<int>(forcedPositions) : null,
            PreviousPositions = previousResult != null ? new List<int>(previousResult.Positions) : null,
            Score = scoreProvider ?? (position => GetAbsoluteHp(battle, targetSide, position)),
            RandomIndex = randomIndex
        };

        if (string.IsNullOrEmpty(targetType) || targetType == "Self")
        {
            int fixedPosition = selfUsesOrigin ? originPosition : sourcePosition;
            request.FixedPositions = fixedPosition > 0 ? new List<int> { fixedPosition } : new List<int>();
        }

        request.IsEligible = position =>
        {
            var entity = battle.GetEntityByPosition(targetSide, position);
            if (entity == null || !entity.IsAlive) return false;
            if (targetType == "AlliesOnly" && entity == source) return false;
            return true;
        };

        if (!string.IsNullOrWhiteSpace(targetOverride) && !targetOverride.Contains(","))
        {
            string value = targetOverride.Trim();
            if (value.StartsWith("PreAlliesDamage(", StringComparison.Ordinal))
            {
                string hookStatusID2 = ExtractHookStatusID(value);
                request.OverridePositions = PreDamageHookSystem.HasPreDamageHook(hookStatusID2)
                    ? new List<int>(battle.PendingActionTargetPositions)
                    : new List<int>();
            }
            else
                request.OverridePositions = FindStatusPositions(battle, targetSide, domain, value);
        }

        return TargetResolver.Resolve(request);
    }

    public static List<BattleEntity> GetEntities(BattleManager battle, TargetResolutionResult result)
    {
        var entities = new List<BattleEntity>();
        if (battle == null || result == null) return entities;

        foreach (int position in result.Positions)
        {
            var entity = battle.GetEntityByPosition(result.Side, position);
            if (entity != null && entity.IsAlive) entities.Add(entity);
        }
        return entities;
    }

    public static List<FieldPosition> GetFields(BattleManager battle, TargetResolutionResult result)
    {
        var fields = new List<FieldPosition>();
        if (battle == null || battle.Field == null || result == null || result.Domain != BattleTargetDomain.Field)
            return fields;

        foreach (int position in result.Positions)
        {
            var field = battle.Field.GetSlot(result.Side, position);
            if (field != null) fields.Add(field);
        }
        return fields;
    }

    public static void DescribeTarget(
        BattleSide sourceSide,
        object origin,
        string targetType,
        out BattleSide targetSide,
        out BattleTargetDomain domain)
    {
        targetSide = sourceSide;
        domain = BattleTargetDomain.Unit;

        switch (targetType)
        {
            case "Enemy":
                targetSide = Opposite(sourceSide);
                break;
            case "EnemyField":
                targetSide = Opposite(sourceSide);
                domain = BattleTargetDomain.Field;
                break;
            case "AllyField":
            case "AlliesField":
                domain = BattleTargetDomain.Field;
                break;
            case "Field":
                targetSide = GetOriginSide(origin, sourceSide);
                domain = BattleTargetDomain.Field;
                break;
        }
    }

    public static BattleSide Opposite(BattleSide side)
    {
        return side == BattleSide.Ally ? BattleSide.Enemy : BattleSide.Ally;
    }

    private static List<int> FindStatusPositions(
        BattleManager battle,
        BattleSide side,
        BattleTargetDomain domain,
        string statusID2)
    {
        var positions = new List<int>();
        int maxPosition = BattlePositionSystem.GetSideCount(side);
        for (int position = 1; position <= maxPosition; position++)
        {
            if (domain == BattleTargetDomain.Unit)
            {
                var entity = battle.GetEntityByPosition(side, position);
                if (entity != null && entity.IsAlive && entity.GetStatus(statusID2) != null)
                    positions.Add(position);
                continue;
            }

            var field = battle.Field.GetSlot(side, position);
            if (field == null) continue;
            foreach (var status in field.StatusList)
            {
                if (status != null && status.StatusID2 == statusID2)
                {
                    positions.Add(position);
                    break;
                }
            }
        }
        return positions;
    }

    private static float GetAbsoluteHp(BattleManager battle, BattleSide side, int position)
    {
        var entity = battle.GetEntityByPosition(side, position);
        return entity != null && entity.IsAlive ? entity.CurrentHP : 0f;
    }

    private static string ExtractHookStatusID(string value)
    {
        int left = value.IndexOf('(');
        int right = value.LastIndexOf(')');
        return left >= 0 && right > left
            ? value.Substring(left + 1, right - left - 1).Trim()
            : string.Empty;
    }

    private static int GetOriginPosition(object origin)
    {
        if (origin is FieldPosition field) return field.SlotIndex;
        if (origin is BattleEntity entity) return entity.SlotPosition;
        return -1;
    }

    private static BattleSide GetOriginSide(object origin, BattleSide fallback)
    {
        if (origin is FieldPosition field) return field.Side;
        if (origin is BattleEntity entity) return entity.Side;
        return fallback;
    }
}
