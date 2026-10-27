using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 角色战斗控制器。
/// 职责：
///  - 初始化角色属性（成长曲线 + 突破加成，突破百分比视为"另类 InitiateStatus"）
///  - AP/冷却/充能/技能次数管理
///  - 技能效果执行器（Damage/ApplyStatus/RemoveStatus/ExecuteSkill/ExecuteEffect/GainEnergy/ChangeControl）
///  - 目标选择（TargetType/TargetNumber/TargetConsecutive/TargetOverride）
/// 按键接口由 BattleTester 或其他输入层调用：ExecuteNormalAttack / ExecuteHeavyAttack / ExecuteSkill / ExecuteBurst
/// </summary>
public partial class CharacterBattleController : MonoBehaviour
{
    public BattleEntity Entity;

    /// <summary>
    /// 强制目标位置（2026-08-07 目标选择）：&gt;=0 时技能效果的目标限定为该敌方位置。
    /// 由 BattleTester 目标选择确认时设置；执行完技能后由 ExecuteSkillByID 清空（-1）。
    /// </summary>
    /// <summary>强制目标位置组合（目标选择阶段确认后由 BattleTester 设置；-1 为空=未设置）。技能序列结束后清空</summary>
public List<int> ForcedTargetPositions = new List<int>();

    // 状态行动延迟快照（2026-08-13）：Display==1 的行动在延迟前先解析目标并缓存，
    // 延迟期间计算照常进行（如标记照常移除），延迟结束后执行时直接用快照目标，顺序不受影响
    private readonly Dictionary<string, List<BattleEntity>> _statusTargetSnapshot = new Dictionary<string, List<BattleEntity>>();
    // 上一次效果选中的目标位置（供后续效果 TargetOverride="0,0" 沿用，如爆发伤害→箭雨状态施加到位置）
    private List<int> _lastTargetPositions = new List<int>();
    private StatusInstance _currentEffectSourceStatus; // 状态触发技能/效果时保留直接来源状态
    public int CharacterID { get; private set; }
    public int Level { get; private set; }
    public int Ascension { get; private set; }
    // 已加载的自身等级（培养+自身固定加级）；正式业务只通过GetEffectiveSkillLevel读取。
    // 数组保留供旧测试/调试输入，不能再把命座加级写进这里累加。
    public int[] SkillLevels = new int[4] { 1, 1, 1, 1 };
    public string LastHitEffectID2; // 最近一次造成命中的效果ID2（Hit()钩子判定：Damage效果有目标即算命中，不看伤害数值）
    // 本技能序列内造成过命中的效果ID2集合（2026-08-13）：
    // T2等插入效果在序列内靠后执行时，Hit()仍能判定"序列内是否命中过指定效果"（不被后续空ID效果覆盖）
    private readonly HashSet<string> _sequenceHitEffectIDs = new HashSet<string>();
    public bool IsActive { get; set; }                      // 当前是否出战

    private CharacterAttributesData _attrData;
    private List<CharacterAscensionData> _ascDataList;
    private Dictionary<string, SkillMainData> _skillById2;
    private Dictionary<string, List<SkillEffectData>> _skillEffects;   // SkillID2 => effects
    private Dictionary<string, SkillEffectData> _effectById;          // SkillEffectID2 => effect（ExecuteEffect用）
    private Dictionary<string, int> _cooldownRemaining;                 // SkillID2 => 剩余冷却回合
    private Dictionary<string, int> _currentCharges;                    // SkillID2 => 当前技能次数
    private Dictionary<int, List<SkillLevelData>> _levelDataBySkillType; // SkillType => levels

    // 当前按钮绑定的技能ID（0普攻/1重击/2战技/3爆发），ChangeControl 替换技能时更新
    private string[] _boundSkills = new string[4];
    // 当前实际执行的技能容器。系统调用可执行未绑定到按钮的 Skill，不能用 _boundSkills 反推来源。
    private string _currentExecutingSkillID2;
    private bool _currentExecutingSkillIsActiveAction;
    // ChangeControl 记录：
    // 状态效果表挂载 = 跟状态实例走（状态消失时按 SourceStatus 引用匹配）；
    // 技能效果表挂载 = Duration 计时（填了 Duration 定时解除；没填 = 永久，不记录）
    private List<ControlChangeRecord> _ccRecords = new List<ControlChangeRecord>();

    /// <summary>按钮绑定变化事件（UI刷新按钮名称/图标用）。</summary>
    public event Action OnControlChanged;

