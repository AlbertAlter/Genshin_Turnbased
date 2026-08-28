using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class CharacterBattleController
{
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

    public void OnTurnStart()
    {
        //AP重置移到 BattleManager 全局（我方每回合共用100，2026-08-14）
        PoiseSystem.RestoreAtTurnStart(Entity);

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
        if (!IsActive || Entity == null || Entity.IsDead) return false;
        if (PoiseSystem.IsKnockedDown(Entity)) return false;
        if (FrozenReactionHandler.IsFrozen(Entity)) return false;
        if (IsButtonFrozen(0)) return false;
        string nid0 = _boundSkills[0];
        if (string.IsNullOrEmpty(nid0)) return false;
        if (_skillById2 == null || !_skillById2.TryGetValue(nid0, out var sk)) return false;
        if (APManager == null || !APManager.CanAfford(sk.APCost)) return false;
        if (_cooldownRemaining != null && _cooldownRemaining.TryGetValue(nid0, out int cd) && cd > 0) return false;
        return true;
    }

    public bool CanUseHeavyAttack()
    {
        if (!IsActive || Entity == null || Entity.IsDead) return false;
        if (PoiseSystem.IsKnockedDown(Entity)) return false;
        if (FrozenReactionHandler.IsFrozen(Entity)) return false;
        if (IsButtonFrozen(1)) return false;
        string hid1 = _boundSkills[1];
        if (string.IsNullOrEmpty(hid1)) return false;
        if (_skillById2 == null || !_skillById2.TryGetValue(hid1, out var sk)) return false;
        if (APManager == null || !APManager.CanAfford(sk.APCost)) return false;
        if (_cooldownRemaining != null && _cooldownRemaining.TryGetValue(hid1, out int cd) && cd > 0) return false;
        return true;
    }

    public bool CanUseSkill()
    {
        if (!IsActive || Entity == null || Entity.IsDead) return false;
        if (PoiseSystem.IsKnockedDown(Entity)) return false;
        if (FrozenReactionHandler.IsFrozen(Entity)) return false;
        if (IsButtonFrozen(2)) return false;
        string sid2 = _boundSkills[2];
        if (string.IsNullOrEmpty(sid2)) return false;
        if (_skillById2 == null || !_skillById2.TryGetValue(sid2, out var sk)) return false;
        if (APManager == null || !APManager.CanAfford(sk.APCost)) return false;
        if (_cooldownRemaining != null && _cooldownRemaining.TryGetValue(sid2, out int cd) && cd > 0) return false;
        if (sk.MaxCharge > 0 && (_currentCharges == null || !_currentCharges.TryGetValue(sid2, out int charges) || charges <= 0)) return false;
        return true;
    }

    public bool CanUseBurst()
    {
        if (Entity == null || Entity.IsDead) return false;
        if (PoiseSystem.IsKnockedDown(Entity)) return false;
        if (FrozenReactionHandler.IsFrozen(Entity)) return false;
        if (IsButtonFrozen(3)) return false;
        string bid3 = _boundSkills[3];
        if (string.IsNullOrEmpty(bid3)) return false;
        if (_skillById2 == null || !_skillById2.TryGetValue(bid3, out var sk)) return false;
        if (Entity.CurrentEnergy < Entity.MaxEnergy) return false;
        if (_cooldownRemaining != null && _cooldownRemaining.TryGetValue(bid3, out int cd) && cd > 0) return false;
        return true;
    }

    public bool CanSwitch()
    {
        return IsActive
            && Entity != null
            && Entity.IsAlive;
    }

    // ================================================================
    //  不可用原因查询（UI/日志用）
    //  返回 null = 可用；否则返回具体原因字符串
    //  skillType: 0普攻 1重击 2战技 3爆发
    // ================================================================
    public string GetBlockReason(int skillType)
    {
        if (Entity == null)
            return "角色实体不存在";
        if (Entity.IsDead)
            return "角色已经死亡";
        if (PoiseSystem.IsKnockedDown(Entity))
            return "角色处于倒地状态，需要先花费10 AP起身";
        if (FrozenReactionHandler.IsFrozen(Entity))
            return "角色处于冻结状态，无法行动";

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
            if (APManager == null)
                return "战斗行动点系统未初始化";
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
}
