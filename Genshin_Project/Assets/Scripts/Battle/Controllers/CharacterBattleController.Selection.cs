using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// 玩家施放准备：只构造显式调用链和收集选择，不求值 ScriptHook/ExecuteCount、不扣资源或执行效果。
/// 全部确认后交给原技能执行入口。自动触发的状态/事件不属于这份预选计划。
/// </summary>
public partial class CharacterBattleController
{
    private sealed class SkillSelectionPlan
    {
        public string SkillID;
        public readonly List<EffectSelectionPlan> Effects = new List<EffectSelectionPlan>();
    }

    private sealed class EffectSelectionPlan
    {
        public SkillEffectData Effect;
        public SkillSelectionPlan CalledSkill;
        public EffectSelectionPlan CalledEffect;
        public List<int> SelectedPositions;
    }

    private SkillSelectionPlan _selectionPlan;
    private List<EffectSelectionPlan> _selectionSteps;
    private int _selectionIndex;
    private int _selectionSkillType;
    private bool _effectSequenceActive;
    private SkillEffectData _pendingSelectEffect;

    // 沿用输入层接口名，但现在只表示选择准备/最终执行尚未结束，不能据此认为资源已经扣除。
    public bool HasPendingEffectSequence => _effectSequenceActive;
    public SkillEffectData PendingSelectEffect => _pendingSelectEffect;

    public void MarkPendingSelectEffect(SkillEffectData effect) => _pendingSelectEffect = effect;

    public SkillEffectData GetFirstSelectEffect(int skillType)
    {
        var steps = new List<EffectSelectionPlan>();
        BuildSkillSelectionPlan(GetBoundSkillID(skillType), steps, new HashSet<string>());
        return steps.Count > 0 ? steps[0].Effect : null;
    }

    public bool TryGetFirstSelectEffect(int skillType, out SkillEffectData effect)
    {
        try { effect = GetFirstSelectEffect(skillType); return true; }
        catch (InvalidOperationException error)
        {
            effect = null;
            LogManager.LogWarning(LogCategory.Select, error.Message);
            return false;
        }
    }

    private SkillSelectionPlan BuildSkillSelectionPlan(string skillID, List<EffectSelectionPlan> steps,
        HashSet<string> path)
    {
        if (string.IsNullOrEmpty(skillID) || !_skillById2.ContainsKey(skillID)) return null;
        string key = "Skill:" + skillID;
        if (!path.Add(key)) throw new InvalidOperationException($"技能预选存在循环调用：{skillID}");
        try
        {
            var plan = new SkillSelectionPlan { SkillID = skillID };
            if (_skillEffects.TryGetValue(skillID, out var effects))
                foreach (var effect in effects)
                    if (effect != null) plan.Effects.Add(BuildEffectSelectionPlan(CloneEffect(effect), steps, path));
            return plan;
        }
        finally { path.Remove(key); }
    }

    private EffectSelectionPlan BuildEffectSelectionPlan(SkillEffectData effect, List<EffectSelectionPlan> steps,
        HashSet<string> path)
    {
        string key = "Effect:" + effect.SkillEffectID2;
        if (!path.Add(key)) throw new InvalidOperationException($"效果预选存在循环调用：{effect.SkillEffectID2}");
        try
        {
            var step = new EffectSelectionPlan { Effect = effect };
            if (effect.EffectType == "ExecuteSkill")
                step.CalledSkill = BuildSkillSelectionPlan(effect.Param1, steps, path);
            else if (effect.EffectType == "ExecuteEffect")
            {
                if (!string.IsNullOrEmpty(effect.Param1))
                {
                    SkillEffectData called;
                    bool found = _effectById.TryGetValue(effect.Param1, out called);
                    if (!found && DataManager.Instance != null)
                        found = DataManager.Instance.SkillEffectDict.TryGetValue(effect.Param1, out called);
                    if (found)
                        step.CalledEffect = BuildEffectSelectionPlan(CreateCalledEffect(called, effect), steps, path);
                }
            }
            else if (EffectNeedsSelection(effect))
                steps.Add(step);
            return step;
        }
        finally { path.Remove(key); }
    }

