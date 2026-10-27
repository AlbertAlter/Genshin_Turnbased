using System;

/// <summary>角色突破阶段0～6与配表等级门槛的唯一映射；不读表、不写养成记录、不代办突破。</summary>
public static class CharacterAscensionRules
{
    private static readonly int[] Thresholds = { 20, 40, 50, 60, 70, 80 };

    public static void Validate(int level, int ascension)
    {
        if (level < 1 || level > 90) throw new ArgumentOutOfRangeException(nameof(level));
        if (ascension < 0 || ascension > 6) throw new ArgumentOutOfRangeException(nameof(ascension));
    }

    /// <summary>旧测试关卡只有布尔值时的兼容转换；正式存档必须直接使用整数阶段，不走推断。</summary>
    public static int FromLegacyFlag(int level, bool isAscended)
    {
        Validate(level, 0);
        int stage = 0;
        foreach (int threshold in Thresholds)
            if (level > threshold || level == threshold && isAscended) stage++;
        return stage;
    }

    public static bool IncludesRow(int rowLevel, int level, int ascension)
    {
        Validate(level, ascension);
        if (rowLevel <= 1) return true;
        int index = Array.IndexOf(Thresholds, rowLevel);
        if (index < 0) throw new ArgumentException("角色突破行不是有效等级门槛：" + rowLevel, nameof(rowLevel));
        return rowLevel <= level && ascension >= index + 1;
    }

    /// <summary>UnlockAfter留空/0默认解锁；其他门槛必须已完成该等级的突破，不按等级补推正式阶段。</summary>
    public static bool IsTalentUnlocked(int unlockAfter, int level, int ascension)
        => unlockAfter <= 0 || IncludesRow(unlockAfter, level, ascension);

    /// <summary>兼容旧条件标记；阶段本身仍独立保留，绝不压成bool后用于面板。</summary>
    public static bool HasCurrentAscension(int level, int ascension)
    {
        Validate(level, ascension);
        int index = Array.IndexOf(Thresholds, level);
        return index >= 0 ? ascension >= index + 1 : ascension > 0;
    }
}
