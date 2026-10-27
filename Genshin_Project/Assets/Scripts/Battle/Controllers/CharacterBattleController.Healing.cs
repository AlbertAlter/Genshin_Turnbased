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
                if (!EquipmentStatusLifetime.CanExecute(sourceStatus)) return;
                string formula = WeaponExpressionEvaluator.Expand(segments[index], sourceStatus);
                // 受术者变量必须逐目标求值；没有Target变量的旧公式仍每段只算一次。
                bool targetDependent = formula.Contains("Target.") || StatusFormulaSnapshot.For(Entity, effectID) != null;
                float amount = targetDependent ? 0f : ResolveHealingAmount(formula, effectID, skillType, element);
                using (var batch = new HealBatch())
                {
                    foreach (BattleEntity target in targets.Distinct())
                    {
                        if (target == null || !target.IsAlive) continue;
                        using var snapshotTarget = StatusFormulaSnapshot.EnterTarget(target);
                        float targetAmount = targetDependent
                            ? ResolveHealingAmount(formula, effectID, skillType, element, target) : amount;
                        var healer = source ?? Entity;
                        string skillID = GetExecutingSkillID2(skillType);
                        var memory = StatusFormulaSnapshot.For(healer, effectID);
                        float outgoing = memory != null ? memory.Raw("HealBonus") + memory.Bonus("HealBonus", effectID, element, target)
                            : healer.HealBonus + healer.GetStatusHealingBonus("HealBonus", effectID, skillID, skillType, element, target);
                        float incoming = target.BeHealedBonus + target.GetStatusHealingBonus(
                            "BeHealedBonus", effectID, skillID, skillType, element, target);
                        // 术语1.20：加成在同一个加法区，每段、每个目标只求值一次。
                        batch.Add(target, targetAmount * Mathf.Max(0f, 1f + outgoing + incoming), source, effectID, index + 1);
                    }
                    batch.Flush();
                    foreach (HealResolvedEvent heal in batch.Results)
                        LogManager.Log(LogCategory.Effect,
                            $"治疗 {heal.Target.EntityID} 段#{heal.SegmentIndex} 实际+{heal.ActualAmount:F1} " +
                            $"抵扣生命之契{heal.BondOfLifeAbsorbedAmount:F1} 溢出{heal.OverhealAmount:F1} " +
                            $"(HP {heal.HPBefore:F1}→{heal.HPAfter:F1})");
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
        int level = SourceSkillLevel(effectID, skillType);
        var levelOne = levels.Find(row => row.ParamID == effectID && row.SkillLevel == 1);
        var current = levels.Find(row => row.ParamID == effectID && row.SkillLevel == level) ?? levelOne;
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

    /// <summary>Buff/减伤的Param1留空时读取自身等级表的单个标量，不重算状态或修改共享表。</summary>
    public bool TryResolveStatusLevelScalar(StatusEffectData effect, out float scalar)
    {
        scalar = 0f;
        if (effect == null || !string.IsNullOrWhiteSpace(effect.Param1)) return false;
        int type = GetEffectSkillType(effect.StatusEffectID2, -1);
        if (type < 0) return false;
        return TryGetSkillLevelScalar(effect.StatusEffectID2, type, out scalar);
    }

    /// <summary>按明确的ParamID与SkillType读取等级表单标量，例如诺艾尔治疗概率；不制造虚构效果或执行状态。</summary>
    public bool TryGetSkillLevelScalar(string paramID, int skillType, out float scalar)
    {
        scalar = 0f;
        if (string.IsNullOrWhiteSpace(paramID)) return false;
        var segments = GetHealingSegments(paramID, null, skillType);
        return segments.Count == 1 && float.TryParse(segments[0], NumberStyles.Float,
            CultureInfo.InvariantCulture, out scalar) && !float.IsNaN(scalar) && !float.IsInfinity(scalar);
    }

    private float ResolveShieldAmount(string formula, string effectID, int skillType, string element)
    {
        var segments = GetHealingSegments(effectID, formula, skillType);
        if (segments.Count == 0) return ResolveBaseValue(formula, effectID, skillType, element);
        if (segments.Count != 1) throw new FormatException("护盾量只接受一段完整公式：" + effectID);
        return ResolveHealingAmount(segments[0], effectID, skillType, element);
    }

    // 治疗与护盾共用既有加法/乘法属性公式，例如 0.15*TotalHP+154，不引入脚本解释器。
    float ResolveHealingAmount(string formula, string effectID, int skillType, string element, BattleEntity target = null)
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
                        case "DEF": case "TotalDEF": value *= SourceAttribute("TotalDEF", effectID, skillType, element); break;
                        case "MaxHP": case "TotalHP": value *= SourceAttribute("TotalHP", effectID, skillType, element); break;
                        case "Target.TotalHP":
                            if (target == null) throw new InvalidOperationException("Target.TotalHP治疗公式缺少本次受术者。");
                            value *= target.GetFinalHP(effectID, GetExecutingSkillID2(skillType), skillType, element);
                            break;
                        default:
                            LogManager.LogWarning(LogCategory.Effect, $"无法解析属性公式 {effectID}: {formula}");
                            return 0f;
                    }
                }
            }
            sum += value;
        }
        return Mathf.Max(0f, sum);
    }
}
