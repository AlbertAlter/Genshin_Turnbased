using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>行秋：保存完整技能/行为的实际命中、雨帘剑消耗和剑雨调用帧；数值、伤害、回能和治疗仍由配表执行。</summary>
public static class XingqiuScriptHooks
{
    public const string Rain = "ST_Xingqiu_RainSwords", Rainbow = "ST_Xingqiu_Rainbow", Healing = "ST_Xingqiu_Hydropathic";
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill, Action;
        internal readonly bool Enhanced;
        internal readonly HashSet<BattleEntity> Hits = new HashSet<BattleEntity>();
        internal readonly List<int> Range = new List<int>();
        internal bool SkillHit;
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill)
        { Owner=owner; Skill=skill?.SkillID2; Action=skill?.ActionType; Enhanced=LiveC4(owner); Casting.Value=this; }
        public void Dispose() => Casting.Value=previous;
    }
    private sealed class Restore : IDisposable
    { public void Dispose() { } }
    private sealed class Volley : IDisposable
    {
        internal readonly StatusInstance Root;
        internal readonly bool Strong;
        internal readonly int Swords, Slot;
        internal readonly BattleEntity Target;
        internal bool Landed;
        private readonly Volley previous=Responding.Value;
        internal Volley(StatusInstance status)
        {
            Responding.Value=this;
            var context=ScriptHookContext.Current;
            if(status?.StatusID2!=Rainbow || context?.Declaration==null
                || !NormalFacts.TryGetValue(context.Declaration,out var cast)) return;
            Root=status; Strong=status.Caster.ConstellationLevel>=6 && status.RuntimeIntValue==2;
            Swords=status.Caster.GetStatus(Rain)?.RuntimeIntValue ?? 0;
            var targets=LivingTargets(status.Caster,cast.Hits);
            if(Swords<=0 || targets.Count==0) return;
            Target=targets[targets.Count==1?0:BattleRandom.NextInt(0,targets.Count)]; Slot=Target.SlotPosition;
        }
        public void Dispose() => Responding.Value=previous;
    }
    private sealed class SwordFact { internal StatusInstance Root; internal bool Strong; }
    private sealed class Loss { internal BattleEntity Caster, Recipient; internal int Number; }
    private static readonly AsyncLocal<Cast> Casting=new AsyncLocal<Cast>();
    private static readonly AsyncLocal<Volley> Responding=new AsyncLocal<Volley>();
    private static readonly ConditionalWeakTable<BattleDeclaration,Cast> NormalFacts=new ConditionalWeakTable<BattleDeclaration,Cast>();
    private static readonly ConditionalWeakTable<BattleDeclaration,SwordFact> SwordFacts=new ConditionalWeakTable<BattleDeclaration,SwordFact>();
    private static readonly ConditionalWeakTable<ResourceEventData,Loss> Losses=new ConditionalWeakTable<ResourceEventData,Loss>();
    private static readonly ConditionalWeakTable<StatusInstance,IDisposable> Healers=new ConditionalWeakTable<StatusInstance,IDisposable>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner,skill)=>new Cast(owner,skill));
        BattleSpecialRules.RegisterStatusActionScope(status=>new Volley(status));
        BattleSpecialRules.RegisterEffectInvocationScope((owner,effect,_)=>CaptureSelection(owner,effect));
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterStatusLifecycle(Healing,StartHealing,StopHealing);
        BattleSpecialRules.RegisterCondition("Xingqiu_C4",(a,c)=>string.IsNullOrEmpty(a) && C4(c?.Caster));
        BattleSpecialRules.RegisterCondition("Xingqiu_NoC4",(a,c)=>string.IsNullOrEmpty(a) && c?.Caster?.EntityID==1023 && !C4(c.Caster));
        BattleSpecialRules.RegisterCondition("Xingqiu_SkillHit",(a,c)=>string.IsNullOrEmpty(a)
            && Casting.Value?.Owner==c?.Caster && Casting.Value.Skill=="SK_Skill_Xingqiu" && Casting.Value.SkillHit);
        BattleSpecialRules.RegisterCondition("Xingqiu_Volley",(a,c)=>string.IsNullOrEmpty(a)
            && Active(c?.Status,c?.Caster) && c.Status.StatusID2==Rainbow && c.Caster.GetStatus(Rain)?.RuntimeIntValue>0
            && NormalTargets(c.Caster,c.Declaration).Count>0);
        BattleSpecialRules.RegisterCondition("Xingqiu_VolleyLanded",(a,c)=>string.IsNullOrEmpty(a)
            && Responding.Value?.Root==c?.Status && Responding.Value.Landed && Active(c.Status,c.Caster));
        BattleSpecialRules.RegisterCondition("Xingqiu_SwordHit",(a,c)=>string.IsNullOrEmpty(a) && Sword(c)!=null);
        BattleSpecialRules.RegisterCondition("Xingqiu_StrongHit",(a,c)=>string.IsNullOrEmpty(a) && Sword(c)?.Strong==true);
        BattleSpecialRules.RegisterCondition("Xingqiu_StrongVolley",(a,c)=>string.IsNullOrEmpty(a)
            && c?.Caster?.EntityID==1023 && Responding.Value?.Root?.Caster==c.Caster && Responding.Value.Strong
            && c.HitEffectID=="STE_Xingqiu_SwordDamage");
        StatusSnapshotConditions.Register("Xingqiu_StrongVolley",true);
        BattleSpecialRules.RegisterCondition("Xingqiu_Healing",(a,c)=>string.IsNullOrEmpty(a) && Lost(c)!=null);
        BattleSpecialRules.RegisterCondition("Xingqiu_LostSwords",(a,c)=>int.TryParse(a,out int n) && Lost(c)?.Number==n);
        BattleSpecialRules.RegisterCount("Xingqiu_SwordCount",owner=>Responding.Value?.Root?.Caster==owner ? Responding.Value.Swords : 0);
        BattleSpecialRules.RegisterBuffScalar("Xingqiu_Reduction()",Reduction);
        BattleSpecialRules.RegisterTargets("Xingqiu_SkillRange()",(_,owner)=>Casting.Value?.Owner==owner
            ? new List<int>(Casting.Value.Range):new List<int>());
        BattleSpecialRules.RegisterTargets("Xingqiu_SkillCenter()",(_,owner)=>Casting.Value?.Owner==owner && Casting.Value.Range.Count>0
            ? new List<int>{Casting.Value.Range[Casting.Value.Range.Count/2]}:new List<int>());
        BattleSpecialRules.RegisterTargets("Xingqiu_VolleyTarget()",(battle,owner)=>
        {
            var frame=Responding.Value;
            return frame?.Root?.Caster==owner && frame.Target?.SlotPosition==frame.Slot && Valid(battle,owner,frame.Target)
                ? new List<int>{frame.Slot}:new List<int>();
        });
        BattleSpecialRules.RegisterTargets("Xingqiu_HitTarget()",(battle,owner)=>Valid(battle,owner,ScriptHookContext.Current?.DamageEvent?.Target)
            ? new List<int>{ScriptHookContext.Current.DamageEvent.Target.SlotPosition}:new List<int>());
        BattleSpecialRules.RegisterTargets("Xingqiu_HealTarget()",(battle,owner)=>
        {
            var loss=Lost(ScriptHookContext.Current); var target=loss?.Recipient;
            return loss?.Caster==owner && target?.IsAlive==true && battle?.GetEntityByPosition(target.Side,target.SlotPosition)==target
                ? new List<int>{target.SlotPosition}:new List<int>();
        });
    }
    private static bool LiveC4(BattleEntity owner)=>owner?.EntityID==1023 && owner.ConstellationLevel>=4 && owner.HasStatus(Rainbow);
    private static bool C4(BattleEntity owner)=>Casting.Value!=null && owner!=null && Casting.Value.Owner==owner
        ? Casting.Value.Enhanced : LiveC4(owner);
    internal static bool Active(StatusInstance status,BattleEntity owner)
        =>status?.IsActive==true && owner?.IsAlive==true && owner.HasStatusInstance(status);
    internal static BattleEntity RequireCaster(StatusInstance status)
    {
        var owner=status?.Caster;
        if(owner?.EntityID!=1023) throw new InvalidOperationException("行秋状态必须保留来源行秋。");
        return owner;
    }
    private static bool Valid(BattleManager battle,BattleEntity owner,BattleEntity target)
        =>battle!=null && owner?.IsAlive==true && target?.IsAlive==true && target.Side!=owner.Side
            && battle.GetEntityByPosition(target.Side,target.SlotPosition)==target;
    private static List<BattleEntity> LivingTargets(BattleEntity owner,IEnumerable<BattleEntity> candidates)
    {
        var result=new List<BattleEntity>();
        if(candidates!=null) foreach(var target in candidates) if(Valid(BattleManager.Instance,owner,target)) result.Add(target);
        result.Sort((a,b)=>a.SlotPosition.CompareTo(b.SlotPosition)); return result;
    }
    internal static List<BattleEntity> NormalTargets(BattleEntity owner,BattleDeclaration cause)
        =>cause!=null && NormalFacts.TryGetValue(cause,out var cast) && cast.Owner?.Side==owner?.Side
            ? LivingTargets(owner,cast.Hits):new List<BattleEntity>();
    private static IDisposable CaptureSelection(BattleEntity owner,string effect)
    {
        var cast=Casting.Value;
        // 选择帧在条件执行前建立。普通首段即使被四命条件跳过，也只保存玩家已确认的位置，不产生伤害声明。
        if(owner?.EntityID==1023 && cast?.Owner==owner && cast.Skill=="SK_Skill_Xingqiu" && effect=="SE_Skill_Xingqiu1")
        {
            var result=BattleTargetResolver.Resolve(BattleManager.Instance,owner,owner,"Enemy",3,1,null,null,
                owner.CharacterCtrl.ForcedTargetPositions,null);
            cast.Range.Clear(); if(result.IsValid) cast.Range.AddRange(result.Positions);
        }
        return new Restore();
    }
    private static void Index(BattleDeclaration declaration)
    {
        var hit=declaration.Get<DamageEventData>(); var source=hit?.Source;
        if(hit?.HitLanded==true && source?.CountsAsHit==true && hit.Subject!=null && hit.Subject.Side!=source.SourceSide)
        {
            var cast=Casting.Value;
            if(cast?.Owner!=null && cast.Owner==source.SourceEntity && source.SourceKind==ReactionSourceKind.CharacterSkill && source.SourceSkillID==cast.Skill)
            {
                if(cast.Action=="Normal") cast.Hits.Add(hit.Subject);
                if(cast.Owner.EntityID==1023 && cast.Skill=="SK_Skill_Xingqiu") cast.SkillHit=true;
            }
            var volley=Responding.Value;
            if(volley?.Root!=null && source.SourceStatus==volley.Root && source.SourceEffectID=="STE_Xingqiu_SwordDamage")
            { volley.Landed=true; SwordFacts.Add(declaration,new SwordFact{Root=volley.Root,Strong=volley.Strong}); }
        }
        var action=declaration.Get<ActionEventData>(); var current=Casting.Value;
        if(declaration.Kind==BattleEventKind.ActionCompleted && action?.ActionType=="Normal"
            && current?.Action=="Normal" && action.Subject==current.Owner && action.SkillId==current.Skill)
            NormalFacts.Add(declaration,current);
    }
    private static SwordFact Sword(ScriptHookContext context)
    {
        var declaration=context?.DamageEvent?.Declaration ?? context?.Declaration;
        return declaration!=null && SwordFacts.TryGetValue(declaration,out var fact) && fact.Root==context.Status
            && fact.Root.Caster==context.Caster ? fact:null;
    }
    private static Loss Lost(ScriptHookContext context)
        =>context?.Declaration?.Get<ResourceEventData>() is ResourceEventData fact && Losses.TryGetValue(fact,out var loss)
            && context.Status?.StatusID2==Healing && loss.Caster==context.Caster ? loss:null;
    internal static void Change(StatusInstance status,BattleEntity host,int value,ResourceChangeReason reason,bool heal=false)
    {
        int before=status.RuntimeIntValue; status.RuntimeIntValue=value;
        if(before==value) return;
        var fact=new ResourceEventData("Count",status.StatusID2,host,before,value,value-before,reason,true,actor:status.Caster);
        if(heal && before>value) Losses.Add(fact,new Loss{Caster=status.Caster,Recipient=host,Number=before-value});
        BattleEventHub.Publish(fact);
    }
    private static void StartHealing(StatusInstance status,object host)
    {
        var owner=RequireCaster(status); if(host!=owner) throw new InvalidOperationException("化水为雨监听必须初始化在行秋本人。");
        status.IsPermanent=true; status.RemainingPhaseCount=0;
        Healers.Add(status,BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,(_,fact)=>
        {
            if(Losses.TryGetValue(fact,out var loss) && loss.Caster==owner && loss.Recipient.IsAlive)
                Dispatch(status,"Xingqiu_Healing()",BattleEventHub.Current);
        },status.ApplyOrder,()=>Active(status,owner)));
    }
    private static void StopHealing(StatusInstance status,object host)
    { if(Healers.TryGetValue(status,out var listener)) listener.Dispose(); Healers.Remove(status); }
    internal static void Dispatch(StatusInstance status,string hook,BattleDeclaration cause)
    {
        var owner=status.Caster; var data=DataManager.Instance;
        if(!Active(status,owner) || data==null || !data.StatusActionDict.TryGetValue(status.StatusID2,out var actions)) return;
        foreach(var action in actions) if(action.ActionType=="OnTrigger" && action.ScriptHook==hook)
            owner.CharacterCtrl.TryExecuteEventStatusAction(status,action,owner,new ScriptHookContext
                {Caster=owner,Status=status,StatusHost=owner,Declaration=cause},()=>Active(status,owner)
                    && (hook!="Xingqiu_Volley()" || NormalTargets(owner,cause).Count>0));
    }
    private static float Reduction(StatusEffectData effect,StatusInstance status)
    {
        var owner=RequireCaster(status);
        if(status.StatusID2!=Rain || status.RuntimeIntValue<=0) return 0;
        if(!owner.CharacterCtrl.TryGetSkillLevelScalar("Xingqiu_Reduction",2,out float basis))
            throw new InvalidOperationException("雨帘剑缺少当前战技等级的减伤比例。");
        float hydro=owner.HydroDmgBonus+owner.WeaponHydroDmgBonus+owner.GetTeamDamageBonus("Hydro");
        var used=new Dictionary<string,int>();
        foreach(var buff in owner.GetEffectiveStatusList())
        {
            var main=buff?.MainData;
            if(main?.GetMultiplierPart()!="DMGBonus" || !buff.IsActive || !EquipmentStatusLifetime.CanExecute(buff)
                || !string.IsNullOrWhiteSpace(main.ApplyReactionType) || string.IsNullOrEmpty(main.ApplyElementType)
                || !Array.Exists(main.ApplyElementType.Split(';'),token=>token.Trim()=="Hydro")
                || !BattleEntity.StatusAppliesTo(main,"","","","Hydro")) continue;
            var query=new ScriptHookContext{Caster=buff.Caster,Status=buff,StatusHost=buff.Host,Recipient=owner,DamageElement="Hydro"};
            if(!ScriptHookEvaluator.Evaluate(main.ScriptHook,query)) continue;
            int count=StatusAggregation.Take(buff,used);
            if(count==0) continue;
            bool collected=false;
            foreach(var row in DataManager.Instance.StatusEffectDict.Values)
            {
                if(row.StatusEffectID/100!=main.StatusID || (row.EffectType!="Buff" && row.EffectType!="Debuff")) continue;
                if(!ScriptHookEvaluator.Evaluate(row.ScriptHook,query)) continue;
                if(HexereiStatusEnhancement.TryResolveScalar(row,buff,out float value)){hydro+=value*count;collected=true;}
            }
            if(!collected && main.MaxCount>0) used[buff.StatusID2]-=count;
        }
        return -(basis+Math.Min(.24f,.2f*Math.Max(0,hydro)));
    }
}

