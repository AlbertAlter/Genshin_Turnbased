using System;

// ============================================================
// 角色档案数据（独立玩法层，2026-08-12）
// 天赋/命座/突破初始化的输入：等级、命座数、突破状态、技能等级。
// 数据源：现在从关卡配置（StageConfig.Allies）读取；
//         存档系统做好后改为从存档读取，初始化逻辑（CharacterBuildApplier）不变。
// ============================================================

[Serializable]
public class CharacterProfile
{
    public int CharacterID;          // 角色ID（1009=安柏）
    public int Level;                // 等级 1-90
    public int ConstellationLevel;   // 命座 0-6
    public bool IsAscended;          // 是否已突破
    public int[] SkillLevels = new int[4] { 1, 1, 1, 1 };  // 0普攻 1重击 2战技 3爆发
    public WeaponLoadout Weapon;       // 可空；章节/存档层提供后由 CharacterBuildApplier 装备

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
