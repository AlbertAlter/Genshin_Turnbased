using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 普通扩散的全局批处理。PrepareBatch 在主伤害前保存扩散源；ResolvePreparedBatch
/// 在主伤害后依次完成扩散伤害、相邻传播与全局残留清算。
/// </summary>
public static class SwirlReactionHandler
{
    private const float Epsilon = 0.0001f;
    private const float SwirlMultiplier = 1.2f;
    private static readonly string[] NormalElements = { "Pyro", "Hydro", "Cryo", "Electro", "Dendro" };

    private sealed class ExternalAmount
    {
        public string Element;
        public float Amount;
        public ReactionContext Source;
        public readonly List<long> CauseEventIds = new List<long>();
        public BattleDeclaration ContactDeclaration;
        public readonly List<PropagationPart> Parts = new List<PropagationPart>();
    }

    // 每个初始受击目标、每种传播元素各一条来源链；不按角色配置ID或全局“最后一次”合并。
    private sealed class PropagationChain
    {
        public ReactionContext Source;
        public readonly HashSet<BattleEntity> Visited = new HashSet<BattleEntity>();
        public readonly HashSet<FieldPosition> Positions = new HashSet<FieldPosition>();
    }
    private sealed class PropagationPart
    {
        public PropagationChain Chain;
        public float Amount;
    }

    private sealed class PairPlan
    {
        public SwirlAuraAtom Own;
        public ExternalAmount External;
        public ReactionType Type;
        public float OwnConsumed;
        public float ExternalConsumed;
        public PendingLunarConduct Lunar;
        public PendingStellarSuperconduct Stellar;
        public PendingLunarCrystallize LunarCrystallize;
    }

    public static SwirlPreparedBatch PrepareBatch(IEnumerable<ReactionContext> contexts)
    {
        var batch = new SwirlPreparedBatch();
        if (contexts == null) return batch;
        foreach (ReactionContext context in contexts)
        {
            if (context == null || context.Target == null || context.AttackElement != "Anemo" && !CanReverseSwirl(context)
                || context.AttackAmount <= Epsilon || !context.CanTriggerReaction)
                continue;
            var snapshot = new SwirlTargetSnapshot
            {
                Context = context,
                Target = context.Target,
                Side = context.Target.Position != null ? context.Target.Position.Side : context.Target.Side,
                SlotIndex = context.Target.Position != null ? context.Target.Position.SlotIndex : context.Target.SlotPosition,
                WasAlive = context.Target.IsAlive,
                IsReverse = context.AttackElement != "Anemo"
            };
            if (snapshot.IsReverse)
                snapshot.SpreadAtoms.Add(new SwirlAuraAtom { Element = context.AttackElement,
                    Kind = SwirlAuraKind.Incoming, Amount = context.AttackAmount, SpreadAmount = context.AttackAmount });
            else
            {
                AddNormalAtoms(snapshot);
                context.EnemyShieldHit = context.EnemyShieldHit ?? EnemyElementShieldSystem.PrepareHit(
                    context.Target, context.AttackElement, context.AttackAmount);
                string shieldElement = context.EnemyShieldHit.ShieldRow == "Freeze"
                    ? "Cryo" : context.EnemyShieldHit.ShieldAtStart?.Element;
                bool frozenHydroShield = context.EnemyShieldHit.ShieldAtStart?.Element == "Hydro"
                    && context.EnemyShieldHit.ShieldRow == "Freeze";
                if (context.EnemyShieldHit.HadShieldAtStart && !frozenHydroShield
                    && ReactionPairRules.IsSwirlable(shieldElement))
                    AddVirtualAtom(snapshot, shieldElement, SwirlAuraKind.EnemyShield,
                        context.EnemyShieldHit.ShieldAtStart.Value);
                AddVirtualAtom(snapshot, "Cryo", SwirlAuraKind.Frozen,
                    context.IgnoreFrozenAura ? 0f : FrozenReactionHandler.GetFrozenAuraAsCryo(context.Target));
                AddVirtualAtom(snapshot, "Pyro", SwirlAuraKind.Burning,
                    BurningReactionHandler.GetBurningAuraAsPyro(context.Target));
                if (shieldElement == "Pyro" && BurningReactionHandler.IsBurning(context.Target))
                {
                    var burningAtoms = snapshot.SpreadAtoms.FindAll(atom => atom.Kind == SwirlAuraKind.Burning);
                    AllocateAnemo(context.AttackAmount, burningAtoms);
                    float spent = 0f;
                    foreach (var atom in burningAtoms) spent += atom.SpreadAmount;
                    AllocateAnemo(Mathf.Max(0f, context.AttackAmount - spent),
                        snapshot.SpreadAtoms.FindAll(atom => atom.Kind != SwirlAuraKind.Burning));
                }
                else AllocateAnemo(context.AttackAmount, snapshot.SpreadAtoms);
                if (shieldElement == "Pyro" && BurningReactionHandler.IsBurning(context.Target))
                {
                    context.EnemyShieldHit.AttackElementAmount = 0f;
                    foreach (var atom in snapshot.SpreadAtoms)
                        if (atom.Kind == SwirlAuraKind.EnemyShield)
                            context.EnemyShieldHit.AttackElementAmount += atom.SpreadAmount;
                }
            }
            batch.Targets.Add(snapshot);
            if (batch.EffectExecutionID == 0) batch.EffectExecutionID = context.EffectExecutionID;
        }
        return batch;
    }

    public static bool CanReverseSwirl(ReactionContext context)
        => context?.Target != null && context.CanTriggerReaction && context.AttackAmount > Epsilon
            && ReactionPairRules.IsSwirlable(context.AttackElement)
            && context.AllowsReactionPool("Anemo", ReactionPoolKind.Intrinsic)
            && ElementQuery.ReadIntrinsic(context.Target, "Anemo") != null;