/// <summary>雨帘剑：各受益角色独立整数Count，初值/上限3、一命4，下限0；完整命中行为结束后扣一次。刷新不治疗。</summary>
public sealed class XingqiuRainSwordCount : CountStatusHandler
{
    private sealed class Attack { internal BattleEntity Actor; internal string Skill; }
    private sealed class Memory
    {
        internal IDisposable Hits,Actions;
        internal readonly Dictionary<long,Attack> Pending=new Dictionary<long,Attack>();
    }
    private static BattleEntity RequireHost(StatusInstance status,object host)
    {
        var source=XingqiuScriptHooks.RequireCaster(status);
        if(!(host is BattleEntity target) || target.Side!=source.Side) throw new InvalidOperationException("雨帘剑只能赋予行秋同队角色。");
        return target;
    }
    private static int Maximum(StatusInstance status)=>status.Caster.ConstellationLevel>=1?4:3;
    private static void Duration(StatusInstance status,int requested)
    {
        status.IsPermanent=false;
        status.RemainingPhaseCount=Math.Max(requested>0?requested:4,status.Caster.GetStatus(XingqiuScriptHooks.Rainbow)?.RemainingPhaseCount ?? 0);
    }
    public override void OnCreated(StatusInstance status,object host)
    {
        var target=RequireHost(status,host); var memory=new Memory(); status.CountRuntimeState=memory;
        status.RuntimeIntValue=Maximum(status); status.StackCount=1; Duration(status,status.RemainingPhaseCount);
        Func<bool> active=()=>status.IsActive && target.IsAlive && status.Caster.IsAlive && target.HasStatusInstance(status);
        memory.Hits=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(header,hit)=>
        {
            var source=hit.Source;
            if(hit.Subject!=target || !hit.HitLanded || !source.CountsAsHit || header.ActionExecutionId==0
                || (source.SourceKind!=ReactionSourceKind.CharacterSkill && source.SourceKind!=ReactionSourceKind.EnemySkill)) return;
            memory.Pending[header.ActionExecutionId]=new Attack{Actor=source.SourceEntity,Skill=source.SourceSkillID};
        },status.ApplyOrder,active);
        memory.Actions=BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted,(header,action)=>
        {
            if(!memory.Pending.TryGetValue(header.ActionExecutionId,out var attack)) return;
            memory.Pending.Remove(header.ActionExecutionId);
            if(action.Subject!=attack.Actor || action.SkillId!=attack.Skill || status.RuntimeIntValue<=0) return;
            XingqiuScriptHooks.Change(status,target,status.RuntimeIntValue-1,ResourceChangeReason.Spend,true);
            if(status.Caster.HasStatus(XingqiuScriptHooks.Rainbow))
                XingqiuScriptHooks.Change(status,target,Maximum(status),ResourceChangeReason.Gain);
            else if(status.RuntimeIntValue==0) target.RemoveStatusInstance(status,"ChargesExhausted");
        },status.ApplyOrder,active);
    }
    public override StatusInstance OnReapplied(StatusInstance status,BattleEntity host,CountStatusApplication application)
    {
        RequireHost(status,host);
        if(application?.Caster!=status.Caster || !string.IsNullOrEmpty(application.Param2))
            throw new InvalidOperationException("雨帘剑仅接受原行秋的刷新，不接受额外Count参数。");
        XingqiuScriptHooks.Change(status,host,Maximum(status),ResourceChangeReason.Gain); Duration(status,application.Duration);
        return status;
    }
    public override void OnCreated(StatusInstance status,object host,CountStatusApplication application)
    {
        if(!string.IsNullOrEmpty(application?.Param2)) throw new InvalidOperationException("雨帘剑没有额外的Count施加参数。");
        OnCreated(status,host);
    }
    public override void OnRemoved(StatusInstance status,object host)
    {
        if(status.CountRuntimeState is Memory memory){memory.Hits?.Dispose();memory.Actions?.Dispose();memory.Pending.Clear();}
        status.CountRuntimeState=null;
        bool expired=BattleEventHub.Current?.Get<StatusEventData>()?.Reason=="Expired";
        XingqiuScriptHooks.Change(status,host as BattleEntity,0,ResourceChangeReason.Clear,expired);
    }
}

