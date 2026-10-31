using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

public partial class EnemyBattleController
{
    private DamageSourceInfo EnemyDamageSource(StatusInstance status, string skillID, string effectID,
        ReactionType reaction = ReactionType.None)
        => status != null ? DamageSourceInfo.FromStatus(status, effectID, reaction)
            : DamageSourceInfo.Create(Entity, ReactionSourceKind.EnemySkill, skillID, effectID, reaction);

    // 敌人状态没有角色技能等级表，直接读取配表的倍率与元素量，不借用当前主动技能。
    internal static List<HitData> ParseEnemyStatusHits(string raw)
    {
        string[] pair = (raw ?? string.Empty).Replace(" ", "").Split(',');
        string basis = pair[0];
        string suffix = basis.EndsWith("*TotalATK", StringComparison.Ordinal) ? "*TotalATK" : "*ATK";
        if (pair.Length != 2 || !basis.EndsWith(suffix, StringComparison.Ordinal)
            || !float.TryParse(basis.Substring(0, basis.Length - suffix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out float multiplier)
            || !float.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float gauge)
            || float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f
            || float.IsNaN(gauge) || float.IsInfinity(gauge) || gauge < 0f)
            throw new FormatException("敌人状态Damage.Param1须为倍率*ATK,元素量：" + raw);
        return new List<HitData> { new HitData(multiplier, gauge) };
    }

    private async Task ExecuteEnemyHealAsync(EnemySkillEffectData effect, StatusInstance sourceStatus)
    {
        string skillID = sourceStatus == null ? _currentSkillID2 : null;
        var targets = ResolveEnemyTargets(effect, sourceStatus);
        await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Heal,
            Entity, effect.SkillEffectID2, 1, targets), () => {
            using var batch = new HealBatch();
            foreach (var target in targets)
            {
                if (target == null || !target.IsAlive) continue;
                float? ceiling = Enemy2011To2020Hooks.HealingCeiling(effect, target);
                if (ceiling.HasValue && target.CurrentHP >= ceiling.Value) continue;
                float amount = ResolveEnemyHealingFormula(effect.Param1, effect, skillID, target);
                float outgoing = Entity.HealBonus + Entity.GetStatusHealingBonus(
                    "HealBonus", effect.SkillEffectID2, skillID, -1, effect.Element, target);
                float incoming = target.BeHealedBonus + target.GetStatusHealingBonus(
                    "BeHealedBonus", effect.SkillEffectID2, skillID, -1, effect.Element, target);
                batch.Add(target, amount * Mathf.Max(0f, 1f + outgoing + incoming), Entity,
                    effect.SkillEffectID2, hpCeiling: ceiling);
            }
        });
    }

    private float ResolveEnemyHealingFormula(string formula, EnemySkillEffectData effect, string skillID, BattleEntity target)
    {
        if (string.IsNullOrWhiteSpace(formula)) throw new FormatException("敌人Heal.Param1缺少治疗量：" + effect.SkillEffectID2);
        float sum = 0f;
        foreach (string term in formula.Replace(" ", "").Split('+'))
        {
            float value = 1f;
            foreach (string factor in term.Split('*'))
            {
                if (float.TryParse(factor, NumberStyles.Float, CultureInfo.InvariantCulture, out float scalar)) value *= scalar;
                else switch (factor)
                {
                    case "ATK": case "TotalATK": value *= Entity.GetFinalATK(effect.SkillEffectID2, skillID, -1, effect.Element); break;
                    case "DEF": case "TotalDEF": value *= Entity.GetFinalDEF(effect.SkillEffectID2, skillID, -1, effect.Element); break;
                    case "MaxHP": case "TotalHP": value *= Entity.GetFinalHP(effect.SkillEffectID2, skillID, -1, effect.Element); break;
                    case "Target.TotalHP": value *= target.GetFinalHP(effect.SkillEffectID2, skillID, -1, effect.Element); break;
                    default: throw new FormatException("无法解析敌人治疗公式：" + formula);
                }
            }
            sum += value;
        }
        if (float.IsNaN(sum) || float.IsInfinity(sum) || sum < 0f) throw new FormatException("敌人治疗量须为有限非负数：" + formula);
        return sum;
    }
}
