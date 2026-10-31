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
        bool selfUsesOrigin = false,
        bool deadOnly = false,
        bool respectTaunt = false,
        Func<int, float> weightProvider = null,
        bool validateForcedTaunt = false)
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
        if (domain == BattleTargetDomain.Team)
        {
            // 队伍是单个宿主，没有伪造的0号/第六号格子，手选和随机位置不参与。
            return new TargetResolutionResult { Side = targetSide, Domain = domain,
                Error = !string.IsNullOrWhiteSpace(targetOverride) || !string.IsNullOrWhiteSpace(targetSelect)
                    ? "队伍目标不使用TargetOverride/TargetSelect位置筛选" : null };
        }
        // 新队伍宿主不改变旧Self的个人来源语义。选择宿主请明确写AllyTeam/EnemyTeam。
        if (origin is BattleTeamStatusHost && targetType == "Self") selfUsesOrigin = false;
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
        request.SelectionWeight = weightProvider;
        request.ValidateForcedTaunt = validateForcedTaunt;

        if (string.IsNullOrEmpty(targetType) || targetType == "Self")
        {
            int fixedPosition = selfUsesOrigin ? originPosition : sourcePosition;
            request.FixedPositions = fixedPosition > 0 ? new List<int> { fixedPosition } : new List<int>();
        }

        var boundStatusHost = selfUsesOrigin && targetType == "Self" ? origin as BattleEntity : null;
        request.IsEligible = position =>
        {
            var entity = battle.GetEntityByPosition(targetSide, position);
            if (entity == null || (deadOnly ? entity.IsAlive : !entity.IsAlive)) return false;
            if (entity.IsOffField && entity != boundStatusHost) return false;
            if (targetType == "AlliesOnly" && entity == source) return false;
            return true;
        };

        // 仅0/1选取可请求嘲讽约束，随机模式2/3/4始终忽略，与执行次数无关。
        // Field/Team、全体、固定/继承范围、治疗及状态赋予均不借此重选。
        if (respectTaunt && !deadOnly && AffectedByTaunt(sourceSide, targetSide, domain,
            targetNumber, targetConsecutive, targetOverride, targetSelect))
        {
            request.TauntPositions = new List<int>();
            for (int position = 1; position <= request.MaxPosition; position++)
            {
                var target = battle.GetEntityByPosition(targetSide, position);
                if (IsTaunter(target)) request.TauntPositions.Add(position);
            }
        }

        if (!string.IsNullOrWhiteSpace(targetOverride) && !targetOverride.Contains(","))
        {
            string value = targetOverride.Trim();
            if (BattleSpecialRules.TryResolveTargets(value, battle, source, out var specialPositions))
            {
                request.OverridePositions = specialPositions;
            }
            else if (value.StartsWith("PreAlliesDamage(", StringComparison.Ordinal))
            {
                string hookStatusID2 = ExtractHookStatusID(value);
                request.OverridePositions = PreDamageHookSystem.HasPreDamageHook(hookStatusID2)
                    ? new List<int>(battle.PendingActionTargetPositions)
                    : new List<int>();
            }
            else if (value.StartsWith("PreSelfDamage(", StringComparison.Ordinal))
            {
                string hookStatusID2 = ExtractHookStatusID(value);
                request.OverridePositions = PreDamageHookSystem.HasPreDamageHook(hookStatusID2)
                    ? new List<int>(battle.PendingActionTargetPositions)
                    : new List<int>();
            }
            else if (value.StartsWith("PostAlliesDamage(", StringComparison.Ordinal))
            {
                request.OverridePositions = PostDamageHookSystem.GetPositions(ExtractHookStatusID(value));
            }
            else if (value.StartsWith("PostSelfDamage(", StringComparison.Ordinal))
            {
                request.OverridePositions = PostDamageHookSystem.GetPositions(ExtractHookStatusID(value));
            }
            else
                request.OverridePositions = FindStatusPositions(battle, targetSide, domain, value);
        }

        var resolved = TargetResolver.Resolve(request);
        resolved.BoundStatusHost = boundStatusHost;
        return resolved;
    }

    public static List<BattleEntity> GetEntities(BattleManager battle, TargetResolutionResult result, bool deadOnly = false)
    {
        var entities = new List<BattleEntity>();
        if (battle == null || result == null) return entities;
        if (result.Domain == BattleTargetDomain.Team)
            throw new InvalidOperationException("队伍宿主不是伤害、治疗、能量或其他个人效果的实体目标。");

        foreach (int position in result.Positions)
        {
            var entity = battle.GetEntityByPosition(result.Side, position);
            if (entity != null && (deadOnly ? entity.IsDead : entity.IsAlive)
                && (!entity.IsOffField || entity == result.BoundStatusHost)) entities.Add(entity);
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

    public static bool IsTeamTarget(string targetType) => targetType == "AllyTeam" || targetType == "EnemyTeam";

    public static bool IsTaunter(BattleEntity entity)
        => entity?.IsOnField == true && entity.HasStatus("Taunt");

    public static bool AffectedByTaunt(BattleSide sourceSide, BattleSide targetSide,
        BattleTargetDomain domain, int count, int mode, string targetOverride, string targetSelect)
        => domain == BattleTargetDomain.Unit && targetSide != sourceSide && count > 0
            && mode >= 0 && mode <= 1 && string.IsNullOrWhiteSpace(targetOverride)
            && string.IsNullOrWhiteSpace(targetSelect);

    public static List<BattleTeamStatusHost> GetTeams(BattleManager battle, TargetResolutionResult result)
    {
        var teams = new List<BattleTeamStatusHost>();
        if (battle != null && result != null && result.IsValid && result.Domain == BattleTargetDomain.Team)
            teams.Add(battle.GetTeam(result.Side));
        return teams;
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
            case "AllyTeam":
                domain = BattleTargetDomain.Team;
                break;
            case "EnemyTeam":
                targetSide = Opposite(sourceSide);
                domain = BattleTargetDomain.Team;
                break;
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
        return entity != null && entity.IsOnField ? entity.CurrentHP : 0f;
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
        if (origin is BattleTeamStatusHost team) return team.Side;
        if (origin is FieldPosition field) return field.Side;
        if (origin is BattleEntity entity) return entity.Side;
        return fallback;
    }
}
