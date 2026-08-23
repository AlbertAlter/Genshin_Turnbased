using System;
using System.Collections.Generic;

// ============================================================
// Characters.xlsx 的 Sheet1 —— 角色总览（战斗外可选角色列表）
// ============================================================
[Serializable]
public class CharacterOverviewData
{
    public int CharacterID;              // 角色唯一ID
    public string Name;                  // 角色显示名
}

// ============================================================
// 单个角色 xlsx 的 Attributes sheet —— 角色基础属性与抗性/增伤
// ============================================================
[Serializable]
public class CharacterAttributesData
{
    public int CharacterID;              // 角色唯一ID
    public string NameID;                // 角色英文标识（用于代码匹配）
    public string Name;                  // 角色显示名
    public int Star;                     // 稀有度（4/5）
    public int GrowthCurveID;            // 成长GrowthCurve的曲线编号
    public int WeaponType;               // 武器类型（用枚举：0单手剑 1双手剑 2弓 3长柄 4法器）
    public string Element;               // 元素属性（Pyro/Hydro/Electro/Cryo/Anemo/Dendro/Geo/None）
    public float BaseHP;                 // Lv1 基础生命值
    public float BaseATK;                // Lv1 基础攻击力
    public float BaseDEF;                // Lv1 基础防御力
    public int MaxEnergy;                // 最大能量值（元素爆发最大方可释放元素爆发）
    public float Poise;                  // 初始韧性（受击打断）
    public float HitWeight;              // 受击权重
    public string NormalAttackID;        // 普攻技能字符串ID
    public string HeavyAttackID;         // 重击技能字符串ID
    public string ElementalSkillID;      // 元素战技字符串ID
    public string ElementalBurstID;      // 元素爆发字符串ID
    public int BaseEM;                   // 基础元素精通
    public float BaseRechargeRate;       // 基础充能效率（1=100%）
    public float CritRate;               // 基础暴击率（0.05=5%）
    public float CritDMG;                // 基础暴击伤害（0.5=50%）
    public float PyroRes;                // 火元素抗性（1=100%抗性，负数=易伤）
    public float HydroRes;
    public float ElectroRes;
    public float CryoRes;
    public float AnemoRes;
    public float DendroRes;
    public float GeoRes;
    public float PhysicalRes;
    public float PyroDmgBonus;           // 火元素增伤（1=100%增伤）
    public float HydroDmgBonus;
    public float ElectroDmgBonus;
    public float CryoDmgBonus;
    public float AnemoDmgBonus;
    public float GeoDmgBonus;
    public float DendroDmgBonus;
    public float PhysicalDmgBonus;
}

// ============================================================
// 单个角色 xlsx 的 Ascension sheet —— 突破属性加成
// ============================================================
[Serializable]
public class CharacterAscensionData
{
    public int AscensionLevel;           // 突破等级要求（20/40/50/60/70/80，第7个=Lv1初始）
    public float BaseHPFlat;             // 固定生命值加成
    public float BaseATKFlat;            // 固定攻击力加成
    public float BaseDEFFlat;            // 固定防御力加成
    public float HPBonus;                // 百分比生命值加成（0.06=6%）
    public float ATKBonus;               // 百分比攻击力加成
    public float DEFBonus;               // 百分比防御力加成
    public float CritRate;               // 暴击率（0.05=5%）
    public float CritDMG;                // 暴击伤害（0.5=50%）
    public float EM;                     // 元素精通
    public float EnergyRechargeRate;     // 充能效率加成
    public float PyroDmgBonus;           // 元素增伤
    public float HydroDmgBonus;
    public float ElectroDmgBonus;
    public float CryoDmgBonus;
    public float AnemoDmgBonus;
    public float GeoDmgBonus;
    public float DendroDmgBonus;
    public float PhysicalDmgBonus;
}

