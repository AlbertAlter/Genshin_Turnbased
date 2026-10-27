using System.Collections.Generic;
using UnityEngine;

/// <summary>雷暴云/月笼共用的来源快照和暴击后加权公式；不负责归功、扣血、计数或场地生命期。</summary>
public static class LunarWeightedDamageCalculator
{
    public static IReadOnlyList<LunarContributionSnapshot> Capture(TeamBonusSystem team,
        IEnumerable<BattleEntity> sources, ReactionType reaction, string element, string effectID)
    {
        var distinct = new HashSet<BattleEntity>(sources);
        var snapshots = new List<LunarContributionSnapshot>();
        foreach (BattleEntity source in team.Members)
            if (distinct.Contains(source))
            {
                var bonuses = ReactionDamageCalculator.CollectLunarReactionBonuses(source, reaction, element, effectID);
                snapshots.Add(new LunarContributionSnapshot(source,
                    ReactionDamageCalculator.GetLevelCoefficient(source.Level), source.TotalEM + bonuses.EMBonus,
                    bonuses, bonuses.CritRate, bonuses.CritDMG));
            }
        return snapshots.AsReadOnly();
    }

    /// <summary>每个实际目标独立抽取；按含暴击得分排序，60%/30%/其余各5%，不归一化。</summary>
    public static float Calculate(IReadOnlyList<LunarContributionSnapshot> sources, float reactionMultiplier,
        float resistance, out IReadOnlyList<LunarDamageContribution> contributions, BattleEntity target = null,
        ReactionType reaction = ReactionType.LunarElectroCharged)
    {
        var scores = new List<KeyValuePair<int, CriticalHitResult>>();
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var current = ReactionDamageCalculator.CollectLiveTargetBonuses(source.Source, target, reaction,
                reaction == ReactionType.LunarCrystallize ? "Geo" : "Electro", includeCrit: true);
            var bonuses = source.Bonuses;
            bonuses.LunarDMGBonus += current.LunarDMGBonus;
            float beforeCritical = Mathf.Max(0f, SpecialSkillDamageCalculator.CalculateBeforeCritical(
                source.LevelCoefficient, reactionMultiplier, source.Mastery, bonuses, true, resistance, bonuses.Elevation));
            scores.Add(new KeyValuePair<int, CriticalHitResult>(i,
                CriticalHitResolver.Resolve(beforeCritical, source.CritRate + current.CritRate, source.CritDMG + current.CritDMG)));
        }
        scores.Sort((a, b) =>
        {
            int comparison = b.Value.DamageAfterCritical.CompareTo(a.Value.DamageAfterCritical);
            return comparison != 0 ? comparison : a.Key.CompareTo(b.Key);
        });
        float damage = 0f;
        var detail = new List<LunarDamageContribution>();
        for (int rank = 0; rank < scores.Count; rank++)
        {
            var score = scores[rank];
            var contribution = new LunarDamageContribution(sources[score.Key], score.Value,
                rank == 0 ? .6f : rank == 1 ? .3f : .05f);
            detail.Add(contribution); damage += contribution.WeightedDamage;
        }
        contributions = detail.AsReadOnly();
        return damage;
    }
}
