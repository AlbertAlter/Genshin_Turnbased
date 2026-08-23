/// <summary>
/// 旧反应兼容入口。已实现的常规反应全部位于 Scripts/Battle/Reactions 的独立 Handler；
/// 尚未迁移的反应在对应阶段实现前保持无效果。
/// </summary>
public static class ElementReactionManager
{
    public static float TryReaction(
        BattleEntity target,
        string attackElement,
        float attackAmount,
        float baseDamage,
        out string reactionName,
        out float attackRemain,
        ReactionContext context = null)
    {
        reactionName = string.Empty;
        attackRemain = attackAmount;
        return baseDamage;
    }
}