    //全局AP（我方共用行动点池，指向 BattleManager 的全局实例；2026-08-14）
    public ActionPointManager APManager => BattleManager.Instance != null ? BattleManager.Instance.APManager : null;

    // ================================================================
    //  初始化
    // ================================================================
    public void Init(int characterID, int level)
        => Init(characterID, level, false);

    /// <summary>
    /// 裸面板构筑。等级等于突破门槛时由isAscended决定是否计入该行；超过门槛则保留已完成的低档突破。
    /// 1级初始行始终计入。这是开战前输入，不实现升级或战斗内重建装备。
    /// </summary>
    public void Init(int characterID, int level, bool isAscended)
        => InitPanel(characterID, level, CharacterAscensionRules.FromLegacyFlag(level, isAscended), isAscended);

    /// <summary>正式养成输入：完整保留0～6突破阶段，不按角色等级猜测已完成的突破。</summary>
    public void Init(int characterID, int level, int ascension)
        => InitPanel(characterID, level, ascension, CharacterAscensionRules.HasCurrentAscension(level, ascension));

    private void InitPanel(int characterID, int level, int ascension, bool isAscended)
    {
        CharacterAscensionRules.Validate(level, ascension);
        CharacterID = characterID;
        Level = level;
        Ascension = ascension;
        _initialStatuses.Clear();

        var dm = DataManager.Instance;

        // 基础属性
        if (!dm.CharacterAttributesDict.TryGetValue(characterID, out _attrData))
        {
            LogManager.LogError(LogCategory.Ally, $"未找到角色属性 CharacterID={characterID}");
            return;
        }
        _ascDataList = new List<CharacterAscensionData>();
        if (dm.CharacterAscensionDict.TryGetValue(characterID, out var ascensions))
            foreach (var row in ascensions)
                if (row != null && CharacterAscensionRules.IncludesRow(row.AscensionLevel, level, Ascension))
                    _ascDataList.Add(row);

        // 技能数据
        _skillById2 = new Dictionary<string, SkillMainData>();
        _skillEffects = new Dictionary<string, List<SkillEffectData>>();
        _effectById = new Dictionary<string, SkillEffectData>();
        _cooldownRemaining = new Dictionary<string, int>();
        _currentCharges = new Dictionary<string, int>();

        if (dm.CharacterSkillDict.TryGetValue(characterID, out var skills))
        {
            foreach (var sk in skills)
            {
                if (string.IsNullOrEmpty(sk.SkillID2)) continue;
                _skillById2[sk.SkillID2] = sk;
                _cooldownRemaining[sk.SkillID2] = 0;
                // InitialCharge 若为0时默认等于 MaxCharge
                int init = sk.InitialCharge;
                if (init <= 0 && sk.MaxCharge > 0) init = sk.MaxCharge;
                _currentCharges[sk.SkillID2] = init;

                // 匹配该技能的效果列表（把 SK_ 替换成 SE_ 前缀）
                string prefix = sk.SkillID2.Replace("SK_", "SE_");
                var list = new List<SkillEffectData>();
                foreach (var kv in dm.SkillEffectDict)
                    if (kv.Key.StartsWith(prefix))
                        list.Add(kv.Value);
                list.Sort((a, b) => a.EffectIndex.CompareTo(b.EffectIndex));
                _skillEffects[sk.SkillID2] = list;

                // 效果ID索引：ExecuteEffect（如箭雨STE_Amber_ArrowRain -> SE_Burst_Amber1）用
                foreach (var eff in list)
                    _effectById[eff.SkillEffectID2] = eff;
            }
        }

        // SkillLevel 数据，按 SkillType 分类
        _levelDataBySkillType = new Dictionary<int, List<SkillLevelData>>();
        foreach (var kv in dm.SkillLevelDict)
        {
            string[] parts = kv.Key.Split('_');
            if (parts.Length < 2) continue;
            if (int.TryParse(parts[0], out int cid) && cid == characterID
                && int.TryParse(parts[1], out int stype))
            {
                if (!_levelDataBySkillType.TryGetValue(stype, out var list))
                {
                    list = new List<SkillLevelData>();
                    _levelDataBySkillType[stype] = list;
                }
                foreach (var lvKv in kv.Value)
                    list.Add(lvKv.Value);
            }
        }
        foreach (var pair in _levelDataBySkillType)
        {
            pair.Value.Sort((a, b) =>
            {
                int paramCompare = string.CompareOrdinal(a.ParamID, b.ParamID);
                return paramCompare != 0 ? paramCompare : a.SkillLevel.CompareTo(b.SkillLevel);
            });
        }

        // Entity
        if (Entity == null) Entity = GetComponent<BattleEntity>();
        if (Entity == null) Entity = gameObject.AddComponent<BattleEntity>();
        Entity.CharacterCtrl = this;
        Entity.Side = BattleSide.Ally;   // 位置系统：角色属于我方阵营（2026-08-06）
        Entity.IsAscended = isAscended;
        BuildEntityPanel();
        // 先于天赋、命座和战斗事件准备本角色的夜魂池；平时为0，不自动进入加持。
        NightSoulStatusHandler.InitializeForCharacter(Entity, characterID);
        RecordInitializationStatus(NightSoulStatusHandler.Find(Entity), 0, 0, InitialStatusKind.Character);

        //AP改为全局共用（BattleManager.APManager），角色不再各自持有（2026-08-14）
    }

