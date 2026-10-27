using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>香菱的整次喷火、完整普攻行为及移除原因索引；伤害、回能、赋予和快照仍由公共流程执行。</summary>
public static class XianglingScriptHooks
{
    public const string Guoba = "ST_Xiangling_Guoba", Pyronado = "ST_Xiangling_Pyronado";
    public const string Pepper = "ST_Xiangling_Pepper", FifthNormal = "ST_Xiangling_FifthNormal";
    private sealed class SkillFrame : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string SkillID, Action;
        internal BattleEntity Target;
        private readonly SkillFrame previous = Skill.Value;
        internal SkillFrame(BattleEntity owner, SkillMainData skill)
        { Owner = owner; SkillID = skill?.SkillID2; Action = skill?.ActionType; Skill.Value = this; }
        public void Dispose() => Skill.Value = previous;
    }
    private sealed class SprayFrame : IDisposable
    {
        internal readonly StatusInstance Status;
        internal bool Hit;
        private readonly SprayFrame previous = Spray.Value;
        internal SprayFrame(StatusInstance status) { Status = status; Spray.Value = this; }
        public void Dispose() => Spray.Value = previous;
    }
    private static readonly AsyncLocal<SkillFrame> Skill = new AsyncLocal<SkillFrame>();
    private static readonly AsyncLocal<SprayFrame> Spray = new AsyncLocal<SprayFrame>();
    private static readonly ConditionalWeakTable<BattleDeclaration, SkillFrame> NormalFacts
        = new ConditionalWeakTable<BattleDeclaration, SkillFrame>();
    private static readonly ConditionalWeakTable<BattleDeclaration, HashSet<StatusInstance>> Requests
        = new ConditionalWeakTable<BattleDeclaration, HashSet<StatusInstance>>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> PepperListeners
        = new ConditionalWeakTable<StatusInstance, IDisposable>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new SkillFrame(owner, skill));
        BattleSpecialRules.RegisterStatusActionScope(status => new SprayFrame(status));
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterStatusLifecycle(Pepper, StartPepper, StopPepper);
        BattleSpecialRules.RegisterCondition("Xiangling_GuobaHit", (a, c) => Empty(a)
            && c?.Caster?.EntityID == 1022 && c.Status?.StatusID2 == Guoba
            && c.DamageEvent?.HitLanded == true && c.DamageEvent.Source?.SourceStatus == c.Status
            && c.DamageEvent.Source.SourceEffectID == "STE_Xiangling_GuobaDamage");
        BattleSpecialRules.RegisterCondition("Xiangling_GuobaEnergy", (a, c) => Empty(a)
            && Active(c?.Status, c?.Caster) && c.Status.StatusID2 == Guoba
            && Spray.Value?.Status == c.Status && Spray.Value.Hit);
        BattleSpecialRules.RegisterCondition("Xiangling_Pepper", (a, c) => Empty(a)
            && c?.Status?.StatusID2 == Pepper && c.Caster?.IsAlive == true
            && c.Declaration?.Get<StatusEventData>() is StatusEventData fact
            && fact.Host == c.Caster && fact.StatusId == Guoba && PepperEnd(fact.Reason));
        BattleSpecialRules.RegisterCondition("Xiangling_Pyronado", (a, c) => Empty(a) && Requested(c, Pyronado));
        BattleSpecialRules.RegisterCondition("Xiangling_PyronadoEmpty", (a, c) => Empty(a)
            && c?.Status?.StatusID2==Pyronado && c.Status.RuntimeIntValue==0);
        BattleSpecialRules.RegisterCondition("Xiangling_FifthNormal", (a, c) => Empty(a) && Requested(c, FifthNormal));
        BattleSpecialRules.RegisterCondition("Xiangling_FifthReached", (a, c) => Empty(a) && Requested(c, FifthNormal)
            && c.Status.RuntimeIntValue==5 && HasNormalFact(c.Declaration,c.Caster));
        BattleSpecialRules.RegisterTargets("Xiangling_GuobaTargets()", (battle, owner) =>
        {
            var candidates = Enemies(battle, owner); var result = new List<int>();
            int count = ScriptHookEvaluator.Evaluate("Check(1022_T1)", new ScriptHookContext { Caster = owner }) ? 2 : 1;
            while (candidates.Count > 0 && result.Count < count)
            {
                int index = candidates.Count == 1 ? 0 : BattleRandom.NextInt(0, candidates.Count);
                result.Add(candidates[index].SlotPosition); candidates.RemoveAt(index);
            }
            return result;
        });
        BattleSpecialRules.RegisterTargets("Xiangling_GuobaHitTarget()", (battle, owner) =>
        {
            var target = ScriptHookContext.Current?.DamageEvent?.Target;
            return Valid(battle, owner, target) ? new List<int> { target.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterTargets("Xiangling_FifthTarget()", (battle, owner) =>
        {
            var cause = ScriptHookContext.Current?.Declaration;
            var target = cause != null && NormalFacts.TryGetValue(cause, out var frame) && frame.Owner == owner ? frame.Target : null;
            return Valid(battle, owner, target) ? new List<int> { target.SlotPosition } : new List<int>();
        });
    }

    private static bool Empty(string argument) => string.IsNullOrEmpty(argument);
    internal static bool Active(StatusInstance status, BattleEntity owner)
        => owner?.IsAlive == true && status?.IsActive == true && owner.HasStatusInstance(status);
    internal static BattleEntity RequireOwner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner.EntityID != 1022 || status.Caster != owner)
            throw new InvalidOperationException("香菱控制状态必须初始化在本人身上。");
        return owner;
    }
    private static bool Valid(BattleManager battle, BattleEntity owner, BattleEntity target)
        => battle != null && owner?.EntityID == 1022 && owner.IsAlive && target?.IsAlive == true
            && target.Side != owner.Side && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target;
    private static List<BattleEntity> Enemies(BattleManager battle, BattleEntity owner)
    {
        var result = new List<BattleEntity>();
        if (battle != null) foreach (var enemy in battle.Enemies)
            if (Valid(battle, owner, enemy?.Entity)) result.Add(enemy.Entity);
        result.Sort((a, b) => a.SlotPosition.CompareTo(b.SlotPosition)); return result;
    }

    private static void Index(BattleDeclaration declaration)
    {
        var hit = declaration.Get<DamageEventData>(); var source = hit?.Source;
        if (hit?.HitLanded == true && source?.CountsAsHit == true && source.SourceEntity?.EntityID == 1022
            && hit.Subject?.Side != source.SourceSide)
        {
            var spray = Spray.Value;
            if (spray?.Status?.StatusID2 == Guoba && source.SourceStatus == spray.Status
                && source.SourceEffectID == "STE_Xiangling_GuobaDamage") spray.Hit = true;
            var frame = Skill.Value;
            // 只记本次普攻的主目标，排除派生伤害、状态协同、溅射和嵌套技能。
            if (frame?.Owner == source.SourceEntity && frame.Action == "Normal" && frame.Target == null
                && source.SourceKind == ReactionSourceKind.CharacterSkill && source.SourceSkillID == frame.SkillID
                && hit.DamageType == "Normal" && declaration.Header.TargetIndex == 1) frame.Target = hit.Subject;
        }
        var action = declaration.Get<ActionEventData>(); var current = Skill.Value;
        if (declaration.Kind == BattleEventKind.ActionCompleted && action?.ActionType == "Normal"
            && current?.Owner?.EntityID == 1022 && action.Subject == current.Owner && action.SkillId == current.SkillID)
            NormalFacts.Add(declaration, current);
    }

    private static bool PepperEnd(string reason) => reason == "Expired" || reason == "Replaced";
    private static void StartPepper(StatusInstance status, object host)
    {
        var owner = RequireOwner(status, host); status.IsPermanent = true; status.RemainingPhaseCount = 0;
        PepperListeners.Add(status, BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusRemoved, (_, fact) =>
        {
            if (fact.Host == owner && fact.StatusId == Guoba && PepperEnd(fact.Reason))
                Dispatch(status, "Xiangling_Pepper()", BattleEventHub.Current);
        }, status.ApplyOrder, () => Active(status, owner)));
    }
    private static void StopPepper(StatusInstance status, object host)
    { if (PepperListeners.TryGetValue(status, out var listener)) listener.Dispose(); PepperListeners.Remove(status); }

    private static bool Requested(ScriptHookContext context, string id)
        => context?.Status?.StatusID2 == id && context.Declaration != null
            && Requests.TryGetValue(context.Declaration, out var statuses) && statuses.Contains(context.Status)
            && Active(context.Status, context.Caster);
    internal static void Request(StatusInstance status, string hook, BattleDeclaration cause)
    {
        if (cause == null) return;
        Requests.GetValue(cause, _ => new HashSet<StatusInstance>()).Add(status);
        Dispatch(status, hook, cause);
    }
    private static void Dispatch(StatusInstance status, string hook, BattleDeclaration cause)
    {
        var owner = status.Caster; var data = DataManager.Instance;
        if (!Active(status, owner) || data == null || !data.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnTrigger" && action.ScriptHook == hook)
                owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, owner,
                    new ScriptHookContext { Caster = owner, Status = status, StatusHost = owner, Declaration = cause },
                    () => Active(status, owner));
    }
    internal static bool HasNormalFact(BattleDeclaration cause, BattleEntity owner)
        => cause != null && NormalFacts.TryGetValue(cause, out var frame) && frame.Owner == owner;
    internal static void Change(StatusInstance status, BattleEntity owner, int value, ResourceChangeReason reason)
    {
        int before = status.RuntimeIntValue; status.RuntimeIntValue = value;
        if (before != value) BattleEventHub.Publish(new ResourceEventData("Count", status.StatusID2,
            owner, before, value, value - before, reason, true));
    }
}

