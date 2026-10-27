using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>True/None固定伤害共用入口，仍通过HitBatch处理免疫、适用护盾、声明、死亡及胜负，不直接改CurrentHP。</summary>
public static class TrueDamageExecutor
{
    public static async Task ExecuteAsync(IReadOnlyList<BattleEntity> targets, IReadOnlyList<HitData> hits,
        Func<int, float> getBase, DamageSourceInfo source, string effectID, string damageType,
        string element = "True", string splash = null, DamageProperties properties = default)
    {
        if (targets == null || hits == null) return;
        element = DamageProperties.NormalizeElement(element);
        if (!DamageProperties.IsFixed(element)) throw new ArgumentException("固定伤害入口仅支持 True/None。", nameof(element));
        for (int index = 0; index < hits.Count; index++)
        {
            int hitIndex = index;
            await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Hit,
                source?.SourceEntity, effectID, hitIndex + 1, new List<BattleEntity>(targets)), () =>
            {
                if (!EquipmentStatusLifetime.CanExecute(source)) return;
                float amount = getBase(hitIndex) * hits[hitIndex].Multiplier;
                if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0f)
                    throw new InvalidOperationException(element + " 伤害必须为有限非负数：" + effectID);
                var formulaMemory = StatusFormulaSnapshot.Find(source?.SourceStatus, effectID);
                if (formulaMemory != null) LogManager.Log(LogCategory.StatusDamage,
                    $"[公式快照] {formulaMemory.Identity} 效果={effectID} hit#{hitIndex + 1} {element}基准伤害={amount:R}");
                using var responses = BattleResolutionContext.BeginCollection();
                var entries = DamageHitTargets.Expand(new List<BattleEntity>(targets), splash, element);
                var actualTargets = entries.ConvertAll(entry => entry.Target);
                long logicalHitID = BattleEventHub.NextIdentity();
                using var snapshot = BattleSpecialRules.BeginHit(logicalHitID, source?.SourceEntity,
                    actualTargets);
                using var batch = new HitBatch(logicalHitID);
                foreach (var entry in entries)
                {
                    BattleEntity target = entry.Target;
                    if (target != null && target.IsAlive)
                        batch.Resolve(new HitRequest
                        {
                            Target = target, DamageBeforeShield = amount * entry.Rate, Element = element, Properties = properties,
                            DamageType = damageType, Source = source,
                            HitIndex = hitIndex, TargetIndex = batch.Results.Count
                        });
                }
            });
        }
    }
}