    void BuildEntityPanel()
    {
        var dm = DataManager.Instance;

        // 成长曲线：按 Attributes 里的 GrowthCurveID 取对应曲线的当前等级倍率
        float growth = dm.GetGrowthCurve(_attrData.GrowthCurveID, Level);

        Entity.Type = BattleEntity.EntityType.Character;
        Entity.EntityID = CharacterID;
        Entity.WeaponType = _attrData.WeaponType;
        Entity.Level = Level;

        // 基础值 × 成长曲线倍率
        float baseHP = _attrData.BaseHP * growth;
        float baseATK = _attrData.BaseATK * growth;
        float baseDEF = _attrData.BaseDEF * growth;

        // 突破加成（另类 InitiateStatus）：
        //  固定值（BaseHPFlat/ATKFlat/DEFFlat）累加进基础值
        //  百分比（HPBonus/ATKBonus/DEFBonus）累加进 (1+Bonus)
        //  其余（CritRate/CritDMG/EM/充能/元素增伤）直接累加到对应字段
        float hpFlat = 0f, atkFlat = 0f, defFlat = 0f;
        float hpBonus = 0f, atkBonus = 0f, defBonus = 0f;
        float critRateBonus = 0f, critDmgBonus = 0f;
        float emBonus = 0f, rechargeBonus = 0f, healBonus = 0f;
        float pyroDmgBonus = 0f, hydroDmgBonus = 0f, electroDmgBonus = 0f;
        float cryoDmgBonus = 0f, anemoDmgBonus = 0f, dendroDmgBonus = 0f, geoDmgBonus = 0f, physicalDmgBonus = 0f;

        if (_ascDataList != null)
        {
            foreach (var asc in _ascDataList)
            {
                hpFlat += asc.BaseHPFlat;
                atkFlat += asc.BaseATKFlat;
                defFlat += asc.BaseDEFFlat;
                hpBonus += asc.HPBonus;
                atkBonus += asc.ATKBonus;
                defBonus += asc.DEFBonus;
                critRateBonus += asc.CritRate;
                critDmgBonus += asc.CritDMG;
                emBonus += asc.EM;
                rechargeBonus += asc.EnergyRechargeRate;
                healBonus += asc.HealBonus;
                pyroDmgBonus += asc.PyroDmgBonus;
                hydroDmgBonus += asc.HydroDmgBonus;
                electroDmgBonus += asc.ElectroDmgBonus;
                cryoDmgBonus += asc.CryoDmgBonus;
                anemoDmgBonus += asc.AnemoDmgBonus;
                dendroDmgBonus += asc.DendroDmgBonus;
                geoDmgBonus += asc.GeoDmgBonus;
                physicalDmgBonus += asc.PhysicalDmgBonus;
            }
        }

        // HP/DEF保留原面板百分比；攻击白值与突破百分比分开，避免后续装备百分比乘已放大的面板。
        Entity.TotalHP = (baseHP + hpFlat) * (1f + hpBonus);
        Entity.BakedHPBonus = hpBonus;
        Entity.TotalATK = baseATK + atkFlat;
        Entity.CharacterATKBonus = atkBonus;
        Entity.TotalDEF = (baseDEF + defFlat) * (1f + defBonus);
        Entity.CharacterDEFBonus = defBonus;
        Entity.CurrentHP = Entity.TotalHP;
        Entity.MaxEnergy = _attrData.MaxEnergy;
        Entity.CurrentEnergy = 0;
        Entity.HitWeight = _attrData.HitWeight;

        // 元素精通 / 充能效率 / 暴击
        Entity.EM = _attrData.BaseEM + emBonus;
        Entity.TotalEM = Entity.EM;
        Entity.TotalRechargeRate = _attrData.BaseRechargeRate + rechargeBonus;
        Entity.CritRate = _attrData.CritRate + critRateBonus;
        Entity.CritDMG = _attrData.CritDMG + critDmgBonus;
        Entity.HealBonus = healBonus; // 重建低突破面板时也覆盖，不能残留上一份面板。

        // 抗性
        Entity.PhysicalRes = _attrData.PhysicalRes;
        Entity.PyroRes = _attrData.PyroRes;
        Entity.HydroRes = _attrData.HydroRes;
        Entity.ElectroRes = _attrData.ElectroRes;
        Entity.CryoRes = _attrData.CryoRes;
        Entity.AnemoRes = _attrData.AnemoRes;
        Entity.DendroRes = _attrData.DendroRes;
        Entity.GeoRes = _attrData.GeoRes;

        // 增伤（基础 + 突破）
        Entity.PhysicalDmgBonus = _attrData.PhysicalDmgBonus + physicalDmgBonus;
        Entity.PyroDmgBonus = _attrData.PyroDmgBonus + pyroDmgBonus;
        Entity.HydroDmgBonus = _attrData.HydroDmgBonus + hydroDmgBonus;
        Entity.ElectroDmgBonus = _attrData.ElectroDmgBonus + electroDmgBonus;
        Entity.CryoDmgBonus = _attrData.CryoDmgBonus + cryoDmgBonus;
        Entity.AnemoDmgBonus = _attrData.AnemoDmgBonus + anemoDmgBonus;
        Entity.DendroDmgBonus = _attrData.DendroDmgBonus + dendroDmgBonus;
        Entity.GeoDmgBonus = _attrData.GeoDmgBonus + geoDmgBonus;

        // 按钮绑定技能（ChangeControl 可替换）
        _boundSkills[0] = _attrData.NormalAttackID;
        _boundSkills[1] = _attrData.HeavyAttackID;
        _boundSkills[2] = _attrData.ElementalSkillID;
        _boundSkills[3] = _attrData.ElementalBurstID;
        _ccRecords.Clear();
        BattleAttributeInitialization.Prepare(Entity);
    }

