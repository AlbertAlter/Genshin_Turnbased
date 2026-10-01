using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public partial class CharacterBattleController
{
    /// <summary>每段在开始执行时计算数值；段内所有目标统一提交，后续段可读取前段响应带来的变化。</summary>
    async Task ExecuteHealingSegmentsAsync(List<BattleEntity> targets, string effectID, string param,
        int skillType, string element, BattleEntity source, StatusInstance sourceStatus)
    {
        using var effectScope = BattleResolutionContext.BeginEffect();
        List<string> segments = GetHealingSegments(effectID, param, skillType);
        for (int index = 0; index < segments.Count; index++)
        {
            await BattleExecution.Play(new BattlePresentationRequest(
                BattlePresentationKind.Heal, source, effectID, index + 1, targets), () =>
            {
                string formula = WeaponExpressionEvaluator.Expand(segments[index], sourceStatus);
                float amount = ResolveHealingAmount(formula, effectID, skillType, element);
                using (var batch = new HealBatch())
                {
                    foreach (BattleEntity target in targets.Distinct())
                        batch.Add(target, amount, source, effectID, index + 1);
                    batch.Flush();
                    foreach (HealResolvedEvent heal in batch.Results)
                        LogManager.Log(LogCategory.Effect,
                            $"治疗 {heal.Target.EntityID} 段#{heal.SegmentIndex} 实际+{heal.ActualAmount:F1} " +
                            $"溢出{heal.OverhealAmount:F1} (HP {heal.HPBefore:F1}→{heal.HPAfter:F1})");
                }
            });
        }
    }

    List<string> GetHealingSegments(string effectID, string param, int skillType)
    {
        var result = new List<string>();
        // 完整公式和引用直接执行一段；M*属性只是等级表公式的基准属性声明。
        if (!string.IsNullOrWhiteSpace(param) && !param.TrimStart().StartsWith("M*", StringComparison.Ordinal))
        {
            result.Add(param);
            return result;
        }
        if (!_levelDataBySkillType.TryGetValue(skillType, out var levels)) return result;
        int level = skillType >= 0 && skillType < SkillLevels.Length ? SkillLevels[skillType] : 1;
        var levelOne = levels.Find(row => row.ParamID == effectID && row.SkillLevel == 1);
        var current = levels.Find(row => row.ParamID == effectID && row.SkillLevel == Mathf.Clamp(level, 1, 15)) ?? levelOne;
        if (current == null) return result;
        for (int index = 1; index <= 7; index++)
        {
            string raw = GetHitsField(current, index);
            string template = GetHitsField(levelOne, index);
            if (string.IsNullOrWhiteSpace(raw)) raw = template;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            raw = raw.Split(',')[0].Trim();
            // 高等级只填倍率时，沿用等级 1 的属性；没有属性模板时数字就是固定治疗量。
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                string basis = !string.IsNullOrWhiteSpace(template) ? template.Split(',')[0] : param;
                if (!string.IsNullOrWhiteSpace(basis) && basis.Contains("*"))
                    raw += basis.Substring(basis.IndexOf('*'));
            }
            result.Add(raw);
        }
        return result;
    }

    // 沿用配表的加法/乘法治疗公式，例如 0.15*TotalHP+154；不引入通用脚本解释器。
    float ResolveHealingAmount(string formula, string effectID, int skillType, string element)
    {
        if (string.IsNullOrWhiteSpace(formula)) return 0f;
        if (formula.StartsWith("%", StringComparison.Ordinal))
            return ResolveBaseValue(formula, effectID, skillType, element);
        string expression = Regex.Replace(formula.Split(',')[0], @"\s+", "");
        string[] terms = expression.Split('+');
        float sum = 0f;
        foreach (string term in terms)
        {
            string[] factors = term.Split('*');
            float value = 1f;
            foreach (string factor in factors)
            {
                if (float.TryParse(factor, NumberStyles.Float, CultureInfo.InvariantCulture, out float scalar))
                    value *= scalar;
                else
                {
                    switch (factor)
                    {
                        case "ATK": case "TotalATK": value *= GetFinalATK(effectID, skillType, element); break;
                        case "DEF": case "TotalDEF": value *= Entity.TotalDEF; break;
                        case "MaxHP": case "TotalHP": value *= Entity.TotalHP; break;
                        default:
                            LogManager.LogWarning(LogCategory.Effect, $"无法解析治疗公式 {effectID}: {formula}");
                            return 0f;
                    }
                }
            }
            sum += value;
        }
        return Mathf.Max(0f, sum);
    }
}
