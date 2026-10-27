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
        public BloomCoreInstance CreatedBloomCore;
        public PendingLunarConduct PendingLunarConduct;
        public PendingLunarCrystallize PendingLunarCrystallize;
        public PendingStellarSuperconduct PendingStellarSuperconduct;
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
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        using var execution = BattleEventHub.BeginEffectExecution(context.EffectExecutionID);
        using var hit = BattleEventHub.BeginHit(context.LogicalHitID != 0 ? context.LogicalHitID : BattleEventHub.CurrentHitId,
            context.TargetIndex);
        using var elements = ElementEventScope.Begin("ElementInteraction", context.Target);
        context.EnemyShieldHit = EnemyElementShieldSystem.PrepareHit(
            context.Target, context.AttackElement, context.AttackAmount);
        ReactionResult result = ResolveCore(context);
        // 风先保存扩散源，随后由主命中或纯元素扩散批次扣盾；其余接触在反应完成后统一提交。
        if (context.AttackElement != "Anemo")
        {
            EnemyElementShieldSystem.CommitHit(context.EnemyShieldHit);
            if (context.EnemyShieldHit.HadShieldAtStart) ElectroChargedReactionHandler.CleanupInvalidStates();
        }
        // 特殊技能公式独立于接触时的普通增幅/激化；真实元素交互及派生反应仍照常声明。
        if (context.DamageComponents?.Properties.UsesSpecialFormula == true)
        {
            result.FinalDamage = context.PreReactionDamage;
            result.PrimaryDamageReactionType = context.DamageComponents.Properties.Reaction;
        }
        result.RuntimeHitID = BattleEventHub.CurrentHitId;
        result.EffectExecutionID = context.EffectExecutionID;
        result.TargetIndex = context.TargetIndex;
        if (result.PendingReverseSwirl == null && context.Target != null && context.AttackAmount > 0f && context.AttackElement != "Anemo"
            && context.AttackElement != "None" && context.AttackElement != "Physical"
            && !string.IsNullOrEmpty(context.AttackElement) && (context.CanApplyAura || context.CanTriggerReaction))
        {
            result.ContactDeclaration = BattleEventHub.Record(result, new ElementContactEventData(context, result),
                causes: context.CauseEventIds);
            elements.AddCause(result.ContactDeclaration.Header.EventId);
            foreach (ReactionOccurrence occurrence in result.TriggeredReactions)
            {
                occurrence.Declaration = BattleEventHub.Record(occurrence, new ReactionEventData(occurrence),
                    causes: new[] { result.ContactDeclaration.Header.EventId });
                StellarTeamBonusHandler.Commit(occurrence);
            }
            foreach (ReactionDerivedHit derived in result.DerivedHits)
                if (derived.CauseEventIds == null)
                {
                    var ids = new List<long>();
                    foreach (var occurrence in result.TriggeredReactions)
                        if (occurrence.Type == derived.ReactionType) ids.Add(occurrence.Declaration.Header.EventId);
                    if (ids.Count == 0) ids.Add(result.ContactDeclaration.Header.EventId);
                    derived.CauseEventIds = ids.AsReadOnly();
                }
            BattleEventHub.Dispatch(result.ContactDeclaration);
        }
        return result;
    }

    private static ReactionResult ResolveCore(ReactionContext context)
    {

        var result = ReactionResult.Unchanged(context);
        if (context.Target == null)
            return result;
        if (DamageProperties.IsFixed(context.AttackElement)) return result;


        if (context.ApplicationPhase <= 0 && BattleManager.Instance != null)
            context.ApplicationPhase = (int)BattleManager.Instance.CurrentPhase;

        // 碎冰先于其余元素反应判定；主命中结算后落地，不要求实际扣到HP。
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
            || context.AttackElement == "Physical")
        {
            return result;
        }

        if (SwirlReactionHandler.CanReverseSwirl(context))
        {
            ElementAuraSystem.PrepareIncomingAura(context);
            result.PendingReverseSwirl = SwirlReactionHandler.PrepareBatch(new[] { context });
            result.RemainingAttackAmount = 0f;
            return result; // 保留原命中伤害；附着消耗、声明和传播由命中后的扩散事务提交。
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
        if (context.EnemyShieldHit?.HadShieldAtStart == true)
        {
            ResolveShieldPools(context, result);
            return;
        }
        // Geo由专用处理器一次选取真实元素（含燃烧/冻结虚拟量），不经不包含Geo的普通两两兼容筛选。
        // 同一份岩配额也不能对共存的每种元素重复结算。
        if (context.AttackElement == "Geo")
        {
            result.RemainingAttackAmount = TryResolveSingle(context, result, out SingleReactionResolution geo)
                ? ApplySingleResolution(context, result, geo, geo.ReactedElement)
                : context.AttackAmount;
            return;
        }

        List<ReactionPoolTarget> pools = SnapshotReactionPools(context);
        if (pools.Count == 0)
        {
            result.RemainingAttackAmount = context.AttackAmount;
            return;
        }

        ReactionPoolTarget hydro = FindPool(pools, "Hydro", ReactionPoolKind.Any);
        ReactionPoolTarget electro = FindPool(pools, "Electro", ReactionPoolKind.Any);
        // 月感电允许水雷共存但不挂个人感电状态；分份依据真实元素池。
        bool splitElectroCharged = hydro != null && electro != null
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

    private static void ResolveShieldPools(ReactionContext context, ReactionResult result)
    {
        float remaining = context.AttackAmount;
        EnemyShieldHitResult shieldHit = context.EnemyShieldHit;
        string element = shieldHit.ShieldRow == "Freeze" ? "Cryo" : shieldHit.ShieldAtStart.Element;
        // 虚拟燃/冻池先结算，不能把同元素盾当作普通附着覆盖，也不能让同一份攻击反应两次。
        if (BurningReactionHandler.IsBurning(context.Target))
        {
            var burning = new ReactionPoolTarget { Element = "Pyro", Kind = ReactionPoolKind.Burning,
                Amount = BurningReactionHandler.GetBurningAuraAsPyro(context.Target) };
            remaining = ResolveAgainstPool(context, result, burning, remaining);
        }
        string coexisting = element == "Hydro" ? "Electro" : element == "Electro" ? "Hydro" : null;
        ElementPoolReading coexist = coexisting != null ? ElementQuery.ReadNormal(context.Target, coexisting) : null;
        bool split = coexist != null && ReactionPairRules.GetReaction(context.AttackElement, element) != ReactionType.None
            && ReactionPairRules.GetReaction(context.AttackElement, coexisting) != ReactionType.None;
        if (split)
        {
            // 与普通感电共存相同，水/雷各分得半份；一份打普通池，一份打盾池。
            remaining *= .5f;
            ResolveAgainstPool(context, result, new ReactionPoolTarget
            { Element = coexisting, Kind = ReactionPoolKind.NormalAura, Amount = coexist.Amount }, remaining);
        }
        else
        {
            foreach (string normalElement in new[] { "Pyro", "Hydro", "Cryo", "Electro", "Dendro" })
            {
                if (remaining <= 0f) break;
                if (normalElement == element || normalElement == shieldHit.ShieldAtStart.Element) continue;
                ElementPoolReading normal = ElementQuery.ReadNormal(context.Target, normalElement);
                if (normal == null) continue;
                remaining = ResolveAgainstPool(context, result, new ReactionPoolTarget
                { Element = normalElement, Kind = ReactionPoolKind.NormalAura, Amount = normal.Amount }, remaining);
            }
        }
        // 燃烧/普通共存池先消耗自己的份额，只有剩余外来量用于盾本体。
        shieldHit.AttackElementAmount = remaining;
        if (shieldHit.ShieldAtStart.Element == "Hydro" && shieldHit.ShieldRow == "Freeze"
            && !context.IgnoreFrozenAura && remaining > 0f)
        {
            var frozen = new ReactionPoolTarget { Element = "Cryo", Kind = ReactionPoolKind.Frozen,
                Amount = FrozenReactionHandler.GetFrozenAuraAsCryo(context.Target) };
            remaining = ResolveAgainstPool(context, result, frozen, remaining);
        }
        // 水盾冻结期间，同一次接触既消耗冻元素又按Freeze行扣GU，不额外使用水盾行。
        if (remaining > 0f)
            remaining = ResolveAgainstPool(context, result, new ReactionPoolTarget
            { Element = element, Kind = ReactionPoolKind.EnemyShield, Amount = shieldHit.ShieldAtStart.Value }, remaining);
        result.RemainingAttackAmount = 0f; // 单独允许感电共存量在下方写回，破盾溢出一律丢弃。
        if ((element == "Hydro" && context.AttackElement == "Electro"
             || element == "Electro" && context.AttackElement == "Hydro") && result.HasReaction)
            result.RemainingAttackAmount = remaining;
        EnemyElementShieldSystem.CommitHit(shieldHit);
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

        return ApplySingleResolution(original, aggregate, resolved, pool.Element);
    }

    private static float ApplySingleResolution(ReactionContext original, ReactionResult aggregate,
        SingleReactionResolution resolved, string fallbackElement)
    {
        if (resolved.ChangesPrimaryDamage
            && aggregate.PrimaryDamageReactionType == ReactionType.None)
        {
            aggregate.FinalDamage = resolved.FinalDamage;
            aggregate.PrimaryDamageReactionType = resolved.Type;
        }
        bool duplicateShieldReaction = aggregate.EnemyShieldHit?.HadShieldAtStart == true
            && aggregate.TriggeredReactions.Exists(item => item.Type == resolved.Type);
        if (resolved.Type != ReactionType.None && !duplicateShieldReaction)
        {
            aggregate.TriggeredReactions.Add(new ReactionOccurrence
            {
                Type = resolved.Type,
                DisplayName = resolved.DisplayName,
                SourceEntity = original.SourceEntity ?? original.SourceSnapshotOverride?.SourceEntity,
                Target = original.Target,
                SourceEffectID = original.SourceEffectID,
                CreatedBloomCore = resolved.CreatedBloomCore,
                PendingLunarConduct = resolved.PendingLunarConduct,
                PendingLunarCrystallize = resolved.PendingLunarCrystallize,
                PendingStellarSuperconduct = resolved.PendingStellarSuperconduct,
                InvolvedElements = BuildElements(original.AttackElement, resolved.ReactedElement ?? fallbackElement)
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

        if (CrystallizeReactionHandler.TryResolve(context, out CrystallizeReactionResolution crystallize))
        {
            resolved = new SingleReactionResolution
            {
                Type = crystallize.Type,
                PendingLunarCrystallize = crystallize.PendingLunarCrystallize,
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

        if (transformative.DerivedHits != null && !(aggregate.EnemyShieldHit?.HadShieldAtStart == true
            && aggregate.TriggeredReactions.Exists(item => item.Type == transformative.Type)))
            aggregate.DerivedHits.AddRange(transformative.DerivedHits);
        resolved = new SingleReactionResolution
        {
            Type = transformative.Type,
            DisplayName = transformative.DisplayName,
            CreatedBloomCore = transformative.CreatedBloomCore,
            PendingLunarConduct = transformative.PendingLunarConduct,
            PendingStellarSuperconduct = transformative.PendingStellarSuperconduct,
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

        if (ElementQuery.ReadIntrinsic(context.Target, "Geo") != null)
            AddPool(context, pools, "Geo", ReactionPoolKind.Intrinsic,
                ReactionPairRules.RequiredAura(context.AttackElement, "Geo", context.AttackAmount));

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
        var aura = ElementQuery.ReadReactionAura(context, element);
        if (aura == null || FindPool(pools, element, aura.Kind) != null) return;
        AddPool(context, pools, element, aura.Kind, aura.IsIntrinsic
            ? ReactionPairRules.RequiredAura(context.AttackElement, element, context.AttackAmount) : aura.Amount);
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
            if (pool.Element == element && (pool.Kind == kind || kind == ReactionPoolKind.Any)) return pool;
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
            EquipmentSource = source.EffectiveEquipmentSource,
            Target = source.Target,
            SourceKind = source.SourceKind,
            SourceSkillID = source.SourceSkillID,
            SourceEffectID = source.SourceEffectID,
            EffectExecutionID = source.EffectExecutionID,
            LogicalHitID = source.LogicalHitID,
            TargetIndex = source.TargetIndex,
            CauseEventIds = source.CauseEventIds,
            ApplicationPhase = source.ApplicationPhase,
            AttackElement = source.AttackElement,
            AttackAmount = attackAmount,
            PreReactionDamage = source.PreReactionDamage,
            DamageComponents = source.DamageComponents,
            DamageType = source.DamageType,
            CanTriggerReaction = source.CanTriggerReaction,
            CanApplyAura = false,
            SourceSnapshotOverride = source.SourceSnapshotOverride,
            SkipImmediateAuraDecay = source.SkipImmediateAuraDecay,
            IgnoreFrozenAura = source.IgnoreFrozenAura,
            CanTriggerShatter = source.CanTriggerShatter,
            EnemyShieldHit = source.EnemyShieldHit,
            PreserveIncomingAuraBeforeReaction = source.PreserveIncomingAuraBeforeReaction,
            RestrictedAuraElement = pool.Element,
            RestrictedAuraKind = pool.Kind
        };
    }

    private static ReactionType FindCoreBlockingReaction(ReactionResult result)
    {
        foreach (ReactionOccurrence occurrence in result.TriggeredReactions)
        {
            if (occurrence.Type == ReactionType.Overloaded
                || occurrence.Type == ReactionType.ElectroCharged
                || occurrence.Type == ReactionType.LunarElectroCharged)
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
