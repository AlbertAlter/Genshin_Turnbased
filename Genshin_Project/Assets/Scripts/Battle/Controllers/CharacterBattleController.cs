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
public class CharacterBattleController : MonoBehaviour
{
    public BattleEntity Entity;

    /// <summary>
    /// 强制目标位置（2026-08-07 目标选择）：&gt;=0 时技能效果的目标限定为该敌方位置。
    /// 由 BattleTester 目标选择确认时设置；执行完技能后由 ExecuteSkillByID 清空（-1）。
    /// </summary>
    /// <summary>强制目标位置组合（目标选择阶段确认后由 BattleTester 设置；-1 为空=未设置）。技能序列结束后清空</summary>
public List<int> ForcedTargetPositions = new List<int>();

    /// <summary>
    /// 上一个效果选中的目标列表（供 TargetOverride="0,0" 沿用）。技能序列执行前清空。
    /// </summary>
    List<BattleEntity> _lastSkillTargets = new List<BattleEntity>();
    // 状态行动延迟快照（2026-08-13）：Display==1 的行动在延迟前先解析目标并缓存，
    // 延迟期间计算照常进行（如标记照常移除），延迟结束后执行时直接用快照目标，顺序不受影响
    private readonly Dictionary<string, List<BattleEntity>> _statusTargetSnapshot = new Dictionary<string, List<BattleEntity>>();
    // 上一次效果选中的目标位置（供后续效果 TargetOverride="0,0" 沿用，如爆发伤害→箭雨状态施加到位置）
    private List<int> _lastTargetPositions = new List<int>();
    // ================= 效果级目标选择（2026-08-15，按《战斗界面》文档） =================
    // 按钮主动释放技能时：每个需选目标的效果在执行完上个效果后单独停一次选择；确认后继续推进。
    private List<SkillEffectData> _effectQueue;       // 当前技能剩余待执行效果
    private int _effectQueueIndex;                    // 下一个待执行效果下标
    private int _effectSkillType;                     // 当前技能类型（0普攻/1重击/2战技/3爆发）
    private bool _effectSequenceActive;               // 效果序列进行中（已确认过首个效果、资源已扣）
    private SkillEffectData _pendingSelectEffect;     // 当前等待玩家选择的效果（null=无）
    public int CharacterID { get; private set; }
    public int Level { get; private set; }
    // 技能等级（1-15，顺序：0=普攻 1=重击 2=战技 3=爆发）
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
    // ChangeControl 记录：
    // 状态效果表挂载 = 跟状态走（状态消失时解除，SourceStatusID2 匹配）；
    // 技能效果表挂载 = Duration 计时（填了 Duration 定时解除；没填 = 永久，不记录）
    private List<ControlChangeRecord> _ccRecords = new List<ControlChangeRecord>();

    /// <summary>按钮绑定变化事件（UI刷新按钮名称/图标用）。</summary>
    public event Action OnControlChanged;

    //全局AP（我方共用行动点池，指向 BattleManager 的全局实例；2026-08-14）
    public ActionPointManager APManager => BattleManager.Instance != null ? BattleManager.Instance.APManager : null;

    /// <summary>查询某按钮（0普攻/1重击/2战技/3爆发）是否被 ChangeControl Freeze 冻结。</summary>
    public bool IsButtonFrozen(int skillType)
    {
        foreach (var rec in _ccRecords)
            if (rec.IsFreeze && rec.FrozenSkillTypes.Contains(skillType))
                return true;
        return false;
    }

    /// <summary>是否有任意按钮被冻结（切换角色判断用）。</summary>
    public bool IsAnyButtonFrozen()
    {
        foreach (var rec in _ccRecords)
            if (rec.IsFreeze && rec.FrozenSkillTypes.Count > 0)
                return true;
        return false;
    }

    /// <summary>本技能序列内是否命中过指定效果（Hit()钩子：序列内命中集合判定）。</summary>
    public bool HasHitEffectThisSequence(string effectID2)
    {
        return !string.IsNullOrEmpty(effectID2) && _sequenceHitEffectIDs.Contains(effectID2);
    }

    /// <summary>查询当前按钮绑定的技能ID2（0普攻/1重击/2战技/3爆发），UI显示用。</summary>
    public string GetBoundSkillID(int skillType)
    {
        if (skillType < 0 || skillType > 3) return null;
        return _boundSkills[skillType];
    }

    /// <summary>查询当前按钮绑定技能的名字（UI按钮显示用）。</summary>
    public string GetBoundSkillName(int skillType)
    {
        if (skillType < 0 || skillType > 3) return "";
        if (_skillById2.TryGetValue(_boundSkills[skillType], out var sk)) return sk.SkillName;
        return "";
    }

    /// <summary>查询当前按钮绑定技能的 AP 消耗（目标选择阶段提示用）。</summary>
    public int GetBoundSkillAPCost(int skillType)
    {
        if (skillType < 0 || skillType > 3) return 0;
        if (_skillById2.TryGetValue(_boundSkills[skillType], out var sk)) return sk.APCost;
        return 0;
    }

    /// <summary>查询当前按钮绑定技能的最大使用次数（0=无次数概念）。</summary>
    public int GetBoundSkillMaxCharge(int skillType)
    {
        if (skillType < 0 || skillType > 3) return 0;
        if (_skillById2.TryGetValue(_boundSkills[skillType], out var sk)) return sk.MaxCharge;
        return 0;
    }

    /// <summary>查询当前按钮绑定技能的剩余使用次数。</summary>
    public int GetBoundSkillCurrentCharge(int skillType)
    {
        if (skillType < 0 || skillType > 3) return 0;
        if (_currentCharges.TryGetValue(_boundSkills[skillType], out int c)) return c;
        return 0;
    }

    /// <summary>查询当前按钮绑定技能的剩余冷却回合（UI 冷却数字显示；0=不在冷却）。</summary>
    public int GetCooldownRemaining(int skillType)
    {
        if (skillType < 0 || skillType > 3) return 0;
        if (_cooldownRemaining.TryGetValue(_boundSkills[skillType], out int cd)) return cd;
        return 0;
    }

    /// <summary>
    /// 目标选择阶段多段技能推进（战斗界面文档）：
    /// 若当前绑定技能 SkillPhase != 0，按 SkillID 顺序将按钮临时绑定至下一个同 Phase 技能。
    /// 返回推进后的技能 ID2；已是最后一段（或非多段）返回 null。
    /// </summary>
    public string AdvanceSkillPhase(int skillType)
    {
        if (skillType < 0 || skillType > 3) return null;
        string cur = _boundSkills[skillType];
        if (!_skillById2.TryGetValue(cur, out var sk) || sk.SkillPhase <= 0) return null;

        SkillMainData next = null;
        foreach (var s in _skillById2.Values)
        {
            if (s.SkillPhase == sk.SkillPhase && s.SkillID > sk.SkillID)
            {
                if (next == null || s.SkillID < next.SkillID) next = s;
            }
        }
        if (next == null) return null;

        _boundSkills[skillType] = next.SkillID2;
        OnControlChanged?.Invoke();
        return next.SkillID2;
    }

    /// <summary>目标选择阶段结束/切换技能：恢复按钮原绑定（多段推进前的技能）。</summary>
    public void RevertSkillPhase(int skillType, string originalSkillID2)
    {
        if (skillType < 0 || skillType > 3 || string.IsNullOrEmpty(originalSkillID2)) return;
        if (_boundSkills[skillType] == originalSkillID2) return;
        _boundSkills[skillType] = originalSkillID2;
        OnControlChanged?.Invoke();
    }

    public string NormalAttackID => _attrData?.NormalAttackID;
    public string HeavyAttackID => _attrData?.HeavyAttackID;
    public string ElementalSkillID => _attrData?.ElementalSkillID;
    public string ElementalBurstID => _attrData?.ElementalBurstID;

