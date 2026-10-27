/// <summary>
/// 单次攻击附着的覆盖顺序与反应后残留写回。
/// 元素量衰减仍由 BattleEntity 在阶段状态机末尾执行。
/// </summary>
public static class ElementAuraSystem
{
    /// <summary>写回分流：固有池满足本次额度但不扣除；有限池仍由原宿主处理。查询类不承担修改。</summary>
    public static void ConsumeReactionAura(ReactionContext context, string element, float amount)
    {
        if (context?.Target == null || amount <= 0f) return;
        // 盾的GU按EnemyShield表统一扣除，反应handler不再按普通元素比例重复扣盾。
        if (context.RestrictedAuraKind == ReactionPoolKind.EnemyShield
            && ElementQuery.ReadShield(context, element) != null) return;
        if (context.AllowsReactionPool(element, ReactionPoolKind.Intrinsic)
            && ElementQuery.ReadIntrinsic(context.Target, element) != null) return;
        float remaining = amount;
        if (context.AllowsReactionPool(element, ReactionPoolKind.NormalAura))
        {
            float consumed = ElementQuery.ReadNormal(context.Target, element)?.AvailableFor(remaining) ?? 0f;
            context.Target.ConsumeAura(element, consumed);
            remaining -= consumed;
        }
        if (remaining <= 0f) return;
        if (element == "Cryo" && !context.IgnoreFrozenAura && context.AllowsReactionPool(element, ReactionPoolKind.Frozen))
            FrozenReactionHandler.ConsumeFrozenAura(context.Target, remaining);
        if (element == "Pyro" && context.AllowsReactionPool(element, ReactionPoolKind.Burning))
            BurningReactionHandler.ConsumeBurningAura(context.Target, remaining);
    }

    public static void PrepareIncomingAura(ReactionContext context)
    {
        if (!CanApply(context) || context.PreserveIncomingAuraBeforeReaction) return;

        // 设计规则：同元素先由本次新附着替换，再与其他共存元素反应。
        // 此处先清除旧值；反应后的新元素残留由 CommitIncomingAura 写回。
        context.Target.RemoveAura(context.AttackElement);
    }

    public static void CommitIncomingAura(ReactionContext context, float remainingAmount)
    {
        if (!CanApply(context)) return;
        if (context.EnemyShieldHit?.HadShieldAtStart == true)
        {
            // 仅感电仍可保留未消耗完的后手水/雷供后续tick；破盾这一击不留下溢出附着。
            Shield shield = context.Target.GetEnemyElementalShield();
            bool conducting = shield != null && !context.EnemyShieldHit.BrokeShield
                && (shield.Element == "Hydro" && context.AttackElement == "Electro"
                    || shield.Element == "Electro" && context.AttackElement == "Hydro");
            if (!conducting) return;
        }
        if (context.PreserveIncomingAuraBeforeReaction)
        {
            // 残留清算不刷新已有池的真实来源和计时原点。
            using var elements = ElementEventScope.Begin("ReactionConsumption", context.Target);
            if (remainingAmount <= 0f) context.Target.RemoveAura(context.AttackElement);
            else
            {
                ElementalAura existing = context.Target.GetAura(context.AttackElement);
                if (existing != null) existing.AuraAmount = remainingAmount;
            }
            return;
        }
        if (remainingAmount <= 0f) return;

        BattleEntity sourceEntity = context.SourceEntity ?? context.SourceSnapshotOverride?.SourceEntity;
        int sourceEntityID = sourceEntity != null ? sourceEntity.EntityID : 0;
        context.Target.ApplyAura(
            context.AttackElement,
            remainingAmount,
            sourceEntityID,
            context.SourceEffectID,
            context.SourceSkillID,
            sourceEntity);
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
            && ElementQuery.IntrinsicStatusID(context.AttackElement) != null
            // Geo and Anemo attacks can react, but neither is written as a normal aura.
            && context.AttackElement != "Geo"
            && context.AttackElement != "Anemo";
    }
}
