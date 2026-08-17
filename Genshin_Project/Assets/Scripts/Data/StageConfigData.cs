using System;
using System.Collections.Generic;

// ============================================================
// 关卡配置（由 Tools/关卡编辑器 生成，运行时 BattleTester 读取）
// 注意：与 DataContainer.cs 中的旧 StageConfigData（xlsx 加载用）区分，
//       本类专用于编辑器工具生成的 JSON 关卡配置。
// ============================================================

[Serializable]
public class StageAllySetup
{
    public int CharacterID;      // 角色ID（0=空槽）
    public int Level;            // 等级 1-90
    public int Constellation;    // 命座 0-6
    public bool IsAscended;      // 是否已突破（吃突破加成）
    // 技能等级（1-15，顺序：0=普攻 1=重击 2=战技 3=爆发）
    public int[] SkillLevels = new int[4] { 1, 1, 1, 1 };   // 默认全1级
}

[Serializable]
public class StageEnemySetup
{
    public int EnemyID;          // 敌人ID（0=空位）
    public int Level;            // 等级 1-100
    public int Slot;             // 敌方位置 1-5（1=最右）
}

[Serializable]
public class StageSetupData
{
    public string StageName = "测试关卡";                 // 关卡名
    public List<StageAllySetup> Allies = new List<StageAllySetup>();      // 我方队伍（最多4）
    public List<StageEnemySetup> Enemies = new List<StageEnemySetup>();   // 敌方阵容（最多5）
    public string InitialEnergyMode = "Percent";   // Percent=百分比 / Fixed=固定数值
    public float InitialEnergyValue = 0f;          // 对应模式的数值

    public static StageSetupData CreateDefault()
    {
        var cfg = new StageSetupData();
        cfg.Allies.Add(new StageAllySetup { CharacterID = 1009, Level = 1, Constellation = 0, IsAscended = false });
        cfg.Enemies.Add(new StageEnemySetup { EnemyID = 20000, Level = 1, Slot = 3 });  // 木桩
        cfg.Enemies.Add(new StageEnemySetup { EnemyID = 20001, Level = 1, Slot = 2 });  // 丘丘人
        cfg.Enemies.Add(new StageEnemySetup { EnemyID = 20001, Level = 1, Slot = 4 });  // 丘丘人
        return cfg;
    }
}