// ============================================================
// 单个角色 xlsx 的 Skills sheet —— 技能基本信息
// ============================================================
[Serializable]
public class SkillMainData
{
    public int SkillID;                  // 技能唯一数字ID
    public string SkillID2;              // 技能字符串ID（SK_Normal_Amber 等）
    public string SkillName;             // 技能显示名
    public int APCost;                   // 释放消耗的行动点
    public float Cooldown;               // 冷却回合数
    public int EnergyUsed;               // 释放消耗的能量值
    public int MaxCharge;                // 可存储技能次数（-1=无限制）
    public int InitialCharge;            // 战斗开始时初始技能次数
    public int SkillPhase;               // 多段技能阶段（0=非多段；同Phase值的技能按SkillID顺序连段）
    public string ActionType;            // Normal/Skill/Burst/Heavy 用于监听
    public string Description;           // 技能描述（长按按钮显示）
}

// ============================================================
// 单个角色 xlsx 的 SkillsEffect sheet —— 技能效果列表
// ============================================================
[Serializable]
public class SkillEffectData
{
    public int SkillEffectID;            // 效果唯一数字ID
    public string SkillEffectID2;        // 效果字符串ID（SE_xxx）
    public int EffectIndex;              // 效果执行顺序（0起）
    public string EffectType;            // Damage / ApplyStatus / BindStatus / ExecuteSkill / ExecuteEffect / GainEnergy / RemoveStatus / ChangeControl 等
    public string Element;               // 元素类型（Pyro/None等，None=物理）
    public string DamageType;            // Normal/Heavy/Skill/Burst（伤害标签）
    public int Duration;                 // ApplyStatus时施加状态的持续回合数
    public int AddInPhase;               // 状态加入的回合制状态机的阶段（1-6）
    public int TriggerPhase;             // 状态触发阶段（1-6）
    public string Param1;                // 效果参数1
    public string Param2;                // GainEnergy：Based=走系数，Flat=固定值
    public string Param3;
    public string TargetType;            // Self / Enemy / EnemyField / Allies / AlliesOnly
    public int TargetNumber;             // 目标数量（-1=全部）
    public int TargetConsecutive;        // 0非连续 1连续 2连续随机 3非连续随机可重复 4非连续随机不可重复
    public bool TargetConsecutiveSet;    // 是否显式填写 TargetConsecutive（2026-08-15：Self 效果"填了"才启动"仅高亮自身"）
    public string TargetOverride;        // X,X偏移 或 状态ID筛选
    public string ScriptHook;            // 条件执行钩子（Hit() 等写法 / Check(角色ID_T1) 天赋判断等）
}

// ============================================================
// 单个角色 xlsx 的 SkillLevel sheet —— 技能等级倍率参数
// ============================================================
[Serializable]
public class SkillLevelData
{
    public int CharacterID;              // 所属角色ID（空=继承上一行）
    public int SkillType;                // 0=普攻 1=重击 2=元素战技 3=元素爆发（空值继承）
    public int SkillLevel;               // 技能等级（1-15）
    public string ParamID;               // 对应SkillsEffect的EffectID2（或Status_Effect对应效果）
    public string Hits1;                 // HitData格式（倍率*属性;削韧;元素量），支持多Hit
    public string Hits2;
    public string Hits3;
    public string Hits4;
    public string Hits5;
    public string Hits6;
    public string Hits7;
}

// ============================================================
// 单个角色 xlsx 的 Constellation sheet —— 命之座
// ============================================================
[Serializable]
public class ConstellationData
{
    public int CharacterID;              // 所属角色ID
    public int ConstellationIndex;       // 命座序号（1-6）
    public string ConstellationName;     // 命座名（2026-08-14 新增列）
    public string Modifiers;             // 修改指令集（InsertEffect/ModifyEffect/ModifySkill/InitiateStatus）
    public string Description;           // 命座文本（用于描述与展示）
}

// ============================================================
// 单个角色 xlsx 的 Talent sheet —— 突破天赋
// ============================================================
[Serializable]
public class TalentData
{
    public int CharacterID;              // 所属角色ID
    public int TalentIndex;              // 天赋序号
    public string TalentName;            // 天赋名（2026-08-14 新增列）
    public int UnlockAfter;              // 解锁所需角色等级
    public string Modifiers;             // 修改指令集（格式同命座）
    public string Description;           // 天赋文本（用于描述与展示）
}

