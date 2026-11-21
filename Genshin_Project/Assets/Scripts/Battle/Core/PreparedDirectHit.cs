/// <summary>单个直伤目标进入反应前已经准备好的数值与承伤信息。</summary>
internal readonly struct PreparedDirectHit
{
    internal ReactionContext Context { get; }
    private readonly DMGTakenSnapshot _taken;
    private readonly CountDamagePlan _countPlan;

    internal PreparedDirectHit(ReactionContext context, DMGTakenSnapshot taken, CountDamagePlan countPlan = null)
    {
        Context = context;
        _taken = taken;
        _countPlan = countPlan;
    }

    internal ReactionResult ResolveReaction()
    {
        var reaction = ReactionResolver.Resolve(Context);
        StatusOnHitHookSystem.NotifyReactions(reaction.TriggeredReactions);
        return reaction;
    }

    internal HitResolution Submit(HitBatch batch, ReactionResult reaction, DamageProperties properties,
        DamageSourceInfo source, int hitIndex, CriticalHitResult? critical = null)
        => batch.Resolve(new HitRequest
        {
            Properties = properties,
            Target = Context.Target,
            DamageBeforeShield = critical?.DamageAfterCritical ?? reaction.FinalDamage,
            Critical = critical,
            Element = Context.AttackElement,
            ElementAmount = Context.AttackAmount,
            DamageType = Context.DamageType,
            EnemyShieldHit = reaction.EnemyShieldHit,
            DMGTaken = _taken,
            Source = source,
            HitIndex = hitIndex,
            TargetIndex = Context.TargetIndex - 1, // 反应上下文从1编号，HitRequest从0编号。
            CountPlan = _countPlan
        });
}
