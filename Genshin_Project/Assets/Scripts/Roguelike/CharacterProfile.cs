using System;

// ============================================================
// 角色档案数据（独立玩法层，2026-08-12）
// 天赋/命座/突破初始化的输入：等级、命座数、突破状态、技能等级。
// 正式数据源为章节运行时的角色养成记录；旧测试关卡仍可用等级+布尔突破输入。
// ============================================================

[Serializable]
public class CharacterProfile
{
    public int CharacterID;          // 角色ID（1009=安柏）
    public int Level;                // 等级 1-90
    public int ConstellationLevel;   // 命座 0-6
    public bool IsAscended;          // 是否已突破
    public int Ascension = -1;       // 正式养成阶段0～6；-1仅表示旧测试配置使用IsAscended，不写回存档。
    public int[] SkillLevels = new int[4] { 1, 1, 1, 1 };  // 培养输入：0普攻 1重击 2战技 3爆发
    public WeaponLoadout Weapon;       // 可空；章节/存档层提供后由 CharacterBuildApplier 装备

    /// <summary>与持有角色实例共用自身加级解析；旧测试档案仅在这里兼容布尔突破。</summary>
    public CharacterSkillLevelInfo GetSkillLevelInfo(DataManager data = null)
        => CharacterSkillLevelInfo.Resolve(CharacterID, Level,
            Ascension == -1 ? CharacterAscensionRules.FromLegacyFlag(Level, IsAscended) : Ascension,
            ConstellationLevel, SkillLevels, data ?? DataManager.Instance);

    /// <summary>从已有养成记录构造独立战斗输入，保留整数突破阶段；不改存档、经验或原技能数组。</summary>
    public static CharacterProfile FromProgress(RoguelikeCharacterProgressData progress, WeaponLoadout weapon = null)
    {
        if (progress == null) throw new ArgumentNullException(nameof(progress));
        CharacterAscensionRules.Validate(progress.Level, progress.Ascension);
        if (progress.SkillLevels == null || progress.SkillLevels.Length != 4)
            throw new ArgumentException("角色养成记录必须有四项技能等级。", nameof(progress));
        return new CharacterProfile
        {
            CharacterID = progress.CharacterID, Level = progress.Level, Ascension = progress.Ascension,
            IsAscended = CharacterAscensionRules.HasCurrentAscension(progress.Level, progress.Ascension),
            ConstellationLevel = progress.ConstellationLevel, SkillLevels = (int[])progress.SkillLevels.Clone(),
            Weapon = weapon == null ? null : new WeaponLoadout { WeaponID = weapon.WeaponID, Level = weapon.Level,
                Ascension = weapon.Ascension, Refinement = weapon.Refinement }
        };
    }

    /// <summary>从关卡编辑器配置构造档案。</summary>
    public static CharacterProfile FromStageAlly(StageAllySetup ally)
    {
        var p = new CharacterProfile
        {
            CharacterID = ally.CharacterID,
            Level = ally.Level,
            ConstellationLevel = ally.Constellation,
            IsAscended = ally.IsAscended,
        };
        if (ally.SkillLevels != null && ally.SkillLevels.Length == 4)
            for (int i = 0; i < 4; i++) p.SkillLevels[i] = ally.SkillLevels[i];
        return p;
    }
}