// ============================================================
// 单个角色 xlsx 的 BattleInitiate sheet —— 战斗进场的初始化状态（通用表占位，暂无内容）
// ============================================================
[Serializable]
public class BattleInitiateData
{
    public int CharacterID;
    public int InitiateIndex;
    public string Modifiers;
}

// ============================================================
// Character_Growth_Curve.xlsx —— 成长曲线（用于等级系数）
// ============================================================
[Serializable]
public class GrowthCurveData
{
    public int Level;                    // 角色等级（1-90）
    public float Multiplier;             // 该等级的属性倍率
}

// ============================================================
// StatusData.xlsx 的 StatusData_Main sheet —— 状态定义
// ============================================================
[Serializable]
public class StatusMainData
{
    public int StatusID;                 // 状态唯一数字ID
    public string StatusID2;             // 状态字符串ID（ST_xxx）
    public string StatusName;            // 状态名称
    public string StatusType;            // Buff / Debuff / Shield 等
    public int Display;                  // 局内显示（0不显示 1显示图标 2显示数值）
    public string Description;           // 状态描述
    public string MultiplierPart1;       // Buff/Debuff加成公式（属性分类之一）
    public string MultiplierPart2;
    public string MultiplierPart3;
    public string ApplyDamageType;       // 限定生效的伤害类型（空=全部）
    public string ApplyReactionType;     // 限定生效的反应类型（空=全部）
    public string ApplyElementType;      // 限定生效的元素类型（空=全部）
    public string ApplyString;           // 限定生效的效果ID(=SE_xxx)或技能ID列表（分号分隔，如 SK_Normal_Kaeya;SK_Heavy_Kaeya）（空=全部）
    public string ScriptHook;            // 状态级钩子（如 Kaeya_C1：目标检查），返回假则该状态加成不生效
    public int MaxCount;                 // 同一级加总最多计入几次
    public int MaxStack;                 // 单一目标上最多叠加层数
    public string WhenMax;               // 超出时行为

    /// <summary>
    /// 获取该状态填写的 MultiplierPart 类别（Part1/Part2/Part3 三列只填其一，
    /// 术语表原文：“因为放不下所以多分了几列，只能填其中一个”）。
    /// 返回三列中第一个非空值；全空返回空字符串。
    /// </summary>
    public string GetMultiplierPart()
    {
        if (!string.IsNullOrEmpty(MultiplierPart1)) return MultiplierPart1;
        if (!string.IsNullOrEmpty(MultiplierPart2)) return MultiplierPart2;
        return MultiplierPart3 ?? string.Empty;
    }
}

// ============================================================
// StatusData.xlsx 的 Status_Effect sheet —— 状态效果列表
// ============================================================
[Serializable]
public class StatusEffectData
{
    public int StatusEffectID;           // 状态效果唯一数字ID
    public string StatusEffectID2;       // 状态效果字符串ID（STE_xxx）
    public int EffectIndex;              // 该状态效果执行顺序
    public string EffectType;            // Damage / ApplyStatus / BindStatus / Buff / Debuff / ExecuteEffect / GainEnergy / RemoveStatus
    public string Element;               // 元素类型（Pyro/None等，None=物理）
    public string DamageType;            // Normal/Heavy/Skill/Burst（伤害标签）
    public int Duration;                 // 施加状态的持续回合数
    public int AddInPhase;               // 状态加入的回合制状态机的阶段
    public int TriggerPhase;             // 状态触发阶段
    public string Param1;                // 参数1（Damage=HitData，支持%引用 P E token / ApplyStatus=目标状态ID2 / BindStatus=绑定的子状态ID2）
    public string Param2;                // 参数2（GainEnergy：Based=走系数，Flat=固定值）
    public string Param3;                // 参数3（Damage=元素量）
    public string TargetType;            // Self / Allies / AlliesOnly / Enemy / EnemyField
    public string TargetSelect;          // X,X格式（原点为状态所在实体）
    public int TargetConsecutive;        // 同技能定义
    public string TargetOverride;        // X,X偏移 或 状态ID
    public string ScriptHook;            // 脚本钩子（Hit() / Check(角色ID_T1) 等）
}

