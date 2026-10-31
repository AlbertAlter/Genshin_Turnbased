using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>重云：记住原战技位置、完整普攻行为及每hit反应前的冰附着/生命比例；效果数值仍由配表执行。</summary>
public static class ChongyunScriptHooks
{
    public const string Frost = "ST_Chongyun_Frost", End = "ST_Chongyun_Rimechaser";
    public const string ThirdNormal = "ST_Chongyun_ThirdNormal", C4 = "ST_Chongyun_C4";
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<Hit> Hitting = new AsyncLocal<Hit>();
    private static readonly ConditionalWeakTable<StatusInstance, List<int>> FieldPositions = new ConditionalWeakTable<StatusInstance, List<int>>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Cast> NormalFacts = new ConditionalWeakTable<BattleDeclaration, Cast>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Hit> CryoFacts = new ConditionalWeakTable<BattleDeclaration, Hit>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> Listeners = new ConditionalWeakTable<StatusInstance, IDisposable>();
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill, Action;
        internal readonly List<int> Positions = new List<int>();
        internal int MainPosition;
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill)
        { Owner = owner; Skill = skill?.SkillID2; Action = skill?.ActionType; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    private sealed class Hit : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly long ID;
        internal readonly HashSet<BattleEntity> Cryo = new HashSet<BattleEntity>();
        internal readonly HashSet<BattleEntity> LowerHP = new HashSet<BattleEntity>();
        private readonly Hit previous = Hitting.Value;
        internal Hit(long id, BattleEntity owner, IEnumerable<BattleEntity> targets)
        {
            Owner = owner; ID = id; Hitting.Value = this;
            if (owner?.EntityID != 1025 || targets == null) return;
            float maximum = owner.GetFinalHP(); float ratio = maximum > 0 ? owner.CurrentHP / maximum : 0;
            foreach (var target in targets)
            {
                if (target == null || target.Side == owner.Side) continue;
                if (ElementQuery.HasAura(target,"Cryo")) Cryo.Add(target);
                float targetMaximum = target.GetFinalHP();
                if (targetMaximum > 0 && target.CurrentHP / targetMaximum < ratio) LowerHP.Add(target);
            }
        }
        public void Dispose() => Hitting.Value = previous;
    }
    private sealed class EmptyScope : IDisposable { public void Dispose() { } }

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterHitScope((id, owner, targets) => new Hit(id, owner, targets));
        BattleSpecialRules.RegisterEffectInvocationScope(CaptureSelection);
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterStatusLifecycle(Frost, RememberField, null);
        BattleSpecialRules.RegisterStatusLifecycle(C4, StartC4, StopListener);
        BattleSpecialRules.RegisterCount("Chongyun_BurstHits", owner => owner?.EntityID == 1025 ? (owner.ConstellationLevel >= 6 ? 4 : 3) : 0);
        BattleSpecialRules.RegisterCastCooldownModifier(CastCooldown);
        BattleSpecialRules.RegisterTargets("Chongyun_MeleeAllies()", (battle, owner) =>
        {
            var result = new List<int>();
            if (battle != null && owner?.EntityID == 1025) foreach (var ally in battle.Allies)
                if (ally?.Entity?.IsAlive == true && ally.Entity.Side == owner.Side && Melee(ally.Entity)) result.Add(ally.Entity.SlotPosition);
            result.Sort(); return result;
        });
        BattleSpecialRules.RegisterTargets("Chongyun_EndPositions()", (_, owner) =>
        {
            var root = ScriptHookContext.Current?.Status;
            return root?.Caster == owner && FieldPositions.TryGetValue(root,out var positions)
                ? new List<int>(positions) : new List<int>();
        });
        BattleSpecialRules.RegisterTargets("Chongyun_ThirdArea()", (_, owner) =>
        {
            var cause = ScriptHookContext.Current?.Declaration;
            if (cause == null || !NormalFacts.TryGetValue(cause,out var cast) || cast.Owner != owner || cast.MainPosition == 0) return new List<int>();
            var result = new List<int>(); var side = owner.Side == BattleSide.Ally ? BattleSide.Enemy : BattleSide.Ally;
            for (int p = Math.Max(1,cast.MainPosition-1); p <= Math.Min(BattlePositionSystem.GetSideCount(side),cast.MainPosition+1); p++) result.Add(p);
            return result;
        });
        BattleSpecialRules.RegisterTargets("Chongyun_HitTarget()", (battle, owner) =>
        {
            var hit = ScriptHookContext.Current?.DamageEvent; var target = hit?.Target;
            return hit?.HitLanded == true && target?.IsAlive == true && target.Side != owner?.Side
                && battle?.GetEntityByPosition(target.Side,target.SlotPosition) == target ? new List<int>{target.SlotPosition} : new List<int>();
        });
        BattleSpecialRules.RegisterCondition("Chongyun_EndHit", (a,c) => Empty(a) && c?.Caster?.EntityID == 1025
            && c.Status?.StatusID2 == End && c.DamageEvent?.HitLanded == true
            && c.DamageEvent.Source?.SourceEntity == c.Caster && c.DamageEvent.Source.SourceEffectID == "STE_Chongyun_EndDamage");
        BattleSpecialRules.RegisterCondition("Chongyun_Normal", (a,c) => Empty(a) && IsNormal(c) && c.Status.RuntimeIntValue < 3);
        BattleSpecialRules.RegisterCondition("Chongyun_Third", (a,c) => Empty(a) && IsNormal(c) && c.Status.RuntimeIntValue == 3);
        BattleSpecialRules.RegisterCondition("Chongyun_C4Hit", (a,c) => Empty(a) && c?.Status?.StatusID2 == C4
            && c.Declaration != null && CryoFacts.TryGetValue(c.Declaration,out var hit) && hit.Owner == c.Caster);
        BattleSpecialRules.RegisterCondition("Chongyun_C6HP", (a,c) => Empty(a) && c?.Caster?.EntityID == 1025
            && Hitting.Value?.Owner == c.Caster && Hitting.Value.LowerHP.Contains(c.Target));
        BattleSpecialRules.RegisterCondition("Chongyun_EndReady", (a,c) => Empty(a) && c?.Status?.StatusID2 == Frost
            && c.Caster?.IsAlive == true && c.Status.FormulaSnapshot != null && Active(c.Caster.GetStatus(End))
            && (c.Status.IsActive || (c.Declaration?.Kind == BattleEventKind.StatusRemoved
                && c.Declaration.Get<StatusEventData>()?.InstanceId == c.Status.ApplyOrder
                && (c.Declaration.Get<StatusEventData>().Reason == "Removed"
                    || c.Declaration.Get<StatusEventData>().Reason == "Replaced"))));
    }
    private static bool Empty(string argument) => string.IsNullOrEmpty(argument);
    private static bool Melee(BattleEntity actor) => actor.WeaponType == 0 || actor.WeaponType == 1 || actor.WeaponType == 3;
    internal static bool Active(StatusInstance status) => status?.Caster?.IsAlive == true && status.IsActive
        && StatusHostAccess.Contains(status.Host,status);
    internal static BattleEntity RequireOwner(StatusInstance status,object host)
    {
        if (!(host is BattleEntity owner) || owner.EntityID != 1025 || status.Caster != owner)
            throw new InvalidOperationException("重云控制状态必须挂在本人。");
        return owner;
    }
    private static int CastCooldown(BattleEntity actor,SkillMainData skill,int rounds)
    {
        if (rounds < 5 || actor?.IsAlive != true || (skill?.ActionType != "Skill" && skill?.ActionType != "Burst")) return rounds;
        var battle = BattleManager.Instance;
        if (battle == null || actor.Side != BattleSide.Ally) return rounds;
        foreach (var ally in battle.Allies)
        {
            var owner = ally?.Entity;
            if (owner?.EntityID == 1025 && owner.IsAlive && owner.ConstellationLevel >= 2
                && owner.Side == actor.Side && owner.HasStatus(Frost)) return rounds-1;
        }
        return rounds;
    }
    private static IDisposable CaptureSelection(BattleEntity owner,string effect,long execution)
    {
        var cast = Casting.Value;
        if (owner?.EntityID == 1025 && cast?.Owner == owner && effect == "SE_Skill_ChongyunDamage")
        {
            var result = BattleTargetResolver.Resolve(BattleManager.Instance,owner,owner,"Enemy",3,1,null,null,
                owner.CharacterCtrl.ForcedTargetPositions,null);
            cast.Positions.Clear(); if (result.IsValid) cast.Positions.AddRange(result.Positions);
        }
        return new EmptyScope();
    }
    private static void RememberField(StatusInstance status,object host)
    {
        var owner = RequireOwner(status,host); var cast = Casting.Value;
        if (cast?.Owner != owner || cast.Skill != "SK_Skill_Chongyun")
            throw new InvalidOperationException("重华叠霜缺少本次战技的原位置。");
        FieldPositions.Add(status,new List<int>(cast.Positions));
    }
    private static bool IsNormal(ScriptHookContext context) => context?.Status?.StatusID2 == ThirdNormal
        && context.Declaration != null && NormalFacts.TryGetValue(context.Declaration,out var cast) && cast.Owner == context.Caster;
    internal static void RequestNormal(StatusInstance status,BattleDeclaration cause)
        => Dispatch(status,"Chongyun_Normal()",cause);
    private static void Dispatch(StatusInstance status,string hook,BattleDeclaration cause)
    {
        if (!Active(status) || cause == null || DataManager.Instance == null
            || !DataManager.Instance.StatusActionDict.TryGetValue(status.StatusID2,out var actions)) return;
        foreach (var action in actions) if (action.ActionType == "OnTrigger" && action.ScriptHook == hook)
            status.Caster.CharacterCtrl.TryExecuteEventStatusAction(status,action,status.Host,
                new ScriptHookContext{Caster=status.Caster,Status=status,StatusHost=status.Host,Declaration=cause},()=>Active(status));
    }
    private static void StartC4(StatusInstance status,object host)
    {
        RequireOwner(status,host);
        Listeners.Add(status,BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,fact)=>
        { if (BattleEventHub.Current != null && CryoFacts.TryGetValue(BattleEventHub.Current,out var hit) && hit.Owner == status.Caster)
                Dispatch(status,"Chongyun_C4Hit()",BattleEventHub.Current);
        },status.ApplyOrder,()=>Active(status)));
    }
    private static void StopListener(StatusInstance status,object host)
    { if (Listeners.TryGetValue(status,out var listener)) listener.Dispose(); Listeners.Remove(status); }
    private static void Index(BattleDeclaration declaration)
    {
        var damage = declaration.Get<DamageEventData>(); var source = damage?.Source; var cast = Casting.Value;
        if (damage != null && source?.CountsAsHit == true && source.SourceEntity?.EntityID == 1025 && damage.Subject?.Side != source.SourceSide)
        {
            if (cast?.Owner == source.SourceEntity && source.SourceKind == ReactionSourceKind.CharacterSkill
                && source.SourceSkillID == cast.Skill && declaration.Header.TargetIndex == 1 && cast.MainPosition == 0)
                cast.MainPosition = damage.Subject.SlotPosition;
            var hit = Hitting.Value;
            if (damage.HitLanded && hit?.Owner == source.SourceEntity && hit.ID == declaration.Header.HitId && hit.Cryo.Contains(damage.Subject))
                CryoFacts.Add(declaration,hit);
        }
        var action = declaration.Get<ActionEventData>();
        if (declaration.Kind == BattleEventKind.ActionCompleted && action?.ActionType == "Normal" && cast?.Owner?.EntityID == 1025
            && action.Subject == cast.Owner && action.SkillId == cast.Skill) NormalFacts.Add(declaration,cast);
    }
}

