using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>角色自身的技能等级只读结果：培养等级与固定加级分开；不含队伍或战斗临时修正。</summary>
public sealed class CharacterSkillLevelInfo
{
    public const int SkillCount = 4;
    public const int MaximumLevel = 15;
    private readonly int[] baseLevels, bonuses, ownLevels;

    private CharacterSkillLevelInfo(int[] basis, int[] extra)
    {
        baseLevels = basis; bonuses = extra; ownLevels = new int[SkillCount];
        for (int i = 0; i < SkillCount; i++)
            ownLevels[i] = (int)Math.Max(1L, Math.Min(MaximumLevel, (long)basis[i] + extra[i]));
    }

    public int GetBaseLevel(int skillType) => baseLevels[CheckedIndex(skillType)];
    public int GetBonus(int skillType) => bonuses[CheckedIndex(skillType)];
    public int GetOwnLevel(int skillType) => ownLevels[CheckedIndex(skillType)];
    public int[] CopyOwnLevels() => (int[])ownLevels.Clone();

    private static int CheckedIndex(int skillType)
    {
        if (skillType < 0 || skillType >= SkillCount) throw new ArgumentOutOfRangeException(nameof(skillType));
        return skillType;
    }

    /// <summary>
    /// 从持有角色的培养记录和已解锁的自身Modifiers读取固定加级。查询不修改存档、配表或战斗控制器。
    /// 兼容旧测试输入1～15；正式升级交易仍只培养1～10，不把加级后的结果写回SkillLevels。
    /// </summary>
    public static CharacterSkillLevelInfo Resolve(int characterID, int level, int ascension,
        int constellation, IReadOnlyList<int> levels, DataManager data)
    {
        if (data == null) throw new InvalidOperationException("读取角色自身技能等级需要已提供的角色配表。");
        CharacterAscensionRules.Validate(level, ascension);
        if (constellation < 0 || constellation > 6) throw new ArgumentOutOfRangeException(nameof(constellation));
        if (levels == null || levels.Count != SkillCount)
            throw new ArgumentException("角色必须提供四项培养技能等级。", nameof(levels));
        var basis = new int[SkillCount]; var extra = new int[SkillCount];
        for (int i = 0; i < SkillCount; i++)
        {
            if (levels[i] < 1 || levels[i] > MaximumLevel)
                throw new ArgumentOutOfRangeException(nameof(levels), "技能等级必须在1～15之间。");
            basis[i] = levels[i];
        }
        if (data.TalentDict.TryGetValue(characterID, out var talents))
            foreach (var talent in talents)
                if (CharacterAscensionRules.IsTalentUnlocked(talent.UnlockAfter, level, ascension))
                    CollectBonuses(talent.Modifiers, extra);
        if (data.ConstellationDict.TryGetValue(characterID, out var constellations))
            foreach (var row in constellations)
                if (row.ConstellationIndex > 0 && row.ConstellationIndex <= constellation)
                    CollectBonuses(row.Modifiers, extra);
        return new CharacterSkillLevelInfo(basis, extra);
    }

    private static void CollectBonuses(string modifiers, int[] bonuses)
    {
        foreach (string command in CharacterModifierParser.SplitCommands(modifiers))
        {
            int opening = command.IndexOf('(');
            string name = opening >= 0 ? command.Substring(0, opening).Trim() : command;
            if (name != "SkillLevelUp") continue;
            if (opening < 0 || !command.EndsWith(")", StringComparison.Ordinal))
                throw new FormatException("SkillLevelUp格式错误：" + command);
            string[] args = command.Substring(opening + 1, command.Length - opening - 2).Split(',');
            if (args.Length != 2
                || !int.TryParse(args[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int type)
                || type < 0 || type >= SkillCount
                || !int.TryParse(args[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
                throw new FormatException("SkillLevelUp需要技能类型0～3与整数加级量：" + command);
            bonuses[type] = checked(bonuses[type] + amount);
        }
    }
}

/// <summary>角色配表Modifiers的共同分句规则；角色养成查询和战斗构筑共用，不执行任何指令。</summary>
internal static class CharacterModifierParser
{
    internal static List<string> SplitCommands(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        int depth = 0, start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth < 0)
                throw new FormatException("Modifiers 括号不匹配: " + text);
            if (depth != 0 || (text[i] != ';' && text[i] != '\n' && text[i] != '\r')) continue;
            string command = text.Substring(start, i - start).Trim();
            if (command.Length > 0) result.Add(command);
            start = i + 1;
        }
        if (depth != 0) throw new FormatException("Modifiers 括号不匹配: " + text);
        string last = text.Substring(start).Trim();
        if (last.Length > 0) result.Add(last);
        return result;
    }
}
