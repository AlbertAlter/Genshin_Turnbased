using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>只保存本hit的适用资格事实。最终面板、伤害和随机仍在原结算点计算。</summary>
public static class ArtifactHitSnapshots
{
    private sealed class TargetFacts
    {
        internal float HP, MaxHP;
        internal bool Cryo, Electro, Pyro, Frozen, Superconduct;
    }
    private sealed class Frame
    {
        internal long HitID;
        internal BattleEntity Source;
        internal bool PolarDomain;
        internal readonly Dictionary<BattleEntity, TargetFacts> Targets = new Dictionary<BattleEntity, TargetFacts>();
    }
    private sealed class Scope : IDisposable
    {
        private readonly Frame previous;
        private bool disposed;
        internal Scope(Frame frame) { previous = Current.Value; Current.Value = frame; }
        public void Dispose() { if (!disposed) { disposed = true; Current.Value = previous; } }
    }
    private static readonly AsyncLocal<Frame> Current = new AsyncLocal<Frame>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterHitScope(Begin);
        BattleSpecialRules.RegisterNumberedCondition("451402", (argument, context) =>
            TryRead(context, out var facts) && (facts.Cryo || facts.Frozen));
        BattleSpecialRules.RegisterNumberedCondition("451403", (argument, context) =>
            TryRead(context, out var facts) && facts.Frozen);
        BattleSpecialRules.RegisterNumberedCondition("451502", (argument, context) =>
            TryRead(context, out var facts) && facts.Electro);
        BattleSpecialRules.RegisterNumberedCondition("451602", (argument, context) =>
            TryRead(context, out var facts) && facts.Pyro);
        BattleSpecialRules.RegisterNumberedCondition("456304", (argument, context) =>
            TryRead(context, out var facts) && (facts.Superconduct || Current.Value.PolarDomain));
    }

    private static IDisposable Begin(long hitID, BattleEntity source, IEnumerable<BattleEntity> targets)
    {
        var frame = new Frame { HitID = hitID, Source = source };
        var team = BattleManager.Instance?.TeamBonuses;
        frame.PolarDomain = team != null && !team.IsDisposed && team.Contains(source) && team.Stellar.IsActive;
        if (targets != null)
            foreach (BattleEntity target in targets)
            {
                if (target == null || !target.IsAlive || frame.Targets.ContainsKey(target)) continue;
                frame.Targets.Add(target, new TargetFacts
                {
                    HP = target.CurrentHP, MaxHP = target.GetFinalHP(),
                    Cryo = HasAura(target, "Cryo"), Electro = HasAura(target, "Electro"), Pyro = HasAura(target, "Pyro"),
                    Frozen = FrozenReactionHandler.IsFrozen(target),
                    Superconduct = ReactionStateSystem.TryGetEntityState(target, ReactionType.Superconduct, out _)
                });
            }
        return new Scope(frame);
    }

    public static bool TargetHPAbove(ScriptHookContext context, float ratio)
        => TryRead(context, out var facts) && facts.MaxHP > 0f && facts.HP > facts.MaxHP * ratio;

    private static bool TryRead(ScriptHookContext context, out TargetFacts facts)
    {
        facts = null;
        Frame frame = Current.Value;
        BattleEntity owner = context?.Status?.Caster ?? context?.Caster;
        return owner != null && frame != null && frame.HitID != 0 && frame.HitID == BattleEventHub.CurrentHitId
            && ReferenceEquals(frame.Source, owner) && context?.Target != null
            && context.Target.Side != owner.Side && frame.Targets.TryGetValue(context.Target, out facts);
        // 没有正确的hit/来源/目标上下文就不通过，不回退为反应后的实时状态或“最近一次”。
    }

    private static bool HasAura(BattleEntity target, string element)
    {
        return ElementQuery.HasAura(target, element);
    }
}
