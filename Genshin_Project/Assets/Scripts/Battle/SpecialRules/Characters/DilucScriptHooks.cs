using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>迪卢克专属条件、火鸟逐目标执行及六命两次普通伤害预算；不另建伤害或响应队列。</summary>
public static class DilucScriptHooks
{
    public const string BirdEffect = "SE_Burst_DilucBird";
    public const string ChainStatus = "ST_Diluc_SkillChain";
    public const string C6Status = "ST_Diluc_C6";

    private sealed class SkillScope : IDisposable
    {
        internal readonly SkillScope Previous = Skill.Value;
        internal readonly BattleEntity Owner;
        internal readonly List<BattleEntity> Targets;
        internal int Next;
        internal SkillScope(BattleEntity owner)
        { Owner = owner; Targets = LivingOpponents(owner); Skill.Value = this; }
        public void Dispose() => Skill.Value = Previous;
    }
    private sealed class BirdScope : IDisposable
    {
        internal readonly BirdScope Previous = Bird.Value;
        internal readonly BattleEntity Owner, Target;
        internal readonly string ID;
        internal BirdScope(BattleEntity owner, string effect)
        {
            Owner = owner; ID = effect;
            var skill = Skill.Value;
            if (effect == BirdEffect && skill?.Owner == owner)
                while (skill.Next < skill.Targets.Count)
                {
                    var candidate = skill.Targets[skill.Next++];
                    if (CurrentTarget(candidate)) { Target = candidate; break; }
                }
            Bird.Value = this;
        }
        public void Dispose() => Bird.Value = Previous;
    }
    private sealed class Budget
    {
        internal StatusInstance Status;
        internal BattleEntity Owner;
        internal int Available = 2, Reserved, Spent;
        internal bool PendingRemoval;
        internal bool Valid => Status.IsActive && Owner.IsAlive && Owner.HasStatusInstance(Status);
    }
    private sealed class EffectScope : IDisposable
    {
        internal readonly EffectScope Previous = Effect.Value;
        internal readonly BattleEntity Owner;
        internal readonly string ID;
        internal readonly long Execution;
        internal readonly Budget Budget;
        internal readonly bool Reserved;
        internal BattleDeclaration Landed;
        private bool closed;
        internal EffectScope(BattleEntity owner, string id, long execution)
        {
            Owner = owner; ID = id; Execution = execution;
            if (owner?.EntityID == 1015 && IsNormalDamage(id))
            {
                Budget = GetBudget(owner.GetStatus(C6Status));
                if (Budget?.Valid == true && Budget.Available > 0)
                { Budget.Available--; Budget.Reserved++; Reserved = true; }
            }
            Effect.Value = this;
        }
        public void Dispose()
        {
            if (closed) return;
            closed = true;
            try
            {
                if (!Reserved) return;
                Budget.Reserved--;
                if (Landed == null) Budget.Available++; // 未命中不消耗；不能按目标/hit重复扣次。
                else Budget.Spent++;
                if (Budget.Valid && Budget.Spent == 2 && Budget.Reserved == 0 && !BattleExecution.IsCancelled)
                    RequestRemoval(Budget, Landed);
            }
            finally { Effect.Value = Previous; }
        }
    }

