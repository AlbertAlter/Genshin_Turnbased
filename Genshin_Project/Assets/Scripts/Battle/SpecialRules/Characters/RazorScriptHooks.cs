using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>雷泽的施放/状态作用域只保存本次真实结果；伤害、回能、状态仍由原效果执行器提交。</summary>
public static class RazorScriptHooks
{
    public const string Sigils = "ST_Razor_Sigils", Wolf = "ST_Razor_Wolf";
    public const string Press = "SK_Skill_Razor1", Hold = "SK_Skill_Razor2";
    private static readonly AsyncLocal<Cast> Current = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<Response> Responding = new AsyncLocal<Response>();
    private static readonly ConditionalWeakTable<StatusInstance, List<IDisposable>> Listeners
        = new ConditionalWeakTable<StatusInstance, List<IDisposable>>();

    private sealed class Cast : IDisposable
    {
        internal readonly Cast Previous = Current.Value;
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly HashSet<BattleEntity> PressTargets = new HashSet<BattleEntity>();
        internal int Consumed;
        internal bool Overflow;
        private readonly IDisposable listener;
        internal Cast(BattleEntity owner, SkillMainData skill)
        {
            Owner = owner; Skill = skill?.SkillID2; Current.Value = this;
            if (owner?.EntityID != 1019) return;
            listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (Skill == Press && PressHit(owner, hit)) PressTargets.Add(hit.Subject);
            }, 0, () => ReferenceEquals(Current.Value, this));
        }
        public void Dispose() { listener?.Dispose(); Current.Value = Previous; }
    }
    private sealed class Response : IDisposable
    {
        private readonly Response previous = Responding.Value;
        internal readonly StatusInstance Status;
        internal readonly bool C6OutsideWolf;
        internal Response(StatusInstance status)
        {
            Status = status; Responding.Value = this;
            C6OutsideWolf = status?.StatusID2 == "ST_Razor_C6" && OwnNormal(ScriptHookContext.Current)
                && !status.Caster.HasStatus(Wolf);
        }
        public void Dispose() => Responding.Value = previous;
    }
    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterStatusActionScope(status => new Response(status));
        BattleSpecialRules.RegisterStatusLifecycle("ST_Razor_C1", StartListener, StopListener);
        BattleSpecialRules.RegisterStatusLifecycle("ST_Razor_C6", StartListener, StopListener);
        BattleSpecialRules.RegisterCondition("Razor_PressHit", (a,c) => Empty(a) && CastFor(c?.Caster)?.PressTargets.Count > 0);
        BattleSpecialRules.RegisterCondition("Razor_PressBaseGain", (a,c) => Empty(a) && c?.Caster?.EntityID==1019
            && c.Caster.ConstellationLevel<6);
        BattleSpecialRules.RegisterCondition("Razor_PressGain", (a,c) => Empty(a) && c?.Caster?.EntityID==1019
            && (c.Caster.ConstellationLevel<6 || CastFor(c.Caster)?.PressTargets.Count>0));
        BattleSpecialRules.RegisterCondition("Razor_Overflow", (a,c) => Empty(a) && CastFor(c?.Caster)?.Overflow == true);
        BattleSpecialRules.RegisterCondition("Razor_HasSigils", (a,c) => Empty(a) && c?.Caster?.EntityID == 1019
            && c.Caster.GetStatus(Sigils)?.RuntimeIntValue > 0);
        BattleSpecialRules.RegisterCondition("Razor_Consumed", (a,c) => int.TryParse(a,out int n) && n > 0
            && CastFor(c?.Caster)?.Consumed == n);
        BattleSpecialRules.RegisterCondition("Razor_WolfRounds", (a,c) => int.TryParse(a,out int n) && n > 0
            && c?.Status?.StatusID2 == Wolf && c.Caster?.IsAlive == true && c.Status.RemainingPhaseCount == n);
        BattleSpecialRules.RegisterCondition("Razor_WolfAction", (a,c) => Empty(a) && c?.Status?.StatusID2 == Wolf
            && c.Caster?.EntityID == 1019 && c.Caster.IsAlive && c.Target == c.Caster);
        BattleSpecialRules.RegisterCondition("Razor_C4Hit", (a,c) => Empty(a) && PressHit(c?.Caster,c?.DamageEvent));
        BattleSpecialRules.RegisterCondition("Razor_NormalHit", (a,c) => Empty(a) && OwnNormal(c));
        BattleSpecialRules.RegisterCondition("Razor_C6OutsideWolf", (a,c) => Empty(a)
            && Responding.Value?.Status == c?.Status && Responding.Value?.C6OutsideWolf == true);
        BattleSpecialRules.RegisterCondition("Razor_LowEnergy", (a,c) => Empty(a) && c?.Caster?.EntityID == 1019
            && c.Caster.CurrentEnergy < .5f*c.Caster.MaxEnergy);
        BattleSpecialRules.RegisterCondition("Razor_LowTargetHP", (a,c) => Empty(a) && c?.Caster?.EntityID == 1019
            && c.Target != null && c.Target.CurrentHP < .3f*c.Target.GetFinalHP());
        BattleSpecialRules.RegisterCondition("Razor_EnergyGained", (a,c) => Empty(a) && EnergyGain(c?.Status,c?.Declaration?.Get<ResourceEventData>()));
        BattleSpecialRules.RegisterCondition("Razor_SigilsSpent", (a,c) => Empty(a) && SigilSpend(c?.Status,c?.Declaration?.Get<ResourceEventData>()));
        BattleSpecialRules.RegisterTargets("Razor_PressTargets()", (battle,owner) => Positions(battle,owner,CastFor(owner)?.PressTargets));
        BattleSpecialRules.RegisterTargets("Razor_HitTarget()", (battle,owner) =>
        {
            var hit=ScriptHookContext.Current?.DamageEvent;
            return Positions(battle,owner,hit?.Target == null ? null : new[] {hit.Target});
        });
        BattleSpecialRules.RegisterTargets("Razor_ActionTargets()", (battle,owner) =>
        {
            var result=new List<int>(); var c=ScriptHookContext.Current;
            if (owner?.EntityID != 1019 || c?.Status?.StatusID2 != Wolf || c.Target != owner || c.ActionTargetPositions == null) return result;
            foreach(int p in c.ActionTargetPositions)
            {
                var target=battle.GetEntityByPosition(owner.Side==BattleSide.Ally?BattleSide.Enemy:BattleSide.Ally,p);
                if (Valid(battle,owner,target) && !result.Contains(p)) result.Add(p);
            }
            return result;
        });
        BattleSpecialRules.RegisterStatusDamage("Razor_WolfMultiplier()", WolfDamage);
    }
    private static Task WolfDamage(StatusDamageExecution execution)
    {
        var e=execution.Effect;var owner=execution.Status?.Caster;
        if(owner?.EntityID!=1019 || execution.Status.StatusID2!=Wolf || e.StatusEffectID2!="STE_Razor_WolfDamage"
            || !string.IsNullOrEmpty(e.DamageType))throw new InvalidOperationException("雷狼协同必须保留本状态来源与空DamageType。");
        float multiplier=NormalMultiplier(owner)*TalentMultiplier(owner,3,"STE_Razor_WolfDamage")
            + (BattleManager.Instance?.TeamBonuses?.HasHexerei == true ? .7f : 0f);
        // 只准备本次基础倍率，不改共享配表；继续复用原目标、效果编号、反应、防抗与独立暴击。
        var prepared=new StatusEffectData
        {
            StatusEffectID=e.StatusEffectID,StatusEffectID2=e.StatusEffectID2,EffectIndex=e.EffectIndex,
            EffectType=e.EffectType,Element=e.Element,DamageType=e.DamageType,
            Param1=multiplier.ToString("R",CultureInfo.InvariantCulture)+"*TotalATK,1",Param2=e.Param2,Param3=e.Param3,
            TargetType=e.TargetType,TargetSelect=e.TargetSelect,TargetConsecutive=e.TargetConsecutive,
            TargetOverride=e.TargetOverride,ScriptHook=e.ScriptHook,DamageHook=e.DamageHook
        };
        return execution.ExecuteDefault(prepared);
    }
    private static bool Empty(string a) => string.IsNullOrEmpty(a);
    private static Cast CastFor(BattleEntity owner) => owner?.EntityID == 1019 && Current.Value?.Owner == owner ? Current.Value : null;
    private static bool PressHit(BattleEntity owner, DamageEventData hit)
        => IsPress(owner,hit?.Source,hit?.HitLanded==true,hit?.Subject);
    private static bool PressHit(BattleEntity owner, DamageResolvedEvent hit)
        => IsPress(owner,hit?.Source,hit?.HitLanded==true,hit?.Target);
    private static bool IsPress(BattleEntity owner,DamageSourceInfo source,bool landed,BattleEntity target)
        => owner?.EntityID==1019 && landed && source?.CountsAsHit==true && source.SourceEntity==owner
            && source.SourceKind==ReactionSourceKind.CharacterSkill && source.SourceSkillID==Press
            && source.SourceEffectID=="SE_Skill_Razor1" && target!=null && target.Side!=owner.Side;
    private static bool OwnNormal(ScriptHookContext c) => c?.Caster?.EntityID == 1019 && c.Caster.IsAlive
        && c.DamageEvent?.HitLanded == true && c.DamageEvent.DamageType == "Normal"
        && c.DamageEvent.Source?.CountsAsHit == true && c.DamageEvent.Source.SourceEntity == c.Caster
        && c.DamageEvent.Target != null && c.DamageEvent.Target.Side != c.Caster.Side;
    private static bool Valid(BattleManager battle,BattleEntity owner,BattleEntity target) => battle != null && target?.IsAlive == true
        && owner?.IsAlive == true && target.Side != owner.Side && battle.GetEntityByPosition(target.Side,target.SlotPosition)==target;
    private static List<int> Positions(BattleManager battle,BattleEntity owner,IEnumerable<BattleEntity> targets)
    {
        var result=new List<int>();if(targets!=null)foreach(var target in targets)
            if(Valid(battle,owner,target) && !result.Contains(target.SlotPosition))result.Add(target.SlotPosition);
        result.Sort();return result;
    }
    internal static void RecordConsumption(BattleEntity owner,int n) {var frame=CastFor(owner);if(frame!=null)frame.Consumed=n;}
    internal static void RecordOverflow(BattleEntity owner)
    {
        var frame=CastFor(owner);
        if(frame?.Skill==Press && owner.HasStatus(Wolf) && BattleManager.Instance?.TeamBonuses?.HasHexerei==true)frame.Overflow=true;
    }
    internal static float NormalMultiplier(BattleEntity owner)
        => TalentMultiplier(owner,0,"SE_Normal_Razor");
    private static float TalentMultiplier(BattleEntity owner,int type,string effectID)
    {
        var dm=DataManager.Instance;SkillLevelData first=null,current=null;
        int level=owner.CharacterCtrl.GetEffectiveSkillLevel(type);
        foreach(var group in dm.SkillLevelDict.Values)foreach(var row in group.Values)
        {
            if(row.CharacterID!=1019 || row.ParamID!=effectID || row.SkillType!=type)continue;
            if(row.SkillLevel==1)first=row;if(row.SkillLevel==level)current=row;
        }
        current=current??first;
        if(current==null)throw new InvalidOperationException("雷狼缺少雷泽等级倍率："+effectID);
        return HitDataParser.MergeWithLevel1(current.Hits1,first?.Hits1??current.Hits1).Multiplier;
    }
    private static BattleEntity Owner(StatusInstance status,object host)
    {
        if(!(host is BattleEntity owner)||owner!=status.Caster||owner.EntityID!=1019)throw new InvalidOperationException("雷泽专属状态必须挂在本人。");
        return owner;
    }
    private static bool EnergyGain(StatusInstance s,ResourceEventData f) => s?.StatusID2=="ST_Razor_C1" && s.Caster.IsAlive
        && f?.Subject==s.Caster && f.Resource=="Energy" && f.Added>0;
    private static bool SigilSpend(StatusInstance s,ResourceEventData f) => s?.StatusID2=="ST_Razor_C6" && s.Caster.IsAlive
        && f?.Subject==s.Caster && f.Resource=="Count" && f.PoolId==Sigils && f.Reason==ResourceChangeReason.Spend && f.Removed>0;
    private static void StartListener(StatusInstance status,object host)
    {
        var owner=Owner(status,host);var list=new List<IDisposable>();Listeners.Add(status,list);
        list.Add(BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,(_,fact)=>
        {
            if(EnergyGain(status,fact))Dispatch(status,host,"Razor_EnergyGained()");
            if(SigilSpend(status,fact))Dispatch(status,host,"Razor_SigilsSpent()");
        },status.ApplyOrder,()=>status.IsActive&&owner.HasStatusInstance(status)));
    }
    private static void Dispatch(StatusInstance status,object host,string hook)
    {
        if(!DataManager.Instance.StatusActionDict.TryGetValue(status.StatusID2,out var actions))return;
        foreach(var action in actions)if(action.ActionType=="OnTrigger"&&action.ScriptHook==hook)
            status.Caster.CharacterCtrl.TryExecuteEventStatusAction(status,action,host,new ScriptHookContext
            {Caster=status.Caster,Status=status,StatusHost=host,Target=status.Caster,Declaration=BattleEventHub.Current},
            ()=>status.IsActive&&StatusHostAccess.Contains(host,status));
    }
    private static void StopListener(StatusInstance status,object host)
    {
        if(Listeners.TryGetValue(status,out var list))foreach(var item in list)item.Dispose();Listeners.Remove(status);
    }
}

