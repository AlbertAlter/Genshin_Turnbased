/// <summary>
/// 单次攻击附着的覆盖顺序与反应后残留写回。
/// 元素量衰减仍由 BattleEntity 在阶段状态机末尾执行。
/// </summary>
public static class ElementAuraSystem
{
    public static void PrepareIncomingAura(ReactionContext context)
    {
        if (!CanApply(context)) return;

        // 设计规则：同元素先由本次新附着替换，再与其他共存元素反应。
        // 此处先清除旧值；反应后的新元素残留由 CommitIncomingAura 写回。
        context.Target.RemoveAura(context.AttackElement);
    }

    public static void CommitIncomingAura(ReactionContext context, float remainingAmount)
    {
        if (!CanApply(context) || remainingAmount <= 0f) return;

        int sourceEntityID = context.SourceEntity != null ? context.SourceEntity.EntityID : 0;
        context.Target.ApplyAura(
            context.AttackElement,
            remainingAmount,
            sourceEntityID,
            context.SourceEffectID,
            context.SourceSkillID);
        if (context.SkipImmediateAuraDecay)
        {
            ElementalAura aura = context.Target.GetAura(context.AttackElement);
            if (aura != null) aura.SkipNextOriginDecay = true;
        }
    }

    private static bool CanApply(ReactionContext context)
    {
        return context != null
            && context.CanApplyAura
            && context.Target != null
            && context.AttackAmount > 0f
            && !string.IsNullOrEmpty(context.AttackElement)
            && context.AttackElement != "None"
            // Geo attacks can react, but their remaining amount is never written as a normal aura.
            && context.AttackElement != "Geo";
    }
}
