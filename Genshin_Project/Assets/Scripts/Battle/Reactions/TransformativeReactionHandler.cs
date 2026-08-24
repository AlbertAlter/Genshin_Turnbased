using System.Collections.Generic;

public struct TransformativeReactionResolution
{
    public ReactionType Type;
    public string DisplayName;
    public float RemainingAttackAmount;
    public List<ReactionDerivedHit> DerivedHits;
}

/// <summary>
/// 剧变反应分类入口。先由具体反应确认命中本分类，再统一读取等级系数。
/// 后续感电、燃烧、绽放等剧变反应继续接入此处。
/// </summary>
public static class TransformativeReactionHandler
{
    public static bool TryResolve(
        ReactionContext context,
        out TransformativeReactionResolution resolution)
    {
        resolution = default;

        if (FrozenReactionHandler.CanResolveFreeze(context))
        {
            if (!FrozenReactionHandler.TryResolveFreeze(context, out FrozenReactionResolution frozen))
                return false;
            resolution.Type = ReactionType.Frozen;
            resolution.DisplayName = frozen.DisplayName;
            resolution.RemainingAttackAmount = frozen.RemainingAttackAmount;
            resolution.DerivedHits = new List<ReactionDerivedHit>();
            return true;
        }

        if (SuperconductReactionHandler.CanResolve(context))
        {
            float levelCoefficient = ReactionDamageCalculator.GetLevelCoefficient(
                context.SourceEntity != null ? context.SourceEntity.Level : 1);
            if (!SuperconductReactionHandler.TryResolve(
                    context,
                    levelCoefficient,
                    out SuperconductReactionResolution superconduct))
                return false;

            resolution.Type = ReactionType.Superconduct;
            resolution.DisplayName = superconduct.DisplayName;
            resolution.RemainingAttackAmount = superconduct.RemainingAttackAmount;
            resolution.DerivedHits = superconduct.DerivedHits;
            return true;
        }

        if (ElectroChargedReactionHandler.CanResolve(context))
        {
            float levelCoefficient = ReactionDamageCalculator.GetLevelCoefficient(
                context.SourceEntity != null ? context.SourceEntity.Level : 1);
            if (!ElectroChargedReactionHandler.TryResolve(
                    context,
                    levelCoefficient,
                    out ElectroChargedReactionResolution electroCharged))
                return false;

            resolution.Type = ReactionType.ElectroCharged;
            resolution.DisplayName = electroCharged.DisplayName;
            resolution.RemainingAttackAmount = electroCharged.RemainingAttackAmount;
            resolution.DerivedHits = electroCharged.DerivedHits;
            return true;
        }

        if (QuickenReactionHandler.CanResolve(context))
        {
            if (!QuickenReactionHandler.TryResolve(
                    context,
                    out QuickenReactionResolution quicken))
                return false;

            resolution.Type = ReactionType.Quicken;
            resolution.DisplayName = quicken.DisplayName;
            resolution.RemainingAttackAmount = quicken.RemainingAttackAmount;
            resolution.DerivedHits = new List<ReactionDerivedHit>();
            return true;
        }

        if (BloomReactionHandler.CanResolve(context))
        {
            float levelCoefficient = context.SourceSnapshotOverride != null
                                     && context.SourceSnapshotOverride.LevelCoefficient > 0f
                ? context.SourceSnapshotOverride.LevelCoefficient
                : ReactionDamageCalculator.GetLevelCoefficient(
                    context.SourceEntity != null ? context.SourceEntity.Level : 1);
            if (!BloomReactionHandler.TryResolve(
                    context,
                    levelCoefficient,
                    out BloomReactionResolution bloom))
                return false;

            resolution.Type = ReactionType.Bloom;
            resolution.DisplayName = bloom.DisplayName;
            resolution.RemainingAttackAmount = bloom.RemainingAttackAmount;
            resolution.DerivedHits = bloom.DerivedHits;
            return true;
        }

        if (BurningReactionHandler.CanResolve(context))
        {
            float levelCoefficient = context.SourceSnapshotOverride != null
                                     && context.SourceSnapshotOverride.LevelCoefficient > 0f
                ? context.SourceSnapshotOverride.LevelCoefficient
                : ReactionDamageCalculator.GetLevelCoefficient(
                    context.SourceEntity != null ? context.SourceEntity.Level : 1);
            if (!BurningReactionHandler.TryResolve(
                    context,
                    levelCoefficient,
                    out BurningReactionResolution burning))
                return false;

            resolution.Type = ReactionType.Burning;
            resolution.DisplayName = burning.DisplayName;
            resolution.RemainingAttackAmount = burning.RemainingAttackAmount;
            resolution.DerivedHits = new List<ReactionDerivedHit>();
            return true;
        }

        if (OverloadedReactionHandler.CanResolve(context))
        {
            float levelCoefficient = ReactionDamageCalculator.GetLevelCoefficient(
                context.SourceEntity != null ? context.SourceEntity.Level : 1);
            if (!OverloadedReactionHandler.TryResolve(
                    context,
                    levelCoefficient,
                    out OverloadedReactionResolution overloaded))
                return false;

            resolution.Type = ReactionType.Overloaded;
            resolution.DisplayName = overloaded.DisplayName;
            resolution.RemainingAttackAmount = overloaded.RemainingAttackAmount;
            resolution.DerivedHits = overloaded.DerivedHits;
            return true;
        }

        return false;
    }
}