// ============================================================
// StatusData.xlsx 的 Status_Action sheet —— 状态行动（在回合制状态机的某个阶段执行什么）
// ============================================================
[Serializable]
public class StatusActionData
{
    public int StatusID;                 // 所属状态数字ID
    public string StatusID2;             // 所属状态字符串ID（ST_xxx）
    public string ActionType;            // OnApply / OnItsTurn / OnTrigger / OnEnd / OnHit 等（触发时机）
    public string Param1;                // 执行的状态效果ID2列表（逗号分隔，如 STE_Bunny2,STE_Bunny4）
    public string Param2;                // 附加参数
    public string Param3;                // 附加参数
    public int MaxTimePerTurn;           // 每回合最大触发次数（0=不限）
    public int MaxTimePerLife;           // 全场最大触发次数（0=不限）
    public int Cooldown;                 // 行动冷却（回合，0=无）
    public string HitSource;             // ActionType=OnHit时限定伤害来源效果ID2
    public string ScriptHook;            // 条件钩子（如 Check(1009_C2)），返回假则不执行
}

// ============================================================
// StatusData.xlsx 的 Status_Attributes sheet —— 状态属性快照定义
//   只有列在此表的状态在生成时需要快照施法者属性（空值表示仅记录用途）
// ============================================================
[Serializable]
public class StatusAttributesData
{
    public int StatusID;                 // 所属状态数字ID
    public string StatusID2;             // 所属状态字符串ID（ST_xxx）
    public int TotalHP;                  // 生成时读取施法者面板并照抄记录
    public int TotalATK;
    public int TotalDEF;
    public int TotalEM;
    public int TotalRechargeRate;
    public int CritRate;
    public int CritDMG;
    public int BaseDMGBonusFlat;         // 意为施法者身上所有状态提供的特效加成，生成时也需记录到实体，因为该状态也拥有此特效
    public int DMGBonus;
    public int DEFReduction;
    public int DEFIgnored;
    public int ResBonus;
    public int ShieldStrength;
    public int HealBonus;
    public int BeHealedBonus;
    public int Elevation;
    public int BaseDMGBonus;
    public int LunarDMGBonus;
    public int StellarDMGBonus;
}

// ============================================================
// EnemyAttributes.xlsx 的 Enemy_Main sheet —— 敌人基础信息
// ============================================================
[Serializable]
public class EnemyMainData
{
    public int EnemyID;                  // 敌人唯一数字ID
    public string EnemyNameID;           // 敌人英文标识
    public string EnemyName;             // 敌人显示名
    public int Threat;                   // 威胁度
    public int ActsGiven;                // 每回合提供的可用行动次数
    public float Poise;                  // 韧性
    public int ATK_Curve;                // 攻击力成长曲线编号
    public float HP_coeff;               // 生命值系数（乘以等级基础值=实际HP）
    public float ATK_coeff;              // 攻击力系数（乘以等级基础值=实际ATK）
    public float PhysicalRes;            // 物理抗性（0.1=10%）
    public float PyroRes;
    public float HydroRes;
    public float ElectroRes;
    public float CryoRes;
    public float AnemoRes;
    public float DendroRes;
    public float GeoRes;
    public float PhysicalDmgBonus;       // 物理增伤（1=100%）
    public float PyroDmgBonus;
    public float HydroDmgBonus;
    public float ElectroDmgBonus;
    public float CryoDmgBonus;
    public float AnemoDmgBonus;
    public float DendroDmgBonus;
    public float GeoDmgBonus;
}

