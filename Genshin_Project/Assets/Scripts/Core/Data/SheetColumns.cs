using System;
using System.Collections.Generic;

// ============================================================
// 各配表的列号常量定义（Excel 列号从 1 开始）
// 集中管理，避免 DataManager 里散落魔法数字。
// 列号与 Charts 源表一致，修改配表列顺序时同步改这里。
// ============================================================
public static class SheetColumns
{
    // ---------------- Skills 表 ----------------
    public static class Skills
    {
        public const int SkillID        = 1;
        public const int SkillID2       = 2;
        public const int SkillName      = 3;
        public const int APCost         = 4;
        public const int Cooldown       = 5;
        public const int EnergyUsed     = 6;
        public const int MaxCharge      = 7;
        public const int InitialCharge  = 8;
        public const int SkillPhase     = 9;
        public const int ActionType     = 10;
        public const int Description    = 11;
    }

    // ---------------- SkillsEffect 表 ----------------
    public static class SkillsEffect
    {
        public const int SkillEffectID     = 1;
        public const int SkillEffectID2    = 2;
        public const int EffectIndex       = 3;
        public const int EffectType        = 4;
        public const int Element           = 5;
        public const int DamageType        = 6;
        public const int Duration          = 7;
        public const int AddInPhase        = 8;
        public const int TriggerPhase      = 9;
        public const int Param1            = 10;
        public const int Param2            = 11;
        public const int Param3            = 12;
        public const int TargetType        = 13;
        public const int TargetNumber      = 14;
        public const int TargetConsecutive = 15;
        public const int TargetOverride    = 16;
        public const int ScriptHook        = 17;
    }

    // ---------------- StatusData_Main 表 ----------------
    public static class StatusDataMain
    {
        public const int StatusID          = 1;
        public const int StatusID2         = 2;
        public const int StatusName        = 3;
        public const int StatusType        = 4;
        public const int Display           = 5;
        public const int Description       = 6;
        public const int MultiplierPart1   = 7;
        public const int MultiplierPart2   = 8;
        public const int MultiplierPart3   = 9;
        public const int ApplyDamageType   = 10;
        public const int ApplyReactionType = 11;
        public const int ApplyElementType  = 12;
        public const int ApplyString       = 13;
        public const int ScriptHook        = 14;
        public const int MaxCount          = 15;
        public const int MaxStack          = 16;
        public const int WhenMax           = 17;
    }

    // ---------------- Status_Effect 表 ----------------
    public static class StatusEffect
    {
        public const int StatusEffectID     = 1;
        public const int StatusEffectID2    = 2;
        public const int EffectIndex        = 3;
        public const int EffectType         = 4;
        public const int Element            = 5;
        public const int DamageType         = 6;
        public const int Duration           = 7;
        public const int AddInPhase         = 8;
        public const int TriggerPhase       = 9;
        public const int Param1             = 10;
        public const int Param2             = 11;
        public const int Param3             = 12;
        public const int TargetType         = 13;
        public const int TargetSelect       = 14;
        public const int TargetConsecutive  = 15;
        public const int TargetOverride     = 16;
        public const int ScriptHook         = 17;
    }

    // ---------------- Enemy_Main 表 ----------------
    public static class EnemyMain
    {
        public const int EnemyID           = 1;
        public const int EnemyNameID       = 2;
        public const int EnemyName         = 3;
        public const int Threat            = 4;
        public const int ActsGiven         = 5;
        public const int Poise             = 6;
        public const int ATK_Curve         = 7;
        public const int HP_coeff          = 8;
        public const int ATK_coeff         = 9;
        public const int PhysicalRes       = 10;
        public const int PyroRes           = 11;
        public const int HydroRes          = 12;
        public const int ElectroRes        = 13;
        public const int CryoRes           = 14;
        public const int AnemoRes          = 15;
        public const int DendroRes         = 16;
        public const int GeoRes            = 17;
        public const int PhysicalDmgBonus  = 18;
        public const int PyroDmgBonus      = 19;
        public const int HydroDmgBonus     = 20;
        public const int ElectroDmgBonus   = 21;
        public const int CryoDmgBonus      = 22;
        public const int AnemoDmgBonus     = 23;
        public const int DendroDmgBonus    = 24;
        public const int GeoDmgBonus       = 25;
    }

    // ---------------- EnemySkill_Main 表 ----------------
    public static class EnemySkillMain
    {
        public const int EnemySkillID    = 1;
        public const int EnemySkillID2   = 2;
        public const int EnemySkillName  = 3;
        public const int Cooldown        = 4;
        public const int UsePerTurn      = 5;
        public const int HighThreat      = 6;
        public const int Description     = 7;
    }

    // ---------------- EnemySkill_Effect 表 ----------------
    public static class EnemySkillEffect
    {
        public const int SkillEffectID     = 1;
        public const int SkillEffectID2    = 2;
        public const int EffectIndex       = 3;
        public const int EffectType        = 4;
        public const int Element           = 5;
        public const int DamageType        = 6;
        public const int Duration          = 7;
        public const int AddInPhase        = 8;
        public const int TriggerPhase      = 9;
        public const int Param1            = 10;
        public const int Param2            = 11;
        public const int Param3            = 12;
        public const int TargetType        = 13;
        public const int TargetNumber      = 14;
        public const int TargetConsecutive = 15;
        public const int TargetOverride    = 16;
        public const int ScriptHook        = 17;
    }
}
