using System.Collections.Generic;
using System.Threading.Tasks;

public partial class CharacterBattleController
{
    // True/None 纯数字 Param1 是固定基准；用 M*属性读取原 SkillLevel 倍率入口。
    // 数字基准也可叠加本效果 SkillLevel 各段倍率；未配倍率时默认一段1倍。
    private async Task ExecuteTrueSkillDamageAsync(SkillEffectData effect, int skillType)
    {
        bool fixedBase = DamageProperties.TryReadFixedTrueBase(effect.Param1, out float basis);
        var query = new SkillEffectData { SkillEffectID2 = effect.SkillEffectID2, Param1 = fixedBase ? "M*ATK" : effect.Param1 };
        List<HitData> hits = GetHitDataList(query, skillType);
        if (fixedBase && hits.Count == 0) hits.Add(new HitData(1f, 0f));
        string[] direct = fixedBase ? null : GetDirectHitExpressions(effect.Param1);
        var targets = ResolveTargets(effect);
        if (targets == null || targets.Count == 0) return;
        using (StatusOnHitHookSystem.BeginDamageEffect(Entity, effect.SkillEffectID2, _currentExecutingSkillIsActiveAction))
        {
            LastHitEffectID2 = effect.SkillEffectID2;
            if (!string.IsNullOrEmpty(effect.SkillEffectID2)) _sequenceHitEffectIDs.Add(effect.SkillEffectID2);
            await TrueDamageExecutor.ExecuteAsync(targets, hits,
                index => fixedBase ? basis : direct != null
                    ? ResolveDirectHitBase(direct[index], effect, skillType)
                    : ResolveBaseValue(effect.Param1, effect.SkillEffectID2, skillType, effect.Element),
                CreateCurrentDamageSource(effect.SkillEffectID2, skillType, ReactionType.None),
                effect.SkillEffectID2, effect.DamageType, effect.Element, effect.Param2,
                DamageProperties.Parse(effect.Element, effect.Param3));
        }
    }

    private Task ExecuteTrueStatusDamageAsync(StatusEffectData effect, StatusInstance status, object host)
    {
        string expanded = WeaponExpressionEvaluator.Expand(effect.Param1, status);
        bool fixedBase = DamageProperties.TryReadFixedTrueBase(expanded, out float basis);
        var query = new StatusEffectData { StatusEffectID2 = effect.StatusEffectID2, Param1 = fixedBase ? "M*ATK" : effect.Param1 };
        List<HitData> hits = GetHitDataFromStatusEffectList(query);
        if (fixedBase && hits.Count == 0) hits.Add(new HitData(1f, 0f));
        string[] direct = fixedBase || (effect.Param1 ?? "").Contains("Index(") ? null : GetDirectHitExpressions(effect.Param1);
        int skillType = GetEffectSkillType(effect.StatusEffectID2, 2);
        return TrueDamageExecutor.ExecuteAsync(ResolveStatusTargets(effect, status, host), hits,
            index => fixedBase ? basis : direct != null
                ? ResolveDirectHitBase(direct[index], effect.StatusEffectID2, skillType, effect.Element)
                : ResolveBaseValue(expanded, effect.StatusEffectID2, skillType, effect.Element),
            CreateStatusDamageSource(status, effect.StatusEffectID2, ReactionType.None),
            effect.StatusEffectID2, effect.DamageType, effect.Element, effect.Param2,
            DamageProperties.Parse(effect.Element, effect.Param3));
    }
}
