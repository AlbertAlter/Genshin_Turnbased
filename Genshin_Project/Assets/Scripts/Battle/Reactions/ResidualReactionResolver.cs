using System;
using System.Collections.Generic;

/// <summary>普通接触和扩散共用的残留清算，只提交元素消耗与反应的非即时伤害效果。</summary>
internal static class ResidualReactionResolver
{
    private const float Epsilon = 0.0001f;
    private static readonly (string Attack, string Aura)[] OrderedPairs =
    {
        ("Electro", "Cryo"), ("Cryo", "Electro"), ("Electro", "Dendro"), ("Dendro", "Electro"),
        ("Pyro", "Electro"), ("Electro", "Pyro"), ("Hydro", "Dendro"), ("Dendro", "Hydro"),
        ("Hydro", "Pyro"), ("Pyro", "Hydro"), ("Pyro", "Cryo"), ("Cryo", "Pyro"),
        ("Hydro", "Cryo"), ("Cryo", "Hydro"),
        ("Pyro", "Geo"), ("Hydro", "Geo"), ("Electro", "Geo"), ("Cryo", "Geo"),
        ("Pyro", "Dendro"), ("Dendro", "Pyro"), ("Hydro", "Electro"), ("Electro", "Hydro"),
    };

    internal static void Resolve(BattleEntity target, ReactionContext source, ReactionResult aggregate, IReadOnlyList<long> causes)
    {
        if (source == null || target == null || !target.IsAlive || target.GetEnemyElementalShield() != null) return;
        var reactionCauses = new List<long>(causes ?? Array.Empty<long>());
        bool conducting = aggregate.TriggeredReactions.Exists(reaction => ReferenceEquals(reaction.Target, target)
            && reaction.Type == ReactionType.LunarElectroCharged);
        while (target.IsAlive && target.GetEnemyElementalShield() == null)
        {
            if (ResolveSuperconductAndQuicken(target, source, aggregate, reactionCauses)) continue;
            if (!TryFindPair(target, source.IgnoreFrozenAura, conducting, out var incoming, out string opposing)) break;

            var context = CreateContext(source, target, incoming.Element, opposing, incoming.Amount, reactionCauses.ToArray());
            var resolved = ReactionResolver.ResolveResidual(context, incoming.Kind);
            foreach (var reaction in resolved.TriggeredReactions)
            {
                aggregate.TriggeredReactions.Add(reaction);
                reactionCauses.Add(reaction.Declaration.Header.EventId);
                conducting |= reaction.Type == ReactionType.LunarElectroCharged || reaction.Type == ReactionType.ElectroCharged;
            }
            // 攻击侧总是有限池；没有实际消耗就停止，避免对固有池或不再适用的组合反复求解。
            if (ElementQuery.GetAmount(target, incoming.Element, incoming.Kind) >= incoming.Amount) break;
        }
    }

    private static bool TryFindPair(BattleEntity target, bool ignoreFrozen, bool conducting,
        out ElementPoolReading incoming, out string opposing)
    {
        foreach (var pair in OrderedPairs)
        {
            var type = ReactionPairRules.GetReaction(pair.Attack, pair.Aura);
            if (type == ReactionType.ElectroCharged && (conducting || ElectroChargedReactionHandler.IsElectroCharged(target)))
                continue;
            incoming = ElementQuery.ReadNormal(target, pair.Attack);
            bool usesNormalPools = type == ReactionType.Frozen || type == ReactionType.Burning;
            if (!usesNormalPools && !(incoming?.Amount > Epsilon))
            {
                if (pair.Attack == "Cryo" && !ignoreFrozen)
                    incoming = ElementQuery.ReadPool(target, pair.Attack, ReactionPoolKind.Frozen);
                else if (pair.Attack == "Pyro")
                    incoming = ElementQuery.ReadPool(target, pair.Attack, ReactionPoolKind.Burning);
            }
            if (!(incoming?.Amount > Epsilon)) continue;

            float required = ReactionPairRules.RequiredAura(pair.Attack, pair.Aura, incoming.Amount);
            float available = usesNormalPools
                ? ElementQuery.ReadAura(target, pair.Aura)?.AvailableFor(required) ?? 0f
                : ElementQuery.GetAvailableReactionAmount(target, pair.Aura, required, ignoreFrozen);
            if (available <= Epsilon) continue;
            opposing = pair.Aura;
            return true;
        }
        incoming = null;
        opposing = null;
        return false;
    }

