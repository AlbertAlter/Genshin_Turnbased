using System;
using System.Collections.Generic;

public sealed class LevelUpResult
{
    public int PreviousLevel;
    public int PreviousExperience;
    public int Level;
    public int Experience;
    public bool ReachedLevelCap;
    public bool IsMaximumLevel;
}

/// <summary>无背包、存档或 UI 副作用的升级计算。</summary>
public static class LevelUpCalculator
{
    public static LevelUpResult ApplyExperience(
        LevelUpRequirementsTable requirements,
        LevelUpTarget target,
        int currentLevel,
        int currentExperience,
        int gainedExperience,
        int levelCap,
        int star = 0)
    {
        if (requirements == null) throw new ArgumentNullException(nameof(requirements));
        if (gainedExperience < 0) throw new ArgumentOutOfRangeException(nameof(gainedExperience));
        int maximumLevel = requirements.GetMaximumLevel(target, star);
        ValidateState(requirements, target, currentLevel, currentExperience, maximumLevel, star);
        if (levelCap < currentLevel || levelCap > maximumLevel)
            throw new ArgumentOutOfRangeException(nameof(levelCap), levelCap,
                $"等级上限必须在当前等级 {currentLevel} 与最大等级 {maximumLevel} 之间。");

        int level = currentLevel;
        long experience = (long)currentExperience + gainedExperience;
        while (level < levelCap)
        {
            int required = requirements.GetExperienceRequired(target, level + 1, star);
            if (experience < required) break;
            experience -= required;
            level++;
        }

        // 临时等级上限保留全部溢出经验，突破提高上限后可用 gainedExperience=0 继续结算。
        // 绝对最高等级不保留经验。
        bool isMaximumLevel = level == maximumLevel;
        if (isMaximumLevel) experience = 0;
        if (experience > int.MaxValue)
            throw new OverflowException("本级经验超过 Int32 上限。");

        return new LevelUpResult
        {
            PreviousLevel = currentLevel,
            PreviousExperience = currentExperience,
            Level = level,
            Experience = (int)experience,
            ReachedLevelCap = level == levelCap,
            IsMaximumLevel = isMaximumLevel
        };
    }

    public static int GetExperienceToReach(
        LevelUpRequirementsTable requirements,
        LevelUpTarget target,
        int currentLevel,
        int currentExperience,
        int targetLevel,
        int star = 0)
    {
        if (requirements == null) throw new ArgumentNullException(nameof(requirements));
        int maximumLevel = requirements.GetMaximumLevel(target, star);
        ValidateState(requirements, target, currentLevel, currentExperience, maximumLevel, star);
        if (targetLevel < currentLevel || targetLevel > maximumLevel)
            throw new ArgumentOutOfRangeException(nameof(targetLevel));
        if (targetLevel == currentLevel) return 0;

        long total = -currentExperience;
        for (int level = currentLevel + 1; level <= targetLevel; level++)
            total += requirements.GetExperienceRequired(target, level, star);
        if (total <= 0) return 0;
        if (total > int.MaxValue)
            throw new OverflowException("升级所需经验超过 Int32 上限。");
        return (int)total;
    }

    public static IReadOnlyDictionary<int, int> GetSkillMaterialsToReach(
        LevelUpRequirementsTable requirements,
        int currentLevel,
        int targetLevel)
    {
        if (requirements == null) throw new ArgumentNullException(nameof(requirements));
        if (currentLevel < 1 || currentLevel > 10)
            throw new ArgumentOutOfRangeException(nameof(currentLevel));
        if (targetLevel < currentLevel || targetLevel > 10)
            throw new ArgumentOutOfRangeException(nameof(targetLevel));

        var result = new Dictionary<int, int>();
        for (int level = currentLevel + 1; level <= targetLevel; level++)
        {
            SkillMaterialRequirement requirement = requirements.GetSkillRequirement(level);
            result.TryGetValue(requirement.MaterialType, out int current);
            result[requirement.MaterialType] = checked(current + requirement.Amount);
        }
        return result;
    }

    private static void ValidateState(
        LevelUpRequirementsTable requirements,
        LevelUpTarget target,
        int level,
        int experience,
        int maximumLevel,
        int star)
    {
        int minimumLevel = target == LevelUpTarget.Artifact ? 0 : 1;
        if (level < minimumLevel || level > maximumLevel)
            throw new ArgumentOutOfRangeException(nameof(level), level,
                $"{target} 等级必须在 {minimumLevel}～{maximumLevel} 之间。");
        if (experience < 0)
            throw new ArgumentOutOfRangeException(nameof(experience));
        if (level == maximumLevel)
        {
            if (experience != 0)
                throw new ArgumentException("达到最大等级后，本级经验必须为 0。", nameof(experience));
            return;
        }

        // 临时等级上限允许本级经验超过下一等级需求；提高上限后会继续连升。
    }
}
