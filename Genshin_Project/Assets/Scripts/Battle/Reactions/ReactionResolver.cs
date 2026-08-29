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

        string reactionName = null;
        ReactionType reactionType = ReactionType.None;
        string reactedElement = null;
        if (context.CanTriggerReaction)
        {
            if (AmplifyingReactionHandler.TryResolve(context, out AmplifyingReactionResolution amplifying))
            {
                result.FinalDamage = amplifying.FinalDamage;
                result.RemainingAttackAmount = Math.Max(0f, amplifying.RemainingAttackAmount);
                reactionName = amplifying.DisplayName;
                reactionType = amplifying.Type;
            }
            else if (CrystallizeReactionHandler.TryResolve(
                         context,
                         out CrystallizeReactionResolution crystallize))
            {
                result.RemainingAttackAmount = Math.Max(0f, crystallize.RemainingAttackAmount);
                if (crystallize.GrantsPartyShield)
                {
                    reactionName = crystallize.DisplayName;
                    reactionType = ReactionType.Crystallize;
                    reactedElement = crystallize.ReactedElement;
                }
            }
            else if (TransformativeReactionHandler.TryResolve(
                         context,
                         out TransformativeReactionResolution transformative))
            {
                result.RemainingAttackAmount = Math.Max(0f, transformative.RemainingAttackAmount);
                if (transformative.DerivedHits != null)
                    result.DerivedHits.AddRange(transformative.DerivedHits);
                reactionName = transformative.DisplayName;
                reactionType = transformative.Type;
            }
            else
            {
                result.FinalDamage = ElementReactionManager.TryReaction(
                    context.Target,
                    context.AttackElement,
                    context.AttackAmount,
                    result.FinalDamage,
                    out reactionName,
                    out float attackRemain,
                    context);
                result.RemainingAttackAmount = Math.Max(0f, attackRemain);
                reactionType = ParseLegacyReactionType(reactionName);
            }
        }

        if (context.CanTriggerReaction
            && BloomSecondaryReactionHandler.TryResolve(
                context,
                reactionType,
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

        if (reactionType != ReactionType.None)
        {
            result.TriggeredReactions.Add(new ReactionOccurrence
            {
                Type = reactionType,
                DisplayName = reactionName,
                SourceEntity = context.SourceEntity,
                Target = context.Target,
                SourceEffectID = context.SourceEffectID,
                InvolvedElements = BuildElements(context.AttackElement, reactedElement)
            });
        }

        return result;
    }

    private static List<string> BuildElements(params string[] elements)
    {
        var result = new List<string>();
        if (elements == null) return result;
        foreach (string element in elements)
            if (!string.IsNullOrWhiteSpace(element) && !result.Contains(element)) result.Add(element);
        return result;
    }

    private static ReactionType ParseLegacyReactionType(string reactionName)
    {
        if (string.IsNullOrEmpty(reactionName)) return ReactionType.None;
        if (reactionName.StartsWith("融化", StringComparison.Ordinal)) return ReactionType.Melt;
        return ReactionType.None;
    }
}