    public static SwirlBatchResult ResolveBatch(IEnumerable<ReactionContext> contexts)
        => ResolvePreparedBatch(PrepareBatch(contexts));

    public static SwirlBatchResult ResolvePreparedBatch(SwirlPreparedBatch batch)
    {
        var output = new SwirlBatchResult();
        if (batch == null || batch.Resolved || batch.Targets.Count == 0) return output;
        batch.Resolved = true;
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        using var execution = BattleEventHub.BeginEffectExecution(batch.EffectExecutionID);
        using var logicalHit = BattleEventHub.BeginHit(batch.Targets[0].Context.LogicalHitID);
        var spread = new Dictionary<FieldPosition, Dictionary<string, ExternalAmount>>();
        var stageOne = new ReactionResult();

        foreach (SwirlTargetSnapshot source in batch.Targets)
        {
            if (source == null || source.Target == null) continue;
            using var hit = BattleEventHub.BeginHit(source.Context.LogicalHitID, source.Context.TargetIndex);
            int derivedStart = stageOne.DerivedHits.Count;
            EnemyElementShieldSystem.CommitHit(source.Context.EnemyShieldHit);
            var contactResult = ReactionResult.Unchanged(source.Context);
            output.DirectTargets.Add(source.Target);
            bool triggered = false;
            var involvedElements = new List<string> { "Anemo" };
            var damageElements = new HashSet<string>();
            foreach (SwirlAuraAtom atom in source.SpreadAtoms)
            {
                if (atom.SpreadAmount <= Epsilon) continue;
                triggered = true;
                if (!involvedElements.Contains(atom.Element)) involvedElements.Add(atom.Element);
                if (source.Context.EnemyShieldHit?.HadShieldAtStart != true || damageElements.Add(atom.Element))
                {
                    var swirlHit = BuildTransformativeHit(
                        source.Context, source.Target, atom.Element, ReactionType.Swirl,
                        SwirlMultiplier);
                    swirlHit.EnemyShieldHit = source.Context.EnemyShieldHit;
                    stageOne.DerivedHits.Add(swirlHit);
                }
                contactResult.RemainingAttackAmount = Mathf.Max(0f, contactResult.RemainingAttackAmount - atom.SpreadAmount);
            }
            if (triggered)
            {
                var occurrence = new ReactionOccurrence
                {
                    Type = ReactionType.Swirl,
                    DisplayName = "扩散",
                    SourceEntity = source.Context.SourceEntity,
                    Target = source.Target,
                    SourceEffectID = source.Context.SourceEffectID,
                    InvolvedElements = involvedElements
                };
                output.Reaction.TriggeredReactions.Add(occurrence);
                contactResult.TriggeredReactions.Add(occurrence);
            }
            source.ContactDeclaration = BattleEventHub.Record(source, new ElementContactEventData(source.Context, contactResult),
                causes: source.Context.CauseEventIds);
            foreach (var occurrence in contactResult.TriggeredReactions)
                occurrence.Declaration = BattleEventHub.Record(occurrence, new ReactionEventData(occurrence),
                    causes: new[] { source.ContactDeclaration.Header.EventId });
            var damageCauses = new List<long>();
            foreach (var occurrence in contactResult.TriggeredReactions) damageCauses.Add(occurrence.Declaration.Header.EventId);
            for (int i = derivedStart; i < stageOne.DerivedHits.Count; i++) stageOne.DerivedHits[i].CauseEventIds = damageCauses.AsReadOnly();
            var outgoing = new Dictionary<string, float>();
            foreach (var atom in source.SpreadAtoms) if (atom.SpreadAmount > Epsilon)
                outgoing[atom.Element] = GetValue(outgoing, atom.Element) + atom.SpreadAmount;
            foreach (var item in outgoing)
            {
                var chain = new PropagationChain { Source = source.Context };
                chain.Visited.Add(source.Target);
                if (source.Target.Position != null) chain.Positions.Add(source.Target.Position);
                AddPropagation(chain, source.Target, item.Key, item.Value,
                    new[] { source.ContactDeclaration.Header.EventId }, spread);
            }
        }

        // 第一阶段伤害必须先落地，死亡的直接目标在第二阶段只继续传播。
        ReactionEffectExecutor.ExecuteDerivedHits(stageOne);
        foreach (SwirlTargetSnapshot source in batch.Targets)
        {
            if (source == null || source.Target == null) continue;
            using var hit = BattleEventHub.BeginHit(source.Context.LogicalHitID, source.Context.TargetIndex);
            using var elements = ElementEventScope.Begin("SwirlOriginConsumption", source.Target);
            elements.AddCause(source.ContactDeclaration.Header.EventId);
            foreach (SwirlAuraAtom atom in source.SpreadAtoms)
                if (atom.SpreadAmount > Epsilon)
                    ConsumeAtom(source.Target, atom, atom.SpreadAmount);
            BattleEventHub.Dispatch(source.ContactDeclaration);
        }

        // 每一波先整体规划再提交；只有遇到固有风并实际反应的份额继续传导。
        // 传导不创建攻击Hit，也不再次生成扩散伤害。各来源链自带访问集合，不会回到起点或重复经过目标。
        while (spread.Count > 0)
        {
            var next = new Dictionary<FieldPosition, Dictionary<string, ExternalAmount>>();
            var transaction = new ReactionTransaction();
            var effects = new List<Action>();
            foreach (var entry in spread)
            {
                FieldPosition position = entry.Key;
                var external = entry.Value;
                effects.Add(() => TriggerCoresAtPosition(position, external, output.Reaction));
                BattleEntity target = position != null && position.IsOccupied ? position.Occupant : null;
                if (target == null || !target.IsOnField || IsStageOneDeadDirectTarget(batch, target)) continue;
                PlanStageTwo(target, external, transaction, effects, output.Reaction, next);
            }
            transaction.Commit();
            foreach (Action effect in effects) effect();
            // 超载等其他反应保留原规则；只是后续扩散本身不产生第二次伤害。
            ReactionEffectExecutor.ExecuteDerivedHits(output.Reaction);
            output.Reaction.DerivedHits.Clear();
            foreach (var entry in spread)
            {
                BattleEntity target = entry.Key != null && entry.Key.IsOccupied ? entry.Key.Occupant : null;
                if (target != null && target.IsOnField)
                    ResolveStageThree(target, FirstSource(entry.Value), output.Reaction, PropagationCauses(entry.Value));
            }
            spread = next;
        }
        ElectroChargedReactionHandler.CleanupInvalidStates();
        return output;
    }

