using System;
using System.Collections.Generic;
using System.IO;

public enum LevelUpTarget
{
    Character,
    Weapon,
    Artifact
}

public sealed class SkillMaterialRequirement
{
    public int NextLevel;
    public int Amount;
    public int MaterialType;
}

/// <summary>LevelUpRequirements(In_use).xlsx 的只读运行时数据。</summary>
public sealed class LevelUpRequirementsTable
{
    private readonly Dictionary<int, int> _character = new Dictionary<int, int>();
    private readonly Dictionary<int, Dictionary<int, int>> _weapon =
        new Dictionary<int, Dictionary<int, int>>();
    private readonly Dictionary<int, Dictionary<int, int>> _artifact =
        new Dictionary<int, Dictionary<int, int>>();
    private readonly Dictionary<int, SkillMaterialRequirement> _skill =
        new Dictionary<int, SkillMaterialRequirement>();

    public void AddCharacter(int nextLevel, int experience)
    {
        AddUnique(_character, nextLevel, experience, "Character");
    }

    public void AddWeapon(int star, int nextLevel, int experience)
    {
        AddUnique(GetStarTable(_weapon, star, "Weapon"), nextLevel, experience, $"Weapon/{star}星");
    }

    public void AddArtifact(int star, int nextLevel, int experience)
    {
        AddUnique(GetStarTable(_artifact, star, "Artifact"), nextLevel, experience, $"Artifact/{star}星");
    }

    public void AddSkill(int nextLevel, int amount, int materialType)
    {
        if (nextLevel <= 1) throw new ArgumentOutOfRangeException(nameof(nextLevel));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (materialType <= 0) throw new ArgumentOutOfRangeException(nameof(materialType));
        if (_skill.ContainsKey(nextLevel))
            throw new InvalidDataException($"SkillLevel 存在重复等级：{nextLevel}");

        _skill.Add(nextLevel, new SkillMaterialRequirement
        {
            NextLevel = nextLevel,
            Amount = amount,
            MaterialType = materialType
        });
    }

    public int GetExperienceRequired(LevelUpTarget target, int nextLevel, int star = 0)
    {
        Dictionary<int, int> table;
        switch (target)
        {
            case LevelUpTarget.Character:
                table = _character;
                break;
            case LevelUpTarget.Weapon:
                table = GetExistingStarTable(_weapon, star, "Weapon");
                break;
            case LevelUpTarget.Artifact:
                table = GetExistingStarTable(_artifact, star, "Artifact");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }

        if (!table.TryGetValue(nextLevel, out int experience))
            throw new ArgumentOutOfRangeException(nameof(nextLevel), nextLevel,
                $"{target}/{star}星没有通往等级 {nextLevel} 的经验配置。");
        return experience;
    }

    public int GetMaximumLevel(LevelUpTarget target, int star = 0)
    {
        Dictionary<int, int> table;
        switch (target)
        {
            case LevelUpTarget.Character:
                table = _character;
                break;
            case LevelUpTarget.Weapon:
                table = GetExistingStarTable(_weapon, star, "Weapon");
                break;
            case LevelUpTarget.Artifact:
                table = GetExistingStarTable(_artifact, star, "Artifact");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, null);
        }

        int maximum = int.MinValue;
        foreach (int level in table.Keys)
            if (level > maximum) maximum = level;
        if (maximum == int.MinValue)
            throw new InvalidDataException($"{target}/{star}星没有经验配置。");
        return maximum;
    }

    public SkillMaterialRequirement GetSkillRequirement(int nextLevel)
    {
        if (!_skill.TryGetValue(nextLevel, out SkillMaterialRequirement requirement))
            throw new ArgumentOutOfRangeException(nameof(nextLevel), nextLevel,
                $"SkillLevel 没有通往等级 {nextLevel} 的材料配置。");
        return requirement;
    }

    public void Validate()
    {
        ValidateRange(_character, 2, 90, "Character");
        ValidateRange(GetExistingStarTable(_weapon, 3, "Weapon"), 2, 90, "Weapon/3星");
        ValidateRange(GetExistingStarTable(_weapon, 4, "Weapon"), 2, 90, "Weapon/4星");
        ValidateRange(GetExistingStarTable(_weapon, 5, "Weapon"), 2, 90, "Weapon/5星");
        ValidateRange(GetExistingStarTable(_artifact, 3, "Artifact"), 1, 12, "Artifact/3星");
        ValidateRange(GetExistingStarTable(_artifact, 4, "Artifact"), 1, 16, "Artifact/4星");
        ValidateRange(GetExistingStarTable(_artifact, 5, "Artifact"), 1, 20, "Artifact/5星");

        for (int level = 2; level <= 10; level++)
            if (!_skill.ContainsKey(level))
                throw new InvalidDataException($"SkillLevel 缺少等级 {level}。");
        if (_skill.Count != 9)
            throw new InvalidDataException("SkillLevel 只允许配置等级 2～10。");
    }

    private static Dictionary<int, int> GetStarTable(
        Dictionary<int, Dictionary<int, int>> source,
        int star,
        string name)
    {
        if (star < 3 || star > 5)
            throw new ArgumentOutOfRangeException(nameof(star), star, $"{name} 星级必须为 3～5。");
        if (!source.TryGetValue(star, out Dictionary<int, int> table))
        {
            table = new Dictionary<int, int>();
            source.Add(star, table);
        }
        return table;
    }

    private static Dictionary<int, int> GetExistingStarTable(
        Dictionary<int, Dictionary<int, int>> source,
        int star,
        string name)
    {
        if (star < 3 || star > 5)
            throw new ArgumentOutOfRangeException(nameof(star), star, $"{name} 星级必须为 3～5。");
        if (!source.TryGetValue(star, out Dictionary<int, int> table))
            throw new InvalidDataException($"{name} 缺少 {star} 星配置。");
        return table;
    }

    private static void AddUnique(Dictionary<int, int> table, int nextLevel, int experience, string name)
    {
        if (nextLevel <= 0) throw new ArgumentOutOfRangeException(nameof(nextLevel));
        if (experience <= 0) throw new ArgumentOutOfRangeException(nameof(experience));
        if (table.ContainsKey(nextLevel))
            throw new InvalidDataException($"{name} 存在重复等级：{nextLevel}");
        table.Add(nextLevel, experience);
    }

    private static void ValidateRange(Dictionary<int, int> table, int first, int last, string name)
    {
        for (int level = first; level <= last; level++)
            if (!table.ContainsKey(level))
                throw new InvalidDataException($"{name} 缺少等级 {level}。");
        if (table.Count != last - first + 1)
            throw new InvalidDataException($"{name} 只能配置等级 {first}～{last}。");
    }
}
