using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class CharacterBattleController
{
    // ================================================================
    //  执行（按键接口）
    // ================================================================
    public void ExecuteNormalAttack() { ExecuteSkillByID(_boundSkills[0], 0, true); }
    public void ExecuteHeavyAttack() { ExecuteSkillByID(_boundSkills[1], 1, true); }
    public void ExecuteSkill() { ExecuteSkillByID(_boundSkills[2], 2, true); }
    public void ExecuteBurst() { ExecuteSkillByID(_boundSkills[3], 3, true); }

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
        _currentExecutingSkillID2 = skillID2;
        if (!PrepareSkillExecution(skillID2, skillType, sk, true, out var effects, out _))
        {
            _currentExecutingSkillID2 = null;
            return true; // AP/能量不足等：视为完成，避免卡在选择态
        }

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
        _currentExecutingSkillID2 = null;
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
        _currentExecutingSkillID2 = null;
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

    public void ExecuteSkillByID(string skillID2, int skillType, bool isPlayerInitiated = false)
    {
        if (string.IsNullOrEmpty(skillID2)) return;
        if (!_skillById2.TryGetValue(skillID2, out var sk)) return;

        string previousSkillID2 = _currentExecutingSkillID2;
        bool previousSkillWasActiveAction = _currentExecutingSkillIsActiveAction;
        bool isolateFromCaller = !isPlayerInitiated;
        List<int> previousForcedPositions = isolateFromCaller
            ? new List<int>(ForcedTargetPositions)
            : null;
        TargetResolutionResult previousTargetResult = isolateFromCaller
            ? _lastSkillTargetResult
            : null;
        List<int> previousTargetPositions = isolateFromCaller
            ? new List<int>(_lastTargetPositions)
            : null;
        string previousLastHitEffectID2 = isolateFromCaller ? LastHitEffectID2 : null;
        HashSet<string> previousSequenceHits = isolateFromCaller
            ? new HashSet<string>(_sequenceHitEffectIDs)
            : null;

        // 系统/状态触发的 Skill 是独立技能序列，不继承外层玩家选择的目标。
        if (isolateFromCaller)
            ForcedTargetPositions.Clear();
        _currentExecutingSkillID2 = skillID2;
        try
        {
            // Skill 容器调用始终服从自身资源、冷却和次数规则；是否主动统一查询 ActiveActionQuery。
            if (!PrepareSkillExecution(skillID2, skillType, sk, isPlayerInitiated,
                    out var effects, out var forcedPositions)) return;

            if (effects != null)
            {
                foreach (var eff in effects)
                    ExecuteEffect(eff, GetEffectSkillType(eff.SkillEffectID2, skillType));
            }

            LogManager.Log(LogCategory.Skill,
                $"{sk.SkillName} used (targetPos={string.Join(",", forcedPositions)}, player={isPlayerInitiated})");
        }
        finally
        {
            ForcedTargetPositions.Clear();
            if (isolateFromCaller)
            {
                ForcedTargetPositions.AddRange(previousForcedPositions);
                _lastSkillTargetResult = previousTargetResult;
                _lastTargetPositions.Clear();
                _lastTargetPositions.AddRange(previousTargetPositions);
                LastHitEffectID2 = previousLastHitEffectID2;
                _sequenceHitEffectIDs.Clear();
                foreach (string effectID2 in previousSequenceHits)
                    _sequenceHitEffectIDs.Add(effectID2);
            }
            _currentExecutingSkillID2 = previousSkillID2;
            _currentExecutingSkillIsActiveAction = previousSkillWasActiveAction;
        }
    }

    int GetSkillTypeForExecution(string skillID2, int fallback)
    {
        if (string.IsNullOrEmpty(skillID2)
            || !_skillEffects.TryGetValue(skillID2, out List<SkillEffectData> effects))
            return fallback;
        foreach (SkillEffectData effect in effects)
        {
            if (effect == null || string.IsNullOrEmpty(effect.SkillEffectID2)) continue;
            int resolved = GetEffectSkillType(effect.SkillEffectID2, int.MinValue);
            if (resolved != int.MinValue) return resolved;
        }
        return fallback;
    }

    string GetExecutingSkillID2(int skillType)
    {
        if (!string.IsNullOrEmpty(_currentExecutingSkillID2)) return _currentExecutingSkillID2;
        return skillType >= 0 && skillType < _boundSkills.Length ? _boundSkills[skillType] : string.Empty;
    }

    /// <summary>
    /// 技能执行准备（2026-08-15，按钮路径/直接路径共用）：
    /// 清序列命中集合、快照强制目标、扣 AP/能量、冷却/次数、清上一条目标、PreAlliesDamage 钩子预解析。
    /// 返回 false = AP 不足等，不应执行效果。
    /// </summary>
    bool PrepareSkillExecution(string skillID2, int skillType, SkillMainData sk, bool isPlayerInitiated,
        out List<SkillEffectData> effects, out List<int> forcedPositions)
    {
        // 技能序列开始：清空"本序列命中集合"（Hit()钩子的序列内判定基准）
        _sequenceHitEffectIDs.Clear();
        LastHitEffectID2 = null;

        // 目标选择（2026-08-07）：读入本次技能的强制目标位置组合；技能序列结束后清空
        forcedPositions = new List<int>(ForcedTargetPositions);

        // Skill 容器可用性：系统调用同样服从容器自身的冷却、次数与资源约束。
        if (_cooldownRemaining.TryGetValue(skillID2, out int cooldown) && cooldown > 0)
        {
            effects = null;
            return false;
        }
        if (sk.MaxCharge > 0
            && (!_currentCharges.TryGetValue(skillID2, out int charges) || charges <= 0))
        {
            effects = null;
            return false;
        }

        // SkillType 与 ActionType 无关；资源只看 Skill 容器实际填写的字段。
        if (sk.EnergyUsed > 0 && Entity.CurrentEnergy < sk.EnergyUsed)
        {
            effects = null;
            return false;
        }
        if (sk.APCost > 0 && (APManager == null || !APManager.CanAfford(sk.APCost)))
        {
            effects = null;
            return false;
        }
        if (sk.APCost > 0 && !APManager.ConsumeAP(sk.APCost))
        {
            effects = null;
            return false;
        }
        if (sk.APCost > 0)
            StatusOnHitHookSystem.NotifyAPUsed(Entity, sk.APCost);
        if (sk.EnergyUsed > 0)
            Entity.CurrentEnergy = Mathf.Max(0, Entity.CurrentEnergy - sk.EnergyUsed);

        // 冷却 & 次数
        if (sk.Cooldown > 0)
            _cooldownRemaining[skillID2] = Mathf.CeilToInt(sk.Cooldown);
        if (sk.MaxCharge > 0 && _currentCharges.ContainsKey(skillID2))
            _currentCharges[skillID2]--;

        effects = _skillEffects.TryGetValue(skillID2, out var list) ? list : null;
        if (effects == null) return true;
        bool isActiveAction = ActiveActionQuery.IsActiveAction(Entity, sk);
        _currentExecutingSkillIsActiveAction = isActiveAction;

        _lastSkillTargetResult = null; // 每个技能序列开始时清空"上一条目标"
        _lastTargetPositions.Clear(); // 同时清空位置继承（TargetOverride="0,0" 只继承本技能序列内的上一条目标）

        // PreAlliesDamage 钩子（2026-08-14）：
        // 预解析本技能所有 Damage 效果的目标位置（并集去重）→ 至少一名实体（不会落空）
        // → 先触发钩子（触发行效果先于该行动实行），再正式执行技能效果。
        var bm = BattleManager.Instance;
        if (isActiveAction && bm != null && bm.PendingActionTargetPositions != null)
        {
            bm.PendingActionTargetPositions.Clear();
            foreach (var eff in effects)
            {
                if (eff == null || eff.EffectType != "Damage") continue;
                var targetResult = ResolveSkillTargetResult(eff);
                if (!targetResult.IsValid) continue;
                foreach (int position in targetResult.Positions)
                    if (!bm.PendingActionTargetPositions.Contains(position))
                        bm.PendingActionTargetPositions.Add(position);
            }
        }

        // 主动行为定义统一由 ActiveActionQuery 提供；本执行器不读取按钮返回值或自行推断。
        if (isActiveAction)
            StatusOnHitHookSystem.NotifyPlayerAction(Entity, sk);
        if (isActiveAction && bm != null && bm.PendingActionTargetPositions != null)
        {
            if (bm.PendingActionTargetPositions.Count > 0)
                PreDamageHookSystem.TriggerPreDamageHooks(Entity); // 钩子逻辑见 PreDamageHookSystem.cs（2026-08-15 独立）

            // 上面只是预解析。正式技能序列必须从“没有上一效果”开始，不能继承预览或钩子效果的目标。
            _lastSkillTargetResult = null;
            _lastTargetPositions.Clear();
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

    /// <summary>替换技能效果列表（InsertEffect/ModifyEffect 用），并完整重建 _effectById 索引。</summary>
    public void SetSkillEffects(string skillID2, List<SkillEffectData> list)
    {
        if (string.IsNullOrEmpty(skillID2) || !_skillById2.ContainsKey(skillID2))
        {
            LogManager.LogWarning(LogCategory.Build, $"SetSkillEffects 技能不存在: {skillID2}");
            return;
        }

        var ownedList = new List<SkillEffectData>();
        if (list != null)
        {
            foreach (SkillEffectData effect in list)
                if (effect != null) ownedList.Add(CloneEffect(effect));
        }
        _skillEffects[skillID2] = ownedList;

        // 不能只覆盖新 ID：效果被移除或改名后，旧索引必须同步消失。
        _effectById.Clear();
        foreach (List<SkillEffectData> effects in _skillEffects.Values)
        {
            foreach (SkillEffectData effect in effects)
            {
                if (effect != null && !string.IsNullOrEmpty(effect.SkillEffectID2))
                    _effectById[effect.SkillEffectID2] = effect;
            }
        }
    }

    /// <summary>克隆后修改技能字段（ModifySkill 用），并同步次数（InitialCharge 变化时）。</summary>
    public void ModifySkillData(string skillID2, System.Action<SkillMainData> mod)
    {
        if (mod == null || !_skillById2.TryGetValue(skillID2, out var sk)) return;
        var clone = CloneSkill(sk);
        mod(clone);
        _skillById2[skillID2] = clone;
        if (clone.MaxCharge > 0)
        {
            int initialCharge = clone.InitialCharge;
            if (initialCharge <= 0) initialCharge = clone.MaxCharge;
            _currentCharges[skillID2] = Mathf.Clamp(initialCharge, 0, clone.MaxCharge);
        }
        else
        {
            _currentCharges.Remove(skillID2);
        }
    }
}