// ============================================================
// EnemySkill.xlsx 的 EnemySkill_Main sheet —— 敌人技能基本信息
// ============================================================
[Serializable]
public class EnemySkillMainData
{
    public int EnemySkillID;             // 敌人技能唯一数字ID
    public string EnemySkillID2;         // 敌人技能字符串ID（SK_Hilichurl_Dance 等）
    public string EnemySkillName;        // 技能显示名
    public float Cooldown;               // 冷却回合数
    public int UsePerTurn;               // 每回合最多使用次数
    public int HighThreat;               // 是否为高威胁技能（AI优先判断用）
    public string Description;           // 技能描述
}

// ============================================================
// EnemySkill.xlsx 的 EnemySkill_Effect sheet —— 敌人技能效果列表
// ============================================================
[Serializable]
public class EnemySkillEffectData
{
    public int SkillEffectID;            // 效果数字ID
    public string SkillEffectID2;        // 效果字符串ID（SE_Hilichurl_Dance 等）
    public int EffectIndex;              // 执行顺序
    public string EffectType;            // Damage / ApplyStatus / ApplyStatusToSelf 等
    public string Element;               // 元素类型
    public string DamageType;            // Normal/Skill/Burst
    public int Duration;                 // 施加状态的持续回合
    public int AddInPhase;               // 添加阶段
    public int TriggerPhase;             // 触发阶段
    public string Param1;                // 参数1
    public string Param2;                // GainEnergy：Based=走系数，Flat=固定值
    public string Param3;                // 参数3
    public string TargetType;            // 目标类型
    public int TargetNumber;             // 目标数量
    public int TargetConsecutive;        // 连续选取模式
    public string TargetOverride;        // 目标覆盖
    public string ScriptHook;            // 脚本钩子
}

// ============================================================
// EnemySkill.xlsx 的 EnemySkill_Param sheet —— 敌人技能伤害参数
// ============================================================
[Serializable]
public class EnemySkillParamData
{
    public int SkillEffectID;            // 对应的效果数字ID
    public string SkillEffectID2;        // 对应的效果字符串ID
    public string EffectType;            // Damage 等
    public string Hits1;                 // HitData格式（倍率;削韧;元素量），敌人伤害统一用敌人TotalATK，倍率后不写属性
    public string Hits2;
    public string Hits3;
    public string Hits4;
    public string Hits5;
    public string Hits6;
    public string Hits7;
    public string Hits8;
}

// ============================================================
// EnemySkill.xlsx 的 EnemyAI sheet —— AI行为模式定义
// ============================================================
[Serializable]
public class EnemyAIData
{
    public int EnemyID;                  // 敌人数字ID
    public string EnemyNameID;           // 敌人英文名
    public string EnemyName;             // 敌人显示名
    public int PatternID;                // 行为模式ID
    public string PatternType;           // Weighted=权重模式
    public int NextPatternID;            // 完成当前模式后切换的模式ID
    public string ConditionType;         // 切换条件类型
    public string ConditionParam;        // 切换条件参数
}

// ============================================================
// EnemySkill.xlsx 的 EnemyAI_Rule sheet —— AI权重规则表
//   其中 PatternID/PatternType 继承上一行
// ============================================================
[Serializable]
public class EnemyAIRuleData
{
    public int EnemyID;                  // 敌人数字ID
    public int PatternID;                // 行为模式ID（空=继承上一行）
    public string PatternType;           // Weighted等
    public int SkillID;                  // 该规则对应的技能数字ID
    public int SkillIndex;               // 技能序号
    public int Weight;                   // 权重值（权重模式使用）
}

// ============================================================
// ReactionLevelCoefficient.xlsx —— 剧变反应等级系数
// ============================================================
[Serializable]
public class ReactionLevelCoefficientData
{
    public int Level;                    // 对应反应的角色等级
    public float Coefficient;            // 反应系数值
}

// ============================================================
// StageConfig.xlsx —— 关卡配置
// ============================================================
[Serializable]
public class StageConfigData
{
    public int StageID;
    public string StageName;
    public string StageDescription;
    public string EnemyIDs;
    public string EnemyCounts;
    public string EnemyLevels;
    public string AllyIDs;
    public string AllyCounts;
    public string AllyLevels;
    public string WinCondition;
    public string LoseCondition;
}