/// <summary>旋火轮：本人整数Count，初值/上限6，四命9，下限0；每次全队Normal行为后按配表攻击，再消费1次。时间到期清零。</summary>
public sealed class XianglingPyronadoCount : CountStatusHandler
{
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void ValidateEffectChange(StatusInstance status,BattleEntity host,CountChangeRequest request)
    {
        base.ValidateEffectChange(status,host,request);XianglingScriptHooks.RequireOwner(status,host);
    }
    public override void ChangeFromEffect(StatusInstance status,BattleEntity host,CountChangeRequest request)
    {
        ValidateEffectChange(status,host,request);
        request.ApplyInteger(status,host,0,host.ConstellationLevel>=4?9:6);
        // 数值0仍保留。结束由状态行动中独立配置的RemoveStatus负责。
    }
    private sealed class Memory { internal IDisposable Listener; internal long LastAction; }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = XianglingScriptHooks.RequireOwner(status, host); var memory = new Memory();
        status.RuntimeIntValue = owner.ConstellationLevel >= 4 ? 9 : 6; status.StackCount = 1;
        status.IsPermanent = false; status.RemainingPhaseCount = owner.ConstellationLevel >= 4 ? 3 : 2;
        status.CountRuntimeState = memory;
        memory.Listener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (header, fact) =>
        {
            if (fact.ActionType != "Normal" || fact.Subject?.Side != owner.Side
                || header.ActionExecutionId == 0 || memory.LastAction == header.ActionExecutionId || status.RuntimeIntValue <= 0) return;
            memory.LastAction = header.ActionExecutionId;
            XianglingScriptHooks.Request(status, "Xiangling_Pyronado()", BattleEventHub.Current);
        }, status.ApplyOrder, () => XianglingScriptHooks.Active(status, owner));
    }
    public override void OnCreated(StatusInstance status, object host, CountStatusApplication application)
    {
        if (!string.IsNullOrEmpty(application?.Param2))
            throw new InvalidOperationException("旋火轮ApplyStatus不接收数值命令；增减请使用Count效果。");
        OnCreated(status, host);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        XianglingScriptHooks.RequireOwner(status, host);
        if (application?.Caster != host) throw new InvalidOperationException("旋火轮不能从其他角色合并次数。");
        if (!string.IsNullOrEmpty(application.Param2))
            throw new InvalidOperationException("旋火轮ApplyStatus不接收数值命令；增减请使用Count效果。");
        host.RemoveStatusInstance(status, "Replaced");
        return application.ApplyTo(host);
    }
    public override void OnRemoved(StatusInstance status, object host)
    {
        if (status.CountRuntimeState is Memory memory) memory.Listener?.Dispose();
        status.CountRuntimeState = null;
        XianglingScriptHooks.Change(status, host as BattleEntity, 0, ResourceChangeReason.Clear);
    }
}

