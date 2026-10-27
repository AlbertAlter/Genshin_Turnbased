using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>541：普通Buff的逐逻辑hit判定。属性查询只读缓存，不能抽随机或改变保底概率。</summary>
public static class EchoesArtifactHooks
{
    public const string StatusID = "ST_Artifact_454102";
    private const string EffectID = "STE_Artifact_45410201";
    private sealed class ChanceState { internal int Failures; }
    private sealed class Decision { internal bool Triggered, Committed; internal int Failures; }
    private sealed class Frame
    {
        internal long Hit;
        internal BattleEntity Source;
        internal readonly HashSet<BattleEntity> Targets = new HashSet<BattleEntity>();
        internal readonly Dictionary<StatusInstance, Decision> Decisions = new Dictionary<StatusInstance, Decision>();
    }
    private sealed class Scope : IDisposable
    {
        private readonly Frame previous;
        private bool disposed;
        internal Scope(Frame frame) { previous = Current.Value; Current.Value = frame; }
        public void Dispose() { if (!disposed) { disposed = true; Current.Value = previous; } }
    }
    private static readonly AsyncLocal<Frame> Current = new AsyncLocal<Frame>();
    private static readonly ConditionalWeakTable<StatusInstance, ChanceState> Chances
        = new ConditionalWeakTable<StatusInstance, ChanceState>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterPreparedBaseDamageEffect(StatusID, 45410201);
        BattleSpecialRules.RegisterHitScope((hit, source, targets) =>
        {
            var frame = new Frame { Hit = hit, Source = source };
            if (targets != null) foreach (var target in targets) if (target != null) frame.Targets.Add(target);
            return new Scope(frame);
        });
        BattleSpecialRules.RegisterStatusLifecycle(StatusID, (status, host) =>
        {
            RefreshingArtifactCount.RequireOwner(status, host);
            Chances.Add(status, new ChanceState());
        }, (status, host) => Chances.Remove(status));
        BattleSpecialRules.RegisterNumberedCondition("454102", (argument, context) =>
        {
            Frame frame = Current.Value;
            return frame != null && frame.Hit == BattleEventHub.CurrentHitId && frame.Hit != 0
                && context?.Status != null && context.Status.Caster == frame.Source
                && context.Target != null && frame.Targets.Contains(context.Target)
                && context.Target.Side != frame.Source.Side
                && frame.Decisions.TryGetValue(context.Status, out var decision) && decision.Triggered;
        });
        BattleSpecialRules.RegisterDamagePreparation(StatusID, Prepare);
    }

    private static CountDamageContribution Prepare(StatusInstance status, object host, CountDamageContext context)
    {
        Frame frame = Current.Value;
        var owner = host as BattleEntity;
        if (owner == null || owner != status.Caster || owner != context.Attacker || !owner.IsAlive
            || context.DamageType != "Normal" || context.Target.Side == owner.Side
            || (context.SourceKind != ReactionSourceKind.CharacterSkill && context.SourceKind != ReactionSourceKind.StatusEffect)
            || frame == null || frame.Source != owner || frame.Hit == 0 || frame.Hit != BattleEventHub.CurrentHitId
            || !frame.Targets.Contains(context.Target)) return null;
        if (!Chances.TryGetValue(status, out var chance))
            throw new InvalidOperationException("余响Buff没有初始化自己的概率记录。");
        if (!frame.Decisions.TryGetValue(status, out var decision))
        {
            float probability = Math.Min(1f, 0.36f + 0.20f * chance.Failures);
            float roll = BattleRandom.NextFloat01();
            decision = new Decision { Triggered = roll < probability, Failures = chance.Failures };
            frame.Decisions.Add(status, decision);
            LogManager.Log(LogCategory.ScriptHook, $"余响 hit={frame.Hit} 概率={probability:P1} 随机={roll:F4} → {decision.Triggered}");
        }
        float bonus = 0f;
        if (decision.Triggered)
        {
            var dm = DataManager.Instance;
            if (dm == null || !dm.StatusEffectDict.TryGetValue(EffectID, out var effect) || effect.EffectType != "Buff")
                throw new InvalidOperationException("余响缺少基础伤害Buff配置。");
            string[] formula = (effect.Param1 ?? string.Empty).Split('*');
            if (formula.Length != 2 || formula[1].Trim() != "TotalATK"
                || !float.TryParse(formula[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio)
                || float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio < 0f)
                throw new InvalidOperationException("余响Param1必须为非负有限系数*TotalATK。");
            bonus = ratio * owner.CharacterCtrl.GetFinalATK(context.EffectID, context.SkillType, context.Element);
        }
        // 与浪潮/武器Count共用唯一的基础加算管线；不再额外执行一段伤害或汇总同一Buff两次。
        return new CountDamageContribution(bonus, result =>
        {
            if (decision.Committed) return;
            decision.Committed = true;
            chance.Failures = decision.Triggered ? 0 : Math.Min(4, decision.Failures + 1);
        }, null);
    }
}
