using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// 琴的专用索引与事件桥。数值、目标和持续时间仍在配表，执行走公共状态响应队列。
/// 普攻命中天赋使用现有OutgoingHit+Chance；此处不再抽随机或重算伤害。
/// </summary>
public static class JeanScriptHooks
{
    public const string FieldID = "ST_Jean_Field";
    public const string EnergyListenerID = "ST_Jean_C2";
    public const string FieldAttackHook = "Jean_FieldAttack()";
    public const string EnergyHook = "Jean_C2Energy()";
    private static readonly AsyncLocal<SkillScope> Current = new AsyncLocal<SkillScope>();
    private sealed class Listeners
    {
        internal readonly List<IDisposable> Items = new List<IDisposable>();
        // 同一完整行动下的多段/多目标只留一个执行身份，不借用共享连锁限次身份。
        internal readonly Dictionary<long, ActionHit> Attacks = new Dictionary<long, ActionHit>();
        internal void Dispose() { foreach (var item in Items) item.Dispose(); Items.Clear(); Attacks.Clear(); }
    }
    private sealed class ActionHit { internal BattleEntity Actor; internal string Skill; }
    private static readonly ConditionalWeakTable<StatusInstance, Listeners> Subscriptions
        = new ConditionalWeakTable<StatusInstance, Listeners>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillScope(owner => new SkillScope(owner));
        BattleSpecialRules.RegisterTargets("Jean_HeavyAirborne()", (battle, owner) => AirborneTargets(battle, owner, false));
        BattleSpecialRules.RegisterTargets("Jean_SkillAirborne()", (battle, owner) => AirborneTargets(battle, owner, true));
        BattleSpecialRules.RegisterCount("Jean_SkillEnergy", owner =>
            owner?.EntityID == 1013 && Current.Value?.Owner == owner && Current.Value.SkillHit ? 1 : 0);
        BattleSpecialRules.RegisterStatusLifecycle(FieldID, StartField, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(EnergyListenerID, StartEnergy, Stop);
        BattleSpecialRules.RegisterCondition("Jean_C2Energy", (argument, context) =>
            string.IsNullOrEmpty(argument) && EnergyMatches(context?.Status, context?.Declaration?.Get<ResourceEventData>()));
        BattleSpecialRules.RegisterCondition("Jean_FieldAttack", (argument, context) =>
            string.IsNullOrEmpty(argument) && context?.Status?.StatusID2 == FieldID
                && context.Declaration?.Kind == BattleEventKind.ActionCompleted
                && context.Declaration.Header.ActionExecutionId != 0
                && context.Declaration.Get<ActionEventData>()?.ActionType == "EnemySkill"
                && context.Declaration.Data.Subject == context.Target
                && context.Target?.IsAlive == true && context.Caster != null
                && context.Target.Side != context.Caster.Side);
        BattleSpecialRules.RegisterTargets("Jean_FieldAttacker()", (battle, owner) =>
        {
            var context = ScriptHookContext.Current;
            var target = context?.Target;
            return battle != null && owner?.EntityID == 1013 && context.Status?.StatusID2 == FieldID
                && context.Declaration?.Kind == BattleEventKind.ActionCompleted && target != null
                && target.IsAlive && target.Side != owner.Side
                && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target
                    ? new List<int> { target.SlotPosition } : new List<int>();
        });
    }