/// <summary>一命：整数Count初值0、范围0～3；每次完整Normal只请求配表加1，阶段1由配表clear；0仍保留。</summary>
public sealed class ChongyunThirdNormalCount : CountStatusHandler
{
    private sealed class Memory { internal IDisposable Listener; internal long LastAction; }
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void ValidateEffectChange(StatusInstance status,BattleEntity host,CountChangeRequest request)
    { base.ValidateEffectChange(status,host,request); ChongyunScriptHooks.RequireOwner(status,host); }
    public override void ChangeFromEffect(StatusInstance status,BattleEntity host,CountChangeRequest request)
    { ValidateEffectChange(status,host,request); request.ApplyInteger(status,host,0,3); }
    public override void OnCreated(StatusInstance status,object host)
    {
        var owner = ChongyunScriptHooks.RequireOwner(status,host);
        status.RuntimeIntValue=0;status.IsPermanent=true;status.RemainingPhaseCount=0;status.TriggerPhase=1;
        var memory = new Memory();status.CountRuntimeState=memory;
        memory.Listener=BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted,(header,action)=>
        { if (action.Subject == owner && action.ActionType == "Normal" && status.RuntimeIntValue < 3
                && header.ActionExecutionId != 0 && memory.LastAction != header.ActionExecutionId)
            { memory.LastAction=header.ActionExecutionId; ChongyunScriptHooks.RequestNormal(status,BattleEventHub.Current); }
        },status.ApplyOrder,()=>ChongyunScriptHooks.Active(status));
    }
    public override void OnCreated(StatusInstance status,object host,CountStatusApplication application)
    {
        if (!string.IsNullOrEmpty(application?.Param2)) throw new InvalidOperationException("重云普攻计数初值为0，增减请使用Count效果。");
        OnCreated(status,host);
    }
    public override StatusInstance OnReapplied(StatusInstance status,BattleEntity host,CountStatusApplication application)
    {
        ChongyunScriptHooks.RequireOwner(status,host);
        if (application?.Caster != host || !string.IsNullOrEmpty(application.Param2))
            throw new InvalidOperationException("重云普攻计数重新施加只接受本人初始化，不接受数值命令。");
        return status;
    }
    public override void OnRemoved(StatusInstance status,object host)
    {
        if (status.CountRuntimeState is Memory memory) memory.Listener.Dispose();status.CountRuntimeState=null;
        int before=status.RuntimeIntValue;status.RuntimeIntValue=0;
        if(before!=0) BattleEventHub.Publish(new ResourceEventData("Count",status.StatusID2,host as BattleEntity,before,0,-before,ResourceChangeReason.Clear,true));
    }
}