    // ================================================================
    //  回合开始：AP 刷新（冷却递减已移至 BattleManager 全局跨回合处理）
    // ================================================================
}

/// <summary>
/// ChangeControl 记录：
/// 状态效果表挂载（IsSkillEffect=false）：状态施加时创建，状态消失时按 SourceStatus 实例解除。
/// 技能效果表挂载（IsSkillEffect=true）：执行时创建，填了 Duration 由 OnTurnStart 递减计时；
/// 没填 Duration（IsPermanent=true）视为永久（替换型不记录，冻结型记录但不解除）。
/// 替换型解除时：按钮返回 OriginalSkillID，冷却/次数写回快照；
/// 若按钮绑定已被其他 Change 覆盖（≠NewSkillID）则不返回。
/// 冻结型解除时：解除按钮冻结。
/// </summary>
public class ControlChangeRecord
{
    public bool IsFreeze;               // true=冻结型（Param2=Freeze）
    public List<int> FrozenSkillTypes;  // 冻结型：被冻结的按钮集合
    public int SkillType;               // 替换型：0普攻/1重击/2战技/3爆发
    public string OriginalSkillID;      // 替换型：执行 ChangeControl 时被换掉的技能
    public int OrigCooldownSnapshot;    // 替换型：被换下技能的冷却快照
    public int OrigChargeSnapshot;      // 替换型：被换下技能的次数快照
    public string NewSkillID;           // 替换型：换上的技能（=效果行 Param2）
    public string SourceStatusID2;      // 状态驱动：来源状态（状态消失时解除）
    public StatusInstance SourceStatus; // 同ID独立实例不能共用解除记录；ID仅供日志
    public bool IsSkillEffect;          // true=技能效果表挂载（OnTurnStart 计时）
    public bool IsPermanent;            // 技能效果版且未填 Duration：永久（不解除）
    public int RemainingTurns;          // 技能效果版：剩余回合（由效果 Duration 决定）
}