    private static readonly AsyncLocal<SkillScope> Skill = new AsyncLocal<SkillScope>();
    private static readonly AsyncLocal<BirdScope> Bird = new AsyncLocal<BirdScope>();
    private static readonly AsyncLocal<EffectScope> Effect = new AsyncLocal<EffectScope>();
    private static readonly ConditionalWeakTable<StatusInstance, Budget> Budgets = new ConditionalWeakTable<StatusInstance, Budget>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillScope(owner => new SkillScope(owner));
        BattleSpecialRules.RegisterDamageEffectScope((owner, id, execution) => new BirdScope(owner, id), (_, __) => { });
        BattleSpecialRules.RegisterEffectInvocationScope((owner, id, execution) => new EffectScope(owner, id, execution));
        BattleSpecialRules.RegisterTargets("Diluc_DawnTargets()", (battle, owner) =>
        {
            var frame = Bird.Value;
            if (frame != null && frame.ID == BirdEffect && frame.Owner == owner)
                return CurrentTarget(frame.Target) ? new List<int> { frame.Target.SlotPosition } : new List<int>();
            var positions = new List<int>();
            foreach (var target in LivingOpponents(owner)) positions.Add(target.SlotPosition);
            return positions; // UI/预解析只查询全体，不推进实际迭代器。
        });
        BattleSpecialRules.RegisterCount("Diluc_DawnCount", owner =>
        {
            var frame = Skill.Value;
            if (frame?.Owner != owner || owner?.EntityID != 1015)
                throw new InvalidOperationException("火鸟重复次数必须读取迪卢克本次技能的目标快照。");
            int count = 0;
            foreach (var target in frame.Targets) if (CurrentTarget(target)) count++;
            return count;
        });
        BattleSpecialRules.RegisterCondition("Diluc_DawnHasTarget", (argument, context) =>
        {
            var frame = Skill.Value;
            if (!string.IsNullOrEmpty(argument) || context?.Caster?.EntityID != 1015) return false;
            if (frame?.Owner != context.Caster) return LivingOpponents(context.Caster).Count > 0;
            for (int i = frame.Next; i < frame.Targets.Count; i++) if (CurrentTarget(frame.Targets[i])) return true;
            return false;
        });
        BattleSpecialRules.RegisterCondition("Diluc_C1HP", (argument, context) =>
            string.IsNullOrEmpty(argument) && context?.Caster?.EntityID == 1015
            && context.Target?.IsAlive == true && context.Target.CurrentHP > context.Target.GetFinalHP() * .5f);
        BattleSpecialRules.RegisterCondition("Diluc_C4OtherAP", (argument, context) =>
        {
            var payment = context?.Declaration?.Get<APSpentEventData>();
            return string.IsNullOrEmpty(argument) && context?.Caster?.EntityID == 1015 && context.Caster.IsAlive
                && context.Status?.StatusID2 == ChainStatus && context.Status.IsActive
                && context.Caster.HasStatusInstance(context.Status) && payment?.Amount > 0
                && payment.SpendKind == APSpendKind.Skill && context.Declaration.Header.ActionExecutionId != 0
                && payment.Subject != null && payment.Subject != context.Caster && payment.Subject.Side == context.Caster.Side;
        });
        BattleSpecialRules.RegisterStatusLifecycle(C6Status, (status, host) =>
        {
            if (!(host is BattleEntity owner) || owner != status.Caster || owner.EntityID != 1015)
                throw new InvalidOperationException("迪卢克六命状态必须施加在本人身上。");
            Budgets.Add(status, new Budget { Status = status, Owner = owner });
        }, (status, host) => Budgets.Remove(status));
        BattleSpecialRules.RegisterCondition("Diluc_C6Damage", (argument, context) =>
        {
            var budget = GetBudget(context?.Status);
            if (!string.IsNullOrEmpty(argument) || budget?.Valid != true) return false;
            var frame = Effect.Value;
            return frame?.Owner == budget.Owner && frame.ID == context.HitEffectID
                ? frame.Budget == budget && frame.Reserved : budget.Available > 0;
        });
        BattleSpecialRules.RegisterCondition("Diluc_C6AP", (argument, context) =>
            string.IsNullOrEmpty(argument) && GetBudget(context?.Status) is Budget budget && budget.Valid && budget.Available > 0);
        BattleSpecialRules.RegisterCondition("Diluc_C6Spent", (argument, context) =>
            string.IsNullOrEmpty(argument) && GetBudget(context?.Status) is Budget budget && budget.Valid && budget.PendingRemoval);
        BattleSpecialRules.RegisterDeclarationObserver(declaration =>
        {
            var frame = Effect.Value;
            var damage = declaration.Get<DamageEventData>();
            if (frame?.Reserved != true || damage?.HitLanded != true || damage.DamageType != "Normal" || damage.Source == null
                || damage.Source.SourceEntity != frame.Owner || damage.Source.SourceEffectID != frame.ID
                || declaration.Header.EffectExecutionId != frame.Execution) return;
            frame.Landed = declaration; // 只索引事实，整条效果（含全部重复）结束后才消费与移除。
        });
    }

    private static Budget GetBudget(StatusInstance status)
        => status != null && Budgets.TryGetValue(status, out var budget) ? budget : null;

    private static bool IsNormalDamage(string id)
    {
        var data = DataManager.Instance;
        if (data == null || string.IsNullOrEmpty(id)) return false;
        if (data.SkillEffectDict.TryGetValue(id, out var skill)) return skill.EffectType == "Damage" && skill.DamageType == "Normal";
        return data.StatusEffectDict.TryGetValue(id, out var status) && status.EffectType == "Damage" && status.DamageType == "Normal";
    }

    private static bool CurrentTarget(BattleEntity target)
        => target?.IsAlive == true && BattleManager.Instance?.GetEntityByPosition(target.Side, target.SlotPosition) == target;

    private static List<BattleEntity> LivingOpponents(BattleEntity owner)
    {
        var result = new List<BattleEntity>(); var battle = BattleManager.Instance;
        if (owner?.EntityID != 1015 || battle == null) return result;
        if (owner.Side == BattleSide.Ally)
        { foreach (var enemy in battle.Enemies) if (CurrentTarget(enemy?.Entity)) result.Add(enemy.Entity); }
        else
        { foreach (var ally in battle.Allies) if (CurrentTarget(ally?.Entity)) result.Add(ally.Entity); }
        // 场地编号：敌方从右至左1～5，我方从左至右1～4；不能一律按降序当作右至左。
        result.Sort((a, b) => owner.Side == BattleSide.Ally
            ? a.SlotPosition.CompareTo(b.SlotPosition) : b.SlotPosition.CompareTo(a.SlotPosition));
        return result;
    }

    private static void RequestRemoval(Budget budget, BattleDeclaration cause)
    {
        var data = DataManager.Instance;
        if (cause == null || data == null || !data.StatusActionDict.TryGetValue(C6Status, out var actions)) return;
        var action = actions.Find(row => row.ActionType == "OnTrigger" && ScriptHookParser.TryFind(row.ScriptHook, "Diluc_C6Spent", out _));
        if (action == null) return;
        budget.PendingRemoval = true;
        using var declaration = BattleEventHub.Enter(cause);
        budget.Owner.CharacterCtrl.TryExecuteEventStatusAction(budget.Status, action, budget.Owner,
            new ScriptHookContext { Caster = budget.Owner, Status = budget.Status, StatusHost = budget.Owner,
                Target = cause.Data.Subject, Declaration = cause }, () => budget.Valid && budget.PendingRemoval);
    }
}