    private sealed class SkillScope : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly HashSet<BattleEntity> HeavyTargets = new HashSet<BattleEntity>();
        internal readonly HashSet<BattleEntity> SkillTargets = new HashSet<BattleEntity>();
        internal bool SkillHit;
        private readonly SkillScope previous;
        private readonly IDisposable listener;
        private bool disposed;
        internal SkillScope(BattleEntity owner)
        {
            Owner = owner; previous = Current.Value; Current.Value = this;
            try
            {
                if (owner?.EntityID == 1013)
                    listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, hit) =>
                    {
                        var source = hit.Source;
                        if (!hit.HitLanded || hit.Subject == null || hit.Subject.Side == owner.Side
                            || source.SourceEntity != owner || source.SourceKind != ReactionSourceKind.CharacterSkill) return;
                        if (source.SourceSkillID == "SK_Heavy_Jean" && source.SourceEffectID == "SE_Heavy_Jean")
                            HeavyTargets.Add(hit.Subject);
                        if (source.SourceSkillID == "SK_Skill_Jean" && source.SourceEffectID == "SE_Skill_Jean1")
                        { SkillHit = true; SkillTargets.Add(hit.Subject); }
                    }, 0, () => !disposed && ReferenceEquals(Current.Value, this));
            }
            catch { Current.Value = previous; throw; }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; listener?.Dispose(); Current.Value = previous;
        }
    }

    private static List<int> AirborneTargets(BattleManager battle, BattleEntity owner, bool skill)
    {
        var result = new List<int>();
        if (battle == null || owner?.EntityID != 1013 || Current.Value?.Owner != owner) return result;
        foreach (var target in skill ? Current.Value.SkillTargets : Current.Value.HeavyTargets)
            if (CanReceiveAirborne(battle, owner, target, skill)) result.Add(target.SlotPosition);
        result.Sort();
        return result;
    }

    private static bool CanReceiveAirborne(BattleManager battle, BattleEntity owner, BattleEntity target, bool skill)
    {
        if (battle == null || owner?.EntityID != 1013 || Current.Value?.Owner != owner || target == null
            || !(skill ? Current.Value.SkillTargets : Current.Value.HeavyTargets).Contains(target)
            || !target.IsAlive || target.Side == owner.Side
            || battle.GetEntityByPosition(target.Side, target.SlotPosition) != target) return false;
        var data = DataManager.Instance;
        if (data == null || !data.EnemyMainDict.TryGetValue(target.EntityID, out var enemy)) return false;
        bool minion = string.Equals(enemy.EnemyType, "Minion", StringComparison.OrdinalIgnoreCase);
        bool elite = string.Equals(enemy.EnemyType, "Elite", StringComparison.OrdinalIgnoreCase);
        // TargetOverride直接筛选本次实际命中、存活且位置身份未变的目标；分类空白不猜、不报错。
        return minion || (skill && owner.ConstellationLevel >= 1 && elite);
    }

    private static BattleEntity RequireOwner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.EntityID != 1013)
            throw new InvalidOperationException("琴的领域、监听及共享次数状态必须施加在琴本人。");
        return owner;
    }

    private static void StartField(StatusInstance status, object host)
    {
        var owner = RequireOwner(status, host);
        // 新领域接管减伤，旧余效不能在新领域期间继续消费，也不能叠成70%。
        foreach (var residual in owner.GetStatusInstances(JeanC6ResidualCount.ID))
            if (residual.Caster == owner) owner.RemoveStatusInstance(residual, "NewDandelionField");
        var state = new Listeners(); Subscriptions.Add(status, state);
        Func<bool> active = () => status.IsActive && owner.HasStatusInstance(status);
        state.Items.Add(BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, hit) =>
        {
            var source = hit.Source;
            // 不增加攻击行动分类，只接受该次敌方技能本体的真实命中。
            // 护盾全吸收/免疫仍算命中；未命中、持续状态和派生反应不触发。
            if (!hit.HitLanded || !source.CountsAsHit || header.ActionExecutionId == 0 || hit.Subject?.Side != owner.Side
                || source.SourceEntity == null || source.SourceSide == owner.Side
                || source.SourceKind != ReactionSourceKind.EnemySkill) return;
            state.Attacks[header.ActionExecutionId] = new ActionHit { Actor = source.SourceEntity, Skill = source.SourceSkillID };
        }, status.ApplyOrder, active));
        state.Items.Add(BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (header, action) =>
        {
            if (!state.Attacks.TryGetValue(header.ActionExecutionId, out var hit)) return;
            state.Attacks.Remove(header.ActionExecutionId);
            if (action.ActionType != "EnemySkill" || action.Subject != hit.Actor || action.SkillId != hit.Skill) return;
            Dispatch(status, host, FieldAttackHook, action.Subject);
        }, status.ApplyOrder, active));
    }

    private static bool EnergyMatches(StatusInstance status, ResourceEventData fact)
        => status?.StatusID2 == EnergyListenerID && status.Caster?.EntityID == 1013
            && status.Caster.ConstellationLevel >= 2 && status.Caster.IsAlive
            && fact?.Resource == "Energy" && fact.Subject == status.Caster
            && fact.SubjectWasOnField && fact.Added > 0d;

    private static void StartEnergy(StatusInstance status, object host)
    {
        var owner = RequireOwner(status, host);
        var state = new Listeners(); Subscriptions.Add(status, state);
        state.Items.Add(BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (header, fact) =>
        {
            if (EnergyMatches(status, fact)) Dispatch(status, host, EnergyHook, owner);
        }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status)));
    }

    private static void Dispatch(StatusInstance status, object host, string hook, BattleEntity target)
    {
        var data = DataManager.Instance;
        if (data == null || !data.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnTrigger" && action.ScriptHook == hook)
                status.Caster.CharacterCtrl.TryExecuteEventStatusAction(status, action, host, new ScriptHookContext
                {
                    Caster = status.Caster, Status = status, StatusHost = host,
                    Target = target, Declaration = BattleEventHub.Current
                }, () => status.IsActive && StatusHostAccess.Contains(host, status));
    }

    private static void Stop(StatusInstance status, object host)
    {
        if (!Subscriptions.TryGetValue(status, out var state)) return;
        state.Dispose(); Subscriptions.Remove(status);
    }
}