    /// <summary>首次确认：保存第一处选择；还有选择则只推进 UI，全部完成后才开始结算。</summary>
    public bool BeginSkillSelectionSequence(int skillType)
    {
        if (BattleExecution.IsBusy) return false;
        var firstSelection = new List<int>(ForcedTargetPositions);
        AbortEffectSequence();
        try
        {
            _selectionSteps = new List<EffectSelectionPlan>();
            _selectionPlan = BuildSkillSelectionPlan(GetBoundSkillID(skillType), _selectionSteps, new HashSet<string>());
            if (_selectionPlan == null) { AbortEffectSequence(); return true; }
            _selectionSkillType = skillType;
            _selectionIndex = 0;
            _effectSequenceActive = true;
            ForcedTargetPositions.AddRange(firstSelection);
            return AdvanceEffectSequence();
        }
        catch
        {
            AbortEffectSequence();
            throw;
        }
    }

    /// <summary>重复执行同一效果时复用本次手选；随机模式只预确认，仍由实际执行独立随机。</summary>
    public bool AdvanceEffectSequence()
    {
        if (BattleExecution.IsBusy) return false;
        if (!_effectSequenceActive || _selectionPlan == null) return true;
        if (GetBoundSkillID(_selectionSkillType) != _selectionPlan.SkillID
            || GetBlockReason(_selectionSkillType) != null)
        {
            AbortEffectSequence();
            return true;
        }

        if (_selectionIndex < _selectionSteps.Count)
        {
            if (ForcedTargetPositions.Count == 0) { AbortEffectSequence(); return true; }
            _selectionSteps[_selectionIndex].SelectedPositions = new List<int>(ForcedTargetPositions);
            _selectionIndex++;
            ForcedTargetPositions.Clear();
        }
        if (!AreConfirmedTargetsValid())
        {
            BattleManager.Instance?.RequestTip(BattleTipKeys.NoValidTarget);
            AbortEffectSequence();
            return true;
        }
        if (_selectionIndex < _selectionSteps.Count)
        {
            _pendingSelectEffect = _selectionSteps[_selectionIndex].Effect;
            LogManager.Log(LogCategory.Select,
                $"已确认 {_selectionIndex}/{_selectionSteps.Count} 处目标，尚未施放，继续选择 {_pendingSelectEffect.SkillEffectID2}");
            return false;
        }

        var plan = _selectionPlan;
        int skillType = _selectionSkillType;
        _pendingSelectEffect = null;
        Task task = BattleExecution.Run(async () =>
        {
            try
            {
                await ExecuteSkillByIDAsync(plan.SkillID, skillType, true, plan);
            }
            finally { AbortEffectSequence(); }
        });
        BattleExecution.Observe(task);
        return task.IsCompleted;
    }

    /// <summary>取消仅丢弃准备数据；已开始的结算由 BattleExecution 负责，不支持回滚。</summary>
    public void AbortEffectSequence()
    {
        _effectSequenceActive = false;
        _selectionPlan = null;
        _selectionSteps = null;
        _selectionIndex = 0;
        _pendingSelectEffect = null;
        ForcedTargetPositions.Clear();
    }

    public bool AreConfirmedTargetsValid()
    {
        if (_selectionSteps == null) return true;
        foreach (var step in _selectionSteps)
        {
            if (step.SelectedPositions == null) continue;
            var effect = step.Effect;
            var result = BattleTargetResolver.Resolve(BattleManager.Instance, Entity, Entity,
                effect.TargetType, effect.TargetNumber, effect.TargetConsecutive, null, null,
                step.SelectedPositions, null, deadOnly: effect.EffectType == "Revive",
                respectTaunt: effect.EffectType == "Damage", validateForcedTaunt: true);
            if (!result.IsValid) return false;
        }
        return true;
    }

    private void UsePlannedTargets(EffectSelectionPlan step)
    {
        ForcedTargetPositions.Clear();
        if (step?.SelectedPositions != null)
            ForcedTargetPositions.AddRange(step.SelectedPositions);
    }
}
