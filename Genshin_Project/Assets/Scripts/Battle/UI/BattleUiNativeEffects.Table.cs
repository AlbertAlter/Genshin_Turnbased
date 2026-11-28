using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class BattleUiNativeEffects
{
    private BattleTableEffectCatalog Tables=>BattleNativeEffectHooks.TableCatalog;
    private readonly HashSet<string> failedTableResources=new HashSet<string>(StringComparer.Ordinal);
    private void PulseRelatedState(BattleTableEffectResource resource,BattlePresentationCue cue)
    {
        long instance=(cue.Result.Data as DamageEventData)?.Source.SourceStatusApplyOrder??0;
        var source=Card(cue.Result.SourceSide,cue.Result.SourceSlot);
        foreach(var fx in effects)
            if(fx.Active&&fx.Sustained&&fx.ResourceKey==resource.ResponseResourceKey&&
                (instance>0?fx.Instances.ContainsKey(instance):source!=null&&fx.Instances.ContainsValue(source)))
                fx.View.PulseTable(resource.Option("PulseGain",.3f),resource.Player=="StatePulse"?resource.Duration:resource.Option("PulseSeconds",.35f));
    }
    private Fx PlayTable(BattleTableEffectResource resource,Targets group,long playback,BattlePresentationCue cue,Action<Exception> finish,int count=-1,string element=null,bool gate=false,bool blockHits=true)
    {
        var damage=cue?.Result.Data as DamageEventData;
        if(cue!=null&&!resource.State)resource=Tables.ForHit(resource,cue.VisualHitIndex,count,element,damage?.SourceActiveStatusIDs,damage?.SourceStatusValues);
        if(catalog==null||layout?.Canvas==null||group.Cards.Count==0)throw new InvalidOperationException("配表特效缺少实际卡位或公共Shader："+resource.Key);
        Fx fx=null;
        foreach(var candidate in effects)if(!candidate.Active&&candidate.ResourceKey==resource.Key){fx=candidate;break;}
        if(fx==null)
        {
            var host=new GameObject("TableEffect/"+resource.Key,typeof(UIBattleEffectPlayer));host.transform.SetParent(transform,false);
            fx=new Fx{ResourceKey=resource.Key,View=host.GetComponent<UIBattleEffectPlayer>()};effects.Add(fx);
            if(!fx.View.BindTableEffect(layout.Canvas.transform as RectTransform,layout.GroupHitLayer(true),layout.GroupHitLayer(false),catalog.shader,resource))
            {string error=fx.View.Error;effects.Remove(fx);Destroy(host);throw new InvalidOperationException(error);}
        }
        fx.View.automaticClock=true;fx.View.Paused=false;
        fx.View.SetTableParameters(resource);
        float lead=resource.Contact/resource.Speed;
        float offset=gate?(float)cue.ProjectileSeconds-lead:0;
        if(!fx.View.TryPlayTable(group.Cards,group.Caster,Math.Max(0,offset),gate,count,element,damage?.SourceActiveStatusIDs,damage?.SourceStatusValues))throw new InvalidOperationException(fx.View.Error);
        fx.TableContact=lead;
        if(offset<0)fx.View.Seek(-offset);
        fx.Active=true;fx.Loop=fx.Sustained=resource.State;fx.Playback=playback;fx.Finish=finish;
        fx.BlocksHits=blockHits;
        fx.Execution=cue!=null?Execution(cue):default;
        fx.Batch=cue?.Result.StatusBatchID??0;fx.Lane=cue?.StatusBatchLane??0;
        fx.Hit=cue?.Result.Data is DamageEventData?NativeHitKey(cue):default;
        fx.Instances.Clear();foreach(var pair in group.Instances)fx.Instances.Add(pair.Key,pair.Value);
        fx.Counts.Clear();foreach(long instance in group.Instances.Keys)if(count>=0)fx.Counts.Add(instance,count);
        return fx;
    }
    private bool TryPresentTableEffect(long playback,BattlePresentationCue cue,string key,Action<Exception> completed)
    {
        if(Tables==null||!isActiveAndEnabled||(cue.SkillTracks&BattleSkillTracks.HitEffects)==0||cue.IsEnemyExit)return false;
        BattleTableEffectResource resource=null;
        if(!string.IsNullOrEmpty(key))Tables.Resources.TryGetValue(key,out resource);
        if(resource==null&&Tables.Effects.TryGetValue(BattleEffectIDMapping.NumericID(cue.Result.EffectID),out var binding))
        {
            if(!binding.Enabled){completed(null);return true;}
            if(!binding.Matches(cue.Result))return false;
            if(!string.IsNullOrEmpty(binding.Trigger)&&binding.Trigger!=cue.Result.Data.Kind.ToString())return false;
            if(BattleReactionEffectPlan.TryGetReaction(cue.Result,out _))return false;
            resource=Tables.Resources[binding.ResourceKey];
        }
        if(resource==null||resource.Player=="Legacy"||resource.State)return false;
        if(!(cue.Result.Data is DamageEventData)&&!(cue.Result.Data is HealEventData)&&cue.Result.Data.Kind!=BattleEventKind.StatusAdded&&cue.Result.Data.Kind!=BattleEventKind.Summoned&&cue.Result.Data.Kind!=BattleEventKind.Withdrawn)return false;
        if(failedTableResources.Contains(resource.Key)){completed(null);return true;}
        if(cue.IsAttackStart){completed(null);return true;}
        bool damage=cue.Result.Data is DamageEventData;
        if(damage&&(!((DamageEventData)cue.Result.Data).HitLanded||cue.HitResults!=null&&!cue.LeadsHit)){completed(null);return true;}
        if(!damage&&cue.IsAnticipation){completed(null);return true;}
        var execution=Execution(cue);long hit=damage?NativeHitKey(cue).Item4:0;
        var once=(playback,execution.Item1,execution.Item2,execution.Item3,hit,resource.Key);
        if(resource.Player=="StatePulse")
        {
            if(started.Add(once))PulseRelatedState(resource,cue);
            completed(null);return true;
        }
        if(!cue.IsAnticipation&&!string.IsNullOrEmpty(resource.ResponseResourceKey)&&started.Add((playback,execution.Item1,execution.Item2,execution.Item3,hit,"response/"+resource.Key)))PulseRelatedState(resource,cue);
        Fx shown=null;
        foreach(var fx in effects)if(fx.Active&&fx.ResourceKey==resource.Key&&fx.Playback==playback&&fx.Execution==execution&&(!damage||fx.Hit==NativeHitKey(cue))){shown=fx;break;}
        if(shown==null&&started.Add(once))
        {
            var group=damage?ForHit(cue):For(cue);
            try{if(group.Cards.Count>0)shown=PlayTable(resource,group,playback,cue,null,element:(cue.Result.Data as DamageEventData)?.Element,gate:cue.IsAnticipation);}
            catch(Exception error){failedTableResources.Add(resource.Key);Debug.LogWarning("配表特效未播放："+resource.Key+" / "+error.Message,this);completed(null);return true;}
            if(shown!=null&&!cue.IsAnticipation&&shown.TableContact>0)shown.View.Seek(shown.TableContact);
        }
        if(shown==null||cue.IsAnticipation){completed(null);return true;}
        shown.View.ConfirmContact();
        if((!damage||cue.VisualHitIndex+1==cue.VisualHitCount)&&shown.Finish==null)shown.Finish=completed;else completed(null);
        return true;
    }
    private List<RectTransform> SideCards(BattleSide side)
    {
        var cards=new List<RectTransform>();int n=side==BattleSide.Ally?BattleField.ALLY_SLOTS:BattleField.ENEMY_SLOTS;
        for(int i=1;i<=n;i++)if(layout.TryGetCard(side,i,out var binding)&&binding.root!=null)cards.Add(binding.root);
        return cards;
    }
    private static StatusInstance CurrentStatus(object host,long instance)
    {
        if(host is BattleEntity entity){foreach(var s in entity.StatusDict.Values)if(s.ApplyOrder==instance)return s;}
        else if(host is FieldPosition field){foreach(var s in field.StatusList)if(s.ApplyOrder==instance)return s;}
        else if(host is BattleTeamStatusHost team){foreach(var s in team.GetStatusList())if(s.ApplyOrder==instance)return s;}
        return null;
    }
    private static int VisualCount(StatusInstance status,BattleTableEffectResource resource)
        =>!string.IsNullOrEmpty(resource.CountMode)&&resource.CountMode!="None"&&status!=null?Math.Max(0,(int)Math.Min(int.MaxValue,status.RuntimeValue)):-1;
    private bool EnsureTableState(BattleTableEffectResource resource,StatusInstance status,object host,BattleSide? side,int slot,long instance,long playback,BattlePresentationCue cue)
    {
        if(failedTableResources.Contains(resource.Key))return false;
        if(!side.HasValue)return false;
        bool team=host is BattleTeamStatusHost||resource.Option("UseSideBounds",0)>0;
        var card=StatusHostCard(host,side,slot);
        if(!team&&card==null)return false;
        var cards=team?SideCards(side.Value):new List<RectTransform>{card};
        if(cards.Count==0)return false;
        int count=VisualCount(status,resource);
        foreach(var fx in effects)
        {
            if(!fx.Active||!fx.Loop||fx.ResourceKey!=resource.Key)continue;
            if(fx.Instances.ContainsKey(instance))
            {
                fx.Instances[instance]=cards[0];
                if(team)fx.View.SetTargets(cards);
                else if(resource.Anchor==BattleTableEffectAnchor.Field||resource.Option("MergeBySide",0)>0)fx.View.SetTargets(new List<RectTransform>(new HashSet<RectTransform>(fx.Instances.Values)));
                else fx.View.SetTargets(cards);
                fx.View.SetTableVariables(count);UpdateTableCount(fx,resource,status,instance,cards,count);return true;
            }
            if((resource.Anchor==BattleTableEffectAnchor.Field||resource.Option("MergeBySide",0)>0)&&fx.ScopeSide==side)
            {
                fx.Instances.Add(instance,cards[0]);fx.View.SetTargets(new List<RectTransform>(new HashSet<RectTransform>(fx.Instances.Values)));return true;
            }
        }
        var group=new Targets {Caster=status?.Caster!=null?Card(status.Caster.Side,status.Caster.SlotPosition):null};
        group.Cards.AddRange(cards);group.Instances.Add(instance,cards[0]);
        try{var created=PlayTable(resource,group,playback,cue,null,count);created.ScopeSide=side;return true;}
        catch(Exception error){failedTableResources.Add(resource.Key);Debug.LogWarning("配表状态特效未播放："+resource.Key+" / "+error.Message,this);return false;}
    }
    private void UpdateTableCount(Fx fx,BattleTableEffectResource resource,StatusInstance status,long instance,List<RectTransform> cards,int count)
    {
        if(count<0)return;
        bool had=fx.Counts.TryGetValue(instance,out int before);fx.Counts[instance]=count;
        if(!had||before==count||status==null||!Tables.Statuses.TryGetValue(status.MainData?.StatusID??0,out var binding))return;
        string key=count<before?binding.CountDecrease:binding.CountIncrease;
        if(string.IsNullOrEmpty(key)||!Tables.Resources.TryGetValue(key,out var transition))return;
        if(transition.Player=="StatePulse"){if(fx.ResourceKey==transition.ResponseResourceKey)fx.View.PulseTable(transition.Option("PulseGain",.3f),transition.Duration);return;}
        var group=new Targets();group.Cards.AddRange(cards);
        try{PlayTable(transition,group,-1,null,null,count,blockHits:false);}catch(Exception error){Debug.LogWarning("Count显示变化未播放："+error.Message,this);}
    }
    private void PresentTableCountExit(StatusEventData removed,long playback,BattlePresentationCue cue)
    {
        if(Tables==null||removed.CountValue!=0||!Tables.Statuses.TryGetValue(removed.StatusNumericId,out var binding)||!binding.Enabled||string.IsNullOrEmpty(binding.CountDecrease))return;
        foreach(var fx in effects.ToArray())
        {
            if(!fx.Active||!fx.Instances.TryGetValue(removed.InstanceId,out var card)||!fx.Counts.TryGetValue(removed.InstanceId,out int before)||before<=0||card==null)continue;
            var transition=Tables.Resources[binding.CountDecrease];
            if(transition.Player=="StatePulse"){if(fx.ResourceKey==transition.ResponseResourceKey)fx.View.PulseTable(transition.Option("PulseGain",.3f),transition.Duration);}
            else{var group=new Targets();group.Cards.Add(card);PlayTable(transition,group,playback,cue,null,0,blockHits:false);}
            fx.Counts[removed.InstanceId]=0;
        }
    }
    private bool TryPresentTableState(long playback,BattlePresentationCue cue,Action<Exception> completed)
    {
        if(Tables==null||!isActiveAndEnabled||(cue.SkillTracks&BattleSkillTracks.HitEffects)==0||cue.IsAttackStart||cue.IsAnticipation||
            !(cue.Result.Data is StatusEventData added)||added.Kind!=BattleEventKind.StatusAdded||!Tables.Statuses.TryGetValue(added.StatusNumericId,out var binding))return false;
        if(!binding.Enabled){completed(null);return true;}
        var resource=Tables.Resources[binding.ResourceKey];if(resource.Player=="Legacy")return false;
        if(added.Host is FieldPosition field)observedField=field.OwnerField;
        if(added.Host is BattleEntity entity&&entity.Position!=null)observedField=entity.Position.OwnerField;
        EnsureTableState(resource,CurrentStatus(added.Host,added.InstanceId),added.Host,added.HostSide,added.HostSlot,added.InstanceId,playback,cue);
        foreach(string overlay in binding.Overlays)EnsureTableState(Tables.Resources[overlay],CurrentStatus(added.Host,added.InstanceId),added.Host,added.HostSide,added.HostSlot,added.InstanceId,playback,cue);
        if(Tables.Effects.TryGetValue(BattleEffectIDMapping.NumericID(cue.Result.EffectID),out var effect)&&effect.Enabled)return false;
        completed(null);return true;
    }
    private void CollectTableStates(BattleField field,HashSet<long> live)
    {
        if(Tables==null)return;
        void Collect(StatusInstance status,object host,BattleSide side,int slot)
        {
            if(!status.IsActive||!Tables.Statuses.TryGetValue(status.MainData?.StatusID??0,out var binding)||!binding.Enabled)return;
            var resource=Tables.Resources[binding.ResourceKey];if(resource.Player=="Legacy")return;
            if(EnsureTableState(resource,status,host,side,slot,status.ApplyOrder,-1,null))live.Add(status.ApplyOrder);
            foreach(string overlay in binding.Overlays)if(EnsureTableState(Tables.Resources[overlay],status,host,side,slot,status.ApplyOrder,-1,null))live.Add(status.ApplyOrder);
        }
        void Slot(FieldPosition position)
        {
            foreach(var status in position.StatusList)Collect(status,position,position.Side,position.SlotIndex);
            if(position.Occupant?.IsOnField==true)foreach(var status in position.Occupant.StatusDict.Values)Collect(status,position.Occupant,position.Side,position.SlotIndex);
        }
        foreach(var position in field.AllySlots)Slot(position);foreach(var position in field.EnemySlots)Slot(position);
        if(battle!=null)foreach(var side in new[]{BattleSide.Ally,BattleSide.Enemy})
        {var team=battle.GetExistingTeam(side);if(team!=null)foreach(var status in team.GetStatusList())Collect(status,team,side,0);}
    }
    private void PresentTableEvents(long playback,BattlePresentationCue cue)
    {
        if(Tables==null||!isActiveAndEnabled||cue.IsAnticipation||cue.IsAttackStart||cue.IsEnemyExit||(cue.SkillTracks&BattleSkillTracks.HitEffects)==0)return;
        string kind=cue.Result.Data.Kind.ToString();
        var elements=new HashSet<string>(StringComparer.Ordinal);
        if(cue.Result.Data is ShieldChangedEventData shields)
        {
            if(shields.Reason!="Absorption")return;
            foreach(var before in shields.Before)
            {
                if(before.Value<=0)continue;
                bool survives=false;foreach(var after in shields.After)if(after.ShieldId==before.ShieldId&&after.Value>0){survives=true;break;}
                if(!survives)elements.Add(before.Element);
            }
            if(elements.Count==0)return;kind="ShieldBroken";
        }
        int summoner=cue.Result.Data is DeathEventData death?death.SummonerID:cue.Result.Data is SummonedEventData summon?summon.SummonerID:0;
        // An explicit per-effect summon animation already contains the arrival. Do not stack a generic one.
        if(kind=="Summoned"&&Tables.Effects.TryGetValue(BattleEffectIDMapping.NumericID(cue.Result.EffectID),out var explicitBinding)&&explicitBinding.Enabled)return;
        foreach(var binding in Tables.Events)
        {
            if(!binding.Enabled||binding.Event!=kind||binding.SummonerID>0&&binding.SummonerID!=summoner||binding.SubjectID>0&&binding.SubjectID!=cue.Result.Data.SubjectId||
                !string.IsNullOrEmpty(binding.Element)&&!elements.Contains(binding.Element))continue;
            if(!started.Add((playback,cue.Result.Header.SessionId,cue.Result.Header.EventId,"event",0,binding.ResourceKey)))continue;
            var card=Card(cue.Result.TargetSide,cue.Result.TargetSlot);if(card==null)continue;
            var group=new Targets{Caster=Card(cue.Result.SourceSide,cue.Result.SourceSlot)};group.Cards.Add(card);
            try{PlayTable(Tables.Resources[binding.ResourceKey],group,playback,cue,null,blockHits:false);}
            catch(Exception error){if(failedTableResources.Add(binding.ResourceKey))Debug.LogWarning("公共配表特效未播放："+binding.ResourceKey+" / "+error.Message,this);}
        }
    }
}