/// <summary>
/// 六命余效：琴本人唯一Count，初值/上限3、下限0。团队Buff是其绑定子状态。
/// 仅实际命中过己方的完整敌人行动结束后扣1；第三次行动的全部hit均享受减伤，2回合到期亦清理。
/// </summary>
public sealed class JeanC6ResidualCount : CountStatusHandler
{
    public const string ID = "ST_Jean_C6Residual";
    private sealed class Memory
    {
        internal IDisposable Listener;
        internal IDisposable Hits;
        internal readonly Dictionary<long, DamageSourceInfo> HitActions = new Dictionary<long, DamageSourceInfo>();
        internal readonly HashSet<long> Actions = new HashSet<long>();
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.EntityID != 1013)
            throw new InvalidOperationException("琴六命余效Count必须挂在琴本人。");
        status.RuntimeIntValue = 3;
        var state = new Memory(); status.CountRuntimeState = state;
        state.Hits = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, hit) =>
        {
            var source = hit.Source;
            if (!hit.HitLanded || !source.CountsAsHit || header.ActionExecutionId == 0 || hit.Subject?.Side != owner.Side
                || source.SourceEntity == null || source.SourceSide == owner.Side
                || source.SourceKind != ReactionSourceKind.EnemySkill) return;
            state.HitActions[header.ActionExecutionId] = source;
        }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status));
        state.Listener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (header, action) =>
        {
            if (header.ActionExecutionId == 0
                || action.ActionType != "EnemySkill" || action.Subject == null || action.Subject.Side == owner.Side) return;
            if (!state.HitActions.TryGetValue(header.ActionExecutionId, out var source)) return;
            state.HitActions.Remove(header.ActionExecutionId);
            if (source.SourceEntity != action.Subject || source.SourceSkillID != action.SkillId) return;
            if (!state.Actions.Add(header.ActionExecutionId)) return;
            int before = status.RuntimeIntValue;
            status.RuntimeIntValue = Math.Max(0, before - 1);
            BattleEventHub.Publish(new ResourceEventData("Count", ID, owner, before, status.RuntimeIntValue,
                -1, ResourceChangeReason.Spend, true, actor: action.Subject));
            if (status.RuntimeIntValue == 0) owner.RemoveStatusInstance(status, "ChargesExhausted");
        }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status));
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        host.RemoveStatusInstance(status, "Replaced");
        return application.ApplyTo(host); // 新的3次/2回合及新的真实绑定，不复用旧行动身份。
    }
    public override void OnRemoved(StatusInstance status, object host)
    {
        if (status.CountRuntimeState is Memory state) { state.Listener?.Dispose(); state.Hits?.Dispose(); }
        status.CountRuntimeState = null;
    }
}