    private static bool ResolveSuperconductAndQuicken(BattleEntity target, ReactionContext source, ReactionResult aggregate,
        List<long> causes)
    {
        float electro = ElementQuery.GetAmount(target, "Electro");
        if (electro <= Epsilon
            || ElementQuery.GetAvailableReactionAmount(target, "Cryo", electro, source.IgnoreFrozenAura) <= Epsilon
            || (ElementQuery.ReadAura(target, "Dendro")?.AvailableFor(electro) ?? 0f) <= Epsilon)
            return false;

        using var equipment = BattleResolutionContext.BeginEquipmentEffectSource(source.EffectiveEquipmentSource != null);
        using var execution = BattleEventHub.BeginEffectExecution(source.EffectExecutionID);
        using var hit = BattleEventHub.BeginHit(source.LogicalHitID != 0 ? source.LogicalHitID : BattleEventHub.CurrentHitId, source.TargetIndex);
        using var elements = ElementEventScope.Begin("ResidualReaction", target);
        var directCauses = causes.ToArray();
        float quota = electro * 0.5f;
        var ice = CreateContext(source, target, "Electro", "Cryo", quota, directCauses);
        float level = ReactionDamageCalculator.GetLevelCoefficient(ice.SourceEntity != null ? ice.SourceEntity.Level : 1);
        bool superconduct = SuperconductReactionHandler.TryResolve(ice, level, out var superconductResult);
        var grass = CreateContext(source, target, "Electro", "Dendro", quota, directCauses);
        bool quicken = QuickenReactionHandler.TryResolve(grass, out var quickenResult);

        float consumed = (superconduct ? quota - superconductResult.RemainingAttackAmount : 0f)
            + (quicken ? quota - quickenResult.RemainingAttackAmount : 0f);
        if (consumed <= 0f) return false;
        target.ConsumeAura("Electro", Math.Min(electro, consumed));
        if (superconduct)
            AddReaction(superconductResult.Type, superconductResult.DisplayName, "Cryo", superconductResult.PendingStellarSuperconduct);
        if (quicken) AddReaction(ReactionType.Quicken, "原激化", "Dendro", null);
        return true;

        void AddReaction(ReactionType type, string name, string opposing, PendingStellarSuperconduct pending)
        {
            var reaction = new ReactionOccurrence
            {
                Type = type,
                DisplayName = name,
                SourceEntity = source.SourceEntity ?? source.SourceSnapshotOverride?.SourceEntity,
                Target = target,
                SourceEffectID = source.SourceEffectID,
                PendingStellarSuperconduct = pending,
                InvolvedElements = new List<string> { "Electro", opposing },
            };
            ReactionResolver.RecordResidualReaction(reaction, directCauses, elements);
            aggregate.TriggeredReactions.Add(reaction);
            causes.Add(reaction.Declaration.Header.EventId);
        }
    }

    private static ReactionContext CreateContext(ReactionContext source, BattleEntity target, string attack, string aura,
        float amount, IReadOnlyList<long> causes)
        => new ReactionContext
        {
            SourceEntity = source.SourceEntity,
            EquipmentSource = source.EffectiveEquipmentSource,
            Target = target,
            SourceKind = source.SourceKind,
            SourceSkillID = source.SourceSkillID,
            SourceEffectID = source.SourceEffectID,
            EffectExecutionID = source.EffectExecutionID,
            LogicalHitID = source.LogicalHitID,
            TargetIndex = source.TargetIndex,
            CauseEventIds = causes,
            ApplicationPhase = source.ApplicationPhase,
            AttackElement = attack,
            AttackAmount = amount,
            DamageType = "Reaction",
            SourceSnapshotOverride = source.SourceSnapshotOverride,
            CanTriggerShatter = false,
            IgnoreFrozenAura = source.IgnoreFrozenAura,
            PreserveIncomingAuraBeforeReaction = true,
            RestrictedAuraElement = aura,
            RestrictedAuraKind = ReactionPoolKind.Any,
        };
}
