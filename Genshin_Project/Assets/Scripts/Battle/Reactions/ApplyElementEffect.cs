using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>纯元素接触：复用元素反应与声明入口，不构造普通伤害、暴击或命中结果。</summary>
public static class ApplyElementEffect
{
    public static string ParseElement(string raw)
    {
        switch ((raw ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "pyro": case "火": return "Pyro";
            case "hydro": case "水": return "Hydro";
            case "electro": case "雷": return "Electro";
            case "cryo": case "冰": return "Cryo";
            case "anemo": case "风": return "Anemo";
            case "dendro": case "草": return "Dendro";
            case "geo": case "岩": return "Geo";
            default: throw new FormatException("ApplyElement.Element必须为单个七元素，不接受空值、物理、None或True：[" + raw + "]");
        }
    }

    public static float ParseAmount(string raw)
    {
        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            || float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            throw new FormatException("ApplyElement.Param1必须为有限正数元素量，不接受公式：[" + raw + "]");
        return value;
    }

    public static void Execute(string element, string amount, BattleEntity source,
        IEnumerable<BattleEntity> targets, ReactionSourceKind sourceKind, string skillID,
        string effectID, long executionID, StatusInstance status = null)
    {
        string resolvedElement = ParseElement(element);
        float resolvedAmount = ParseAmount(amount);
        if (source == null) throw new InvalidOperationException("ApplyElement缺少真实施加者：" + effectID);
        if (!EquipmentStatusLifetime.CanExecute(status)) return;
        var selected = new List<BattleEntity>();
        if (targets != null) foreach (var target in targets)
            if (target != null && target.IsAlive && !selected.Contains(target)) selected.Add(target);
        if (selected.Count == 0) return;
        // 与普通岩命中一致，依位置稳定结算结晶，后面的护盾覆盖前面的护盾。
        if (resolvedElement == "Geo") selected.Sort((a, b) => a.SlotPosition.CompareTo(b.SlotPosition));

        using var resolution = BattleEventHub.BeginResolution();
        using var responses = BattleResolutionContext.BeginCollection();
        using var execution = BattleEventHub.BeginEffectExecution(executionID);
        // 该编号仅用于元素交互批次身份及月反应汇总；不提交DamageResolved/命中声明。
        long interactionID = BattleEventHub.NextIdentity();
        var contexts = new List<ReactionContext>();
        foreach (var target in selected)
            contexts.Add(new ReactionContext
            {
                SourceEntity = source, EquipmentSource = status?.EquipmentSource, Target = target,
                SourceKind = sourceKind, SourceSkillID = skillID ?? string.Empty, SourceEffectID = effectID,
                EffectExecutionID = executionID, LogicalHitID = interactionID, TargetIndex = contexts.Count + 1,
                AttackElement = resolvedElement, AttackAmount = resolvedAmount, PreReactionDamage = 0f,
                DamageType = string.Empty, CanApplyAura = true, CanTriggerReaction = true, CanTriggerShatter = false
            });

        var results = new List<ReactionResult>();
        foreach (var context in contexts) results.Add(ReactionResolver.Resolve(context));
        if (resolvedElement == "Anemo")
            results.Add(SwirlReactionHandler.ResolveBatch(contexts).Reaction);
        // 无普通主命中：不调用HitBatch或FinalizePrimaryHit。反应伤害/状态/雷云/月笼仍走公共收尾。
        ReactionEffectExecutor.ExecuteDerivedHitBatch(results);
    }
}
