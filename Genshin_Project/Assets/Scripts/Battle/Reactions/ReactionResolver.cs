using System;
using System.Collections.Generic;

/// <summary>
/// 角色技能、角色状态伤害和敌人技能共用的唯一反应入口。
/// 阶段0保留旧融化/冻结行为；元素附着的覆盖与写回也统一在此入口结算。
/// 后续阶段由独立 handler 逐项替换。
/// </summary>
public static class ReactionResolver
{
    private static long _effectExecutionCounter;

    private sealed class ReactionPoolTarget
    {
        public string Element;
        public ReactionPoolKind Kind;
        public float Amount;
    }

    private struct SingleReactionResolution
    {
        public ReactionType Type;
        public string DisplayName;
        public string ReactedElement;
        public float RemainingAttackAmount;
        public float FinalDamage;
        public bool ChangesPrimaryDamage;
    }

    public static long BeginEffectExecution()
    {
        return ++_effectExecutionCounter;
    }

    public static void ResetSession()
    {
        _effectExecutionCounter = 0;
        ReactionStateSystem.ClearAll();
        BloomCoreSystem.ClearAll();
    }

    public static ReactionResult Resolve(ReactionContext context)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var result = ReactionResult.Unchanged(context);
        if (context.Target == null)
            return result;


        if (context.ApplicationPhase <= 0 && BattleManager.Instance != null)
            context.ApplicationPhase = (int)BattleManager.Instance.CurrentPhase;

        // 碎冰先于目标身上其余元素反应判定；实际扣到HP后才在伤害执行器中落地。
        if (context.CanTriggerReaction && FrozenReactionHandler.CanShatter(context))
        {
            float levelCoefficient = ReactionDamageCalculator.GetLevelCoefficient(
                context.SourceEntity != null ? context.SourceEntity.Level : 1);
            result.PendingShatter = FrozenReactionHandler.BuildPendingShatter(
                context,
                levelCoefficient);
            context.IgnoreFrozenAura = true;
        }

        // 风元素由 SwirlReactionHandler 以同一 Hit 的全部目标为一个批次结算；
        // 这里仍保留上面的通用主命中预处理（例如碎冰），风本身不形成普通附着。
        if (context.AttackElement == "Anemo")
            return result;

        if (context.AttackAmount <= 0f
            || string.IsNullOrEmpty(context.AttackElement)
            || context.AttackElement == "None")
        {
            return result;
        }

        if (QuickenDamageHandler.CanResolve(context))
        {
            float levelCoefficient = ReactionDamageCalculator.GetLevelCoefficient(
                context.SourceEntity != null ? context.SourceEntity.Level : 1);
            if (QuickenDamageHandler.TryResolve(
                    context,
                    levelCoefficient,
                    out QuickenDamageResolution quickenDamage))
            {
                result.FinalDamage = quickenDamage.FinalDamage;
                result.PrimaryDamageReactionType = quickenDamage.Type;
                result.TriggeredReactions.Add(new ReactionOccurrence
                {
                    Type = quickenDamage.Type,
                    DisplayName = quickenDamage.DisplayName,
                    SourceEntity = context.SourceEntity,
                    Target = context.Target,
                    SourceEffectID = context.SourceEffectID,
                    InvolvedElements = BuildElements(context.AttackElement)
                });
            }
        }

        // 同元素共存时必须先移除旧附着，使本次新元素量参与后续反应。
        // 反应结束后只写回本次攻击的剩余元素量。
        ElementAuraSystem.PrepareIncomingAura(context);

        if (context.CanTriggerReaction)
            ResolvePlannedPools(context, result);

        ReactionType coreBlockingReaction = FindCoreBlockingReaction(result);
        if (context.CanTriggerReaction
            && BloomSecondaryReactionHandler.TryResolve(
                context,
                coreBlockingReaction,
                0f,
                out BloomSecondaryReactionResolution bloomSecondary))
        {
            result.DerivedHits.AddRange(bloomSecondary.DerivedHits);
            result.TriggeredReactions.Add(new ReactionOccurrence
            {
                Type = bloomSecondary.Type,
                DisplayName = bloomSecondary.DisplayName,
                SourceEntity = context.SourceEntity,
                Target = context.Target,
                SourceEffectID = context.SourceEffectID,
                InvolvedElements = BuildElements(context.AttackElement)
            });
        }

