using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战斗实体类，角色和敌人共用。
/// 属性字段对应伤害计算配表术语 1.1~1.4。
/// </summary>
public partial class BattleEntity : MonoBehaviour
{
    public enum EntityType { Character, Enemy }

    /// <summary>全局施加序号计数器：保证状态按施加顺序结算</summary>
    public static long _applyOrderCounter;

    // ========== 身份 ==========
    public EntityType Type;
    public int EntityID;
    /// <summary>角色控制器引用（角色实体在 Init 时由 CharacterBattleController 设置，用于状态行动回找执行器）</summary>
    [NonSerialized] public CharacterBattleController CharacterCtrl;

    // ========== 位置（2026-08-06 位置系统） ==========
    /// <summary>所在阵营（Ally=我方/Enemy=敌方）</summary>
    public BattleSide Side;
    /// <summary>在己方队列中的位置编号（我方 1-4 从左往右；敌方 1-5 从右往左）</summary>
    public int SlotPosition;
    /// <summary>威胁度（Enemy_Main.Threat，自动选择目标用；空=0）</summary>
    public int Threat;
    public int ConstellationLevel;   // 命座等级 0-6（效果实装前仅存储）
    public bool IsAscended;          // 是否已突破（突破加成实装前仅存储）

    // ========== 等级 ==========
    public int Level;

    // ========== 基础属性（公式 1.1 ~ 1.4） ==========
    public float TotalHP;
    public float CurrentHP;
    public float TotalATK;
    public float TotalDEF;

    // ========== 武器面板（角色基础面板之外独立保存，便于安全卸装） ==========
    public float WeaponATK;
    public float WeaponATKBonus;
    public float WeaponHPBonus;
    public float WeaponDEFBonus;
    public float WeaponEM;
    public float WeaponRechargeBonus;
    public float WeaponCritRate;
    public float WeaponCritDMG;
    public float WeaponPhysicalDmgBonus;
    public float WeaponPyroDmgBonus;
    public float WeaponHydroDmgBonus;
    public float WeaponElectroDmgBonus;
    public float WeaponCryoDmgBonus;
    public float WeaponAnemoDmgBonus;
    public float WeaponDendroDmgBonus;
    public float WeaponGeoDmgBonus;

    // ========== 元素精通（公式 1.11: TotalEM = EM + WeaponEM + EMBonus） ==========
    public float EM;
    public float TotalEM;

    // ========== 暴击（公式 1.6） ==========
    public float CritRate;
    public float CritDMG;

    // ========== 充能（公式 1.12） ==========
    public float TotalRechargeRate;
    public int MaxEnergy;
    public int CurrentEnergy;

    // ========== 伤害加成类（公式 1.8 DMGBonus） ==========
    public float BaseDMGBonusFlat;
    public float DMGBonus;
    public float BaseDMGBonus;
    public float LunarDMGBonus;
    public float StellarDMGBonus;

    // ========== 防御穿透类（公式 1.5 DEF） ==========
    public float DEFReduction;
    public float DEFIgnored;

    // ========== 抗性（公式 1.7 Res） ==========
    public float PhysicalRes;
    public float PyroRes;
    public float HydroRes;
    public float ElectroRes;
    public float CryoRes;
    public float AnemoRes;
    public float DendroRes;
    public float GeoRes;
    public float ResBonus;

    // ========== 元素增伤 ==========
    public float PhysicalDmgBonus;
    public float PyroDmgBonus;
    public float HydroDmgBonus;
    public float ElectroDmgBonus;
    public float CryoDmgBonus;
    public float AnemoDmgBonus;
    public float DendroDmgBonus;
    public float GeoDmgBonus;

    // ========== 防护/治疗 ==========
    public float ShieldStrength;
    public float HealBonus;
    public float BeHealedBonus;
    public float Elevation;

    // ========== 韧性（公式 1.9 Poise） ==========
    public float Poise;
    public float MaxPoise;

    // ========== 受击权重（公式 1.10 HitWeight） ==========
    public float HitWeight;

    // ========== 护盾 ==========
    public List<Shield> Shields = new List<Shield>();

    // ========== 元素附着 ==========
    public List<ElementalAura> ElementalAuras = new List<ElementalAura>();

    // ========== 状态 ==========
    public Dictionary<string, StatusInstance> StatusDict = new Dictionary<string, StatusInstance>();

    // 场地位置引用：该单位当前占据的格子（可能为null=不在场上）
    public FieldPosition Position;

    // ========== 生死判断 ==========
    public bool IsDead => CurrentHP <= 0;
    public bool IsAlive => CurrentHP > 0;

    // ========== 初始化（子类覆写） ==========
}