    private static void AddNormalAtoms(SwirlTargetSnapshot snapshot)
    {
        foreach (string element in NormalElements)
        {
            if (element == "Dendro") continue;
            ElementPoolReading aura = ElementQuery.ReadAura(snapshot.Target, element);
            if (aura?.IsPresent == true)
                snapshot.SpreadAtoms.Add(new SwirlAuraAtom
                { Element = element, Kind = aura.IsIntrinsic ? SwirlAuraKind.Intrinsic : SwirlAuraKind.Normal,
                    Amount = aura.IsIntrinsic ? 0f : aura.Amount });
        }
    }

    private static void AddVirtualAtom(SwirlTargetSnapshot snapshot, string element, SwirlAuraKind kind, float amount)
    {
        if (amount > Epsilon)
            snapshot.SpreadAtoms.Add(new SwirlAuraAtom { Element = element, Kind = kind, Amount = amount });
    }

    private static void AllocateAnemo(float anemo, List<SwirlAuraAtom> atoms)
    {
        var active = new List<SwirlAuraAtom>(atoms);
        float remaining = Mathf.Max(0f, anemo);
        while (remaining > Epsilon && active.Count > 0)
        {
            float share = remaining / active.Count;
            float spent = 0f;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                SwirlAuraAtom atom = active[i];
                float capacity = atom.Kind == SwirlAuraKind.Intrinsic ? remaining : atom.Amount - atom.SpreadAmount;
                float take = Mathf.Min(share, capacity);
                atom.SpreadAmount += take;
                spent += take;
                if (capacity - take <= Epsilon) active.RemoveAt(i);
            }
            if (spent <= Epsilon) break;
            remaining -= spent;
        }
    }

    private static void ConsumeAtom(BattleEntity target, SwirlAuraAtom atom, float amount)
    {
        if (atom.Kind == SwirlAuraKind.Intrinsic || atom.Kind == SwirlAuraKind.Incoming
            || atom.Kind == SwirlAuraKind.EnemyShield) return;
        if (atom.Kind == SwirlAuraKind.Frozen) FrozenReactionHandler.ConsumeFrozenAura(target, amount);
        else if (atom.Kind == SwirlAuraKind.Burning) BurningReactionHandler.ConsumeBurningAura(target, amount);
        else target.ConsumeAura(atom.Element, amount);
    }

    private static void AddPropagation(PropagationChain chain, BattleEntity origin, string element, float quantity,
        IReadOnlyList<long> causes,
        Dictionary<FieldPosition, Dictionary<string, ExternalAmount>> spread)
    {
        BattleField field = origin.Position != null ? origin.Position.OwnerField : null;
        if (field == null && BattleManager.Instance != null) field = BattleManager.Instance.Field;
        int slot = origin.Position != null ? origin.Position.SlotIndex : origin.SlotPosition;
        BattleSide side = origin.Position != null ? origin.Position.Side : origin.Side;
        if (field == null || slot <= 0 || quantity <= Epsilon) return;
        foreach (int index in BattlePositionSystem.GetAdjacentPositions(side, slot, 1))
        {
            if (index == slot) continue;
            FieldPosition position = field.GetSlot(side, index);
            if (position == null || chain.Positions.Contains(position)
                || position.Occupant != null && chain.Visited.Contains(position.Occupant)) continue;
            chain.Positions.Add(position);
            if (position.Occupant != null) chain.Visited.Add(position.Occupant);
            if (!spread.TryGetValue(position, out Dictionary<string, ExternalAmount> byElement))
                spread[position] = byElement = new Dictionary<string, ExternalAmount>();
            if (!byElement.TryGetValue(element, out ExternalAmount amount))
                byElement[element] = amount = new ExternalAmount { Element = element, Source = chain.Source };
            amount.Amount += quantity;
            amount.Parts.Add(new PropagationPart { Chain = chain, Amount = quantity });
            foreach (long cause in causes)
                if (!amount.CauseEventIds.Contains(cause)) amount.CauseEventIds.Add(cause);
        }
    }

    private static void PlanStageTwo(BattleEntity target, Dictionary<string, ExternalAmount> external,
        ReactionTransaction transaction, List<Action> effects, ReactionResult result,
        Dictionary<FieldPosition, Dictionary<string, ExternalAmount>> next)
    {
        if (target.GetEnemyElementalShield() != null)
        {
            // 盾不进入普通附着事务。传播仍使用统一反应入口，各份额保留真实来源与因果。
            foreach (ExternalAmount amount in external.Values)
            {
                ExternalAmount captured = amount;
                effects.Add(() =>
                {
                    var context = CloneFor(captured.Source, target, captured.Element, captured.Amount);
                    context.CanTriggerShatter = false;
                    context.CauseEventIds = captured.CauseEventIds;
                    ReactionResult contact = ReactionResolver.Resolve(context);
                    result.TriggeredReactions.AddRange(contact.TriggeredReactions);
                    foreach (var derived in contact.DerivedHits)
                        if (derived.Target == target) derived.EnemyShieldHit = contact.EnemyShieldHit;
                    result.DerivedHits.AddRange(contact.DerivedHits);
                });
            }
            return;
        }
        List<SwirlAuraAtom> own = SnapshotReactableAuras(target);
        var pairs = new List<PairPlan>();
        foreach (SwirlAuraAtom atom in own)
        {
            var compatible = new List<ExternalAmount>();
            foreach (ExternalAmount ext in external.Values)
                if (ReactionPairRules.GetReaction(ext.Element, atom.Element) != ReactionType.None) compatible.Add(ext);
            foreach (ExternalAmount ext in compatible)
            {
                int ownCount = compatible.Count;
                int extCount = CountCompatibleOwn(ext, own);
                float extQuota = ext.Amount / extCount;
                float ownQuota = atom.Kind == SwirlAuraKind.Intrinsic
                    ? ReactionPairRules.RequiredAura(ext.Element, atom.Element, extQuota) : atom.Amount / ownCount;
                ReactionType type = ReactionPairRules.GetReaction(ext.Element, atom.Element);
                PendingLunarConduct lunar = null;
                PendingStellarSuperconduct stellar = null;
                if (type == ReactionType.ElectroCharged
                    && LunarElectroChargedReactionHandler.TryPrepare(
                        CloneFor(ext.Source, target, ext.Element, extQuota), ElementQuery.ReadAura(target, atom.Element), out lunar))
                    type = ReactionType.LunarElectroCharged;
                float externalConsumed, ownConsumed;
                if (type == ReactionType.LunarElectroCharged)
                {
                    externalConsumed = Mathf.Min(extQuota, LunarElectroChargedReactionHandler.Consumption);
                    ownConsumed = Mathf.Min(ownQuota, LunarElectroChargedReactionHandler.Consumption);
                }
                else ReactionPairRules.GetConsumption(type, ext.Element, extQuota, ownQuota,
                    out externalConsumed, out ownConsumed);
                // 星超导沿普通冰雷比例分份；规划只能确认类型，不能提前生成领域或普通超导伤害。
                if (type == ReactionType.Superconduct)
                {
                    var context = CloneFor(ext.Source, target, ext.Element, externalConsumed);
                    context.RestrictedAuraElement = atom.Element;
                    context.RestrictedAuraKind = atom.Kind == SwirlAuraKind.Frozen
                        ? ReactionPoolKind.Frozen : atom.Kind == SwirlAuraKind.Intrinsic
                            ? ReactionPoolKind.Intrinsic : ReactionPoolKind.NormalAura;
                    if (BattleManager.Instance?.TeamBonuses?.Stellar.TryPrepareSuperconduct(context, out stellar) == true)
                        type = ReactionType.StellarSuperconduct;
                }
                // 先按普通水草比例分配，再选择月绽放；不改变本批次已锁定的元素额度。
                if (type == ReactionType.Bloom)
                    type = LunarReactionConversion.SelectReaction(
                        CloneFor(ext.Source, target, ext.Element, extQuota), type, atom.Element);
                PendingLunarCrystallize lunarCrystallize = null;
                if (type == ReactionType.Crystallize)
                {
                    var context = CloneFor(ext.Source, target, ext.Element, externalConsumed);
                    if (LunarCrystallizeReactionHandler.TryPrepare(context, atom.Element, out lunarCrystallize))
                        type = ReactionType.LunarCrystallize;
                    else if (!CrystallizeReactionHandler.IsPartySource(context)) type = ReactionType.None;
                }
                if (externalConsumed > Epsilon && ownConsumed > Epsilon)
                    pairs.Add(new PairPlan { Own = atom, External = ext, Type = type,
                        ExternalConsumed = externalConsumed, OwnConsumed = ownConsumed, Lunar = lunar, Stellar = stellar,
                        LunarCrystallize = lunarCrystallize });
            }
        }

        var ownConsumedTotals = new Dictionary<SwirlAuraAtom, float>();
        var externalConsumedTotals = new Dictionary<ExternalAmount, float>();
        foreach (PairPlan pair in pairs)
        {
            ownConsumedTotals[pair.Own] = GetValue(ownConsumedTotals, pair.Own) + pair.OwnConsumed;
            externalConsumedTotals[pair.External] = GetValue(externalConsumedTotals, pair.External) + pair.ExternalConsumed;
            PairPlan captured = pair;
            effects.Add(() => ApplyPairEffect(target, captured, result, next));
        }

        transaction.Enqueue(() =>
        {
            using var elements = ElementEventScope.Begin("SwirlPropagationCommit", target);
            foreach (KeyValuePair<SwirlAuraAtom, float> consumed in ownConsumedTotals)
                ConsumeAtom(target, consumed.Key, consumed.Value);
            foreach (ExternalAmount ext in external.Values)
            {
                float remain = Mathf.Max(0f, ext.Amount - GetValue(externalConsumedTotals, ext));
                if (remain <= Epsilon) continue;
                int sourceID = ext.Source != null && ext.Source.SourceEntity != null ? ext.Source.SourceEntity.EntityID : 0;
                target.ApplyAura(ext.Element, remain, sourceID,
                    ext.Source != null ? ext.Source.SourceEffectID : string.Empty,
                    ext.Source != null ? ext.Source.SourceSkillID : string.Empty,
                    ext.Source?.SourceEntity ?? ext.Source?.SourceSnapshotOverride?.SourceEntity);
            }
            foreach (ExternalAmount ext in external.Values)
            {
                var context = CloneFor(ext.Source, target, ext.Element, ext.Amount);
                var contact = ReactionResult.Unchanged(context);
                contact.RemainingAttackAmount = Mathf.Max(0f, ext.Amount - GetValue(externalConsumedTotals, ext));
                foreach (var pair in pairs) if (pair.Type != ReactionType.None && ReferenceEquals(pair.External, ext))
                    contact.TriggeredReactions.Add(new ReactionOccurrence { Type = pair.Type });
                ext.ContactDeclaration = BattleEventHub.Record(ext, new ElementContactEventData(context, contact),
                    causes: ext.CauseEventIds);
                elements.AddCause(ext.ContactDeclaration.Header.EventId, target);
            }
        });
        effects.Add(() => { foreach (var ext in external.Values) BattleEventHub.Dispatch(ext.ContactDeclaration); });
    }

    private static List<SwirlAuraAtom> SnapshotReactableAuras(BattleEntity target)
    {
        var result = new List<SwirlAuraAtom>();
        foreach (string element in NormalElements)
        {
            ElementPoolReading aura = ElementQuery.ReadAura(target, element);
            if (aura?.IsPresent == true)
                result.Add(new SwirlAuraAtom { Element = element,
                    Kind = aura.IsIntrinsic ? SwirlAuraKind.Intrinsic : SwirlAuraKind.Normal,
                    Amount = aura.IsIntrinsic ? 0f : aura.Amount });
        }
        foreach (string element in new[] { "Anemo", "Geo" })
            if (ElementQuery.ReadIntrinsic(target, element) != null)
                result.Add(new SwirlAuraAtom { Element = element, Kind = SwirlAuraKind.Intrinsic });
        AddVirtual(result, "Cryo", SwirlAuraKind.Frozen, FrozenReactionHandler.GetFrozenAuraAsCryo(target));
        AddVirtual(result, "Pyro", SwirlAuraKind.Burning, BurningReactionHandler.GetBurningAuraAsPyro(target));
        return result;
    }

    private static void AddVirtual(List<SwirlAuraAtom> list, string element, SwirlAuraKind kind, float amount)
    {
        if (amount > Epsilon) list.Add(new SwirlAuraAtom { Element = element, Kind = kind, Amount = amount });
    }

    private static int CountCompatibleOwn(ExternalAmount ext, List<SwirlAuraAtom> own)
    {
        int count = 0;
        foreach (SwirlAuraAtom atom in own)
            if (ReactionPairRules.GetReaction(ext.Element, atom.Element) != ReactionType.None) count++;
        return Mathf.Max(1, count);
    }

    private static void ApplyPairEffect(BattleEntity target, PairPlan pair, ReactionResult result,
        Dictionary<FieldPosition, Dictionary<string, ExternalAmount>> next)
    {
        ReactionContext context = CloneFor(pair.External.Source, target, pair.External.Element, pair.ExternalConsumed);
        context.CauseEventIds = pair.External.ContactDeclaration != null
            ? new[] { pair.External.ContactDeclaration.Header.EventId } : pair.External.CauseEventIds.ToArray();
        if (pair.Type == ReactionType.None) return; // 敌方结晶只中和元素，不创建反应/护盾事实。
        using var hit = BattleEventHub.BeginHit(context.LogicalHitID, context.TargetIndex);
        using var elements = ElementEventScope.Begin("SwirlReactionEffect", target);
        foreach (long cause in context.CauseEventIds) elements.AddCause(cause);
        var occurrence = new ReactionOccurrence
        { Type = pair.Type, DisplayName = DisplayName(pair.Type), SourceEntity = context.SourceEntity,
          Target = target, SourceEffectID = context.SourceEffectID,
          InvolvedElements = new List<string> { pair.External.Element, pair.Own.Element },
          PendingLunarConduct = pair.Lunar, PendingStellarSuperconduct = pair.Stellar,
          PendingLunarCrystallize = pair.LunarCrystallize };
        occurrence.Declaration = BattleEventHub.Record(occurrence, new ReactionEventData(occurrence), causes: context.CauseEventIds);
        result.TriggeredReactions.Add(occurrence);
        context.CauseEventIds = new[] { occurrence.Declaration.Header.EventId };
        StellarTeamBonusHandler.Commit(occurrence);
        if (pair.Type == ReactionType.Swirl)
        {
            foreach (var part in pair.External.Parts)
                AddPropagation(part.Chain, target, pair.External.Element,
                    part.Amount * (pair.ExternalConsumed / pair.External.Amount), context.CauseEventIds, next);
            // 有真实反应声明和元素传播，但不建立Hit或再次产生扩散伤害。
        }
        else if (pair.Type == ReactionType.Overloaded)
            AddAreaHits(context, result, ReactionType.Overloaded, "Pyro", 4f);
        else if (pair.Type == ReactionType.Crystallize)
            CrystallizeReactionHandler.GrantPartyShield(context, pair.External.Element);
        else if (pair.Type == ReactionType.Superconduct)
        {
            AddAreaHits(context, result, ReactionType.Superconduct, "Cryo", 1f);
            foreach (BattleEntity affected in GetAreaTargets(target))
                if (!ReactionStateSystem.TryGetEntityState(affected, ReactionType.Superconduct, out _))
                    ReactionStateSystem.SetEntityState(affected, ReactionType.Superconduct, 3,
                        ReactionSourceSnapshot.Capture(context), context.ApplicationPhase);
        }
        else if (pair.Type == ReactionType.ElectroCharged)
        {
            result.DerivedHits.Add(BuildTransformativeHit(context, target, "Electro", ReactionType.ElectroCharged, 9.6f));
            ReactionStateSystem.SetEntityState(target, ReactionType.ElectroCharged, 1,
                ReactionSourceSnapshot.Capture(context, ReactionType.ElectroCharged, "Electro"),
                context.ApplicationPhase, false, Mathf.Min(pair.ExternalConsumed, pair.OwnConsumed));
        }
        else if (pair.Type == ReactionType.LunarElectroCharged)
            ReactionStateSystem.RemoveEntityState(target, ReactionType.ElectroCharged);
        else if (pair.Type == ReactionType.Frozen)
            ReactionStateSystem.SetEntityState(target, ReactionType.Frozen,
                Mathf.CeilToInt(Mathf.Min(pair.ExternalConsumed, pair.OwnConsumed)),
                ReactionSourceSnapshot.Capture(context), context.ApplicationPhase, true,
                Mathf.Min(pair.ExternalConsumed, pair.OwnConsumed));
        else if (pair.Type == ReactionType.Quicken)
            ReactionStateSystem.SetEntityState(target, ReactionType.Quicken, 1,
                ReactionSourceSnapshot.Capture(context), context.ApplicationPhase);
        else if (pair.Type == ReactionType.Burning)
            ReactionStateSystem.SetEntityState(target, ReactionType.Burning,
                Mathf.CeilToInt(Mathf.Min(pair.ExternalConsumed, pair.OwnConsumed)),
                ReactionSourceSnapshot.Capture(context, ReactionType.Burning, "Pyro"),
                context.ApplicationPhase, true, Mathf.Min(pair.ExternalConsumed, pair.OwnConsumed));
        else if (pair.Type == ReactionType.Bloom || pair.Type == ReactionType.LunarBloom)
        {
            float level = ReactionDamageCalculator.GetLevelCoefficient(context.SourceEntity != null ? context.SourceEntity.Level : 1);
            occurrence.CreatedBloomCore = BloomCoreSystem.CreateCore(context, level, out List<ReactionDerivedHit> overflow);
            result.DerivedHits.AddRange(overflow);
        }
    }

    private static void AddAreaHits(ReactionContext context, ReactionResult result, ReactionType type,
        string element, float multiplier)
    {
        foreach (BattleEntity target in GetAreaTargets(context.Target))
            result.DerivedHits.Add(BuildTransformativeHit(context, target, element, type, multiplier));
    }

    private static IEnumerable<BattleEntity> GetAreaTargets(BattleEntity origin)
    {
        BattleField field = origin.Position != null ? origin.Position.OwnerField : null;
        int slot = origin.Position != null ? origin.Position.SlotIndex : origin.SlotPosition;
        BattleSide side = origin.Position != null ? origin.Position.Side : origin.Side;
        if (field == null && BattleManager.Instance != null) field = BattleManager.Instance.Field;
        if (field == null) { if (origin.IsAlive) yield return origin; yield break; }
        foreach (int index in BattlePositionSystem.GetAdjacentPositions(side, slot, 1))
        {
            FieldPosition position = field.GetSlot(side, index);
            if (position?.Occupant?.IsOnField == true) yield return position.Occupant;
        }
    }

    private static ReactionDerivedHit BuildTransformativeHit(ReactionContext context, BattleEntity target,
        string element, ReactionType type, float multiplier)
    {
        ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(context, type, element);
        float level = ReactionDamageCalculator.GetLevelCoefficient(snapshot.Level > 0 ? snapshot.Level : 1);
        float resistance = ReactionDamageCalculator.GetResistanceMultiplier(target, element);
        bool enemy = snapshot.SourceKind == ReactionSourceKind.EnemySkill
            || (snapshot.SourceEntity != null && snapshot.SourceEntity.Type == BattleEntity.EntityType.Enemy);
        float damage = enemy
            ? ReactionDamageCalculator.CalculateEnemyTransformative(level, multiplier, snapshot.DMGBonus, resistance)
            : ReactionDamageCalculator.CalculateCharacterTransformative(level, multiplier, snapshot.TotalEM,
                snapshot.DMGBonus + ReactionDamageCalculator.CollectLiveTargetBonuses(
                    snapshot.SourceEntity, target, type, element).DMGBonus, snapshot.BaseDMGBonusFlat, resistance);
        return new ReactionDerivedHit
        { Target = target, Damage = damage, DamageElement = element, CauseEventIds = context.CauseEventIds,
          ElementAmount = 0f, Source = DamageSourceInfo.FromReactionSnapshot(snapshot, type), ReactionType = type };
    }

    private static void TriggerCoresAtPosition(FieldPosition position,
        Dictionary<string, ExternalAmount> external, ReactionResult result)
    {
        foreach (ExternalAmount amount in external.Values)
        {
            if (amount.Element != "Pyro" && amount.Element != "Electro") continue;
            ReactionContext context = CloneFor(amount.Source, position?.Occupant?.IsOnField == true ? position.Occupant : null,
                amount.Element, amount.Amount);
            context.CauseEventIds = amount.ContactDeclaration != null
                ? new[] { amount.ContactDeclaration.Header.EventId } : amount.CauseEventIds.ToArray();
            if (BloomSecondaryReactionHandler.TryResolveAtPosition(context, position, ReactionType.Swirl, 0f,
                    out BloomSecondaryReactionResolution resolution))
            {
                var occurrence = new ReactionOccurrence
                { Type = resolution.Type, DisplayName = resolution.DisplayName, SourceEntity = context.SourceEntity,
                  Target = context.Target, SourceEffectID = context.SourceEffectID,
                  InvolvedElements = new List<string> { amount.Element, "Dendro" } };
                occurrence.Declaration = BattleEventHub.Record(occurrence, new ReactionEventData(occurrence), causes: context.CauseEventIds);
                result.TriggeredReactions.Add(occurrence);
                foreach (var derived in resolution.DerivedHits) derived.CauseEventIds = new[] { occurrence.Declaration.Header.EventId };
                result.DerivedHits.AddRange(resolution.DerivedHits);
            }
        }
    }

    private static void ResolveStageThree(BattleEntity target, ReactionContext source, ReactionResult aggregate, IReadOnlyList<long> causes)
    {
        if (source == null || target.GetEnemyElementalShield() != null) return;
        source = CloneFor(source, target, source.AttackElement, source.AttackAmount);
        source.CauseEventIds = causes;
        for (int guard = 0; guard < 16; guard++)
        {
            if (TryResolveSimultaneousSuperconductAndQuicken(target, source, aggregate))
                continue;
            if (!TryFindStageThreePair(
                    target,
                    out string attackElement,
                    out string auraElement,
                    out float amount,
                    HasLunarContact(aggregate, target))) break;
            ReactionContext context = CloneFor(source, target, attackElement, amount);
            context.PreReactionDamage = 0f;
            context.PreserveIncomingAuraBeforeReaction = true;
            context.RestrictedAuraElement = auraElement;
            context.RestrictedAuraKind = ReactionPoolKind.Any;
            ReactionResult resolved = ReactionResolver.Resolve(context);
            if (!resolved.HasReaction) break;
            foreach (ReactionOccurrence occurrence in resolved.TriggeredReactions)
            {
                if (occurrence.PendingLunarConduct != null)
                    occurrence.PendingLunarConduct.SuppressImmediateDamage = true;
                aggregate.TriggeredReactions.Add(occurrence);
            }
            // 第三阶段反应自身不造成伤害，handler 已经施加的非伤害效果保留。
        }
    }

    private static bool TryResolveSimultaneousSuperconductAndQuicken(
        BattleEntity target,
        ReactionContext source,
        ReactionResult aggregate)
    {
        ElementPoolReading electroAura = ElementQuery.ReadNormal(target, "Electro");
        float electro = electroAura != null ? electroAura.Amount : 0f;
        float cryo = ElementQuery.GetAvailableReactionAmount(target, "Cryo", electro);
        float dendro = ElementQuery.ReadAura(target, "Dendro")?.AvailableFor(electro) ?? 0f;
        if (electro <= Epsilon || cryo <= Epsilon || dendro <= Epsilon)
            return false;
        using var hit = BattleEventHub.BeginHit(source.LogicalHitID);
        using var elements = ElementEventScope.Begin("SwirlResidualCommit", target);
        int firstOccurrence = aggregate.TriggeredReactions.Count;

        float quota = electro * 0.5f;
        var superconductContext = CloneFor(source, target, "Electro", quota);
        superconductContext.CanApplyAura = false;
        superconductContext.PreserveIncomingAuraBeforeReaction = true;
        superconductContext.RestrictedAuraElement = "Cryo";
        superconductContext.RestrictedAuraKind = ReactionPoolKind.Any;
        float level = ReactionDamageCalculator.GetLevelCoefficient(
            superconductContext.SourceEntity != null ? superconductContext.SourceEntity.Level : 1);
        bool superconduct = SuperconductReactionHandler.TryResolve(
            superconductContext,
            level,
            out SuperconductReactionResolution superconductResult);

        var quickenContext = CloneFor(source, target, "Electro", quota);
        quickenContext.CanApplyAura = false;
        quickenContext.RestrictedAuraElement = "Dendro";
        quickenContext.RestrictedAuraKind = ReactionPoolKind.Any;
        bool quicken = QuickenReactionHandler.TryResolve(
            quickenContext,
            out QuickenReactionResolution quickenResult);

        float electroConsumed = 0f;
        if (superconduct)
        {
            electroConsumed += Mathf.Max(0f, quota - superconductResult.RemainingAttackAmount);
            AddStageThreeOccurrence(aggregate, superconductContext, superconductResult.Type,
                superconductResult.DisplayName, "Cryo", superconductResult.PendingStellarSuperconduct);
        }
        if (quicken)
        {
            electroConsumed += Mathf.Max(0f, quota - quickenResult.RemainingAttackAmount);
            AddStageThreeOccurrence(aggregate, quickenContext, ReactionType.Quicken, "原激化", "Dendro");
        }
        if (electroConsumed > Epsilon)
            target.ConsumeAura("Electro", Mathf.Min(electro, electroConsumed));
        if (superconduct || quicken)
        {
            ReactionContext contactContext = CloneFor(source, target, "Electro", electro);
            contactContext.CanApplyAura = false;
            var contact = ReactionResult.Unchanged(contactContext);
            contact.RemainingAttackAmount = Mathf.Max(0f, electro - electroConsumed);
            for (int i = firstOccurrence; i < aggregate.TriggeredReactions.Count; i++)
                contact.TriggeredReactions.Add(aggregate.TriggeredReactions[i]);
            contact.ContactDeclaration = BattleEventHub.Record(contact, new ElementContactEventData(contactContext, contact), causes: source.CauseEventIds);
            elements.AddCause(contact.ContactDeclaration.Header.EventId);
            foreach (var occurrence in contact.TriggeredReactions)
            {
                occurrence.Declaration = BattleEventHub.Record(occurrence, new ReactionEventData(occurrence),
                    causes: new[] { contact.ContactDeclaration.Header.EventId });
                StellarTeamBonusHandler.Commit(occurrence);
            }
            BattleEventHub.Dispatch(contact.ContactDeclaration);
        }
        return superconduct || quicken;
    }

    private static void AddStageThreeOccurrence(
        ReactionResult aggregate,
        ReactionContext context,
        ReactionType type,
        string displayName,
        string auraElement,
        PendingStellarSuperconduct stellar = null)
    {
        aggregate.TriggeredReactions.Add(new ReactionOccurrence
        {
            Type = type,
            DisplayName = displayName,
            SourceEntity = context.SourceEntity,
            Target = context.Target,
            SourceEffectID = context.SourceEffectID,
            PendingStellarSuperconduct = stellar,
            InvolvedElements = new List<string> { context.AttackElement, auraElement }
        });
    }

    private static bool TryFindStageThreePair(
        BattleEntity target,
        out string attack,
        out string aura,
        out float amount,
        bool lunarContact = false)
    {
        string[,] ordered = {
            {"Electro","Cryo"},{"Cryo","Electro"},{"Electro","Dendro"},{"Dendro","Electro"},
            {"Pyro","Electro"},{"Electro","Pyro"},{"Hydro","Dendro"},{"Dendro","Hydro"},
            {"Hydro","Pyro"},{"Pyro","Hydro"},{"Pyro","Cryo"},{"Cryo","Pyro"},
            {"Hydro","Cryo"},{"Cryo","Hydro"},
            {"Pyro","Geo"},{"Hydro","Geo"},{"Electro","Geo"},{"Cryo","Geo"},
            {"Pyro","Dendro"},{"Dendro","Pyro"},
            {"Hydro","Electro"},{"Electro","Hydro"}
        };
        for (int i = 0; i < ordered.GetLength(0); i++)
        {
            bool waterElectro = (ordered[i, 0] == "Hydro" && ordered[i, 1] == "Electro")
                || (ordered[i, 0] == "Electro" && ordered[i, 1] == "Hydro");
            // 感电是允许水雷共存的状态，不是需要循环反应到一方耗尽的残留。
            if (waterElectro && (lunarContact || ElectroChargedReactionHandler.IsElectroCharged(target)))
                continue;
            ElementPoolReading incoming = ElementQuery.ReadNormal(target, ordered[i,0]);
            float opposing = incoming == null ? 0f : ElementQuery.GetAvailableReactionAmount(target, ordered[i,1],
                ReactionPairRules.RequiredAura(ordered[i,0], ordered[i,1], incoming.Amount));
            if (incoming != null && incoming.Amount > Epsilon && opposing > Epsilon)
            {
                attack = ordered[i,0];
                aura = ordered[i,1];
                amount = incoming.Amount;
                return true;
            }
        }
        attack = null;
        aura = null;
        amount = 0f;
        return false;
    }

    private static bool HasLunarContact(ReactionResult result, BattleEntity target)
    {
        foreach (ReactionOccurrence occurrence in result.TriggeredReactions)
            if (occurrence.Type == ReactionType.LunarElectroCharged && ReferenceEquals(occurrence.Target, target))
                return true;
        return false;
    }

    private static ReactionContext CloneFor(ReactionContext source, BattleEntity target, string element, float amount)
    {
        return new ReactionContext
        {
            SourceEntity = source != null ? source.SourceEntity : null,
            EquipmentSource = source?.EffectiveEquipmentSource,
            Target = target,
            SourceKind = source != null ? source.SourceKind : ReactionSourceKind.DerivedReaction,
            SourceSkillID = source != null ? source.SourceSkillID : string.Empty,
            SourceEffectID = source != null ? source.SourceEffectID : string.Empty,
            EffectExecutionID = source != null ? source.EffectExecutionID : 0,
            LogicalHitID = source != null ? source.LogicalHitID : 0,
            TargetIndex = 0,
            CauseEventIds = source?.CauseEventIds,
            ApplicationPhase = source != null ? source.ApplicationPhase : -1,
            AttackElement = element,
            AttackAmount = amount,
            PreReactionDamage = 0f,
            DamageType = "Reaction",
            CanTriggerReaction = true,
            CanApplyAura = true,
            SourceSnapshotOverride = source != null ? source.SourceSnapshotOverride : null
        };
    }

    private static bool IsStageOneDeadDirectTarget(SwirlPreparedBatch batch, BattleEntity target)
    {
        foreach (SwirlTargetSnapshot snapshot in batch.Targets)
            if (ReferenceEquals(snapshot.Target, target) && snapshot.WasAlive && !target.IsAlive) return true;
        return false;
    }

    private static ReactionContext FirstSource(Dictionary<string, ExternalAmount> values)
    {
        foreach (ExternalAmount value in values.Values) return value.Source;
        return null;
    }

    private static IReadOnlyList<long> PropagationCauses(Dictionary<string, ExternalAmount> values)
    {
        var ids = new List<long>();
        foreach (var value in values.Values)
        {
            if (value.ContactDeclaration != null) ids.Add(value.ContactDeclaration.Header.EventId);
            else foreach (long id in value.CauseEventIds) if (!ids.Contains(id)) ids.Add(id);
        }
        return ids.AsReadOnly();
    }

    private static float GetValue<TKey>(Dictionary<TKey, float> dict, TKey key)
        => dict.TryGetValue(key, out float value) ? value : 0f;

    private static string DisplayName(ReactionType type)
    {
        switch (type)
        {
            case ReactionType.Superconduct: return "超导";
            case ReactionType.StellarSuperconduct: return "星超导";
            case ReactionType.Quicken: return "原激化";
            case ReactionType.Overloaded: return "超载";
            case ReactionType.Bloom: return "绽放";
            case ReactionType.LunarBloom: return "月绽放";
            case ReactionType.Vaporize: return "蒸发";
            case ReactionType.Melt: return "融化";
            case ReactionType.Frozen: return "冻结";
            case ReactionType.Burning: return "燃烧";
            case ReactionType.ElectroCharged: return "感电";
            case ReactionType.LunarElectroCharged: return "月感电";
            case ReactionType.Swirl: return "扩散";
            case ReactionType.Crystallize: return "结晶";
            default: return string.Empty;
        }
    }
}
