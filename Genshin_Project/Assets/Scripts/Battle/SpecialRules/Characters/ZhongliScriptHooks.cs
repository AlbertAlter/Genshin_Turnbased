using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

/// <summary>钟离的造物上限、实际承伤及共鸣来源识别；数值、行动和绑定关系仍由角色表执行。</summary>
public static class ZhongliScriptHooks
{
    public const string Stele = "ST_Zhongli_Stele", Jade = ZhongliC6Healing.JadeShieldID;
    public const string Fortify = "ST_Zhongli_Fortify", Rules = "ST_Zhongli_Rules";
    public const string Petrify = "ST_Zhongli_Petrify", Resonance = "STE_Zhongli_Resonance";
    private sealed class Memory : IDisposable
    {
        internal IDisposable Listener;
        internal int Turn = -1;
        internal readonly HashSet<long> RolledHits = new HashSet<long>();
        public void Dispose() { Listener?.Dispose(); RolledHits.Clear(); }
    }
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly HashSet<BattleEntity> BurstHits = new HashSet<BattleEntity>();
        private readonly Cast previous = Casting.Value;
        private readonly IDisposable listener;
        internal Cast(BattleEntity owner, SkillMainData skill)
        {
            Owner = owner; Skill = skill?.SkillID2; Casting.Value = this;
            if (owner?.EntityID == 1032 && Skill == "SK_Burst_Zhongli")
                listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
                {
                    if (fact.HitLanded && fact.Source?.CountsAsHit == true && fact.Source.SourceEntity == owner
                        && fact.Source.SourceEffectID == "SE_Burst_Zhongli" && fact.Subject?.Side != owner.Side)
                        BurstHits.Add(fact.Subject);
                }, 0, () => Casting.Value == this);
        }
        public void Dispose() { listener?.Dispose(); Casting.Value = previous; }
    }
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    internal static bool Active(StatusInstance status) => status?.IsActive == true && status.Caster?.IsAlive == true
        && status.Caster.EntityID == 1032 && StatusHostAccess.Contains(status.Host, status);

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterStatusLifecycle(Stele, LimitSteles, null);
        BattleSpecialRules.RegisterStatusLifecycle(Rules, StartEnergy, Stop);
        BattleSpecialRules.RegisterBattleReady(battle =>
        {
            foreach (var controller in battle.Allies)
            {
                var owner = controller?.Entity;
                if (owner?.EntityID != 1032 || !owner.IsAlive || owner.HasStatus(Rules)) continue;
                if (!DataManager.Instance.StatusMainDict.TryGetValue(Rules, out var main))
                    throw new InvalidOperationException("钟离缺少岩脊产能规则状态。");
                controller.RecordCharacterInitializationStatus(owner.AddStatus(Rules, owner, 0, 2, 0, main), 0);
            }
        });
        BattleSpecialRules.RegisterShieldBase("STE_Zhongli_CreateShield", (owner, basis) =>
        {
            if (owner?.EntityID != 1032) throw new InvalidOperationException("玉璋盾只能由钟离生成。");
            return basis * OverallShieldSystem.Members(BattleManager.Instance, owner.Side).Count;
        });
        BattleSpecialRules.RegisterBuffScalar("Zhongli_MaxHPFlat()", (effect, status) =>
        {
            if (status?.Caster?.EntityID != 1032 || !float.TryParse(effect.Param1,
                NumberStyles.Float, CultureInfo.InvariantCulture, out float coefficient))
                throw new InvalidOperationException("钟离生命值基础加算缺少合法系数。");
            return coefficient * status.Caster.GetFinalHP();
        });
        BattleSpecialRules.RegisterCondition("Zhongli_SteleEnergy", (argument, context) =>
            string.IsNullOrEmpty(argument) && RollEnergy(context));
        BattleSpecialRules.RegisterCondition("Zhongli_ShieldPaid", (argument, context) =>
            string.IsNullOrEmpty(argument) && ProtectedByJade(context));
        BattleSpecialRules.RegisterCondition("Zhongli_BeforeC4", (argument, context) =>
            string.IsNullOrEmpty(argument) && context?.Caster?.EntityID == 1032 && context.Caster.ConstellationLevel < 4);
        BattleSpecialRules.RegisterTargets("Zhongli_PetrifyTargets()", (battle, owner) =>
        {
            var cast = Casting.Value;
            if (owner?.EntityID != 1032 || cast?.Owner != owner || cast.Skill != "SK_Burst_Zhongli") return new List<int>();
            return cast.BurstHits.Where(target => target?.IsAlive == true && target.Side != owner.Side
                && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target && BelowBoss(target))
                .Select(target => target.SlotPosition).OrderBy(slot => slot).ToList();
        });
        // 石化为不能行动的普通状态，不附加冻结元素，不参与碎冰，也不只限制普通AI动作。
        BattleSpecialRules.RegisterEnemyActionBlocker(enemy => enemy.HasStatus(Petrify));
    }

    private static bool BelowBoss(BattleEntity target)
        => DataManager.Instance.EnemyMainDict.TryGetValue(target.EntityID, out var main)
            && (string.Equals(main.EnemyType, "Minion", StringComparison.OrdinalIgnoreCase)
                || string.Equals(main.EnemyType, "Elite", StringComparison.OrdinalIgnoreCase));

    private static void LimitSteles(StatusInstance state, object host)
    {
        if (!(host is FieldPosition field) || state.Caster?.EntityID != 1032 || field.Side == state.Caster.Side)
            throw new InvalidOperationException("岩脊必须由钟离施加在敌方场地。");
        // 同格MaxStack=1先由通用入口替换；此时新实例尚未入容器，仅清理超出的旧岩脊。
        var old = field.OwnerField.GetSlots(field.Side, Enumerable.Range(1, BattlePositionSystem.GetSideCount(field.Side)))
            .SelectMany(slot => slot.StatusList).Where(s => s.IsActive && s.StatusID2 == Stele && s.Caster == state.Caster)
            .OrderBy(s => s.ApplyOrder).ToList();
        int maximum = state.Caster.ConstellationLevel >= 1 ? 2 : 1;
        foreach (var previous in old.Take(Math.Max(0, old.Count - maximum + 1)))
            ((FieldPosition)previous.Host).RemoveStatusInstance(previous, "Replaced");
    }

    private static void StartEnergy(StatusInstance state, object host)
    {
        RequireOwner(state, host);
        var memory = new Memory(); state.ScriptRuntimeState = memory;
        memory.Listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
        {
            if (IsSteleHit(state, fact)) Dispatch(state, "Zhongli_SteleEnergy()");
        }, state.ApplyOrder, () => Active(state));
    }
    private static bool IsSteleHit(StatusInstance state, DamageEventData fact)
        => fact?.HitLanded == true && fact.Source?.CountsAsHit == true && fact.Source.SourceEntity == state.Caster
            && fact.Source.SourceEffectID == Resonance && fact.Subject?.Side != state.Caster.Side
            && fact.Resonance?.IsStoneStele == true && fact.Resonance.Emitter.StatusID2 == Stele;
    private static bool RollEnergy(ScriptHookContext context)
    {
        var state = context?.Status; var declaration = context?.Declaration;
        if (!Active(state) || state.StatusID2 != Rules || !IsSteleHit(state, declaration?.Get<DamageEventData>())
            || !(state.ScriptRuntimeState is Memory memory) || declaration.Header.HitId == 0) return false;
        int turn = BattleManager.Instance.TurnCount;
        if (memory.Turn != turn) { memory.Turn = turn; memory.RolledHits.Clear(); }
        if (!memory.RolledHits.Add(declaration.Header.HitId)) return false;
        float roll = BattleRandom.NextFloat01();
        LogManager.Log(LogCategory.ScriptHook, $"钟离岩脊产能 hit={declaration.Header.HitId} 概率=70% 随机={roll:F4}");
        return roll < .7f;
    }

    internal static bool ProtectedByJade(ScriptHookContext context)
    {
        var count = context?.Status;
        if (!Active(count) || count.StatusID2 != Fortify) return false;
        var jade = count.Caster.GetStatus(Jade);
        if (jade?.IsActive != true || !jade.BoundStatuses.Contains(count)) return false;
        var fact = context.Declaration?.Get<DamageEventData>();
        return fact?.OverallShieldProtections.Any(p => p.Owner == count.Caster && p.SourceStatus == jade
            && p.DamageBeforeShield > 0f) == true;
    }
    internal static void Dispatch(StatusInstance state, string hook)
    {
        if (!DataManager.Instance.StatusActionDict.TryGetValue(state.StatusID2, out var rows))
            throw new InvalidOperationException("钟离状态缺少事件行动：" + state.StatusID2);
        foreach (var row in rows)
            if (row.ActionType == "OnTrigger" && row.ScriptHook == hook)
                state.Caster.CharacterCtrl.TryExecuteEventStatusAction(state, row, state.Host,
                    new ScriptHookContext { Caster = state.Caster, Status = state, StatusHost = state.Host,
                        Target = state.Caster, Declaration = BattleEventHub.Current }, () => Active(state));
    }
    internal static BattleEntity RequireOwner(StatusInstance state, object host)
    {
        if (!(host is BattleEntity owner) || owner != state.Caster || owner.EntityID != 1032)
            throw new InvalidOperationException("钟离计数与规则状态必须挂本人。");
        return owner;
    }
    private static void Stop(StatusInstance state, object host)
    { (state.ScriptRuntimeState as IDisposable)?.Dispose(); state.ScriptRuntimeState = null; }
}

/// <summary>悬岩宸断：int初值0、范围0～5；Count增减不刷新不删除，盾绑定清理，重新造盾从0开始。</summary>
public sealed class ZhongliFortifyCount : CountStatusHandler
{
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance status, object host)
    {
        ZhongliScriptHooks.RequireOwner(status, host); status.RuntimeIntValue = 0;
        status.CountRuntimeState = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
        {
            if (fact.OverallShieldProtections.Any(p => p.Owner == status.Caster && p.SourceStatus?.StatusID2 == ZhongliScriptHooks.Jade))
                ZhongliScriptHooks.Dispatch(status, "Zhongli_ShieldPaid()");
        }, status.ApplyOrder, () => ZhongliScriptHooks.Active(status));
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    { ZhongliScriptHooks.RequireOwner(status, host); return status; }
    public override void ChangeFromEffect(StatusInstance status, BattleEntity host, CountChangeRequest request)
    { ZhongliScriptHooks.RequireOwner(status, host); request.ApplyInteger(status, host, 0, 5); }
    public override void OnRemoved(StatusInstance status, object host)
    { (status.CountRuntimeState as IDisposable)?.Dispose(); status.CountRuntimeState = null; }
}