/// <summary>雷之印整数Count，初值0、范围0～3；ApplyStatus只建立/刷新，Count效果修改数值。归零保留。</summary>
public sealed class RazorSigilCount : CountStatusHandler
{
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance status,object host)=>OnCreated(status,host,null);
    public override void OnCreated(StatusInstance status,object host,CountStatusApplication application)
    {
        Require(status,host);
        RejectApplicationValue(application);
        status.StackCount=1;status.RuntimeIntValue=0;
    }
    private static void Require(StatusInstance status,object host)
    {
        if(!(host is BattleEntity owner)||owner!=status.Caster||owner.EntityID!=1019)throw new InvalidOperationException("雷之印必须由雷泽持有。");
    }
    private static void RejectApplicationValue(CountStatusApplication application)
    {
        if(!string.IsNullOrEmpty(application?.Param2))
            throw new ArgumentException("雷之印ApplyStatus不接收数值命令；增减请使用Count效果。");
    }
    public override void ValidateEffectChange(StatusInstance status,BattleEntity host,CountChangeRequest request)
    {
        base.ValidateEffectChange(status,host,request);Require(status,host);
    }
    public override void ChangeFromEffect(StatusInstance status,BattleEntity host,CountChangeRequest request)
    {
        ValidateEffectChange(status,host,request);
        int before=status.RuntimeIntValue;
        int after=request.IntegerAfter(before,0,3);
        // 保留请求溢出与实际支付快照，不将满值伪装成真实获点，也不把clear当作支付。
        if(request.Operation==CountChangeOperation.Add && request.Delta>0d && before+request.Delta>3d)
            RazorScriptHooks.RecordOverflow(host);
        if(request.Reason==ResourceChangeReason.Spend && after<before)
            RazorScriptHooks.RecordConsumption(host,before-after);
        request.ApplyInteger(status,host,0,3);
    }
    public override StatusInstance OnReapplied(StatusInstance status,BattleEntity host,CountStatusApplication application)
    {
        Require(status,host);RejectApplicationValue(application);
        if(application?.Caster!=host)throw new InvalidOperationException("雷之印只能由本人重施。");
        bool phaseChanged=status.AddInPhase!=application.AddInPhase || status.TriggerPhase!=application.TriggerPhase;
        if(phaseChanged)BattleManager.Instance?.UnregisterStatusTick(status);
        status.IsPermanent=application.Duration<=0;status.RemainingPhaseCount=application.Duration;
        status.AddInPhase=application.AddInPhase;status.TriggerPhase=application.TriggerPhase;
        if(phaseChanged)BattleManager.Instance?.RegisterStatusTick(status,host,null);
        return status; // 只刷新生命周期，保留余额、实例和施加顺序。
    }
    public override void OnRemoved(StatusInstance status,object host)
    {
        int before=status.RuntimeIntValue;status.RuntimeIntValue=0;
        if(before>0)BattleEventHub.Publish(new ResourceEventData("Count",RazorScriptHooks.Sigils,
            host as BattleEntity,before,0,-before,ResourceChangeReason.Clear,true,actor:status.Caster));
    }
}