    // ================================================================
    //  Init
    // ================================================================
    public void Init(int characterID, int level)
    {
        CharacterID = characterID;
        Level = level;

        var dm = DataManager.Instance;

        // 基础属性
        if (!dm.CharacterAttributesDict.TryGetValue(characterID, out _attrData))
        {
            LogManager.LogError(LogCategory.Ally, $"未找到角色属性 CharacterID={characterID}");
            return;
        }
        dm.CharacterAscensionDict.TryGetValue(characterID, out _ascDataList);

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
                var list = new List<SkillLevelData>();
                foreach (var lvKv in kv.Value)
                    list.Add(lvKv.Value);
                list.Sort((a, b) => a.SkillLevel.CompareTo(b.SkillLevel));
                _levelDataBySkillType[stype] = list;
            }
        }

        // Entity
        if (Entity == null) Entity = GetComponent<BattleEntity>();
        if (Entity == null) Entity = gameObject.AddComponent<BattleEntity>();
        Entity.CharacterCtrl = this;
        Entity.Side = BattleSide.Ally;   // 位置系统：角色属于我方阵营（2026-08-06）
        BuildEntityPanel();

        //AP改为全局共用（BattleManager.APManager），角色不再各自持有（2026-08-14）
    }

    void BuildEntityPanel()
    {
        var dm = DataManager.Instance;

        // 成长曲线：按 Attributes 里的 GrowthCurveID 取对应曲线的当前等级倍率
        float growth = dm.GetGrowthCurve(_attrData.GrowthCurveID, Level);

        Entity.Type = BattleEntity.EntityType.Character;
        Entity.EntityID = CharacterID;
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
        float emBonus = 0f, rechargeBonus = 0f;
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

        // 最终属性： (基础值×成长 + 突破固定值) × (1 + 突破百分比)
        Entity.TotalHP = (baseHP + hpFlat) * (1f + hpBonus);
        Entity.TotalATK = (baseATK + atkFlat) * (1f + atkBonus);
        Entity.TotalDEF = (baseDEF + defFlat) * (1f + defBonus);
        Entity.CurrentHP = Entity.TotalHP;
        Entity.MaxEnergy = _attrData.MaxEnergy;
        Entity.CurrentEnergy = 0;
        Entity.MaxPoise = _attrData.Poise;
        Entity.Poise = _attrData.Poise;
        Entity.HitWeight = _attrData.HitWeight;

        // 元素精通 / 充能效率 / 暴击
        Entity.EM = _attrData.BaseEM + emBonus;
        Entity.TotalEM = Entity.EM;
        Entity.TotalRechargeRate = _attrData.BaseRechargeRate + rechargeBonus;
        Entity.CritRate = _attrData.CritRate + critRateBonus;
        Entity.CritDMG = _attrData.CritDMG + critDmgBonus;

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
    }

    // ================================================================
    //  回合开始：AP 刷新（冷却递减已移至 BattleManager 全局跨回合处理）
    // ================================================================
    public void OnTurnStart()
    {
        //AP重置移到 BattleManager 全局（我方每回合共用100，2026-08-14）

        // 技能效果表挂载的 ChangeControl（填了Duration）：每回合递减，到0时结束（返回原技能/解冻）
        // 状态效果表挂载的由状态计时（状态消失时解除），永久型（IsPermanent）不递减
        for (int i = _ccRecords.Count - 1; i >= 0; i--)
        {
            var rec = _ccRecords[i];
            if (!rec.IsSkillEffect || rec.IsPermanent) continue;
            rec.RemainingTurns--;
            if (rec.RemainingTurns <= 0)
            {
                if (RevertChangeControlRecord(rec))
                    _ccRecords.RemoveAt(i);
            }
        }
    }

    // ================================================================
    //  技能冷却全局递减（由 BattleManager 在全局跨回合时统一调用）
    //  冷却完成时恢复一个技能次数（次数用完 = 在等冷却，二者是同一件事）
    // ================================================================
    public void TickSkillCooldowns()
    {
        // 只有界面上按钮绑定的技能会实时冷却（术语：其他技能被换下去后冷却计时被冻结，直至重新出现）
        var bound = new HashSet<string>();
        for (int i = 0; i < _boundSkills.Length; i++)
            if (!string.IsNullOrEmpty(_boundSkills[i]))
                bound.Add(_boundSkills[i]);

        foreach (var k in bound)
        {
            if (!_cooldownRemaining.TryGetValue(k, out int cd) || cd <= 0) continue;
            _cooldownRemaining[k]--;
            if (_cooldownRemaining[k] == 0)
            {
                // 冷却完成：若有次数限制且未满，恢复1次
                if (_currentCharges.TryGetValue(k, out int cur))
                {
                    var sk = _skillById2.TryGetValue(k, out var s) ? s : null;
                    if (sk != null && sk.MaxCharge > 0 && cur < sk.MaxCharge)
                        _currentCharges[k] = cur + 1;
                }
            }
        }
    }

    // ================================================================
    //  可用性判断
    // ================================================================
    public bool CanUseNormalAttack()
    {
        if (!IsActive) return false;
        if (IsButtonFrozen(0)) return false;
        string nid0 = _boundSkills[0];
        if (string.IsNullOrEmpty(nid0)) return false;
        var sk = _skillById2[nid0];
        if (!APManager.CanAfford(sk.APCost)) return false;
        if (_cooldownRemaining[nid0] > 0) return false;
        return true;
    }

    public bool CanUseHeavyAttack()
    {
        if (!IsActive) return false;
        if (IsButtonFrozen(1)) return false;
        string hid1 = _boundSkills[1];
        if (string.IsNullOrEmpty(hid1)) return false;
        var sk = _skillById2[hid1];
        if (!APManager.CanAfford(sk.APCost)) return false;
        if (_cooldownRemaining[hid1] > 0) return false;
        return true;
    }

    public bool CanUseSkill()
    {
        if (!IsActive) return false;
        if (IsButtonFrozen(2)) return false;
        string sid2 = _boundSkills[2];
        if (string.IsNullOrEmpty(sid2)) return false;
        var sk = _skillById2[sid2];
        if (!APManager.CanAfford(sk.APCost)) return false;
        if (_cooldownRemaining[sid2] > 0) return false;
        if (sk.MaxCharge > 0 && _currentCharges[sid2] <= 0) return false;
        return true;
    }

    public bool CanUseBurst()
    {
        if (IsButtonFrozen(3)) return false;
        string bid3 = _boundSkills[3];
        if (string.IsNullOrEmpty(bid3)) return false;
        var sk = _skillById2[bid3];
        if (Entity.CurrentEnergy < Entity.MaxEnergy) return false;
        if (_cooldownRemaining[bid3] > 0) return false;
        return true;
    }

    public bool CanSwitch()
    {
        return IsActive && !IsAnyButtonFrozen();
    }

    // ================================================================
    //  不可用原因查询（UI/日志用）
    //  返回 null = 可用；否则返回具体原因字符串
    //  skillType: 0普攻 1重击 2战技 3爆发
    // ================================================================
    public string GetBlockReason(int skillType)
    {
        // 通用前置（2026-08-15：爆发例外——文档“该角色无需出战”，未出战角色仍可施放爆发；死亡由调用方另行拦截）
        if (!IsActive && skillType != 3)
            return "角色未出战";

        if (IsButtonFrozen(skillType))
            return "按键被冻结";

        string skillID2;
        switch (skillType)
        {
            case 0:
                if (string.IsNullOrEmpty(_boundSkills[0])) return "未配置普攻技能";
                skillID2 = _boundSkills[0];
                break;
            case 1:
                if (string.IsNullOrEmpty(_boundSkills[1])) return "未配置重击技能";
                skillID2 = _boundSkills[1];
                break;
            case 2:
                if (string.IsNullOrEmpty(_boundSkills[2])) return "未配置战技技能";
                skillID2 = _boundSkills[2];
                break;
            case 3:
                if (string.IsNullOrEmpty(_boundSkills[3])) return "未配置爆发技能";
                skillID2 = _boundSkills[3];
                break;
            default:
                return "未知技能类型";
        }

        if (!_skillById2.TryGetValue(skillID2, out var sk))
            return $"技能数据缺失({skillID2})";

        // AP 不足（爆发不消耗AP，改消耗能量）
        if (skillType != 3)
        {
            if (!APManager.CanAfford(sk.APCost))
                return $"AP不足（需要{sk.APCost}，当前{APManager.CurrentAP}）";
        }
        else
        {
            if (Entity.CurrentEnergy < Entity.MaxEnergy)
                return $"能量不足（{Entity.CurrentEnergy}/{Entity.MaxEnergy}）";
        }

        // 冷却中（含技能次数用完：次数用完 = 正在等冷却恢复）
        if (_cooldownRemaining.TryGetValue(skillID2, out int cd) && cd > 0)
            return $"冷却中（剩余{cd}回合）";

        // 战技次数：冷却已归零但次数仍为0（不应出现，防御性兜底）
        if (skillType == 2 && sk.MaxCharge > 0)
        {
            int cur = _currentCharges.TryGetValue(skillID2, out int c) ? c : 0;
            if (cur <= 0)
                return $"技能次数未恢复（{cur}/{sk.MaxCharge}）";
        }

        return null; // 可用
    }

    // ================================================================
    //  执行（按键接口）
    // ================================================================
    public void ExecuteNormalAttack() { ExecuteSkillByID(_boundSkills[0], 0); }
    public void ExecuteHeavyAttack() { ExecuteSkillByID(_boundSkills[1], 1); }
    public void ExecuteSkill() { ExecuteSkillByID(_boundSkills[2], 2); }
    public void ExecuteBurst() { ExecuteSkillByID(_boundSkills[3], 3); }

    /// <summary>
    /// 查询某个技能类型（0普攻/1重击/2战技/3爆发）第一个"需要玩家选目标的效果"的目标参数。
    /// 返回 (targetCount, consecutive)；找不到则 (0,0)（表示不需要玩家选目标/自动执行）。
    /// </summary>
    public (int count, int consecutive) GetSkillTargetParams(int skillType)
    {
        string skillID2 = skillType switch
        {
            0 => _boundSkills[0],
            1 => _boundSkills[1],
            2 => _boundSkills[2],
            3 => _boundSkills[3],
            _ => null
        };
        if (string.IsNullOrEmpty(skillID2)) return (0, 0);

        if (_skillEffects.TryGetValue(skillID2, out var effects))
        {
            foreach (var eff in effects)
            {
                // 有 TargetNumber（>0）的效果才是"要选数量目标"的；有 TargetOverride 的效果由上一条目标决定，不算
                if (eff.TargetNumber > 0)
                    return (eff.TargetNumber, eff.TargetConsecutive);
            }
        }
        return (0, 0);
    }

    /// <summary>
    /// 当前按钮技能的目标方向（2026-08-14，目标选择高亮用）：
    /// 找第一个需选目标（TargetNumber>0）的效果的方向——Enemy/EnemyField→"Enemy"，Allies/AlliesOnly/AllyField→"Ally"，其余""。
    /// </summary>
    public string GetSkillTargetDirection(int skillType)
    {
        string skillID2 = skillType switch
        {
            0 => _boundSkills[0],
            1 => _boundSkills[1],
            2 => _boundSkills[2],
            3 => _boundSkills[3],
            _ => null
        };
        if (string.IsNullOrEmpty(skillID2)) return "";
        if (_skillEffects.TryGetValue(skillID2, out var effects))
        {
            foreach (var eff in effects)
            {
                if (eff.TargetNumber > 0 && !string.IsNullOrEmpty(eff.TargetType))
                {
                    if (eff.TargetType == "Enemy" || eff.TargetType == "EnemyField") return "Enemy";
                    if (eff.TargetType == "Allies" || eff.TargetType == "AlliesOnly" || eff.TargetType == "AllyField") return "Ally";
                }
            }
        }
        return "";
    }

    /// <summary>
    /// 技能是否含 TargetType=Self 的效果（2026-08-15）：
    /// Self/无目标技能（如凯亚爆发，效果只有给自身挂状态）施放时也进入目标选择流程，
    /// 只高亮施放者自身槽位，空格确认后施放——避免"按技能后空格直接结束回合"的误触。
    /// </summary>
    public bool HasSelfTargetEffect(int skillType)
    {
        string skillID2 = skillType switch
        {
            0 => _boundSkills[0],
            1 => _boundSkills[1],
            2 => _boundSkills[2],
            3 => _boundSkills[3],
            _ => null
        };
        if (string.IsNullOrEmpty(skillID2)) return false;
        if (_skillEffects.TryGetValue(skillID2, out var effects))
        {
            foreach (var eff in effects)
                if (eff != null && eff.TargetType == "Self")
                    return true;
        }
        return false;
    }

    /// <summary>
    /// 效果是否需启动目标选择流程（2026-08-15，按《战斗界面》文档）：
    ///  - Self：显式填了 TargetConsecutive(0/1) → 仅高亮自身（文档"目标类型为Self，且填了Consecutive"；
    ///    没填（如安柏重击效果1/3）→ 不启动，直接执行）
    ///  - 非 Self：TargetNumber>0 → 选数量目标；Consecutive=2/3/4（随机）→ 全体特效走流程
    /// </summary>
    public bool EffectNeedsSelection(SkillEffectData eff)
    {
        if (eff == null) return false;
        if (eff.TargetType == "Self")
            return eff.TargetConsecutiveSet && eff.TargetConsecutive <= 1;
        if (eff.TargetNumber > 0) return true;
        return eff.TargetConsecutive >= 2 && eff.TargetConsecutive <= 4;
    }

    /// <summary>当前按钮技能的第一个需选效果（进入目标选择用），无则 null。</summary>
    public SkillEffectData GetFirstSelectEffect(int skillType)
    {
        string skillID2 = skillType switch
        {
            0 => _boundSkills[0],
            1 => _boundSkills[1],
            2 => _boundSkills[2],
            3 => _boundSkills[3],
            _ => null
        };
        if (string.IsNullOrEmpty(skillID2)) return null;
        if (_skillEffects.TryGetValue(skillID2, out var effects))
        {
            foreach (var eff in effects)
                if (EffectNeedsSelection(eff)) return eff;
        }
        return null;
    }

    /// <summary>效果级序列是否进行中（已确认过首个效果、资源已扣）。</summary>
    public bool HasPendingEffectSequence => _effectSequenceActive;

    /// <summary>当前等待玩家选择的效果（UI 高亮/Splash 读参数用），无=null。</summary>
    public SkillEffectData PendingSelectEffect => _pendingSelectEffect;

    /// <summary>挂载当前待选效果（2026-08-15：进入选择阶段时由 BattleInputController 调用，UI 高亮/溅射预览用）。</summary>
    public void MarkPendingSelectEffect(SkillEffectData eff)
    {
        _pendingSelectEffect = eff;
    }

    /// <summary>
    /// 按钮路径：开始效果级执行（首次确认后调用）——扣资源+预解析钩子，然后推进到下一个待选效果。
    /// 返回是否全部完成（true=无待选效果，false=停在需选效果等待确认）。
    /// </summary>
    public bool BeginSkillSelectionSequence(int skillType)
    {
        string skillID2 = GetBoundSkillID(skillType);
        if (string.IsNullOrEmpty(skillID2) || !_skillById2.TryGetValue(skillID2, out var sk)) return true;
        if (!PrepareSkillExecution(skillID2, skillType, sk, out var effects, out _)) return true; // AP 不足等：视为完成，避免卡在选择态

        _effectQueue = effects != null ? new List<SkillEffectData>(effects) : new List<SkillEffectData>();
        _effectQueueIndex = 0;
        _effectSkillType = skillType;
        _effectSequenceActive = true;
        _pendingSelectEffect = null;
        // 首个需选效果（目标已确认，在 ForcedTargetPositions）
        for (int i = 0; i < _effectQueue.Count; i++)
        {
            if (EffectNeedsSelection(_effectQueue[i])) { _pendingSelectEffect = _effectQueue[i]; break; }
        }
        LogManager.Log(LogCategory.Skill, $"{sk.SkillName} 效果序列开始（效果级选择）");
        return AdvanceEffectSequence();
    }

    /// <summary>
    /// 推进效果序列：执行非需选效果与已确认的需选效果，停在下一个未确认的需选效果。
    /// 返回是否全部完成（true=序列结束，false=停在需选效果等待确认）。
    /// </summary>
    public bool AdvanceEffectSequence()
    {
        if (!_effectSequenceActive) return true;
        while (_effectQueueIndex < _effectQueue.Count)
        {
            var eff = _effectQueue[_effectQueueIndex];
            if (EffectNeedsSelection(eff))
            {
                if (_pendingSelectEffect == eff)
                {
                    // 已确认（目标在 ForcedTargetPositions）→ 执行
                    ExecuteEffect(eff, _effectSkillType);
                    ForcedTargetPositions.Clear(); // 每效果独立，防后续非需选效果误用
                    _effectQueueIndex++;
                    continue;
                }
                // 未确认的需选效果 → 停下，等待玩家选择
                _pendingSelectEffect = eff;
                LogManager.Log(LogCategory.Select, $"效果 {eff.SkillEffectID2} 需选目标，等待确认");
                return false;
            }
            ExecuteEffect(eff, _effectSkillType);
            _effectQueueIndex++;
        }
        _effectSequenceActive = false;
        _pendingSelectEffect = null;
        ForcedTargetPositions.Clear();
        return true;
    }

    /// <summary>终止效果序列（选择中途取消/异常收尾，2026-08-15）。</summary>
    public void AbortEffectSequence()
    {
        _effectSequenceActive = false;
        _pendingSelectEffect = null;
        _effectQueue = null;
        _effectQueueIndex = 0;
        ForcedTargetPositions.Clear();
    }

    /// <summary>Splash（溅射）参数（2026-08-14）：倍率 + 左右扩展位。</summary>
    public struct SplashInfo
    {
        public float Rate;
        public int Left;
        public int Right;
    }

    /// <summary>
    /// 效果级 Splash（溅射）参数解析（2026-08-15）：Damage 效果的 Param2 含 "Splash(n; L,R)"。
    /// 返回溅射倍率与左右扩展；无 Splash 返回 null。UI 目标选择高亮按"当前待选效果"读取。
    /// </summary>
    public SplashInfo? GetEffectSplash(SkillEffectData eff)
    {
        if (eff == null || eff.EffectType != "Damage" || string.IsNullOrEmpty(eff.Param2)) return null;
        int p = eff.Param2.IndexOf("Splash(");
        if (p < 0) return null;
        string inner = eff.Param2.Substring(p + 7);
        int close = inner.IndexOf(')');
        if (close < 0) return null;
        inner = inner.Substring(0, close);
        var segs = inner.Contains(';') ? inner.Split(';') : inner.Split(',');
        if (segs.Length < 2) return null;
        float rate; int left, right;
        if (!float.TryParse(segs[0].Trim(), out rate)) return null;
        if (segs.Length >= 3)
        {
            int.TryParse(segs[1].Trim(), out left);
            int.TryParse(segs[2].Trim(), out right);
        }
        else
        {
            var r = segs[1].Trim().Split(',');
            if (r.Length < 2) return null;
            int.TryParse(r[0].Trim(), out left);
            int.TryParse(r[1].Trim(), out right);
        }
        return new SplashInfo { Rate = rate, Left = left, Right = right };
    }

    /// <summary>
    /// 当前按钮技能的 Splash（溅射）参数（2026-08-14）：遍历技能效果找第一个带 Splash 的 Damage 效果。
    /// </summary>
    public SplashInfo? GetSkillSplash(int skillType)
    {
        string skillID2 = skillType switch
        {
            0 => _boundSkills[0],
            1 => _boundSkills[1],
            2 => _boundSkills[2],
            3 => _boundSkills[3],
            _ => null
        };
        if (string.IsNullOrEmpty(skillID2) || !_skillEffects.TryGetValue(skillID2, out var effects)) return null;

        foreach (var eff in effects)
        {
            var s = GetEffectSplash(eff);
            if (s != null) return s;
        }
        return null;
    }

    /// <summary>
    /// 目标选择阶段：计算当前选择的实际命中目标数（主目标 + 溅射目标，2026-08-14）。
    /// 溅射目标 = 各主目标左右扩展位上有敌人的位置（去重）。
    /// </summary>
    public int GetSelectedHitCount(int skillType, System.Collections.Generic.List<int> selectedPositions)
    {
        if (selectedPositions == null || selectedPositions.Count == 0) return 0;
        int count = selectedPositions.Count;
        var splash = GetSkillSplash(skillType);
        if (splash == null) return count;
        var bm = BattleManager.Instance;
        var added = new System.Collections.Generic.HashSet<int>();
        foreach (var pos in selectedPositions)
        {
            int left = Mathf.Max(1, pos - splash.Value.Left);
            int right = Mathf.Min(5, pos + splash.Value.Right);
            for (int p2 = left; p2 <= right; p2++)
            {
                if (p2 == pos || selectedPositions.Contains(p2)) continue;
                if (bm != null && bm.IsEnemyAliveAt(p2) && added.Add(p2)) count++;
            }
        }
        return count;
    }

    public void ExecuteSkillByID(string skillID2, int skillType)
    {
        if (string.IsNullOrEmpty(skillID2)) return;
        if (!_skillById2.TryGetValue(skillID2, out var sk)) return;

        // 资源扣除 + 预解析钩子（非按钮路径同样执行；AP 不足返回 false 时直接终止）
        if (!PrepareSkillExecution(skillID2, skillType, sk, out var effects, out var forcedPositions)) return;

        // 直接执行全部效果（状态/钩子触发等非玩家选择路径，不做效果级暂停，2026-08-15）
        if (effects != null)
        {
            foreach (var eff in effects)
                ExecuteEffect(eff, skillType);
        }

        // 清空强制目标位置（一次性）
        ForcedTargetPositions.Clear();

        LogManager.Log(LogCategory.Skill, $"{sk.SkillName} used (targetPos={string.Join(",", forcedPositions)})");
    }

    /// <summary>
    /// 技能执行准备（2026-08-15，按钮路径/直接路径共用）：
    /// 清序列命中集合、快照强制目标、扣 AP/能量、冷却/次数、清上一条目标、PreAlliesDamage 钩子预解析。
    /// 返回 false = AP 不足等，不应执行效果。
    /// </summary>
    bool PrepareSkillExecution(string skillID2, int skillType, SkillMainData sk, out List<SkillEffectData> effects, out List<int> forcedPositions)
    {
        // 技能序列开始：清空"本序列命中集合"（Hit()钩子的序列内判定基准）
        _sequenceHitEffectIDs.Clear();

        // 目标选择（2026-08-07）：读入本次技能的强制目标位置组合；技能序列结束后清空
        forcedPositions = new List<int>(ForcedTargetPositions);

        // AP消耗（爆发不消耗AP，改为清空能量）
        if (skillType != 3)
        {
            if (!APManager.ConsumeAP(sk.APCost)) { effects = null; return false; }
        }
        else
        {
            Entity.CurrentEnergy = 0;
        }

        // 冷却 & 次数
        if (sk.Cooldown > 0)
            _cooldownRemaining[skillID2] = Mathf.CeilToInt(sk.Cooldown);
        if (sk.MaxCharge > 0 && _currentCharges.ContainsKey(skillID2))
            _currentCharges[skillID2]--;

        effects = _skillEffects.TryGetValue(skillID2, out var list) ? list : null;
        if (effects == null) return true;

        _lastSkillTargets.Clear(); // 每个技能序列开始时清空"上一条目标"
        _lastTargetPositions.Clear(); // 同时清空位置继承（TargetOverride="0,0" 只继承本技能序列内的上一条目标）

        // PreAlliesDamage 钩子（2026-08-14）：
        // 预解析本技能所有 Damage 效果的目标位置（并集去重）→ 至少一名实体（不会落空）
        // → 先触发钩子（触发行效果先于该行动实行），再正式执行技能效果。
        var bm = BattleManager.Instance;
        if (bm != null && bm.PendingActionTargetPositions != null)
        {
            bm.PendingActionTargetPositions.Clear();
            foreach (var eff in effects)
            {
                if (eff == null || eff.EffectType != "Damage") continue;
                var targets = ResolveTargets(eff);
                if (targets == null) continue;
                foreach (var t in targets)
                {
                    if (t != null && t.Position != null && !bm.PendingActionTargetPositions.Contains(t.Position.SlotIndex))
                        bm.PendingActionTargetPositions.Add(t.Position.SlotIndex);
                }
            }
            if (bm.PendingActionTargetPositions.Count > 0)
                PreDamageHookSystem.TriggerPreDamageHooks(); // 钩子逻辑见 PreDamageHookSystem.cs（2026-08-15 独立）
        }
        return true;
    }

    // ================================================================
    //  效果/技能数据修改接口（2026-08-12，供独立玩法层注入：天赋命座/武器等）
    //  全部基于克隆，不污染 DataManager 全局表；多次进出战斗不叠加。
    // ================================================================

    /// <summary>深拷贝技能效果（新实例）。</summary>
    public SkillEffectData CloneEffect(SkillEffectData src)
    {
        if (src == null) return null;
        return new SkillEffectData
        {
            SkillEffectID = src.SkillEffectID,
            SkillEffectID2 = src.SkillEffectID2,
            EffectIndex = src.EffectIndex,
            EffectType = src.EffectType,
            Element = src.Element,
            DamageType = src.DamageType,
            Duration = src.Duration,
            AddInPhase = src.AddInPhase,
            TriggerPhase = src.TriggerPhase,
            Param1 = src.Param1,
            Param2 = src.Param2,
            Param3 = src.Param3,
            TargetType = src.TargetType,
            TargetNumber = src.TargetNumber,
            TargetConsecutive = src.TargetConsecutive,
            TargetConsecutiveSet = src.TargetConsecutiveSet,
            TargetOverride = src.TargetOverride,
            EnergyGainMode = src.EnergyGainMode,
            ScriptHook = src.ScriptHook
        };
    }

    /// <summary>深拷贝技能基本信息（新实例）。</summary>
    public SkillMainData CloneSkill(SkillMainData src)
    {
        if (src == null) return null;
        return new SkillMainData
        {
            SkillID = src.SkillID,
            SkillID2 = src.SkillID2,
            SkillName = src.SkillName,
            APCost = src.APCost,
            Cooldown = src.Cooldown,
            EnergyUsed = src.EnergyUsed,
            MaxCharge = src.MaxCharge,
            InitialCharge = src.InitialCharge,
            SkillPhase = src.SkillPhase,
            ActionType = src.ActionType,
            Description = src.Description
        };
    }

    /// <summary>取技能效果列表（深拷贝副本，外部可安全修改）。</summary>
    public List<SkillEffectData> GetSkillEffects(string skillID2)
    {
        var result = new List<SkillEffectData>();
        if (_skillEffects.TryGetValue(skillID2, out var list))
            foreach (var e in list)
                if (e != null) result.Add(CloneEffect(e));
        return result;
    }

    /// <summary>取该角色全部技能ID2（独立玩法层定位效果所属技能用）。</summary>
    public List<string> GetSkillID2List()
    {
        return new List<string>(_skillById2.Keys);
    }

    /// <summary>替换技能效果列表（InsertEffect/ModifyEffect 用），同步 _effectById 索引。</summary>
    public void SetSkillEffects(string skillID2, List<SkillEffectData> list)
    {
        _skillEffects[skillID2] = list;
        foreach (var e in list)
            if (e != null && !string.IsNullOrEmpty(e.SkillEffectID2))
                _effectById[e.SkillEffectID2] = e;
    }

    /// <summary>克隆后修改技能字段（ModifySkill 用），并同步次数（InitialCharge 变化时）。</summary>
    public void ModifySkillData(string skillID2, System.Action<SkillMainData> mod)
    {
        if (!_skillById2.TryGetValue(skillID2, out var sk)) return;
        var clone = CloneSkill(sk);
        mod(clone);
        _skillById2[skillID2] = clone;
        if (clone.MaxCharge > 0 && _currentCharges.ContainsKey(skillID2))
        {
            int init = clone.InitialCharge;
            if (init <= 0) init = clone.MaxCharge;
            _currentCharges[skillID2] = init;
        }
    }

    // ================================================================
    //  效果执行器
    // ================================================================
    void ExecuteEffect(SkillEffectData eff, int skillType)
    {
        // 效果级 ScriptHook（2026-08-12）：Hit()/Check() 返回假则该效果不执行（T2 命中后加攻等）
        if (eff != null && !ScriptHookEvaluator.Evaluate(eff.ScriptHook, Entity, LastHitEffectID2))
            return;
        switch (eff.EffectType)
        {
            case "Damage":
                ExecuteDamage(eff, skillType);
                break;
            case "ApplyStatus":
                ExecuteApplyStatus(eff);
                break;
            case "RemoveStatus":
                ExecuteRemoveStatus(eff);
                break;
            case "ExecuteSkill":
                ExecuteExecuteSkill(eff);
                break;
            case "ExecuteEffect":
                ExecuteExecuteEffect(eff);
                break;
            case "GainEnergy":
                ExecuteGainEnergy(eff);
                break;
            case "ChangeControl":
                ExecuteChangeControl(eff);
                break;
            case "Heal":
                ExecuteHeal(eff, skillType);
                break;
            default:
                LogManager.LogWarning(LogCategory.Effect, $"未知类型: {eff.EffectType}");
                break;
        }
    }

    /// <summary>治疗效果（2026-08-14）：Param1=治疗量公式（如 0.15*TotalATK），目标=TargetType解析。</summary>
    void ExecuteHeal(SkillEffectData eff, int skillType)
    {
        var targets = ResolveTargets(eff);
        if (targets == null) return;
        float heal = ResolveBaseValue(eff.Param1, eff.SkillEffectID2, skillType, eff.Element);
        foreach (var t in targets)
        {
            if (t == null || !t.IsAlive) continue;
            t.Heal(heal);
            LogManager.Log(LogCategory.Effect, $"治疗 {t.EntityID} +{heal:F1} (HP {t.CurrentHP:F1})");
        }
    }

    // ----------------------------------------------------------------
    //  Damage
    // ----------------------------------------------------------------
    void ExecuteDamage(SkillEffectData eff, int skillType)
    {
        // 多段 HitData（Hits1-7，每段独立结算；空段跳过）
        var hits = GetHitDataList(eff, skillType);
        if (hits.Count == 0 || (hits.Count == 1 && hits[0].Multiplier <= 0))
        {
            LogManager.LogWarning(LogCategory.Damage, $"{eff.SkillEffectID2} 倍率为0或未匹配等级行 (skillType={skillType})，无伤害");
            return;
        }

        // 基准值（Param1 决定用哪个属性；% 引用由 ResolveBaseValue 处理）
        float baseValue = ResolveBaseValue(eff.Param1, eff.SkillEffectID2, skillType, eff.Element);

        // 选目标（支持 TargetType/TargetNumber/TargetConsecutive/TargetOverride）
        var targets = ResolveTargets(eff);
        if (targets == null || targets.Count == 0)
        {
            LogManager.Log(LogCategory.Damage, $"{Entity.EntityID} 无目标，攻击落空 ({eff.SkillEffectID2})");
            return;
        }

        // 命中记录（Hit()钩子：有目标即算命中，不管是否造成伤害/伤害数值）
        LastHitEffectID2 = eff.SkillEffectID2;
        //记录序列内命中（Hit()钩子：序列内后续效果可判定"该效果命中过"）
        if (!string.IsNullOrEmpty(eff.SkillEffectID2))
            _sequenceHitEffectIDs.Add(eff.SkillEffectID2);

        // 记录本次选中的目标位置（供后续效果 TargetOverride="0,0" 沿用，如爆发伤害→箭雨状态施加到同一批位置）
        _lastTargetPositions.Clear();
        foreach (var t in targets)
            if (t.Position != null) _lastTargetPositions.Add(t.Position.SlotIndex);

        foreach (var target in targets)
        {
            // 多段：每个 hit 完整结算（伤害/暴击/护盾/附着/削韧/OnHit）后才进入下一 hit；目标死亡后后续 hit 不再结算
            foreach (var hit in hits)
            {
                if (!target.IsAlive) break;

                float damage = CalculateDamage(baseValue, hit, eff, target, skillType);

                // 元素反应（2026-08-14）：增幅反应（融化等）在伤害计算后、护盾吸收前结算；
                // 2026-08-15：按元素量模型消耗双方元素，攻击元素残留量决定是否上附着（冰攻融化火后不残留冰）
                float reactedDamage = ElementReactionManager.TryReaction(target, eff.Element, hit.ElementAura, damage, out string reactionName, out float attackRemain);
                if (reactionName.Length > 0)
                    LogManager.Log(LogCategory.Damage, $"{Entity.EntityID}攻击 {target.EntityID} 触发{reactionName}");

                // 护盾吸收
                float finalDamage = target.AbsorbDamageWithShield(reactedDamage, eff.Element);
                target.TakeDamage(finalDamage);

                // 元素附着（2026-08-15：使用反应后的残留量，残留0不上附着）
                if (!string.IsNullOrEmpty(eff.Element) && eff.Element != "None" && attackRemain > 0f)
                    target.ApplyAura(eff.Element, attackRemain, Entity.EntityID);

                // 削韧
                target.Poise -= hit.Poise;

                // 溅射（Splash(n; L,R)）：以该 hit 主目标为原点，左右各扩展 L/R 位，
                // 溅射目标受到主目标实际伤害 × n（继承暴击/加成结果，不独立判定）；
                // 每 1 hit 的主目标结算完（含溅射）才进入下一 hit
                if (finalDamage > 0f && TryParseSplash(eff.Param2, out float splashRate, out int splashL, out int splashR))
                {
                    int origin = target.Position != null ? target.Position.SlotIndex : -1;
                    if (origin >= 0)
                    {
                        var bm = BattleManager.Instance;
                        int leftPos = Mathf.Max(1, origin - splashL);
                        int rightPos = Mathf.Min(5, origin + splashR);
                        for (int pos = leftPos; pos <= rightPos; pos++)
                        {
                            if (pos == origin) continue;
                            var slot = bm != null && bm.Field != null ? bm.Field.GetSlot(BattleSide.Enemy, pos) : null;
                            if (slot == null || !slot.IsOccupied || slot.Occupant == null || !slot.Occupant.IsAlive) continue;
                            var splashTarget = slot.Occupant;
                            // 溅射伤害 = 主目标该hit实际伤害 × 倍率（乘倍率）
                            float splashDmg = finalDamage * splashRate;
                            // 溅射目标【独立暴击判定】：每个敌人每次伤害单独 roll，不能三个目标共用一次暴击
                            float splashCritRate = Mathf.Clamp(splashTarget.CritRate + splashTarget.GetStatusCritRate("", "", 0, ""), 0f, 1f);
                            float splashCritMult = 1f;
                            if (UnityEngine.Random.value < splashCritRate)
                                splashCritMult = 1f + splashTarget.CritDMG;
                            float splashFinal = splashTarget.AbsorbDamageWithShield(splashDmg * splashCritMult, eff.Element);
                            splashTarget.TakeDamage(splashFinal);
                            LogManager.Log(LogCategory.Splash, $"{target.EntityID} -> {splashTarget.EntityID} : {splashFinal:F1} (基础{splashDmg:F1}={finalDamage:F1}×{splashRate}, {(splashCritMult > 1f ? $"暴击x{splashCritMult:F2}" : "未暴击")})");
                        }
                    }
                }

                // OnHit：目标受击 → 目标身上及所在位置的场地状态触发 OnHit 行动（HitSource 匹配 + ScriptHook）
                TriggerOnHit(target, eff);

                LogManager.Log(LogCategory.Damage, $"{Entity.EntityID} 攻击 {target.EntityID} : {finalDamage:F1} ({damage:F1} raw, {hit.Multiplier}×{baseValue:F1}) | {eff.Element} | 削韧 {hit.Poise}");
            }
        }
    }

    // ----------------------------------------------------------------
    //  ApplyStatus
    // ----------------------------------------------------------------
    void ExecuteApplyStatus(SkillEffectData eff)
    {
        string statusID2 = eff.Param1;
        if (string.IsNullOrEmpty(statusID2)) return;

        var dm = DataManager.Instance;
        if (!dm.StatusMainDict.TryGetValue(statusID2, out var statusMain))
        {
            LogManager.LogWarning(LogCategory.ApplyStatus, $"状态不存在: {statusID2}");
            return;
        }

        // EnemyField 目标：状态挂到场地位置（含空位），不依赖单位
        if (eff.TargetType == "EnemyField")
        {
            ApplyStatusToField(eff, statusID2, statusMain);
            return;
        }

        // 其他目标：挂到实体
        var targets = ResolveTargets(eff);
        if (targets == null || targets.Count == 0) return;

        foreach (var target in targets)
        {
            target.AddStatus(statusID2, Entity, eff.Duration, eff.AddInPhase, eff.TriggerPhase, statusMain);
            LogManager.Log(LogCategory.ApplyStatus, $"{statusID2} -> {target.EntityID} (dur={eff.Duration}, phase={eff.AddInPhase}, trig={eff.TriggerPhase})");
        }
    }

    // 施加状态到场地位置（EnemyField）：按 TargetOverride/强制位置选位置，状态挂到 FieldPosition.StatusList
    void ApplyStatusToField(SkillEffectData eff, string statusID2, StatusMainData statusMain)
    {
        var bm = BattleManager.Instance;
        if (bm == null || bm.Field == null) return;

        // 选择要施加的位置：强制位置组合优先（爆发选中[2,3,4]），否则按 TargetOverride="0,0" 沿用上一条目标位置
        List<int> positions = new List<int>();
        if (ForcedTargetPositions.Count > 0)
        {
            positions.AddRange(ForcedTargetPositions);
        }
        else if (!string.IsNullOrEmpty(eff.TargetOverride))
        {
            // "0,0"：沿用上一条效果（SE_Burst_Amber1 爆发伤害）选中的目标位置
            positions.AddRange(_lastTargetPositions);
        }

        if (positions.Count == 0)
        {
            // 都没有 → 默认全部敌方位置
            foreach (var s in bm.Field.EnemySlots) positions.Add(s.SlotIndex);
        }

        foreach (int pos in positions)
        {
            var slot = bm.Field.GetSlot(BattleSide.Enemy, pos);
            if (slot == null) continue;

            var inst = new StatusInstance
            {
                StatusID2 = statusID2,
                Caster = Entity,
                RemainingPhaseCount = eff.Duration,
                AddInPhase = eff.AddInPhase,
                TriggerPhase = eff.TriggerPhase,
                MainData = statusMain,
                ApplyOrder = ++BattleEntity._applyOrderCounter
            };
            slot.StatusList.Add(inst);
            LogManager.Log(LogCategory.ApplyStatus, $"{statusID2} -> {slot} (dur={eff.Duration}, phase={eff.AddInPhase}, trig={eff.TriggerPhase})");
            AutoBindStatusToSlot(slot, inst);
            // OnApply：位置状态施加时立刻生效（由状态施放者控制器执行）
            if (inst.Caster != null && inst.Caster.CharacterCtrl != null)
                inst.Caster.CharacterCtrl.OnStatusApplied(inst, slot);
            // 状态结算登记（2026-08-12）：位置状态按 AddInPhase/TriggerPhase 登记阶段桶
            BattleManager.Instance?.RegisterStatusTick(inst, null, slot);
        }
    }

    /// <summary>
    /// 位置状态绑定（BindStatus）：状态挂到场地位置后，扫描该状态的 BindStatus 效果，
    /// 把 Param1 子状态挂到"状态所在位置 ± range"的相邻位置（TargetSelect "X,X"）。
    /// 位置状态不随单位死亡消失（2026-08-06）。
    /// </summary>
    /// <summary>
    /// 解析 TargetSelect "X,X" 的左右扩展范围（取左侧值即可，右侧按对称处理）。
    /// </summary>
    private static int ParseTargetSelectRange(string targetSelect)
    {
        if (string.IsNullOrEmpty(targetSelect)) return 0;
        var parts = targetSelect.Split(',');
        if (parts.Length >= 1 && int.TryParse(parts[0].Trim(), out int left)) return left;
        return 0;
    }

    /// <summary>
    /// 位置状态绑定（BindStatus）：状态挂到场地位置后，扫描该状态的 BindStatus 效果，
    /// 把 Param1 子状态挂到"状态所在位置 ± range"的相邻位置（TargetSelect "X,X"）。
    /// 位置状态不随单位死亡消失（2026-08-06）。
    /// </summary>
    void AutoBindStatusToSlot(FieldPosition slot, StatusInstance parent)
    {
        if (parent == null || parent.MainData == null) return;
        var dm = DataManager.Instance;
        if (dm == null || dm.StatusEffectDict == null) return;

        int parentStatusID = parent.MainData.StatusID;
        foreach (var kv in dm.StatusEffectDict)
        {
            var eff = kv.Value;
            if (eff.StatusEffectID / 100 != parentStatusID) continue;
            if (eff.EffectType != "BindStatus") continue;

            string childStatusID = eff.Param1;
            if (string.IsNullOrEmpty(childStatusID)) continue;
            if (!dm.StatusMainDict.TryGetValue(childStatusID, out var childMain)) continue;

            // TargetSelect "X,X"：原点=状态所在位置，左右各扩展 range
            int range = ParseTargetSelectRange(eff.TargetSelect);
            List<int> positions = BattlePositionSystem.GetAdjacentPositions(BattleSide.Enemy, slot.SlotIndex, range);
            LogManager.Log(LogCategory.Bind, $"状态{parent.StatusID2} 在位置{slot.SlotIndex} 绑定子状态{childStatusID} 到相邻位置[{string.Join(",", positions)}]");

            var bm = BattleManager.Instance;
            if (bm == null || bm.Field == null) continue;

            foreach (int pos in positions)
            {
                var targetSlot = bm.Field.GetSlot(BattleSide.Enemy, pos);
                if (targetSlot == null) continue;
                if (targetSlot.StatusList.Exists(s => s.StatusID2 == childStatusID)) continue; // 去重

                var childInst = new StatusInstance
                {
                    StatusID2 = childStatusID,
                    Caster = parent.Caster,
                    RemainingPhaseCount = parent.RemainingPhaseCount,
                    AddInPhase = eff.AddInPhase,
                    TriggerPhase = eff.TriggerPhase,
                    MainData = childMain,
                    ApplyOrder = ++BattleEntity._applyOrderCounter
                };
                targetSlot.StatusList.Add(childInst);
                // 状态结算登记（2026-08-12）：子状态同样登记（随父状态移除时注销）
                BattleManager.Instance?.RegisterStatusTick(childInst, null, targetSlot);
                parent.BoundStatuses.Add(childInst);
                LogManager.Log(LogCategory.Bind, $"子状态{childStatusID} 挂到位置{pos} 成功");
            }
        }
    }

    // ----------------------------------------------------------------
    //  RemoveStatus
    // ----------------------------------------------------------------
    /// <summary>从某侧所有位置移除指定状态（含子状态/ChangeControl/计时清理）。</summary>
    void RemoveStatusFromField(string statusID2, BattleSide side)
    {
        var bm = BattleManager.Instance;
        if (bm == null || bm.Field == null) return;
        var slots = side == BattleSide.Enemy ? bm.Field.EnemySlots : bm.Field.AllySlots;
        foreach (var slot in slots)
        {
            if (slot == null) continue;
            for (int i = slot.StatusList.Count - 1; i >= 0; i--)
            {
                var inst = slot.StatusList[i];
                if (inst == null || inst.StatusID2 != statusID2) continue;
                //连带移除位置子状态（BindStatus子状态跟随父状态）
                if (inst.BoundStatuses != null && inst.BoundStatuses.Count > 0)
                {
                    foreach (var child in inst.BoundStatuses)
                    {
                        if (child == null) continue;
                        foreach (var cs in slots)
                        {
                            if (cs == null) continue;
                            int ci = cs.StatusList.FindIndex(s => s == child);
                            if (ci >= 0)
                            {
                                bm.UnregisterStatusTick(child);
                                cs.StatusList.RemoveAt(ci);
                                break;
                            }
                        }
                    }
                    inst.BoundStatuses.Clear();
                }
                //解除 ChangeControl记录 + 计时注销
                if (inst.Caster != null && inst.Caster.CharacterCtrl != null)
                    inst.Caster.CharacterCtrl.OnStatusChangeControlRemoved(inst);
                bm.UnregisterStatusTick(inst);
                slot.StatusList.RemoveAt(i);
                LogManager.Log(LogCategory.RemoveStatus, $"{statusID2} removed from {slot}");
            }
        }
    }

    void ExecuteRemoveStatus(SkillEffectData eff)
    {
        string statusID2 = eff.Param1;
        if (string.IsNullOrEmpty(statusID2)) return;

        //位置状态移除（EnemyField/AllyField目标）：从目标侧的位置状态列表移除（兔兔伯爵爆炸后移除兔子）
        if (eff.TargetType == "EnemyField" || eff.TargetType == "AllyField")
        {
            var side = eff.TargetType == "EnemyField" ? BattleSide.Enemy : BattleSide.Ally;
            RemoveStatusFromField(statusID2, side);
            return;
        }

        var targets = ResolveTargets(eff);
        if (targets == null) return;
        foreach (var target in targets)
        {
            bool removed = target.RemoveStatus(statusID2, eff.TargetNumber < 0 ? -1 : 1);
            if (removed)
                LogManager.Log(LogCategory.RemoveStatus, $"{statusID2} removed from {target.EntityID}");
        }
    }

    // ----------------------------------------------------------------
    //  ExecuteSkill（递归执行目标技能的效果序列）
    // ----------------------------------------------------------------
    void ExecuteExecuteSkill(SkillEffectData eff)
    {
        string targetSkillID2 = eff.Param1;
        if (string.IsNullOrEmpty(targetSkillID2)) return;
        if (!_skillEffects.TryGetValue(targetSkillID2, out var effects)) return;

        foreach (var subEff in effects)
            ExecuteEffect(subEff, GetEffectSkillType(subEff.SkillEffectID2, 1));
    }

    // ----------------------------------------------------------------
    //  ExecuteEffect（直接执行目标效果，箭雨状态触发用）
    // ----------------------------------------------------------------
    void ExecuteExecuteEffect(SkillEffectData eff)
    {
        string targetEffID2 = eff.Param1;
        if (string.IsNullOrEmpty(targetEffID2)) return;

        var dm = DataManager.Instance;
        if (!dm.SkillEffectDict.TryGetValue(targetEffID2, out var targetEff)) return;
        if (targetEff.EffectType == "Damage")
        {
            // 箭雨：直接按爆发类型取伤害等级
            ExecuteDamage(targetEff, 3);
        }
        else
        {
            ExecuteEffect(targetEff, GetEffectSkillType(targetEff.SkillEffectID2, 3));
        }
    }

    // ----------------------------------------------------------------
    //  GainEnergy（能量获取）
    //  配表术语 1.21:
    //    TotalEnergy = GainedEnergy × OnstageStatus × ElementStatus × (EnergyRechargeRate + RechargeBonus)
    //    Based=走该公式；Direct/Flat=固定值不受系数影响
    // ----------------------------------------------------------------
    void ExecuteGainEnergy(SkillEffectData eff)
    {
        if (!float.TryParse(eff.Param1, out float gained)) return;
        var targets = ResolveTargets(eff);
        if (targets == null) return;

        foreach (var target in targets)
        {
            float total = gained;
            if (eff.EnergyGainMode == "Based")
            {
                // 元素匹配：同元素1.5，不同0.5
                float elementStatus = string.IsNullOrEmpty(eff.Element)
                    ? 1f
                    : (target.Type == BattleEntity.EntityType.Character && target.CharacterCtrl != null
                       && target.CharacterCtrl._attrData != null
                       && target.CharacterCtrl._attrData.Element == eff.Element)
                        ? 1.5f : 0.5f;

                // 出战系数：出战=1（本阶段单人出战简化）
                float onstage = 1f;
                // 充能效率
                float recharge = target.TotalRechargeRate > 0 ? target.TotalRechargeRate : 1f;

                total = gained * onstage * elementStatus * recharge;
            }
            // Direct/Flat：固定值

            target.CurrentEnergy = Mathf.Min(target.CurrentEnergy + Mathf.RoundToInt(total), target.MaxEnergy);
            LogManager.Log(LogCategory.GainEnergy, $"{target.EntityID} +{total:F1} energy (mode={eff.EnergyGainMode})");
        }
    }

    // ----------------------------------------------------------------
    //  ChangeControl（修改按键绑定）
    //  术语：param1=按钮类型(Normal/Heavy/Skill/Burst/All)，param2=新技能ID2或Freeze，param3=0/1
    //  状态效果表挂载 = 跟状态 duration 走（状态施加时执行，状态消失时解除）；
    //  技能效果表挂载 = 填了 Duration 一切照常（Duration 回合后结束）；没填 = 视为永久（不记录旧技能信息）
    //  覆盖判定（术语）：解除时识别当前按钮绑定是否依旧为效果所设定的 skill 字段（Param2），
    //  若不同（已被其他 Change 覆盖）则不返回。
    // ----------------------------------------------------------------
    void ExecuteChangeControl(SkillEffectData eff)
    {
        var btnTypes = ParseButtonTypes(eff.Param1);
        if (btnTypes == null)
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.SkillEffectID2} Param1 非法: {eff.Param1}（需 Normal/Heavy/Skill/Burst/All）");
            return;
        }

        // 冻结型：Param2=Freeze，指定按钮无法使用（All=全部按钮）
        if (eff.Param2 == "Freeze")
        {
            _ccRecords.Add(new ControlChangeRecord
            {
                IsFreeze = true,
                FrozenSkillTypes = btnTypes,
                IsSkillEffect = true,
                IsPermanent = eff.Duration <= 0,
                RemainingTurns = eff.Duration,
            });
            LogManager.Log(LogCategory.ChangeControl, $"{eff.SkillEffectID2} 冻结按钮 [{string.Join(",", btnTypes)}] (dur={eff.Duration}, 永久={eff.Duration <= 0})");
            OnControlChanged?.Invoke();
            return;
        }

        // 替换型：Param2 = 新技能ID2（只能替换单个按钮，All 只能配 Freeze）
        string newSkillID2 = eff.Param2;
        if (string.IsNullOrEmpty(newSkillID2))
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.SkillEffectID2} 未填 Param2（新技能ID）");
            return;
        }
        if (!_skillById2.ContainsKey(newSkillID2))
        {
            LogManager.LogError(LogCategory.ChangeControl, $"替换技能不存在: {newSkillID2}");
            return;
        }
        if (btnTypes.Count != 1)
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.SkillEffectID2} 替换型 Param1 只能指定单个按钮（{eff.Param1}），All 只能配 Freeze");
            return;
        }
        int skillType = btnTypes[0];
        string originalID = _boundSkills[skillType];
        int origCD = _cooldownRemaining.TryGetValue(originalID, out int cd) ? cd : 0;
        int origCharge = _currentCharges.TryGetValue(originalID, out int ch) ? ch : 0;

        ApplyParam3OnSwap(eff.Param3, newSkillID2);

        _boundSkills[skillType] = newSkillID2;

        // 没填 Duration：视为永久，不记录旧技能信息
        if (eff.Duration > 0)
        {
            _ccRecords.Add(new ControlChangeRecord
            {
                SkillType = skillType,
                OriginalSkillID = originalID,
                OrigCooldownSnapshot = origCD,
                OrigChargeSnapshot = origCharge,
                NewSkillID = newSkillID2,
                IsSkillEffect = true,
                RemainingTurns = eff.Duration,
            });
        }
        LogManager.Log(LogCategory.ChangeControl, $"{eff.SkillEffectID2} 替换: {originalID} -> {newSkillID2} (Param3={eff.Param3}, dur={eff.Duration})");
        OnControlChanged?.Invoke();
    }

    // ----------------------------------------------------------------
    //  状态驱动入口（BattleEntity 在状态施加/消失时调用）
    // ----------------------------------------------------------------
    /// <summary>状态施加成功后调用：扫描该状态名下所有 ChangeControl 效果并执行。</summary>
    public void OnStatusChangeControlApplied(StatusInstance inst)
    {
        if (inst == null || inst.MainData == null) return;
        var dm = DataManager.Instance;
        if (dm == null) return;
        int parentStatusID = inst.MainData.StatusID;
        foreach (var kv in dm.StatusEffectDict)
        {
            var eff = kv.Value;
            if (eff.StatusEffectID / 100 != parentStatusID) continue;
            if (eff.EffectType != "ChangeControl") continue;
            ApplyChangeControlEffect(eff, inst);
        }
    }

    /// <summary>状态消失（移除/到期）时调用：解除该状态名下所有 ChangeControl 记录。</summary>
    public void OnStatusChangeControlRemoved(StatusInstance inst)
    {
        if (inst == null) return;
        bool changed = false;
        for (int i = _ccRecords.Count - 1; i >= 0; i--)
        {
            var rec = _ccRecords[i];
            if (rec.IsSkillEffect) continue; // 技能效果版由 OnTurnStart 计时
            if (rec.SourceStatusID2 != inst.StatusID2) continue;
            if (RevertChangeControlRecord(rec))
                _ccRecords.RemoveAt(i);
            changed = true;
        }
        if (changed) OnControlChanged?.Invoke();
    }

    /// <summary>执行一条状态效果表的 ChangeControl（状态施加时）。</summary>
    void ApplyChangeControlEffect(StatusEffectData eff, StatusInstance inst)
    {
        var btnTypes = ParseButtonTypes(eff.Param1);
        if (btnTypes == null)
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.StatusEffectID2} Param1 非法: {eff.Param1}（需 Normal/Heavy/Skill/Burst/All）");
            return;
        }

        // 冻结型
        if (eff.Param2 == "Freeze")
        {
            _ccRecords.Add(new ControlChangeRecord
            {
                IsFreeze = true,
                FrozenSkillTypes = btnTypes,
                SourceStatusID2 = inst.StatusID2,
            });
            LogManager.Log(LogCategory.ChangeControl, $"{inst.StatusID2} 冻结按钮 [{string.Join(",", btnTypes)}]");
            OnControlChanged?.Invoke();
            return;
        }

        // 替换型
        string newSkillID2 = eff.Param2;
        if (string.IsNullOrEmpty(newSkillID2))
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.StatusEffectID2} 未填 Param2（新技能ID）");
            return;
        }
        if (!_skillById2.ContainsKey(newSkillID2))
        {
            LogManager.LogError(LogCategory.ChangeControl, $"替换技能不存在: {newSkillID2}");
            return;
        }
        if (btnTypes.Count != 1)
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.StatusEffectID2} 替换型 Param1 只能指定单个按钮（{eff.Param1}），All 只能配 Freeze");
            return;
        }
        int skillType = btnTypes[0];
        string originalID = _boundSkills[skillType];
        int origCD = _cooldownRemaining.TryGetValue(originalID, out int cd) ? cd : 0;
        int origCharge = _currentCharges.TryGetValue(originalID, out int ch) ? ch : 0;

        ApplyParam3OnSwap(eff.Param3, newSkillID2);

        _boundSkills[skillType] = newSkillID2;
        _ccRecords.Add(new ControlChangeRecord
        {
            SkillType = skillType,
            OriginalSkillID = originalID,
            OrigCooldownSnapshot = origCD,
            OrigChargeSnapshot = origCharge,
            NewSkillID = newSkillID2,
            SourceStatusID2 = inst.StatusID2,
        });
        LogManager.Log(LogCategory.ChangeControl, $"{inst.StatusID2} 替换: {originalID} -> {newSkillID2} (Param3={eff.Param3})");
        OnControlChanged?.Invoke();
    }

    /// <summary>Param3：1=换上时重置冷却、次数读 InitialCharge；0/空=不重置（保持冻结值）。</summary>
    void ApplyParam3OnSwap(string param3, string newSkillID2)
    {
        if (param3 != "1") return;
        if (_cooldownRemaining.ContainsKey(newSkillID2))
            _cooldownRemaining[newSkillID2] = 0;
        if (_skillById2.TryGetValue(newSkillID2, out var nsk) && nsk.MaxCharge > 0)
        {
            int init = nsk.InitialCharge;
            if (init <= 0) init = nsk.MaxCharge;
            _currentCharges[newSkillID2] = init;
        }
    }

    /// <summary>
    /// 解除一条 ChangeControl 记录。返回 true = 记录应被移除。
    /// 术语：该状态消失时需识别当前技能按键绑定是否依旧为效果所设定的 skill 字段（Param2），
    /// 若不同（已被其他 Change 覆盖）则不将其返回；相同则返回原技能，冷却/次数写回快照。
    /// </summary>
    bool RevertChangeControlRecord(ControlChangeRecord rec)
    {
        if (rec.IsFreeze)
        {
            LogManager.Log(LogCategory.ChangeControl, $"{rec.SourceStatusID2} 解冻按钮 [{string.Join(",", rec.FrozenSkillTypes)}]");
            return true;
        }

        bool stillBound = _boundSkills[rec.SkillType] == rec.NewSkillID;
        if (!stillBound)
        {
            LogManager.Log(LogCategory.ChangeControl, $"{rec.SourceStatusID2} 结束：按钮已被其他 Change 覆盖({_boundSkills[rec.SkillType]})，不返回 {rec.OriginalSkillID}");
            return true;
        }

        _boundSkills[rec.SkillType] = rec.OriginalSkillID;
        if (_cooldownRemaining.ContainsKey(rec.OriginalSkillID))
            _cooldownRemaining[rec.OriginalSkillID] = rec.OrigCooldownSnapshot;
        if (_currentCharges.ContainsKey(rec.OriginalSkillID))
            _currentCharges[rec.OriginalSkillID] = rec.OrigChargeSnapshot;
        LogManager.Log(LogCategory.ChangeControl, $"{rec.SourceStatusID2} 结束：按钮返回 {rec.OriginalSkillID}，冷却/次数恢复快照({rec.OrigCooldownSnapshot}/{rec.OrigChargeSnapshot})");
        return true;
    }

    /// <summary>解析 Param1 按钮类型，非法返回 null。All=全部按钮。</summary>
    List<int> ParseButtonTypes(string param1)
    {
        switch (param1)
        {
            case "Normal": return new List<int> { 0 };
            case "Heavy": return new List<int> { 1 };
            case "Skill": return new List<int> { 2 };
            case "Burst": return new List<int> { 3 };
            case "All": return new List<int> { 0, 1, 2, 3 };
            default: return null;
        }
    }

    // ================================================================
    //  目标选择
    //  TargetType: Self / Enemy / EnemyField / Allies / AlliesOnly
    //  TargetNumber: 数量，-1=全部
    //  TargetConsecutive: 0非连续 1连续 2连续随机 3非连续随机可重复 4非连续随机不可重复
    //  TargetOverride: X,X 偏移 或 状态ID筛选
    // ================================================================
    List<BattleEntity> ResolveTargets(SkillEffectData eff)
    {
        // 诊断（2026-08-14）：蓄力箭打全体定位——打印每次目标解析的关键字段
        LogManager.Log(LogCategory.Damage, $"[目标解析] {eff.SkillEffectID2} Type={eff.TargetType} Override='{eff.TargetOverride}' Num={eff.TargetNumber} Forced={ForcedTargetPositions.Count}");
        var bm = BattleManager.Instance;
        var result = new List<BattleEntity>();

        switch (eff.TargetType)
        {
            case "Self":
                result.Add(Entity);
                break;

            case "Enemy":
            {
                // 强制目标位置组合（2026-08-07 目标选择）：
                //   由 TargetSelector 确认后传入（如爆发选中[2,3,4]），逐位置取该位置的存活敌人，空位自然跳过。
                if (ForcedTargetPositions.Count > 0 && eff.TargetNumber > 0)
                {
                    foreach (int pos in ForcedTargetPositions)
                    {
                        var e = bm.GetEntityByPosition(BattleSide.Enemy, pos);
                        if (e != null && e.IsAlive) result.Add(e);
                    }
                    break;
                }
                foreach (var e in bm.Enemies)
                    if (e.Entity != null && e.Entity.IsAlive)
                        result.Add(e.Entity);
                break;
            }

            case "EnemyField":
            {
                // 敌方场地目标：以位置为目标，该位置上有没有单位都不影响目标选择（术语表131行）
                if (bm.Field == null) break;
                List<int> positions = ForcedTargetPositions.Count > 0
                    ? new List<int>(ForcedTargetPositions)
                    : new List<int>(bm.Field.EnemySlots.Select(s => s.SlotIndex));
                var slots = bm.Field.GetSlots(BattleSide.Enemy, positions);
                foreach (var slot in slots)
                    if (slot.IsOccupied) result.Add(slot.Occupant);
                break;
            }

            case "Allies":
            {
                // 单选/多选我方（2026-08-14）：目标选择阶段选中了特定我方位置（ForcedTargetPositions=我方位置1~4）时按位置取；
                // 否则默认全部我方
                if (ForcedTargetPositions.Count > 0 && eff.TargetNumber > 0)
                {
                    foreach (var pos in ForcedTargetPositions)
                    {
                        if (pos >= 1 && pos <= bm.Allies.Count)
                        {
                            var a = bm.Allies[pos - 1];
                            if (a != null && a.Entity != null && a.Entity.IsAlive && !result.Contains(a.Entity))
                                result.Add(a.Entity);
                        }
                    }
                }
                else
                {
                    foreach (var a in bm.Allies)
                        if (a.Entity != null && a.Entity.IsAlive)
                            result.Add(a.Entity);
                }
                break;
            }

            case "AlliesOnly":
            {
                if (ForcedTargetPositions.Count > 0 && eff.TargetNumber > 0)
                {
                    foreach (var pos in ForcedTargetPositions)
                    {
                        if (pos >= 1 && pos <= bm.Allies.Count)
                        {
                            var a = bm.Allies[pos - 1];
                            if (a != null && a.Entity != null && a.Entity != Entity && a.Entity.IsAlive && !result.Contains(a.Entity))
                                result.Add(a.Entity);
                        }
                    }
                }
                else
                {
                    foreach (var a in bm.Allies)
                        if (a.Entity != null && a.Entity.IsAlive && a.Entity != Entity)
                            result.Add(a.Entity);
                }
                break;
            }

            default:
                result.Add(Entity);
                break;
        }

        // TargetOverride（配表术语139行）：
        //   "0,0" = 直接沿用上一条效果选中的目标（此时不能填 TargetNumber/TargetConsecutive）
        //   状态ID2 = 目标 = 所有带有该状态的敌方单位（蓄力箭只打蓄力标记的位置）
        //   其他 "X,X" 数值偏移 = 基于上一条目标左右扩展（当前无场地系统，先按沿用处理）
        if (!string.IsNullOrEmpty(eff.TargetOverride))
        {
            string ov = eff.TargetOverride.Trim();

            // 状态ID2筛选（2026-08-14）：目标=所有带有该状态的敌方单位（实体+位置，蓄力箭只打标记目标）
            if (!ov.Contains(",") && ov != "0,0" && !ov.StartsWith("PreAlliesDamage("))
            {
                var filtered2 = new List<BattleEntity>();
                foreach (var e in bm.Enemies)
                    if (e.Entity != null && e.Entity.IsAlive && e.Entity.GetStatus(ov) != null)
                        filtered2.Add(e.Entity);
                int entityHit2 = filtered2.Count;
                if (bm.Field != null)
                {
                    foreach (var slot in bm.Field.EnemySlots)
                    {
                        if (slot == null) continue;
                        bool has2 = false;
                        foreach (var inst in slot.StatusList)
                            if (inst != null && inst.StatusID2 == ov) { has2 = true; break; }
                        if (has2 && slot.IsOccupied && slot.Occupant != null && slot.Occupant.IsAlive && !filtered2.Contains(slot.Occupant))
                            filtered2.Add(slot.Occupant);
                    }
                }
                result = filtered2;
                LogManager.Log(LogCategory.Damage, $"[筛选] ov={ov} 实体命中={entityHit2} 位置命中={filtered2.Count - entityHit2} 总计={filtered2.Count}");
            }
            else if (ov == "0,0")
            {
                // 沿用上一条效果的目标；若没有上一条（例如第一条效果），则 override 不执行，
                // 保持当前 result（按 TargetType/TargetNumber 正常解析）
                if (_lastSkillTargets.Count > 0)
                    result = new List<BattleEntity>(_lastSkillTargets);
            }
            else if (ov.StartsWith("PreAlliesDamage("))
            {
                // PreAlliesDamage 钩子目标（2026-08-14）：
                // 读主动行为预解析的总伤害目标位置（PendingActionTargetPositions = 位置编号并集），
                // 映射为有敌人的位置（空位跳过）；TargetNumber/TargetConsecutive 在下方"数量限制"中于该范围内选择。
                // 括号内状态ID必须已登记钩子（未触发/已消失则返回空，无目标）。
                string hookStatusID2 = "";
                int lp = ov.IndexOf('(');
                int rp = ov.IndexOf(')');
                if (lp >= 0 && rp > lp)
                    hookStatusID2 = ov.Substring(lp + 1, rp - lp - 1).Trim();
                var pending = new List<BattleEntity>();
                if (PreDamageHookSystem.HasPreDamageHook(hookStatusID2))
                {
                    foreach (var pos in bm.PendingActionTargetPositions)
                    {
                        if (bm.Field == null) break;
                        var slot = bm.Field.GetSlot(BattleSide.Enemy, pos);
                        if (slot != null && slot.IsOccupied && slot.Occupant != null && slot.Occupant.IsAlive)
                            pending.Add(slot.Occupant);
                }
                result = pending;
            }
            else if (ov.Contains(","))
            {
                // "X,X" 偏移：当前无场地系统，按"沿用上一条目标"处理（偏移逻辑待场地系统）
                if (_lastSkillTargets.Count > 0)
                    result = new List<BattleEntity>(_lastSkillTargets);
            }
            else
            {
                // 状态ID2 筛选：目标 = 所有带有该状态的敌方单位
                //（蓄力标记 ST_Amber_ChargeTarget 是【位置状态】，挂在敌方位置上——除了实体状态，
                //  还要查位置状态：带该状态的位置上的敌人也算目标；空位不算）
                var filtered = new List<BattleEntity>();
                foreach (var e in bm.Enemies)
                {
                    if (e.Entity == null || !e.Entity.IsAlive) continue;
                    if (e.Entity.GetStatus(ov) != null)
                        filtered.Add(e.Entity);
                }
                int entityHit = filtered.Count;
                if (bm.Field != null)
                {
                    foreach (var slot in bm.Field.EnemySlots)
                    {
                        if (slot == null) continue;
                        bool hasStatus = false;
                        foreach (var inst in slot.StatusList)
                            if (inst != null && inst.StatusID2 == ov) { hasStatus = true; break; }
                        if (!hasStatus) continue;
                        if (slot.IsOccupied && slot.Occupant != null && slot.Occupant.IsAlive && !filtered.Contains(slot.Occupant))
                            filtered.Add(slot.Occupant);
                    }
                }
                LogManager.Log(LogCategory.Damage, $"[筛选] ov={ov} 实体命中={entityHit} 位置命中={filtered.Count - entityHit} 总计={filtered.Count} (enemySlots={(bm.Field != null ? bm.Field.EnemySlots.Count : -1)})");
                result = filtered;
            }
        }

        // 数量限制（TargetNumber 空=0 时忽略，由 TargetOverride 或默认规则决定）
        if (eff.TargetNumber > 0 && result.Count > eff.TargetNumber)
        {
            if (eff.TargetConsecutive == 1)
                result = result.GetRange(0, eff.TargetNumber);      // 连续（从前往后）
            else if (eff.TargetConsecutive == 2)
            {
                // 连续随机：从随机起点取N个（简化：随机起点）
                int start = UnityEngine.Random.Range(0, result.Count - eff.TargetNumber + 1);
                result = result.GetRange(start, eff.TargetNumber);
            }
            else
            {
                // 随机取N个不重复
                result = result.OrderBy(x => UnityEngine.Random.value).Take(eff.TargetNumber).ToList();
            }
        }
        }

        // 记录本次效果选中的目标（供下一条效果的 TargetOverride 沿用）
        _lastSkillTargets = new List<BattleEntity>(result);
        return result;
    }

    // ================================================================
    //  等级数据查询
    // ================================================================
    // 查 SkillLevel 表里 ParamID==效果ID 的行，返回其 SkillType（找不到回退 fallback）
    private int GetEffectSkillType(string effectID2, int fallback)
    {
        var dm = DataManager.Instance;
        if (dm == null) return fallback;
        foreach (var kv in dm.SkillLevelDict)
        {
            foreach (var lv in kv.Value.Values)
            {
                if (lv.ParamID == effectID2)
                    return lv.SkillType;
            }
        }
        return fallback;
    }

    /// <summary>
    /// 多段 HitData 解析（2026-08-14）：技能等级表 Hits1-Hits7 逐段解析，每段独立结算（暴击/削韧/元素量）。
    /// %引用效果为单段（倍率在 Param1，削韧/元素量取 P,E 段）。
    /// </summary>
    List<HitData> GetHitDataList(SkillEffectData eff, int skillType)
    {
        var list = new List<HitData>();
        if (eff == null) return list;

        // %引用效果（如 C1 第二支箭 %SE_HeavyAttack_Amber1.Hits1.M*0.2,10,1）：单段
        // 倍率已在 Param1 中（ResolveBaseValue 计算），等级表倍率不再乘；削韧/元素量取 Param1 的 P,E 段
        if (!string.IsNullOrEmpty(eff.Param1) && eff.Param1.StartsWith("%"))
        {
            string rest = eff.Param1.Contains(",") ? eff.Param1.Substring(eff.Param1.IndexOf(',')) : "";
            float poise = 0f, aura = 0f;
            var p = rest.Split(',');
            if (p.Length > 1) float.TryParse(p[1].Trim(), out poise);
            if (p.Length > 2) float.TryParse(p[2].Trim(), out aura);
            list.Add(new HitData(1f, poise, aura));
            return list;
        }

        if (!_levelDataBySkillType.TryGetValue(skillType, out var levelList) || levelList.Count == 0)
            return list;

        // 找到 ParamID 匹配的行（SkillLevel.ParamID = SkillEffect.SkillEffectID2）
        SkillLevelData match = null;
        foreach (var sl in levelList)
        {
            if (sl.ParamID == eff.SkillEffectID2)
            {
                match = sl;
                break;
            }
        }
        if (match == null && levelList.Count > 0)
        {
            match = levelList[0]; // fallback（待办#12：校验工具需抓此路径）
            LogManager.LogWarning(LogCategory.GetHitData, $"{eff.SkillEffectID2} 未匹配到SkillLevel行，fallback到 {match.ParamID}");
        }
        if (match == null) return list;

        int skillLevel = (skillType >= 0 && skillType < SkillLevels.Length) ? SkillLevels[skillType] : 1;
        int curLevel = Mathf.Clamp(skillLevel, 1, 15);
        SkillLevelData lv1 = levelList.Find(s => s.SkillLevel == 1);
        SkillLevelData cur = levelList.Find(s => s.SkillLevel == curLevel);
        if (cur == null) cur = lv1;

        // Hits1-7 逐段解析（空段跳过）
        for (int n = 1; n <= 7; n++)
        {
            string curStr = GetHitsField(cur, n);
            string lv1Str = GetHitsField(lv1, n);
            if (string.IsNullOrEmpty(curStr) && string.IsNullOrEmpty(lv1Str)) continue;
            list.Add(MergeHitData(curStr, lv1Str));
        }
        return list;
    }

    /// <summary>
    /// 解析 Param2 的 Splash 参数（格式：Splash(倍率; 左扩展, 右扩展)，如 Splash(0.5; 1,1)）。
    /// 溅射伤害 = 主目标实际伤害 × 倍率，向目标位置左右各扩展指定位数。
    /// </summary>
    static bool TryParseSplash(string param2, out float rate, out int left, out int right)
    {
        rate = 0f;
        left = 0;
        right = 0;
        if (string.IsNullOrEmpty(param2)) return false;

        int p = param2.IndexOf("Splash(");
        if (p < 0) return false;

        string inner = param2.Substring(p + 7);
        int close = inner.IndexOf(')');
        if (close < 0) return false;
        inner = inner.Substring(0, close);

        // 严格按配表格式：Splash(n; X,X)——倍率后分号，左右扩展内逗号（2026-08-14 统一）
        var segs = inner.Split(';');
        if (segs.Length < 2) return false;
        if (!float.TryParse(segs[0].Trim(), out rate)) return false;

        var range = segs[1].Trim().Split(',');
        if (range.Length < 2) return false;
        int.TryParse(range[0].Trim(), out left);
        int.TryParse(range[1].Trim(), out right);
        return true;
    }

    static string GetHitsField(SkillLevelData d, int idx)
    {
        if (d == null) return "";
        switch (idx)
        {
            case 1: return d.Hits1;
            case 2: return d.Hits2;
            case 3: return d.Hits3;
            case 4: return d.Hits4;
            case 5: return d.Hits5;
            case 6: return d.Hits6;
            case 7: return d.Hits7;
            default: return "";
        }
    }

    HitData GetHitData(SkillEffectData eff, int skillType)
    {
        var list = GetHitDataList(eff, skillType);
        return list.Count > 0 ? list[0] : new HitData(0, 0, 0);
    }

    HitData MergeHitData(string curStr, string lv1Str)
    {
        HitData cur = HitDataParser.Parse(curStr);
        HitData lv1 = HitDataParser.Parse(lv1Str);
        return HitDataParser.MergeWithLevel1(cur, lv1);
    }

    /// <summary>
    /// 最终攻击力 = 基础 TotalATK × (1 + 状态ATKBonus加成，按生效范围过滤)（T2/C6 攻击%生效）。
    /// </summary>
    public float GetFinalATK(string effectID2, int skillType, string element)
    {
        return Entity.TotalATK * (1f + Entity.GetStatusATKBonus(effectID2, _boundSkills[skillType], skillType, element));
    }

    float ResolveBaseValue(string param1, string effectID2, int skillType, string element)
    {
        // Param1 格式： "M*ATK" 或 "%SE_HeavyAttack_Amber1.Hits1.M*0.2"
        if (string.IsNullOrEmpty(param1)) return GetFinalATK(effectID2, skillType, element);

        // % 引用：查来源效果的倍率×系数（C1 第二支箭 / STE_Bunny3 3倍爆炸）
        if (param1.StartsWith("%"))
        {
            return ResolveReferenceBaseValue(param1, effectID2, skillType, element);
        }

        // 匹配属性后缀
        if (param1.Contains("*ATK")) return GetFinalATK(effectID2, skillType, element);
        if (param1.Contains("*DEF")) return Entity.TotalDEF;
        if (param1.Contains("*MaxHP") || param1.Contains("*TotalHP")) return Entity.TotalHP;
        if (param1.Contains("*TotalATK")) return GetFinalATK(effectID2, skillType, element);
        if (param1.Contains("*TotalDEF")) return Entity.TotalDEF;

        return GetFinalATK(effectID2, skillType, element);
    }

    /// <summary>
    /// 解析 % 引用：格式 %效果唯一ID.HitsN.M,P,E 或 %效果唯一ID.HitsN.M*系数,P,E
    /// 返回"基准属性值 × 引用倍率"（M token 的倍率会乘以 scale）。
    /// 削韧(P)/元素量(E) 的继承由调用方另行处理（当前 C1/STE_Bunny3 主要用 M）。
    /// </summary>
    float ResolveReferenceBaseValue(string expression, string effectID2, int skillType, string element)
    {
        // 例：%SE_HeavyAttack_Amber1.Hits1.M*0.2,10,1
        // 拆出 P,E 部分（逗号分隔）
        string main = expression;
        if (expression.Contains(","))
            main = expression.Substring(0, expression.IndexOf(','));

        // 处理 *scale（只影响 M）
        float scale = 1f;
        string refPart = main;
        if (main.Contains("*"))
        {
            string[] parts = main.Split('*');
            refPart = parts[0];
            if (parts.Length > 1 && float.TryParse(parts[1], out float s))
                scale = s;
        }

        // refPart = %SE_HeavyAttack_Amber1.Hits1.M
        string trimmed = refPart.TrimStart('%');
        string[] dotParts = trimmed.Split('.');
        if (dotParts.Length < 3) return Entity.TotalATK;

        string srcEffectID2 = dotParts[0];
        string hitToken = dotParts[1]; // Hits1
        string field = dotParts[2];    // M / P / E

        int hitIndex = 0;
        if (hitToken.StartsWith("Hits") && hitToken.Length > 4
            && int.TryParse(hitToken.Substring(4), out int hIdx))
            hitIndex = hIdx - 1;

        // 查来源效果的 HitData（按效果类型查对应 SkillLevel）
        var dm = DataManager.Instance;
        if (!dm.SkillEffectDict.TryGetValue(srcEffectID2, out var srcEff))
            return Entity.TotalATK;

        // 用来源效果对应的 SkillType 查等级（蓄力箭=重击1，爆发=3）
        int srcSkillType = 3;
        if (srcEff.SkillEffectID2.Contains("HeavyAttack") || srcEff.SkillEffectID2.Contains("Heavy"))
            srcSkillType = 1;

        HitData srcHit = GetHitData(srcEff, srcSkillType);

        // 基准属性：按源效果元素/类型简化取攻击力
        float baseVal = GetFinalATK(srcEffectID2, skillType, element);

        switch (field)
        {
            case "M": return baseVal * srcHit.Multiplier * scale;
            case "P": return srcHit.Poise * scale;
            case "E": return srcHit.ElementAura * scale;
            default: return baseVal * srcHit.Multiplier * scale;
        }
    }

    // ================================================================
    //  伤害公式（非反应版）
    //  配表术语 1.1: BaseDMG × (1+CritDMG) × (1+DMGBonus) × DEFRes × Res
    // ================================================================
    float CalculateDamage(float baseValue, HitData hit, SkillEffectData eff, BattleEntity target, int skillType)
    {
        // 基础伤害
        float baseDMG = baseValue * hit.Multiplier;

        // 暴击判定（配表术语：暴击时取 (1+CritDMG)，不暴击取 1）
        // 每个敌人每个 hit 单独判定；CritRate 含状态加成（按 ApplyString/ApplyDamageType/ApplyElementType 过滤）
        float critMult = 1f;
        float critRate = Mathf.Clamp(Entity.CritRate + Entity.GetStatusCritRate(eff.SkillEffectID2, _boundSkills[skillType], skillType, eff.Element), 0f, 1f);
        float critRoll = UnityEngine.Random.value;
        if (critRoll < critRate)
        {
            critMult = 1f + Entity.CritDMG;
        }
        LogManager.Log(LogCategory.Crit, $"率={critRate:P0}(基础{Entity.CritRate:P0}+状态{Entity.GetStatusCritRate(eff.SkillEffectID2, _boundSkills[skillType], skillType, eff.Element):P0}) 随机={critRoll:F4} → {(critRoll < critRate ? $"暴击 x{critMult:F2}" : "未暴击")}");

        // 增伤区（配表术语 1.8）
        float dmgBonus = 1f + Entity.DMGBonus + GetElementBonus(eff.Element);

        // DEF 承伤（配表术语 1.12，对怪物用等级公式）
        float defRes;
        if (target.Type == BattleEntity.EntityType.Enemy)
        {
            // 怪物 DEFRes = (AttackerLevel+100) / [(AttackerLevel+100)+(DefenderLevel+100)*(1-减防)*(1-无视)]
            float attacker = Entity.Level + 100f;
            float defender = (target.Level + 100f) * (1f - Entity.DEFReduction) * (1f - Entity.DEFIgnored);
            defRes = attacker / (attacker + defender);
        }
        else
        {
            // 对角色：DEFRes = 1 - TotalDEF/(TotalDEF + 5×AttackerLevel + 500)
            float effectiveDef = target.TotalDEF * (1f - Entity.DEFReduction) * (1f - Entity.DEFIgnored);
            if (effectiveDef < 0) effectiveDef = 0;
            defRes = 1f - effectiveDef / (effectiveDef + 5f * Entity.Level + 500f);
            defRes = Mathf.Clamp(defRes, 0.1f, 1f);
        }

        // RES 抗性承伤（配表术语 1.7）
        float res = GetElementResistance(target, eff.Element ?? "None");
        float resMultiplier;
        if (res < 0)
            resMultiplier = 1f - res / 2f;
        else if (res < 0.75f)
            resMultiplier = 1f - res;
        else
            resMultiplier = 1f / (1f + 4f * res);

        return baseDMG * critMult * dmgBonus * defRes * resMultiplier;
    }

    float GetElementBonus(string element)
    {
        switch (element)
        {
            case "Pyro": return Entity.PyroDmgBonus;
            case "Hydro": return Entity.HydroDmgBonus;
            case "Electro": return Entity.ElectroDmgBonus;
            case "Cryo": return Entity.CryoDmgBonus;
            case "Anemo": return Entity.AnemoDmgBonus;
            case "Dendro": return Entity.DendroDmgBonus;
            case "Geo": return Entity.GeoDmgBonus;
            default: return Entity.PhysicalDmgBonus;
        }
    }

    float GetElementResistance(BattleEntity target, string element)
    {
        switch (element)
        {
            case "Pyro": return target.PyroRes + target.ResBonus;
            case "Hydro": return target.HydroRes + target.ResBonus;
            case "Electro": return target.ElectroRes + target.ResBonus;
            case "Cryo": return target.CryoRes + target.ResBonus;
            case "Anemo": return target.AnemoRes + target.ResBonus;
            case "Dendro": return target.DendroRes + target.ResBonus;
            case "Geo": return target.GeoRes + target.ResBonus;
            default: return target.PhysicalRes + target.ResBonus;
        }
    }

    // ================================================================
    //  状态行动执行入口（BattleManager 在 OnTrigger/OnEnd 时调用）
    //  把状态行动的参数（效果ID列表）转成效果执行
    // ================================================================
    /// <summary>
    /// 状态行动触发限制（2026-08-14，Status_Action 新列）：
    /// MaxTimePerTurn（每回合最大次数）/ MaxTimePerLife（全场最大次数）/ Cooldown（行动冷却回合）。
    /// 返回 true = 已达上限/冷却中，不应触发。
    /// </summary>
    bool StatusActionLimitReached(StatusInstance inst, StatusActionData act)
    {
        if (inst == null || act == null) return false;
        if (act.MaxTimePerTurn <= 0 && act.MaxTimePerLife <= 0 && act.Cooldown <= 0) return false;
        var bm = BattleManager.Instance;
        int turn = bm != null ? bm.TurnCount : 0;
        // 懒重置：新回合时清零本回合计数
        if (inst.LastActionTurn != turn)
        {
            inst.LastActionTurn = turn;
            inst.TurnTriggerCount = 0;
        }
        if (act.MaxTimePerTurn > 0 && inst.TurnTriggerCount >= act.MaxTimePerTurn)
            return true;
        if (act.MaxTimePerLife > 0 && inst.LifeTriggerCount >= act.MaxTimePerLife)
            return true;
        if (act.Cooldown > 0 && inst.LastTriggerTurn >= 0 && turn - inst.LastTriggerTurn < act.Cooldown)
            return true;
        return false;
    }

    /// <summary>状态行动触发后计数（MaxTimePerTurn/MaxTimePerLife/Cooldown）。</summary>
    void CountStatusActionTrigger(StatusInstance inst, StatusActionData act)
    {
        if (inst == null || act == null) return;
        if (act.MaxTimePerTurn <= 0 && act.MaxTimePerLife <= 0 && act.Cooldown <= 0) return;
        var bm = BattleManager.Instance;
        int turn = bm != null ? bm.TurnCount : 0;
        if (inst.LastActionTurn != turn)
        {
            inst.LastActionTurn = turn;
            inst.TurnTriggerCount = 0;
        }
        if (act.MaxTimePerTurn > 0) inst.TurnTriggerCount++;
        if (act.MaxTimePerLife > 0) inst.LifeTriggerCount++;
        if (act.Cooldown > 0) inst.LastTriggerTurn = turn;
    }

    public void ExecuteStatusActions(StatusInstance inst, List<StatusActionData> actions, object host, string actionType)
    {
        foreach (var act in actions)
        {
            if (act.ActionType != actionType) continue;
            // 钩子驱动行（ScriptHook=PreAlliesDamage）：不参与普通触发（阶段/回合等），由钩子机制单独触发（2026-08-14）
            if (act.ScriptHook == "PreAlliesDamage") continue;
            // ScriptHook：只有该特殊处理器返回真值时才触发该行行动（统一求值入口 ScriptHookEvaluator）
            if (!ScriptHookEvaluator.Evaluate(act.ScriptHook, inst != null ? inst.Caster : null)) continue;
            // 触发限制：每回合/全场次数上限、行动冷却（2026-08-14）
            if (StatusActionLimitReached(inst, act)) continue;
            CountStatusActionTrigger(inst, act);
            ExecuteStatusActionEffects(act, host);
        }
    }

    /// <summary>
    /// 状态行动协程版（2026-08-13，战斗流程协程链使用）：
    /// Display==1（局内显示图标）的行动先 yield 0.5s 再执行——【整个战斗流程暂停等待】，
    /// 演出完成才继续后续结算（如蓄力射击 OnEnd 等待期间，同批到期状态的移除也被暂停，标记不会提前消失）。
    /// OnTrigger/OnItsTurn/OnEnd 由 BattleManager 状态机协程调用；OnApply/OnHit 用同步版（技能执行链内无法暂停）。
    /// </summary>
    public System.Collections.IEnumerator ExecuteStatusActionsAsync(StatusInstance inst, List<StatusActionData> actions, object host, string actionType)
    {
        foreach (var act in actions)
        {
            if (act.ActionType != actionType) continue;
            // 钩子驱动行（ScriptHook=PreAlliesDamage）：不参与普通触发，由钩子机制单独触发（2026-08-14）
            if (act.ScriptHook == "PreAlliesDamage") continue;
            if (!ScriptHookEvaluator.Evaluate(act.ScriptHook, inst != null ? inst.Caster : null)) continue;
            // 触发限制：每回合/全场次数上限、行动冷却（2026-08-14）
            if (StatusActionLimitReached(inst, act)) continue;
            CountStatusActionTrigger(inst, act);
            if (inst != null && inst.MainData != null && inst.MainData.Display == 1)
            {
                // OnEnd延迟=流程暂停（expired循环协程等待），标记不会被提前移除，无需预解析快照（2026-08-14）
                yield return new WaitForSeconds(0.5f);
            }
            ExecuteStatusActionEffects(act, host);
        }
    }

    /// <summary>
    /// 预解析状态行动的效果目标并快照（结果先记着）：
    /// 遍历行动的效果（Param1 逗号分隔），对 Damage 类效果调用 ResolveTargets（此刻目标/标记还在），
    /// 结果存入快照表；延迟结束后 ExecuteDamage 直接用快照，不重新解析。
    /// </summary>
    void PreResolveStatusActionTargets(StatusActionData act, object host)
    {
        if (string.IsNullOrEmpty(act.Param1)) return;
        var dm = DataManager.Instance;
        if (dm == null) return;
        foreach (var tid in act.Param1.Split(','))
        {
            var t = tid.Trim();
            if (t.StartsWith("SK_"))
            {
                if (!_skillEffects.TryGetValue(t, out var effects)) continue;
                foreach (var eff in effects)
                    PreResolveStatusEffectTarget(eff);
            }
            else
            {
                if (dm.SkillEffectDict.TryGetValue(t, out var eff))
                    PreResolveStatusEffectTarget(eff);
            }
        }
    }

    void PreResolveStatusEffectTarget(SkillEffectData eff)
    {
        if (eff == null || string.IsNullOrEmpty(eff.SkillEffectID2) || eff.EffectType != "Damage") return;
        var targets = ResolveTargets(eff);
        _statusTargetSnapshot[eff.SkillEffectID2] = new List<BattleEntity>(targets);
    }

    /// <summary>有快照用快照（用后即清），否则正常解析。延迟执行的效果用触发时的目标，不受延迟期间状态变化影响。</summary>
    List<BattleEntity> TakeStatusTargetSnapshot(SkillEffectData eff)
    {
        if (eff != null && !string.IsNullOrEmpty(eff.SkillEffectID2) && _statusTargetSnapshot.TryGetValue(eff.SkillEffectID2, out var snap))
        {
            _statusTargetSnapshot.Remove(eff.SkillEffectID2);
            return new List<BattleEntity>(snap);
        }
        return ResolveTargets(eff);
    }

    /// <summary>状态施加时立刻生效（OnApply）：由状态施放者控制器调用（实体/位置通用）。</summary>
    public void OnStatusApplied(StatusInstance inst, object host)
    {
        if (inst == null) return;
        var dm = DataManager.Instance;
        if (dm == null || !dm.StatusActionDict.TryGetValue(inst.StatusID2, out var actions)) return;
        ExecuteStatusActions(inst, actions, host, "OnApply");
    }

    /// <summary>
    /// OnHit：拥有该状态的敌方目标或场地位置受击后生效（术语）。
    /// 命中实体时检查实体身上的状态 + 实体所在位置的场地状态（如兔兔伯爵）。
    /// HitSource：只有被指定效果或技能命中后才触发；ScriptHook 判定通过才执行。
    /// </summary>
    void TriggerOnHit(BattleEntity target, SkillEffectData eff)
    {
        var dm = DataManager.Instance;
        if (dm == null) return;

        foreach (var inst in target.GetStatusList())
            CheckOnHitStatus(inst, eff, target, target);
        if (target.Position != null)
        {
            //位置状态：行动上下文传【位置】（兔兔伯爵爆炸以兔子所在位置为中心计算范围）
            //复制列表再遍历：爆炸链会移除位置状态（STE_Bunny5），枚举中修改会抛异常
            foreach (var inst in new List<StatusInstance>(target.Position.StatusList))
                CheckOnHitStatus(inst, eff, target, target.Position);
        }
    }

    void CheckOnHitStatus(StatusInstance inst, SkillEffectData eff, BattleEntity target, object host)
    {
        var dm = DataManager.Instance;
        if (dm == null || !dm.StatusActionDict.TryGetValue(inst.StatusID2, out var actions)) return;
        foreach (var act in actions)
        {
            if (act.ActionType != "OnHit") continue;
            // HitSource：只有被指定效果或技能命中后才触发此次OnHit对应行动
            if (!string.IsNullOrEmpty(act.HitSource) && act.HitSource != eff.SkillEffectID2) continue;
            if (!ScriptHookEvaluator.Evaluate(act.ScriptHook, inst.Caster)) continue;
            // 触发限制：每回合/全场次数上限、行动冷却（2026-08-14，凯亚C4护盾 Cooldown=15）
            if (StatusActionLimitReached(inst, act)) continue;
            CountStatusActionTrigger(inst, act);
            ExecuteStatusActionEffects(act, host);
            LogManager.Log(LogCategory.Status, $"OnHit {inst.StatusID2} by {eff.SkillEffectID2} on {target.EntityID}");
        }
    }

    /// <summary>
    /// PreAlliesDamage 钩子触发行执行（2026-08-14，PreDamageHookSystem.TriggerPreDamageHooks 调用）：
    /// 执行该行的 Param1 效果（如凯亚冰棱 SE_Kaeya_BurstTrigger），先于主动行为实行；
    /// 触发前检查次数限制（MaxTimePerTurn 等），触发后计数。
    /// </summary>
    public void ExecutePreDamageHookLine(StatusActionData act, BattleEntity caster)
    {
        if (act == null) return;
        var inst = caster != null ? caster.GetStatus(act.StatusID2) : null;
        if (StatusActionLimitReached(inst, act)) return;
        CountStatusActionTrigger(inst, act);
        ExecuteStatusActionEffects(act, caster);
    }

    void ExecuteStatusActionEffects(StatusActionData act, object host)
    {
        if (string.IsNullOrEmpty(act.Param1)) return;
        var dm = DataManager.Instance;

        // Param1 是效果ID2列表（逗号分隔），如 "STE_Bunny2,STE_Bunny4"
        // 特殊：以 SK_ 开头的是技能ID（如蓄力箭 SK_HeavyAttack_Amber），执行技能效果序列
        string[] ids = act.Param1.Split(',');
        foreach (var id in ids)
        {
            string tid = id.Trim();
            if (string.IsNullOrEmpty(tid)) continue;

            if (tid.StartsWith("SK_"))
            {
                // 技能ID：执行该技能的全部效果（蓄力箭等）
                if (_skillEffects.TryGetValue(tid, out var skillEffects))
                {
                    foreach (var subEff in skillEffects)
                    {
                        ExecuteEffect(subEff, GetEffectSkillType(subEff.SkillEffectID2, 1));
                    }
                    LogManager.Log(LogCategory.StatusAction, $"ExecuteSkill {tid} via status");
                }
                else
                {
                    LogManager.LogWarning(LogCategory.StatusAction, $"技能不存在: {tid}");
                }
                continue;
            }

            if (!dm.StatusEffectDict.TryGetValue(tid, out var statusEff))
            {
                // SE_ 开头 = 技能效果（如凯亚冰棱 SE_Kaeya_BurstTrigger），2026-08-14 新增支持
                if (dm.SkillEffectDict.TryGetValue(tid, out var skillEff))
                {
                    ExecuteEffect(skillEff, GetEffectSkillType(tid, 1));
                }
                else
                {
                    LogManager.LogWarning(LogCategory.StatusAction, $"状态效果不存在: {tid}");
                }
                continue;
            }
            ExecuteStatusEffect(statusEff, host);
        }
    }

    void ExecuteStatusEffect(StatusEffectData statusEff, object host)
    {
        switch (statusEff.EffectType)
        {
            case "Damage":
            {
                // 状态伤害（兔兔伯爵爆炸 STE_Bunny2 等）：从 SkillType=2 查等级，多段 Hits1-7 逐段结算
                var hits = GetHitDataFromStatusEffectList(statusEff);
                if (hits.Count == 0 || (hits.Count == 1 && hits[0].Multiplier <= 0)) return;
                int stSkillType = GetEffectSkillType(statusEff.StatusEffectID2, 2);
                float baseValue = ResolveBaseValue(statusEff.Param1, statusEff.StatusEffectID2, stSkillType, statusEff.Element);
                var targets = ResolveStatusTargets(statusEff, host);
                if (targets == null) return;

                foreach (var target in targets)
                {
                    foreach (var hit in hits)
                    {
                        if (!target.IsAlive) break;
                        float damage = CalculateStatusDamage(baseValue, hit, statusEff, target, stSkillType);
                        // 元素反应（2026-08-14）：状态伤害同样参与反应判定；
                        // 2026-08-15：按元素量模型消耗，攻击元素残留量决定是否上附着
                        float reactedDamage = ElementReactionManager.TryReaction(target, statusEff.Element, hit.ElementAura, damage, out string reactionName, out float attackRemain);
                        if (reactionName.Length > 0)
                            LogManager.Log(LogCategory.StatusDamage, $"{statusEff.StatusEffectID2} -> {target.EntityID} 触发{reactionName}");
                        float final = target.AbsorbDamageWithShield(reactedDamage, statusEff.Element);
                        target.TakeDamage(final);
                        if (!string.IsNullOrEmpty(statusEff.Element) && statusEff.Element != "None" && attackRemain > 0f)
                            target.ApplyAura(statusEff.Element, attackRemain, Entity.EntityID);
                        target.Poise -= hit.Poise;
                        LogManager.Log(LogCategory.StatusDamage, $"{statusEff.StatusEffectID2} -> {target.EntityID} : {final:F1}");
                    }
                }
                break;
            }

            case "GainEnergy":
            {
                if (!float.TryParse(statusEff.Param1, out float gained)) return;
                var targets = ResolveStatusTargets(statusEff, host);
                if (targets == null) return;
                foreach (var target in targets)
                {
                    float total = gained;
                    if (statusEff.EnergyGainMode == "Based")
                    {
                        float elementStatus = (target.Type == BattleEntity.EntityType.Character
                            && target.CharacterCtrl != null && target.CharacterCtrl._attrData != null
                            && target.CharacterCtrl._attrData.Element == statusEff.Element) ? 1.5f : 0.5f;
                        float recharge = target.TotalRechargeRate > 0 ? target.TotalRechargeRate : 1f;
                        total = gained * 1f * elementStatus * recharge;
                    }
                    target.CurrentEnergy = Mathf.Min(target.CurrentEnergy + Mathf.RoundToInt(total), target.MaxEnergy);
                    LogManager.Log(LogCategory.StatusEnergy, $"{target.EntityID} +{total:F1}");
                }
                break;
            }

            case "RemoveStatus":
            {
                //位置状态移除（兔兔伯爵爆炸 STE_Bunny5：从状态所在位置移除）
                if (host is FieldPosition slot)
                {
                    RemoveStatusFromField(statusEff.Param1, slot.Side);
                    break;
                }
                var targets = ResolveStatusTargets(statusEff, host);
                if (targets == null) return;
                foreach (var target in targets)
                    target.RemoveStatus(statusEff.Param1, -1);
                break;
            }

            case "ExecuteEffect":
            {
                // 状态触发执行指定技能效果（箭雨 STE_Amber_ArrowRain -> SE_Burst_Amber1）
                if (string.IsNullOrEmpty(statusEff.Param1))
                {
                    LogManager.Log(LogCategory.StatusEffect, $"{statusEff.StatusEffectID2} ExecuteEffect 缺 Param1");
                    break;
                }
                if (!_effectById.TryGetValue(statusEff.Param1, out var targetEff))
                {
                    LogManager.LogWarning(LogCategory.StatusEffect, $"{statusEff.StatusEffectID2} ExecuteEffect 找不到技能效果 {statusEff.Param1}");
                    break;
                }
                // 目标：以状态所在位置为基准（箭雨状态挂在爆发选中的 EnemyField 位置上）
                List<int> savedForced = new List<int>(ForcedTargetPositions);
                ForcedTargetPositions.Clear();
                int statusHostPos = GetHostSlotPosition(host);
                if (statusHostPos >= 1) ForcedTargetPositions.Add(statusHostPos);

                if (targetEff != null)
                {
                    ExecuteEffect(targetEff, GetEffectSkillType(targetEff.SkillEffectID2, 3));
                }

                ForcedTargetPositions.Clear();
                ForcedTargetPositions.AddRange(savedForced); // 恢复外层强制位置
                LogManager.Log(LogCategory.StatusAction, $"ExecuteEffect {statusEff.Param1} via status (pos={statusHostPos})");
                break;
            }

            case "BindStatus":
            {
                // BindStatus 是被动效果（2026-08-06）：
                // 术语表原文"只要该效果来源的状态存在，其目标就一直存在子状态"，
                // 绑定动作在 BattleEntity.AutoBindStatus（状态施加成功时）已按位置系统完成，
                // 这里只做确认日志，避免走到 default 报"未支持类型"。
                LogManager.Log(LogCategory.StatusEffect, $"{statusEff.StatusEffectID2} BindStatus -> {statusEff.Param1} (已由施加时自动绑定)");
                break;
            }

            case "AddTurn":
            {
                // 延长状态回合（凯亚 C2 击杀延长：Param1 逗号分隔多个状态ID，存在的都 +Param2）
                ExecuteAddTurn(statusEff, host);
                break;
            }

            case "Heal":
            {
                // 治疗（2026-08-14）：Param1=治疗量公式，目标=TargetType解析
                var healTargets = ResolveStatusTargets(statusEff, host);
                if (healTargets == null) return;
                float healAmt = ResolveBaseValue(statusEff.Param1, statusEff.StatusEffectID2, GetEffectSkillType(statusEff.StatusEffectID2, 2), statusEff.Element);
                foreach (var t in healTargets)
                {
                    if (t == null || !t.IsAlive) continue;
                    t.Heal(healAmt);
                    LogManager.Log(LogCategory.Effect, $"治疗 {t.EntityID} +{healAmt:F1} (HP {t.CurrentHP:F1})");
                }
                break;
            }

            case "Shield":
            {
                // 护盾（2026-08-14）：Param1=护盾量公式（如 0.3*TotalHP），Element=护盾元素，Duration=持续回合
                var shieldTargets = ResolveStatusTargets(statusEff, host);
                if (shieldTargets == null) return;
                float shieldAmt = ResolveBaseValue(statusEff.Param1, statusEff.StatusEffectID2, GetEffectSkillType(statusEff.StatusEffectID2, 2), statusEff.Element);
                float shieldDur = statusEff.Duration > 0 ? statusEff.Duration : 999;
                foreach (var t in shieldTargets)
                {
                    if (t == null || !t.IsAlive) continue;
                    t.AddShield(shieldAmt, statusEff.Element, shieldDur);
                    LogManager.Log(LogCategory.Effect, $"护盾 {t.EntityID} +{shieldAmt:F1} (元素 {statusEff.Element})");
                }
                break;
            }

            default:
                if (statusEff.EffectType == "Buff" || statusEff.EffectType == "Debuff")
                {
                    // Buff/Debuff 为被动数值加成：属性聚合（GetStatusATKBonus/CritRate）直接读配表，无需执行
                    break;
                }
                LogManager.Log(LogCategory.StatusEffect, $"未支持类型 {statusEff.EffectType}");
                break;
        }
    }

    /// <summary>
    /// AddTurn：延长目标状态回合（2026-08-14）。
    /// Param1 = 状态ID2（逗号分隔多个，存在的都 +N），Param2 = 增加回合数（空默认 +1）。
    /// host 为实体时查实体状态；为位置时查位置状态；找不到对应状态则静默跳过。
    /// </summary>
    void ExecuteAddTurn(StatusEffectData statusEff, object host)
    {
        if (host == null || statusEff == null || string.IsNullOrEmpty(statusEff.Param1)) return;
        int add = 1;
        if (!string.IsNullOrEmpty(statusEff.Param2) && !int.TryParse(statusEff.Param2.Trim(), out add))
            add = 1;

        foreach (var sid in statusEff.Param1.Split(','))
        {
            string tid = sid.Trim();
            if (tid.Length == 0) continue;

            if (host is BattleEntity entity)
            {
                var inst = entity.GetStatus(tid);
                if (inst == null) continue; // 找不到静默跳过（C6状态下 ST_Burst_Kaeya 不存在）
                inst.RemainingPhaseCount += add;
                LogManager.Log(LogCategory.AddTurn, $"{tid} +{add}t -> {inst.RemainingPhaseCount}");
            }
            else if (host is FieldPosition slot)
            {
                foreach (var inst in slot.StatusList)
                {
                    if (inst.StatusID2 == tid)
                    {
                        inst.RemainingPhaseCount += add;
                        LogManager.Log(LogCategory.AddTurn, $"{tid} +{add}t -> {inst.RemainingPhaseCount} ({slot})");
                        break;
                    }
                }
            }
        }
    }

    // 获取状态宿主的位置编号（host 可能是 FieldPosition 或 BattleEntity）
    int GetHostSlotPosition(object host)
    {
        if (host is FieldPosition slot) return slot.SlotIndex;
        if (host is BattleEntity entity) return entity.SlotPosition;
        return -1;
    }

    // 状态效果目标：默认以状态所在实体/位置为原点（host 可能是 BattleEntity 或 FieldPosition）
    // 位置状态：目标 = 该位置上的单位（按 TargetOverride 扩展，重叠伤害叠加）
    List<BattleEntity> ResolveStatusTargets(StatusEffectData statusEff, object host)
    {
        var bm = BattleManager.Instance;
        var result = new List<BattleEntity>();

        if (host is FieldPosition slot)
        {
            // ===== 状态挂在位置上 =====
            // TargetOverride：以状态所在位置为原点扩展
            //  "0,0" = 状态所在位置
            //  "X,X" = 左右各扩展X位
            if (!string.IsNullOrEmpty(statusEff.TargetOverride))
            {
                var positions = ExpandPositions(slot.SlotIndex, statusEff.TargetOverride, slot.Side);
                foreach (var pos in positions)
                {
                    var e = bm.GetEntityByPosition(slot.Side, pos);
                    if (e != null && e.IsAlive) result.Add(e);
                }
                return result;
            }

            // TargetSelect "X,X"：以状态所在位置为中心左右扩展（兔兔伯爵爆炸打相邻位置）
            if (!string.IsNullOrEmpty(statusEff.TargetSelect))
            {
                var positions = ExpandPositions(slot.SlotIndex, statusEff.TargetSelect, slot.Side);
                foreach (var pos in positions)
                {
                    var e = bm.GetEntityByPosition(slot.Side, pos);
                    if (e != null && e.IsAlive) result.Add(e);
                }
                return result;
            }

            // 无 override/TargetSelect：按 TargetType 解析（群体类型走全体，位置类型默认=所在位置单位）
            switch (statusEff.TargetType)
            {
                case "Allies":
                case "AlliesOnly":
                    foreach (var a in bm.Allies)
                        if (a.Entity != null && a.Entity.IsAlive)
                            result.Add(a.Entity);
                    return result;
                case "Enemy":
                    foreach (var e in bm.Enemies)
                        if (e.Entity != null && e.Entity.IsAlive)
                            result.Add(e.Entity);
                    return result;
                default:
                    var oe = bm.GetEntityByPosition(slot.Side, slot.SlotIndex);
                    if (oe != null && oe.IsAlive) result.Add(oe);
                    return result;
            }
        }

        if (host is BattleEntity entity)
        {
            // ===== 状态挂在实体上 =====
            if (!string.IsNullOrEmpty(statusEff.TargetOverride))
            {
                if (entity != null)
                {
                    var e = bm.GetEntityByPosition(BattleSide.Enemy, entity.SlotPosition);
                    if (e != null && e.IsAlive) result.Add(e);
                }
                if (result.Count == 0 && entity != null) result.Add(entity);
                return result;
            }

            switch (statusEff.TargetType)
            {
                case "Enemy":
                    foreach (var e in bm.Enemies)
                        if (e.Entity != null && e.Entity.IsAlive)
                            result.Add(e.Entity);
                    break;
                case "Allies":
                case "AlliesOnly":
                    foreach (var a in bm.Allies)
                        if (a.Entity != null && a.Entity.IsAlive)
                            result.Add(a.Entity);
                    break;
                default:
                    if (entity != null && entity.IsAlive)
                        result.Add(entity);
                    break;
            }
            if (result.Count == 0 && entity != null) result.Add(entity);
            return result;
        }

        return result;
    }

    // 解析 TargetOverride "X,X"：以原点为中心左右扩展，返回位置列表（含原点）
    // "0,0" = 只取原点位置
    List<int> ExpandPositions(int origin, string overrideStr, BattleSide side)
    {
        var result = new List<int> { origin };
        if (string.IsNullOrEmpty(overrideStr)) return result;

        var parts = overrideStr.Split(',');
        if (parts.Length < 2) return result;
        int left = int.Parse(parts[0].Trim());
        int right = int.Parse(parts[1].Trim());
        if (left == 0 && right == 0) return result;

        int max = side == BattleSide.Ally ? BattleField.ALLY_SLOTS : BattleField.ENEMY_SLOTS;
        for (int i = 1; i <= left; i++)
        {
            int pos = origin - i;
            if (pos >= 1) result.Add(pos);
        }
        for (int i = 1; i <= right; i++)
        {
            int pos = origin + i;
            if (pos <= max) result.Add(pos);
        }
        result.Sort();
        return result;
    }

    /// <summary>
    /// 状态伤害多段 HitData 解析（2026-08-14）：Hits1-7 逐段，空段跳过；%引用单段。
    /// </summary>
    List<HitData> GetHitDataFromStatusEffectList(StatusEffectData statusEff)
    {
        var list = new List<HitData>();
        if (statusEff == null) return list;

        // %引用（状态伤害如兔兔伯爵爆炸 STE_Bunny3）：单段，倍率在 Param1
        if (!string.IsNullOrEmpty(statusEff.Param1) && statusEff.Param1.StartsWith("%"))
        {
            string rest = statusEff.Param1.Contains(",") ? statusEff.Param1.Substring(statusEff.Param1.IndexOf(',')) : "";
            float poise = 0f, aura = 0f;
            var p = rest.Split(',');
            if (p.Length > 1) float.TryParse(p[1].Trim(), out poise);
            if (p.Length > 2) float.TryParse(p[2].Trim(), out aura);
            list.Add(new HitData(1f, poise, aura));
            return list;
        }

        // 状态伤害的等级表：查该效果在 SkillLevel 表里对应的 SkillType（兔兔伯爵爆炸=战技）
        int effSkillType = GetEffectSkillType(statusEff.StatusEffectID2, 2);
        if (!_levelDataBySkillType.TryGetValue(effSkillType, out var levelList) || levelList.Count == 0)
            return list;

        SkillLevelData match = null;
        foreach (var sl in levelList)
        {
            if (sl.ParamID == statusEff.StatusEffectID2)
            {
                match = sl;
                break;
            }
        }
        if (match == null && levelList.Count > 0)
            match = levelList[0]; // fallback

        if (match == null) return list;

        SkillLevelData lv1 = levelList.Find(s => s.SkillLevel == 1);
        int curLevel = Mathf.Clamp(Level, 1, 15);
        SkillLevelData cur = levelList.Find(s => s.SkillLevel == curLevel);
        if (cur == null) cur = lv1;

        for (int n = 1; n <= 7; n++)
        {
            string curStr = GetHitsField(cur, n);
            string lv1Str = GetHitsField(lv1, n);
            if (string.IsNullOrEmpty(curStr) && string.IsNullOrEmpty(lv1Str)) continue;
            list.Add(MergeHitData(curStr, lv1Str));
        }
        return list;
    }

    HitData GetHitDataFromStatusEffect(StatusEffectData statusEff)
    {
        var list = GetHitDataFromStatusEffectList(statusEff);
        return list.Count > 0 ? list[0] : new HitData(0, 0, 0);
    }

    float CalculateStatusDamage(float baseValue, HitData hit, StatusEffectData eff, BattleEntity target, int skillType)
    {
        float baseDMG = baseValue * hit.Multiplier;

        float critMult = 1f;
        float critRate = Mathf.Clamp(Entity.CritRate + Entity.GetStatusCritRate(eff.StatusEffectID2, "", skillType, eff.Element), 0f, 1f);
        float critRoll = UnityEngine.Random.value;
        if (critRoll < critRate)
            critMult = 1f + Entity.CritDMG;
        LogManager.Log(LogCategory.Crit, $"率={critRate:P0}(基础{Entity.CritRate:P0}+状态{Entity.GetStatusCritRate(eff.StatusEffectID2, "", skillType, eff.Element):P0}) 随机={critRoll:F4} → {(critRoll < critRate ? $"暴击 x{critMult:F2}" : "未暴击")}");

        float dmgBonus = 1f + Entity.DMGBonus + GetElementBonus(eff.Element);

        float defRes;
        if (target.Type == BattleEntity.EntityType.Enemy)
        {
            float attacker = Entity.Level + 100f;
            float defender = (target.Level + 100f) * (1f - Entity.DEFReduction) * (1f - Entity.DEFIgnored);
            defRes = attacker / (attacker + defender);
        }
        else
        {
            float effectiveDef = target.TotalDEF * (1f - Entity.DEFReduction) * (1f - Entity.DEFIgnored);
            if (effectiveDef < 0) effectiveDef = 0;
            defRes = 1f - effectiveDef / (effectiveDef + 5f * Entity.Level + 500f);
            defRes = Mathf.Clamp(defRes, 0.1f, 1f);
        }

        float res = GetElementResistance(target, eff.Element ?? "None");
        float resMultiplier;
        if (res < 0)
            resMultiplier = 1f - res / 2f;
        else if (res < 0.75f)
            resMultiplier = 1f - res;
        else
            resMultiplier = 1f / (1f + 4f * res);

        return baseDMG * critMult * dmgBonus * defRes * resMultiplier;
    }
}

/// <summary>
/// ChangeControl 记录：
/// 状态效果表挂载（IsSkillEffect=false）：状态施加时创建，状态消失时解除（SourceStatusID2 匹配）。
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
    public bool IsSkillEffect;          // true=技能效果表挂载（OnTurnStart 计时）
    public bool IsPermanent;            // 技能效果版且未填 Duration：永久（不解除）
    public int RemainingTurns;          // 技能效果版：剩余回合（由效果 Duration 决定）
}