/// <summary>虹剑势：本人整数Count保存六命循环0～2；普通攻击后整轮协同完成且命中才推进，每回合3次由行动表限制。</summary>
public sealed class XingqiuRainbowCount : CountStatusHandler
{
    private sealed class Memory { internal IDisposable Listener; internal long LastAction; }
    public override void OnCreated(StatusInstance status,object host)
    {
        var owner=XingqiuScriptHooks.RequireCaster(status);
        if(host!=owner) throw new InvalidOperationException("虹剑势必须赋予行秋本人。");
        status.RuntimeIntValue=0; status.StackCount=1; status.IsPermanent=false;
        status.RemainingPhaseCount=owner.ConstellationLevel>=2?5:4; var memory=new Memory();status.CountRuntimeState=memory;
        memory.Listener=BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted,(header,action)=>
        {
            if(action.ActionType!="Normal" || action.Subject?.Side!=owner.Side || header.ActionExecutionId==0
                || memory.LastAction==header.ActionExecutionId) return;
            memory.LastAction=header.ActionExecutionId;
            XingqiuScriptHooks.Dispatch(status,"Xingqiu_Volley()",BattleEventHub.Current);
        },status.ApplyOrder,()=>XingqiuScriptHooks.Active(status,owner));
    }
    public override void OnCreated(StatusInstance status,object host,CountStatusApplication application)
    {
        if(!string.IsNullOrEmpty(application?.Param2)) throw new InvalidOperationException("虹剑势Finish只允许推进现存实例。");
        OnCreated(status,host);
    }
    public override StatusInstance OnReapplied(StatusInstance status,BattleEntity host,CountStatusApplication application)
    {
        if(host!=status.Caster || application?.Caster!=host) throw new InvalidOperationException("虹剑势只能由本人刷新。");
        if(string.IsNullOrEmpty(application.Param2)){host.RemoveStatusInstance(status,"Replaced");return application.ApplyTo(host);}
        if(application.Param2!="Finish" || ScriptHookContext.Current?.Status!=status
            || !ScriptHookEvaluator.Evaluate("Xingqiu_VolleyLanded()",ScriptHookContext.Current))
            throw new InvalidOperationException("虹剑势Finish只接受本轮实际剑雨结算后的推进。");
        if(host.ConstellationLevel>=6)
            XingqiuScriptHooks.Change(status,host,(status.RuntimeIntValue+1)%3,ResourceChangeReason.Adjustment);
        return status;
    }
    public override void OnRemoved(StatusInstance status,object host)
    {
        if(status.CountRuntimeState is Memory memory) memory.Listener?.Dispose();
        status.CountRuntimeState=null; XingqiuScriptHooks.Change(status,host as BattleEntity,0,ResourceChangeReason.Clear);
    }
}