        ElementAuraSystem.CommitIncomingAura(context, result.RemainingAttackAmount);
        ElectroChargedReactionHandler.CleanupInvalidStates();

        return result;
    }

    private static void ResolvePlannedPools(ReactionContext context, ReactionResult result)
    {
        List<ReactionPoolTarget> pools = SnapshotReactionPools(context);
        if (pools.Count == 0)
        {
            result.RemainingAttackAmount = context.AttackAmount;
            return;
        }

        ReactionPoolTarget hydro = FindPool(pools, "Hydro", ReactionPoolKind.NormalAura);
        ReactionPoolTarget electro = FindPool(pools, "Electro", ReactionPoolKind.NormalAura);
        bool splitElectroCharged = ElectroChargedReactionHandler.IsElectroCharged(context.Target)
            && hydro != null && electro != null
            && ReactionPairRules.GetReaction(context.AttackElement, "Hydro") != ReactionType.None
            && ReactionPairRules.GetReaction(context.AttackElement, "Electro") != ReactionType.None;

        if (splitElectroCharged)
        {
            float half = context.AttackAmount * 0.5f;
            float hydroRemain = ResolveAgainstPool(context, result, hydro, half);
            float electroRemain = ResolveAgainstPool(context, result, electro, half);
            result.RemainingAttackAmount = Math.Max(0f, hydroRemain + electroRemain);
            return;
        }

        float remaining = context.AttackAmount;
        foreach (ReactionPoolTarget pool in pools)
        {
            if (remaining <= 0f) break;
            remaining = ResolveAgainstPool(context, result, pool, remaining);
        }
        result.RemainingAttackAmount = Math.Max(0f, remaining);
    }

    private static float ResolveAgainstPool(
        ReactionContext original,
        ReactionResult aggregate,
        ReactionPoolTarget pool,
        float attackQuota)
    {
        if (attackQuota <= 0f || pool == null) return Math.Max(0f, attackQuota);
        var branch = CloneForPool(original, pool, attackQuota);
        if (!TryResolveSingle(branch, aggregate, out SingleReactionResolution resolved))
            return attackQuota;

        if (resolved.ChangesPrimaryDamage
            && aggregate.PrimaryDamageReactionType == ReactionType.None)
        {
            aggregate.FinalDamage = resolved.FinalDamage;
            aggregate.PrimaryDamageReactionType = resolved.Type;
        }
        if (resolved.Type != ReactionType.None)
        {
            aggregate.TriggeredReactions.Add(new ReactionOccurrence
            {
                Type = resolved.Type,
                DisplayName = resolved.DisplayName,
                SourceEntity = original.SourceEntity,
                Target = original.Target,
                SourceEffectID = original.SourceEffectID,
                InvolvedElements = BuildElements(original.AttackElement, resolved.ReactedElement ?? pool.Element)
            });
        }
        return Math.Max(0f, resolved.RemainingAttackAmount);
    }

    private static bool TryResolveSingle(
        ReactionContext context,
        ReactionResult aggregate,
        out SingleReactionResolution resolved)
    {
        resolved = default;
        if (AmplifyingReactionHandler.TryResolve(context, out AmplifyingReactionResolution amplifying))
        {
            resolved = new SingleReactionResolution
            {
                Type = amplifying.Type,
                DisplayName = amplifying.DisplayName,
                RemainingAttackAmount = amplifying.RemainingAttackAmount,
                FinalDamage = amplifying.FinalDamage,
                ChangesPrimaryDamage = true
            };
            return true;
        }

        if (context.AttackElement == "Geo"
            && CrystallizeReactionHandler.TryResolve(context, out CrystallizeReactionResolution crystallize))
        {
            resolved = new SingleReactionResolution
            {
                Type = crystallize.GrantsPartyShield ? ReactionType.Crystallize : ReactionType.None,
                DisplayName = crystallize.DisplayName,
                ReactedElement = crystallize.ReactedElement,
                RemainingAttackAmount = crystallize.RemainingAttackAmount
            };
            return true;
        }

        if (!TransformativeReactionHandler.TryResolve(
                context,
                out TransformativeReactionResolution transformative))
            return false;

        if (transformative.DerivedHits != null)
            aggregate.DerivedHits.AddRange(transformative.DerivedHits);
        resolved = new SingleReactionResolution
        {
            Type = transformative.Type,
            DisplayName = transformative.DisplayName,
            RemainingAttackAmount = transformative.RemainingAttackAmount
        };
        return true;
    }

    private static List<ReactionPoolTarget> SnapshotReactionPools(ReactionContext context)
    {
        var pools = new List<ReactionPoolTarget>();
        if (context == null || context.Target == null) return pools;

        bool burning = BurningReactionHandler.IsBurning(context.Target);
        bool frozen = FrozenReactionHandler.IsFrozen(context.Target) && !context.IgnoreFrozenAura;
        if (burning)
        {
            AddNormalPool(context, pools, "Dendro");
            AddNormalPool(context, pools, "Pyro");
        }
        if (frozen)
        {
            AddNormalPool(context, pools, "Hydro");
            AddNormalPool(context, pools, "Cryo");
        }

        foreach (string element in new[] { "Pyro", "Hydro", "Cryo", "Electro", "Dendro" })
            AddNormalPool(context, pools, element);

        if (burning)
            AddPool(context, pools, "Pyro", ReactionPoolKind.Burning,
                BurningReactionHandler.GetBurningAuraAsPyro(context.Target));
        if (frozen)
            AddPool(context, pools, "Cryo", ReactionPoolKind.Frozen,
                FrozenReactionHandler.GetFrozenAuraAsCryo(context.Target));
        return pools;
    }

    private static void AddNormalPool(
        ReactionContext context,
        List<ReactionPoolTarget> pools,
        string element)
    {
        if (FindPool(pools, element, ReactionPoolKind.NormalAura) != null) return;
        AddPool(context, pools, element, ReactionPoolKind.NormalAura,
            context.Target.GetAura(element)?.AuraAmount ?? 0f);
    }

    private static void AddPool(
        ReactionContext context,
        List<ReactionPoolTarget> pools,
        string element,
        ReactionPoolKind kind,
        float amount)
    {
        if (amount <= 0f
            || !context.AllowsReactionPool(element, kind)
            || ReactionPairRules.GetReaction(context.AttackElement, element) == ReactionType.None)
            return;
        pools.Add(new ReactionPoolTarget { Element = element, Kind = kind, Amount = amount });
    }

    private static ReactionPoolTarget FindPool(
        List<ReactionPoolTarget> pools,
        string element,
        ReactionPoolKind kind)
    {
        foreach (ReactionPoolTarget pool in pools)
            if (pool.Element == element && pool.Kind == kind) return pool;
        return null;
    }

    private static ReactionContext CloneForPool(
        ReactionContext source,
        ReactionPoolTarget pool,
        float attackAmount)
    {
        return new ReactionContext
        {
            SourceEntity = source.SourceEntity,
            Target = source.Target,
            SourceKind = source.SourceKind,
            SourceSkillID = source.SourceSkillID,
            SourceEffectID = source.SourceEffectID,
            EffectExecutionID = source.EffectExecutionID,
            ApplicationPhase = source.ApplicationPhase,
            AttackElement = source.AttackElement,
            AttackAmount = attackAmount,
            PreReactionDamage = source.PreReactionDamage,
            DamageComponents = source.DamageComponents,
            DamageType = source.DamageType,
            PoiseDamage = source.PoiseDamage,
            CanTriggerReaction = source.CanTriggerReaction,
            CanApplyAura = false,
            SourceSnapshotOverride = source.SourceSnapshotOverride,
            SkipImmediateAuraDecay = source.SkipImmediateAuraDecay,
            IgnoreFrozenAura = source.IgnoreFrozenAura,
            RestrictedAuraElement = pool.Element,
            RestrictedAuraKind = pool.Kind
        };
    }

    private static ReactionType FindCoreBlockingReaction(ReactionResult result)
    {
        foreach (ReactionOccurrence occurrence in result.TriggeredReactions)
        {
            if (occurrence.Type == ReactionType.Overloaded
                || occurrence.Type == ReactionType.ElectroCharged)
                return occurrence.Type;
        }
        return ReactionType.None;
    }

    private static List<string> BuildElements(params string[] elements)
    {
        var result = new List<string>();
        if (elements == null) return result;
        foreach (string element in elements)
            if (!string.IsNullOrWhiteSpace(element) && !result.Contains(element)) result.Add(element);
        return result;
    }

}
