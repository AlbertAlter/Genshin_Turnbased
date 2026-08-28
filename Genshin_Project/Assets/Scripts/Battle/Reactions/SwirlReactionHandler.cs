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
    }

    private sealed class PairPlan
    {
        public SwirlAuraAtom Own;
        public ExternalAmount External;
        public ReactionType Type;
        public float OwnConsumed;
        public float ExternalConsumed;
    }

    public static SwirlPreparedBatch PrepareBatch(IEnumerable<ReactionContext> contexts)
    {
        var batch = new SwirlPreparedBatch();
        if (contexts == null) return batch;
        foreach (ReactionContext context in contexts)
        {
            if (context == null || context.Target == null || context.AttackElement != "Anemo"
                || context.AttackAmount <= Epsilon || !context.CanTriggerReaction)
                continue;
            var snapshot = new SwirlTargetSnapshot
            {
                Context = context,
                Target = context.Target,
                Side = context.Target.Position != null ? context.Target.Position.Side : context.Target.Side,
                SlotIndex = context.Target.Position != null ? context.Target.Position.SlotIndex : context.Target.SlotPosition,
                WasAlive = context.Target.IsAlive
            };
            AddNormalAtoms(snapshot);
            AddVirtualAtom(snapshot, "Cryo", SwirlAuraKind.Frozen,
                context.IgnoreFrozenAura ? 0f : FrozenReactionHandler.GetFrozenAuraAsCryo(context.Target));
            AddVirtualAtom(snapshot, "Pyro", SwirlAuraKind.Burning,
                BurningReactionHandler.GetBurningAuraAsPyro(context.Target));
            AllocateAnemo(context.AttackAmount, snapshot.SpreadAtoms);
            batch.Targets.Add(snapshot);
            if (batch.EffectExecutionID == 0) batch.EffectExecutionID = context.EffectExecutionID;
        }
        return batch;
    }

    public static SwirlBatchResult ResolveBatch(IEnumerable<ReactionContext> contexts)
        => ResolvePreparedBatch(PrepareBatch(contexts));

    public static SwirlBatchResult ResolvePreparedBatch(SwirlPreparedBatch batch)
    {
        var output = new SwirlBatchResult();
        if (batch == null || batch.Targets.Count == 0) return output;
        var spread = new Dictionary<FieldPosition, Dictionary<string, ExternalAmount>>();
        var stageOne = new ReactionResult();

        foreach (SwirlTargetSnapshot source in batch.Targets)
        {
            if (source == null || source.Target == null) continue;
            output.DirectTargets.Add(source.Target);
            bool triggered = false;
            var involvedElements = new List<string> { "Anemo" };
            foreach (SwirlAuraAtom atom in source.SpreadAtoms)
            {
                if (atom.SpreadAmount <= Epsilon) continue;
                triggered = true;
                if (!involvedElements.Contains(atom.Element)) involvedElements.Add(atom.Element);
                ConsumeAtom(source.Target, atom, atom.SpreadAmount);
                stageOne.DerivedHits.Add(BuildTransformativeHit(
                    source.Context, source.Target, atom.Element, ReactionType.Swirl,
                    SwirlMultiplier, 0f));
                AddPropagation(source, atom, spread);
            }
            if (triggered)
            {
                output.Reaction.TriggeredReactions.Add(new ReactionOccurrence
                {
                    Type = ReactionType.Swirl,
                    DisplayName = "扩散",
                    SourceEntity = source.Context.SourceEntity,
                    Target = source.Target,
                    SourceEffectID = source.Context.SourceEffectID,
                    InvolvedElements = involvedElements
                });
            }
        }

        // 第一阶段伤害必须先落地，死亡的直接目标在第二阶段只继续传播。
        ReactionEffectExecutor.ExecuteDerivedHits(stageOne);

        var transaction = new ReactionTransaction();
        var effects = new List<Action>();
        foreach (KeyValuePair<FieldPosition, Dictionary<string, ExternalAmount>> entry in spread)
        {
            FieldPosition position = entry.Key;
            Dictionary<string, ExternalAmount> external = entry.Value;
            TriggerCoresAtPosition(position, external, output.Reaction);
            BattleEntity target = position != null && position.IsOccupied ? position.Occupant : null;
            if (target == null || !target.IsAlive || IsStageOneDeadDirectTarget(batch, target))
                continue;
            PlanStageTwo(target, external, transaction, effects, output.Reaction);
        }
        transaction.Commit();
        foreach (Action effect in effects) effect();

        // 第三阶段只清理批量写回后仍能反应的元素；直接反应伤害在此阶段被抑制。
        foreach (KeyValuePair<FieldPosition, Dictionary<string, ExternalAmount>> entry in spread)
        {
            BattleEntity target = entry.Key != null && entry.Key.IsOccupied ? entry.Key.Occupant : null;
            if (target != null && target.IsAlive)
                ResolveStageThree(target, FirstSource(entry.Value), output.Reaction);
        }
        ElectroChargedReactionHandler.CleanupInvalidStates();
        return output;
    }

    private static void AddNormalAtoms(SwirlTargetSnapshot snapshot)
    {
        foreach (string element in NormalElements)
        {
            if (element == "Dendro") continue;
            ElementalAura aura = snapshot.Target.GetAura(element);
            if (aura != null && aura.AuraAmount > Epsilon)
                snapshot.SpreadAtoms.Add(new SwirlAuraAtom
                { Element = element, Kind = SwirlAuraKind.Normal, Amount = aura.AuraAmount });
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
                float capacity = atom.Amount - atom.SpreadAmount;
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
        if (atom.Kind == SwirlAuraKind.Frozen) FrozenReactionHandler.ConsumeFrozenAura(target, amount);
        else if (atom.Kind == SwirlAuraKind.Burning) BurningReactionHandler.ConsumeBurningAura(target, amount);
        else target.ConsumeAura(atom.Element, amount);
    }

    private static void AddPropagation(SwirlTargetSnapshot source, SwirlAuraAtom atom,
        Dictionary<FieldPosition, Dictionary<string, ExternalAmount>> spread)
    {
        BattleField field = source.Target.Position != null ? source.Target.Position.OwnerField : null;
        if (field == null && BattleManager.Instance != null) field = BattleManager.Instance.Field;
        if (field == null || source.SlotIndex <= 0) return;
        foreach (int index in BattlePositionSystem.GetAdjacentPositions(source.Side, source.SlotIndex, 1))
        {
            if (index == source.SlotIndex) continue;
            FieldPosition position = field.GetSlot(source.Side, index);
            if (position == null) continue;
            if (!spread.TryGetValue(position, out Dictionary<string, ExternalAmount> byElement))
                spread[position] = byElement = new Dictionary<string, ExternalAmount>();
            if (!byElement.TryGetValue(atom.Element, out ExternalAmount amount))
                byElement[atom.Element] = amount = new ExternalAmount
                { Element = atom.Element, Source = source.Context };
            amount.Amount += atom.SpreadAmount;
        }
    }

    private static void PlanStageTwo(BattleEntity target, Dictionary<string, ExternalAmount> external,
        ReactionTransaction transaction, List<Action> effects, ReactionResult result)
    {
        List<SwirlAuraAtom> own = SnapshotReactableAuras(target);
        var pairs = new List<PairPlan>();
        foreach (SwirlAuraAtom atom in own)
        {
            var compatible = new List<ExternalAmount>();
            foreach (ExternalAmount ext in external.Values)
                if (GetReaction(ext.Element, atom.Element) != ReactionType.None) compatible.Add(ext);
            foreach (ExternalAmount ext in compatible)
            {
                int ownCount = compatible.Count;
                int extCount = CountCompatibleOwn(ext, own);
                float ownQuota = atom.Amount / ownCount;
                float extQuota = ext.Amount / extCount;
                ReactionType type = GetReaction(ext.Element, atom.Element);
                GetConsumption(type, ext.Element, extQuota, ownQuota,
                    out float externalConsumed, out float ownConsumed);
                if (externalConsumed > Epsilon && ownConsumed > Epsilon)
                    pairs.Add(new PairPlan { Own = atom, External = ext, Type = type,
                        ExternalConsumed = externalConsumed, OwnConsumed = ownConsumed });
            }
        }

        var ownConsumedTotals = new Dictionary<SwirlAuraAtom, float>();
        var externalConsumedTotals = new Dictionary<ExternalAmount, float>();
        foreach (PairPlan pair in pairs)
        {
            ownConsumedTotals[pair.Own] = GetValue(ownConsumedTotals, pair.Own) + pair.OwnConsumed;
            externalConsumedTotals[pair.External] = GetValue(externalConsumedTotals, pair.External) + pair.ExternalConsumed;
            PairPlan captured = pair;
            effects.Add(() => ApplyPairEffect(target, captured, result));
        }

        transaction.Enqueue(() =>
        {
            foreach (KeyValuePair<SwirlAuraAtom, float> consumed in ownConsumedTotals)
                ConsumeAtom(target, consumed.Key, Mathf.Min(consumed.Key.Amount, consumed.Value));
            foreach (ExternalAmount ext in external.Values)
            {
                float remain = Mathf.Max(0f, ext.Amount - GetValue(externalConsumedTotals, ext));
                if (remain <= Epsilon) continue;
                int sourceID = ext.Source != null && ext.Source.SourceEntity != null ? ext.Source.SourceEntity.EntityID : 0;
                target.ApplyAura(ext.Element, remain, sourceID,
                    ext.Source != null ? ext.Source.SourceEffectID : string.Empty,
                    ext.Source != null ? ext.Source.SourceSkillID : string.Empty);
            }
        });
    }

    private static List<SwirlAuraAtom> SnapshotReactableAuras(BattleEntity target)
    {
        var result = new List<SwirlAuraAtom>();
        foreach (string element in NormalElements)
        {
            ElementalAura aura = target.GetAura(element);
            if (aura != null && aura.AuraAmount > Epsilon)
                result.Add(new SwirlAuraAtom { Element = element, Kind = SwirlAuraKind.Normal, Amount = aura.AuraAmount });
        }
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
            if (GetReaction(ext.Element, atom.Element) != ReactionType.None) count++;
        return Mathf.Max(1, count);
    }

    private static ReactionType GetReaction(string attack, string aura)
    {
        if ((attack == "Electro" && aura == "Cryo") || (attack == "Cryo" && aura == "Electro")) return ReactionType.Superconduct;
        if ((attack == "Electro" && aura == "Dendro") || (attack == "Dendro" && aura == "Electro")) return ReactionType.Quicken;
        if ((attack == "Pyro" && aura == "Electro") || (attack == "Electro" && aura == "Pyro")) return ReactionType.Overloaded;
        if ((attack == "Hydro" && aura == "Dendro") || (attack == "Dendro" && aura == "Hydro")) return ReactionType.Bloom;
        if ((attack == "Hydro" && aura == "Pyro") || (attack == "Pyro" && aura == "Hydro")) return ReactionType.Vaporize;
        if ((attack == "Pyro" && aura == "Cryo") || (attack == "Cryo" && aura == "Pyro")) return ReactionType.Melt;
        if ((attack == "Hydro" && aura == "Cryo") || (attack == "Cryo" && aura == "Hydro")) return ReactionType.Frozen;
        if ((attack == "Pyro" && aura == "Dendro") || (attack == "Dendro" && aura == "Pyro")) return ReactionType.Burning;
        if ((attack == "Hydro" && aura == "Electro") || (attack == "Electro" && aura == "Hydro")) return ReactionType.ElectroCharged;
        return ReactionType.None;
    }

    private static void GetConsumption(ReactionType type, string externalElement, float ext, float own,
        out float externalConsumed, out float ownConsumed)
    {
        float extPerUnit = 1f, ownPerUnit = 1f;
        if (type == ReactionType.Vaporize)
        {
            extPerUnit = externalElement == "Pyro" ? 2f : 1f;
            ownPerUnit = externalElement == "Pyro" ? 1f : 2f;
        }
        else if (type == ReactionType.Melt)
        {
            extPerUnit = externalElement == "Cryo" ? 2f : 1f;
            ownPerUnit = externalElement == "Cryo" ? 1f : 2f;
        }
        else if (type == ReactionType.Bloom)
        {
            extPerUnit = externalElement == "Hydro" ? 2f : 1f;
            ownPerUnit = externalElement == "Hydro" ? 1f : 2f;
        }
        float units = Mathf.Min(ext / extPerUnit, own / ownPerUnit);
        if (type == ReactionType.ElectroCharged) units = Mathf.Min(1f, units);
        externalConsumed = units * extPerUnit;
        ownConsumed = units * ownPerUnit;
    }

    private static void ApplyPairEffect(BattleEntity target, PairPlan pair, ReactionResult result)
    {
        ReactionContext context = CloneFor(pair.External.Source, target, pair.External.Element, pair.ExternalConsumed);
        result.TriggeredReactions.Add(new ReactionOccurrence
        { Type = pair.Type, DisplayName = DisplayName(pair.Type), SourceEntity = context.SourceEntity,
          Target = target, SourceEffectID = context.SourceEffectID,
          InvolvedElements = new List<string> { pair.External.Element, pair.Own.Element } });
        if (pair.Type == ReactionType.Overloaded)
            AddAreaHits(context, result, ReactionType.Overloaded, "Pyro", 4f, 100f);
        else if (pair.Type == ReactionType.Superconduct)
        {
            AddAreaHits(context, result, ReactionType.Superconduct, "Cryo", 1f, 0f);
            foreach (BattleEntity affected in GetAreaTargets(target))
                if (!ReactionStateSystem.TryGetEntityState(affected, ReactionType.Superconduct, out _))
                    ReactionStateSystem.SetEntityState(affected, ReactionType.Superconduct, 3,
                        ReactionSourceSnapshot.Capture(context), context.ApplicationPhase);
        }
        else if (pair.Type == ReactionType.ElectroCharged)
        {
            result.DerivedHits.Add(BuildTransformativeHit(context, target, "Electro", ReactionType.ElectroCharged, 9.6f, 50f));
            ReactionStateSystem.SetEntityState(target, ReactionType.ElectroCharged, 1,
                ReactionSourceSnapshot.Capture(context, ReactionType.ElectroCharged, "Electro"),
                context.ApplicationPhase, false, Mathf.Min(pair.ExternalConsumed, pair.OwnConsumed));
        }
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
        else if (pair.Type == ReactionType.Bloom)
        {
            float level = ReactionDamageCalculator.GetLevelCoefficient(context.SourceEntity != null ? context.SourceEntity.Level : 1);
            BloomCoreSystem.CreateCore(context, level, out List<ReactionDerivedHit> overflow);
            result.DerivedHits.AddRange(overflow);
        }
    }

    private static void AddAreaHits(ReactionContext context, ReactionResult result, ReactionType type,
        string element, float multiplier, float poise)
    {
        foreach (BattleEntity target in GetAreaTargets(context.Target))
            result.DerivedHits.Add(BuildTransformativeHit(context, target, element, type, multiplier, poise));
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
            if (position != null && position.IsOccupied) yield return position.Occupant;
        }
    }

    private static ReactionDerivedHit BuildTransformativeHit(ReactionContext context, BattleEntity target,
        string element, ReactionType type, float multiplier, float poise)
    {
        ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(context, type, element);
        float level = ReactionDamageCalculator.GetLevelCoefficient(snapshot.Level > 0 ? snapshot.Level : 1);
        float resistance = ReactionDamageCalculator.GetResistanceMultiplier(target, element);
        bool enemy = snapshot.SourceKind == ReactionSourceKind.EnemySkill
            || (snapshot.SourceEntity != null && snapshot.SourceEntity.Type == BattleEntity.EntityType.Enemy);
        float damage = enemy
            ? ReactionDamageCalculator.CalculateEnemyTransformative(level, multiplier, snapshot.DMGBonus, resistance)
            : ReactionDamageCalculator.CalculateCharacterTransformative(level, multiplier, snapshot.TotalEM,
                snapshot.DMGBonus, snapshot.BaseDMGBonusFlat, resistance);
        return new ReactionDerivedHit
        { Target = target, Damage = damage, DamageElement = element, PoiseDamage = poise,
          ElementAmount = 0f, Source = DamageSourceInfo.FromReactionSnapshot(snapshot, type), ReactionType = type };
    }

    private static void TriggerCoresAtPosition(FieldPosition position,
        Dictionary<string, ExternalAmount> external, ReactionResult result)
    {
        foreach (ExternalAmount amount in external.Values)
        {
            if (amount.Element != "Pyro" && amount.Element != "Electro") continue;
            ReactionContext context = CloneFor(amount.Source, position != null ? position.Occupant : null,
                amount.Element, amount.Amount);
            if (BloomSecondaryReactionHandler.TryResolveAtPosition(context, position, ReactionType.Swirl, 0f,
                    out BloomSecondaryReactionResolution resolution))
            {
                result.DerivedHits.AddRange(resolution.DerivedHits);
                result.TriggeredReactions.Add(new ReactionOccurrence
                { Type = resolution.Type, DisplayName = resolution.DisplayName, SourceEntity = context.SourceEntity,
                  Target = context.Target, SourceEffectID = context.SourceEffectID,
                  InvolvedElements = new List<string> { amount.Element, "Dendro" } });
            }
        }
    }

    private static void ResolveStageThree(BattleEntity target, ReactionContext source, ReactionResult aggregate)
    {
        if (source == null) return;
        for (int guard = 0; guard < 16; guard++)
        {
            if (!TryFindStageThreePair(target, out string attackElement, out float amount)) break;
            ReactionContext context = CloneFor(source, target, attackElement, amount);
            context.PreReactionDamage = 0f;
            ReactionResult resolved = ReactionResolver.Resolve(context);
            if (!resolved.HasReaction) break;
            foreach (ReactionOccurrence occurrence in resolved.TriggeredReactions)
                aggregate.TriggeredReactions.Add(occurrence);
            // 第三阶段反应自身不造成伤害，handler 已经施加的非伤害效果保留。
        }
    }

    private static bool TryFindStageThreePair(BattleEntity target, out string attack, out float amount)
    {
        string[,] ordered = {
            {"Electro","Cryo"},{"Cryo","Electro"},{"Electro","Dendro"},{"Dendro","Electro"},
            {"Pyro","Electro"},{"Electro","Pyro"},{"Hydro","Dendro"},{"Dendro","Hydro"},
            {"Hydro","Pyro"},{"Pyro","Hydro"},{"Pyro","Cryo"},{"Cryo","Pyro"},
            {"Hydro","Cryo"},{"Cryo","Hydro"},{"Pyro","Dendro"},{"Dendro","Pyro"},
            {"Hydro","Electro"},{"Electro","Hydro"}
        };
        for (int i = 0; i < ordered.GetLength(0); i++)
        {
            ElementalAura incoming = target.GetAura(ordered[i,0]);
            float opposing = GetElementAmount(target, ordered[i,1]);
            if (incoming != null && incoming.AuraAmount > Epsilon && opposing > Epsilon)
            { attack = ordered[i,0]; amount = incoming.AuraAmount; return true; }
        }
        attack = null; amount = 0f; return false;
    }

    private static float GetElementAmount(BattleEntity target, string element)
    {
        float amount = target.GetAura(element)?.AuraAmount ?? 0f;
        if (element == "Pyro") amount += BurningReactionHandler.GetBurningAuraAsPyro(target);
        if (element == "Cryo") amount += FrozenReactionHandler.GetFrozenAuraAsCryo(target);
        return amount;
    }

    private static ReactionContext CloneFor(ReactionContext source, BattleEntity target, string element, float amount)
    {
        return new ReactionContext
        {
            SourceEntity = source != null ? source.SourceEntity : null,
            Target = target,
            SourceKind = source != null ? source.SourceKind : ReactionSourceKind.DerivedReaction,
            SourceSkillID = source != null ? source.SourceSkillID : string.Empty,
            SourceEffectID = source != null ? source.SourceEffectID : string.Empty,
            EffectExecutionID = source != null ? source.EffectExecutionID : 0,
            ApplicationPhase = source != null ? source.ApplicationPhase : -1,
            AttackElement = element,
            AttackAmount = amount,
            PreReactionDamage = 0f,
            DamageType = "Reaction",
            PoiseDamage = 0f,
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

    private static float GetValue<TKey>(Dictionary<TKey, float> dict, TKey key)
        => dict.TryGetValue(key, out float value) ? value : 0f;

    private static string DisplayName(ReactionType type)
    {
        switch (type)
        {
            case ReactionType.Superconduct: return "超导";
            case ReactionType.Quicken: return "原激化";
            case ReactionType.Overloaded: return "超载";
            case ReactionType.Bloom: return "绽放";
            case ReactionType.Vaporize: return "蒸发";
            case ReactionType.Melt: return "融化";
            case ReactionType.Frozen: return "冻结";
            case ReactionType.Burning: return "燃烧";
            case ReactionType.ElectroCharged: return "感电";
            default: return string.Empty;
        }
    }
}