/// <summary>大火宽油：本人永久整数Count，初值0、范围0～5；每个我方回合只在第五次完整Normal行为施加一次内爆。</summary>
public sealed class XianglingFifthNormalCount : CountStatusHandler
{
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void ValidateEffectChange(StatusInstance status,BattleEntity host,CountChangeRequest request)
    {
        base.ValidateEffectChange(status,host,request);XianglingScriptHooks.RequireOwner(status,host);
    }
    public override void ChangeFromEffect(StatusInstance status,BattleEntity host,CountChangeRequest request)
    {
        ValidateEffectChange(status,host,request);request.ApplyInteger(status,host,0,5);
    }
    private sealed class Memory { internal IDisposable Listener; internal long LastAction; }
    public override void OnCreated(StatusInstance status,object host,CountStatusApplication application)
    {
        if(!string.IsNullOrEmpty(application?.Param2))
            throw new InvalidOperationException("大火宽油ApplyStatus不接收数值命令；请用Count效果。");
        OnCreated(status,host);
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = XianglingScriptHooks.RequireOwner(status, host);
        var memory = new Memory();
        status.RuntimeIntValue = 0; status.StackCount = 1; status.IsPermanent = true; status.RemainingPhaseCount = 0;
        status.TriggerPhase = 1; status.CountRuntimeState = memory;
        memory.Listener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (header, fact) =>
        {
            if (fact.Subject != owner || fact.ActionType != "Normal" || header.ActionExecutionId == 0
                || memory.LastAction == header.ActionExecutionId) return;
            memory.LastAction = header.ActionExecutionId;
            var cause = BattleEventHub.Current;
            if (status.RuntimeIntValue >= 5) return;
            // 每次完整本人普攻只提交一次请求，配表先Count+1，再按第五次和真实主目标施加内爆。
            XianglingScriptHooks.Request(status, "Xiangling_FifthNormal()", cause);
        }, status.ApplyOrder, () => XianglingScriptHooks.Active(status, owner));
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        XianglingScriptHooks.RequireOwner(status, host);
        if (application?.Caster != host || !string.IsNullOrEmpty(application.Param2))
            throw new InvalidOperationException("大火宽油ApplyStatus只接受本人初始化；数值修改请使用Count效果。");
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host)
    {
        if (status.CountRuntimeState is Memory memory) memory.Listener?.Dispose();
        status.CountRuntimeState = null; XianglingScriptHooks.Change(status, host as BattleEntity, 0, ResourceChangeReason.Clear);
    }
}
