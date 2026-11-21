using System;
using System.Collections.Generic;

/// <summary>保存单个敌人的当前行为模式，并按表内顺序筛选和抽取技能。</summary>
internal sealed class EnemySkillSelector
{
    private readonly List<EnemyAIRuleData> _rules = new List<EnemyAIRuleData>();
    private readonly List<EnemyAIRuleData> _candidates = new List<EnemyAIRuleData>();

    internal int CurrentPatternID { get; private set; }

    internal void LoadPattern(int enemyID, int phase, int patternID, IReadOnlyList<EnemyAIRuleData> rules)
    {
        CurrentPatternID = patternID;
        _rules.Clear();
        if (rules == null) return;

        foreach (var rule in rules)
            if (rule != null && rule.Phase == phase && rule.PatternID == patternID)
                _rules.Add(rule);
        if (_rules.Count > 0)
            LogManager.Log(LogCategory.Enemy, $"{enemyID} loaded pattern {patternID}: {_rules.Count} rules");
    }

    internal int? PickSkill(BattleEntity caster, Func<int, bool> meetsSkillLimits)
    {
        _candidates.Clear();
        int totalWeight = 0;
        foreach (var rule in _rules)
        {
            if (rule == null || rule.Weight <= 0 || !meetsSkillLimits(rule.SkillID)) continue;
            if (!ScriptHookEvaluator.Evaluate(rule.ScriptHook, new ScriptHookContext { Caster = caster })) continue;

            _candidates.Add(rule);
            totalWeight += rule.Weight;
        }
        if (_candidates.Count == 0 || totalWeight <= 0) return null;

        int roll = BattleRandom.NextInt(0, totalWeight);
        if (LogManager.IsEnabled(LogCategory.AI))
            LogManager.Log(LogCategory.AI,
                $"候选技能: {string.Join(",", _candidates.ConvertAll(rule => rule.SkillID))} (总权重{totalWeight}) roll={roll}");

        int accumulated = 0;
        foreach (var rule in _candidates)
        {
            accumulated += rule.Weight;
            if (roll < accumulated) return rule.SkillID;
        }
        return _candidates[_candidates.Count - 1].SkillID;
    }

    internal void Clear()
    {
        CurrentPatternID = 0;
        _rules.Clear();
        _candidates.Clear();
    }
}
