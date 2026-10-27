/// <summary>月曜转化资格只读查询：由角色天赋施加公共标记，不按角色名单或月兆等级猜测。</summary>
public static class LunarReactionConversion
{
    public const string ConductActive = "LunarConductActive";
    public const string BloomActive = "LunarBloomActive";
    public const string CrystallizeActive = "LunarCrystallizeActive";

    public static bool IsTalentMarker(string statusID)
        => statusID == ConductActive || statusID == BloomActive || statusID == CrystallizeActive;

    /// <summary>扫描本场编队全部存活角色，包含后台；不缓存，失效/移除/阵亡后立即不计。</summary>
    public static bool HasActiveTalent(ReactionType lunarReaction, BattleManager battle = null)
    {
        string marker = MarkerFor(lunarReaction);
        battle = battle ?? BattleManager.Instance;
        var team = battle?.TeamBonuses;
        if (marker == null || battle == null || !battle.IsBattleRunning || battle.IsBattleOver
            || team == null || team.IsDisposed) return false;
        foreach (var member in team.Members)
            if (member != null && member.Type == BattleEntity.EntityType.Character
                && member.Side == BattleSide.Ally && member.IsAlive && member.HasStatus(marker)) return true;
        return false;
    }

    /// <summary>调用者传入本次真实元素组合；仅选择类型，不消耗元素、不产生反应声明或伤害。</summary>
    public static ReactionType SelectReaction(ReactionContext context, ReactionType ordinary, string otherElement)
    {
        var battle = BattleManager.Instance;
        var team = battle?.TeamBonuses;
        var source = context?.SourceEntity ?? context?.SourceSnapshotOverride?.SourceEntity;
        if (context == null || !context.CanTriggerReaction || context.AttackAmount <= 0f
            || context.Target == null || context.Target.Side != BattleSide.Enemy
            || context.SourceKind == ReactionSourceKind.EnemySkill || source?.Side != BattleSide.Ally
            || team == null || !team.Contains(source)) return ordinary;
        ReactionType lunar = ReactionType.None;
        if (ordinary == ReactionType.ElectroCharged && Pair(context.AttackElement, otherElement, "Hydro", "Electro"))
            lunar = ReactionType.LunarElectroCharged;
        else if (ordinary == ReactionType.Bloom && Pair(context.AttackElement, otherElement, "Hydro", "Dendro"))
            lunar = ReactionType.LunarBloom;
        else if (ordinary == ReactionType.Crystallize && Pair(context.AttackElement, otherElement, "Hydro", "Geo"))
            lunar = ReactionType.LunarCrystallize;
        return lunar != ReactionType.None && HasActiveTalent(lunar, battle) ? lunar : ordinary;
    }

    private static string MarkerFor(ReactionType reaction)
    {
        switch (reaction)
        {
            case ReactionType.LunarElectroCharged: return ConductActive;
            case ReactionType.LunarBloom: return BloomActive;
            case ReactionType.LunarCrystallize: return CrystallizeActive;
            default: return null;
        }
    }

    private static bool Pair(string attack, string other, string first, string second)
        => attack == first && other == second || attack == second && other == first;
}
