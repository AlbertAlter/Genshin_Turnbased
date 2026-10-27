using UnityEngine;

public struct CrystallizeReactionResolution
{
    public ReactionType Type;
    public PendingLunarCrystallize PendingLunarCrystallize;
    public string DisplayName;
    public string ReactedElement;
    public float RemainingAttackAmount;
    public float ShieldValue;
    public bool GrantsPartyShield;
}

/// <summary>Ordinary Geo crystallize and enemy Geo neutralization.</summary>
public static class CrystallizeReactionHandler
{
    private const float Epsilon = 0.0001f;

    public static bool CanResolve(ReactionContext context)
    {
        return TrySelectReactedElement(context, out _, out _);
    }

    public static bool TryResolve(
        ReactionContext context,
        out CrystallizeReactionResolution resolution)
    {
        resolution = default;
        if (!TrySelectReactedElement(context, out string reactedElement, out float availableAmount))
            return false;

        bool reverse = context.AttackElement != "Geo";
        bool lunar = LunarCrystallizeReactionHandler.TryPrepare(context, reverse ? "Geo" : reactedElement, out var pending);

        float otherConsumed = reverse ? context.AttackAmount : Mathf.Min(availableAmount, context.AttackAmount / 2f);
        if (otherConsumed <= Epsilon) return false;

        if (!reverse) ConsumeReactedElement(context, reactedElement, otherConsumed);

        if (lunar)
        {
            resolution = new CrystallizeReactionResolution { Type = ReactionType.LunarCrystallize,
                DisplayName = "月结晶", ReactedElement = reverse ? "Geo" : reactedElement, RemainingAttackAmount = 0f,
                PendingLunarCrystallize = pending };
            return true; // 水岩消耗仍按1:2；不生成普通结晶盾，也不把协奏伪装成本次接触的即时伤害。
        }

        ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(context);
        bool sourceIsEnemy = IsEnemySource(context, snapshot);
        bool sourceIsConfirmedAlly = !sourceIsEnemy && IsConfirmedAllySource(context, snapshot);
        float shieldValue = sourceIsConfirmedAlly ? GrantPartyShield(context, reactedElement) : 0f;

        resolution = new CrystallizeReactionResolution
        {
            Type = sourceIsConfirmedAlly ? ReactionType.Crystallize : ReactionType.None,
            DisplayName = sourceIsConfirmedAlly ? "结晶" : null,
            ReactedElement = reverse ? "Geo" : reactedElement,
            RemainingAttackAmount = 0f,
            ShieldValue = shieldValue,
            GrantsPartyShield = sourceIsConfirmedAlly
        };
        return true;
    }

    internal static bool IsPartySource(ReactionContext context)
    {
        var snapshot = ReactionSourceSnapshot.Capture(context);
        return !IsEnemySource(context, snapshot) && IsConfirmedAllySource(context, snapshot);
    }

    /// <summary>只提交护盾，供已经完成元素消耗的扩散分份复用；不会再次反应或扣量。</summary>
    internal static float GrantPartyShield(ReactionContext context, string element)
    {
        var snapshot = ReactionSourceSnapshot.Capture(context);
        if (!IsPartySource(context)) return 0f;
        int level = snapshot != null && snapshot.Level > 0 ? snapshot.Level : context.SourceEntity != null ? context.SourceEntity.Level : 1;
        float coefficient = snapshot != null && snapshot.LevelCoefficient > 0f
            ? snapshot.LevelCoefficient : ReactionDamageCalculator.GetLevelCoefficient(level);
        float shield = Mathf.Max(0f, ReactionDamageCalculator.CalculateCharacterCrystalShield(coefficient,
            snapshot != null ? Mathf.Max(0f, snapshot.TotalEM) : 0f));
        var battle = BattleManager.Instance;
        if (battle != null) CrystallizeShieldSystem.ApplyPartyShield(battle, element, shield, context.ApplicationPhase,
            context.SourceEntity ?? context.SourceSnapshotOverride?.SourceEntity);
        else LogManager.LogWarning(LogCategory.Reaction, "结晶反应已完成，但当前没有 BattleManager，无法为队伍施加结晶盾。");
        return shield;
    }

    private static bool TrySelectReactedElement(
        ReactionContext context,
        out string reactedElement,
        out float availableAmount)
    {
        reactedElement = null;
        availableAmount = 0f;
        if (context == null
            || !context.CanTriggerReaction
            || context.Target == null
            || !context.Target.IsAlive
            || context.AttackAmount <= Epsilon)
            return false;

        if (context.AttackElement != "Geo")
        {
            if (!ReactionPairRules.IsSwirlable(context.AttackElement)
                || !(context.AllowsReactionPool("Geo", ReactionPoolKind.Intrinsic)
                    && ElementQuery.ReadIntrinsic(context.Target, "Geo") != null
                    || ElementQuery.ReadShield(context, "Geo") != null)) return false;
            reactedElement = context.AttackElement;
            availableAmount = context.AttackAmount;
            return true;
        }

        float pyro = ElementQuery.GetReactionAmount(context, "Pyro");
        if (pyro > Epsilon)
        {
            reactedElement = "Pyro";
            availableAmount = pyro;
            return true;
        }

        float hydro = ElementQuery.GetReactionAmount(context, "Hydro");
        if (hydro > Epsilon)
        {
            reactedElement = "Hydro";
            availableAmount = hydro;
            return true;
        }

        float electro = ElementQuery.GetReactionAmount(context, "Electro");
        if (electro > Epsilon)
        {
            reactedElement = "Electro";
            availableAmount = electro;
            return true;
        }

        float cryo = ElementQuery.GetReactionAmount(context, "Cryo");
        if (cryo <= Epsilon) return false;

        reactedElement = "Cryo";
        availableAmount = cryo;
        return true;
    }

    private static void ConsumeReactedElement(
        ReactionContext context,
        string reactedElement,
        float amount)
    {
        ElementAuraSystem.ConsumeReactionAura(context, reactedElement, amount);
    }

    private static bool IsEnemySource(ReactionContext context, ReactionSourceSnapshot snapshot)
    {
        return context.SourceKind == ReactionSourceKind.EnemySkill
            || (context.SourceEntity != null
                && (context.SourceEntity.Type == BattleEntity.EntityType.Enemy
                    || context.SourceEntity.Side == BattleSide.Enemy))
            || (snapshot != null
                && (snapshot.SourceKind == ReactionSourceKind.EnemySkill
                    || snapshot.SourceSide == BattleSide.Enemy
                    || (snapshot.SourceEntity != null
                        && snapshot.SourceEntity.Type == BattleEntity.EntityType.Enemy)));
    }

    private static bool IsConfirmedAllySource(
        ReactionContext context,
        ReactionSourceSnapshot snapshot)
    {
        if (context.SourceEntity != null)
            return context.SourceEntity.Type == BattleEntity.EntityType.Character
                && context.SourceEntity.Side == BattleSide.Ally;

        return context.SourceSnapshotOverride != null
            && snapshot != null
            && snapshot.SourceSide == BattleSide.Ally
            && (snapshot.SourceEntity != null || snapshot.SourceEntityID != 0);
    }
}
