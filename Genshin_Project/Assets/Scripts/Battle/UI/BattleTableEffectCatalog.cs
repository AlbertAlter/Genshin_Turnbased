using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using UnityEngine;

public enum BattleTableEffectAnchor { Holder, Source, EachTarget, Group, Field }
public sealed class BattleTableEffectValueVariant {public string Key,Clip;public float Minimum;}

/// <summary>Data from the Resources worksheet, not a per-character implementation or enum.</summary>
public sealed class BattleTableEffectResource
{
    public string Key, Name, Category, OwnerName, Player, Clip, LegacyKind, RangeParts, CountMode, ShaderResource, ActorPattern, ResponseResourceKey;
    public float ClipStart, ClipEnd;
    public float[] Contacts=Array.Empty<float>();
    public int OwnerID, PrimaryPort;
    public int ValueStatusID;
    public string ValueMember;
    public bool Selectable=true;
    public bool State;
    public BattleTableEffectAnchor Anchor;
    public float Duration, LoopStart, LoopEnd, Contact, Speed, RangeScale, PartScale, Opacity, Intensity, FadeIn, FadeOut;
    public readonly Dictionary<int,string> CountVariants = new Dictionary<int,string>();
    public readonly Dictionary<string,string> ElementVariants = new Dictionary<string,string>(StringComparer.Ordinal);
    public readonly Dictionary<int,string> SourceStatusVariants = new Dictionary<int,string>();
    public readonly List<BattleTableEffectValueVariant> SourceValueVariants=new List<BattleTableEffectValueVariant>();
    public Dictionary<string,float> Options = new Dictionary<string,float>(StringComparer.Ordinal);
    public float Option(string key,float fallback)=>Options.TryGetValue(key,out var value)?value:fallback;
    public string ResolveClip(int count,string element,IReadOnlyList<int> sourceStatuses=null,IReadOnlyDictionary<string,float> sourceValues=null)
    {
        if(ValueStatusID>0&&sourceValues!=null&&sourceValues.TryGetValue(ValueStatusID.ToString(CultureInfo.InvariantCulture)+":"+ValueMember,out float value)&&!float.IsNaN(value)&&!float.IsInfinity(value))
            count=(int)Math.Max(0,Math.Min(int.MaxValue,value));
        string next=Clip;
        if(CountVariants.TryGetValue(count,out var counted))next=counted;
        else if(CountMode=="Threshold")
        {int best=-1;foreach(var pair in CountVariants)if(pair.Key<=count&&pair.Key>best){best=pair.Key;next=pair.Value;}}
        if(element!=null&&ElementVariants.TryGetValue(element,out var converted))next=converted;
        if(sourceStatuses!=null)foreach(int status in sourceStatuses)if(SourceStatusVariants.TryGetValue(status,out var matched)){next=matched;break;}
        if(sourceValues!=null)foreach(var variant in SourceValueVariants)if(sourceValues.TryGetValue(variant.Key,out float number)&&number>=variant.Minimum){next=variant.Clip;break;}
        return next;
    }
    public static float PulseMultiplier(float elapsed,float duration,float gain)
        =>duration>0&&elapsed>0&&elapsed<duration?1+Math.Max(0,gain)*(float)Math.Sin(Math.PI*elapsed/duration):1;
    public BattleTableEffectResource Copy()
    {
        var result=(BattleTableEffectResource)MemberwiseClone();
        result.Options=new Dictionary<string,float>(Options,StringComparer.Ordinal);
        return result; // Variant dictionaries are immutable after catalog validation.
    }
    public BattleTableEffectResource ForHit(int index)
    {
        if(Contacts.Length==0)return this;
        var result=Copy();int selected=(int)Option("ContactIndex",index);float contact=Contacts[Math.Max(0,selected)%Contacts.Length];
        result.ClipStart=Math.Max(ClipStart,contact-Option("SliceLead",.12f));
        result.ClipEnd=Math.Min(ClipEnd>ClipStart?ClipEnd:Duration,contact+Option("SliceTail",.24f));
        result.Contact=contact-result.ClipStart;return result;
    }
}

public sealed class BattleTableEffectBinding
{
    public int ID;
    public string ResourceKey, Trigger, Element, CountDecrease, CountIncrease;
    public int SourceStatusID;
    public bool Enabled;
    public string[] Overlays=Array.Empty<string>();
    public bool Matches(BattlePresentationResult result)
    {
        if(!string.IsNullOrEmpty(Element)&&(!(result?.Data is DamageEventData damage)||damage.Element!=Element))return false;
        if(SourceStatusID>0)
        {
            bool found=false;
            if(result?.Data is DamageEventData snapshot&&snapshot.SourceActiveStatusIDs!=null)foreach(int statusID in snapshot.SourceActiveStatusIDs)
                if(statusID==SourceStatusID){found=true;break;}
            if(!found)return false;
        }
        return true;
    }
}

public sealed class BattleTableEffectEventBinding
{
    public string Event,ResourceKey,Element;
    public int SummonerID,SubjectID;
    public bool Enabled;
}

/// <summary>One read of the published workbook; animation payloads are loaded only when played.</summary>
public sealed class BattleTableEffectCatalog
{
    public const string FileName="BattleEffectMapping.xlsx";
    public readonly Dictionary<string,BattleTableEffectResource> Resources = new Dictionary<string,BattleTableEffectResource>(StringComparer.Ordinal);
    public readonly Dictionary<int,BattleTableEffectBinding> Statuses = new Dictionary<int,BattleTableEffectBinding>();
    public readonly Dictionary<int,BattleTableEffectBinding> Effects = new Dictionary<int,BattleTableEffectBinding>();
    public readonly List<BattleTableEffectEventBinding> Events = new List<BattleTableEffectEventBinding>();
    private readonly Dictionary<string,BattleTableEffectResource> clipDefinitions=new Dictionary<string,BattleTableEffectResource>(StringComparer.Ordinal);
    public BattleTableEffectResource ForHit(BattleTableEffectResource resource,int index,int count,string element,IReadOnlyList<int> statuses,IReadOnlyDictionary<string,float> values)
    {
        string clip=resource.ResolveClip(count,element,statuses,values);
        if(clip!=resource.Clip&&clipDefinitions.TryGetValue(clip,out var definition))
        {
            resource=resource.Copy();resource.Clip=clip;resource.Duration=definition.Duration;resource.Contact=definition.Contact;
            resource.Contacts=definition.Contacts;resource.ClipStart=definition.ClipStart;resource.ClipEnd=definition.ClipEnd;
        }
        return resource.ForHit(index);
    }
    public static string RuntimePath => Path.Combine(Application.streamingAssetsPath,"Data","UE",FileName);
    public static string ClipPath(string relative)
    {
        ValidateRelative(relative);
        return Path.Combine(Application.streamingAssetsPath,"Data","UE","Clips",relative.Replace('/',Path.DirectorySeparatorChar));
    }
    private static void ValidateRelative(string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.Contains(":"))throw new InvalidDataException("特效资源必须使用发布目录内的相对路径："+relative);
        foreach(string part in relative.Replace('\\','/').Split('/'))if(part.Length==0||part==".."||part==".")throw new InvalidDataException("特效资源路径包含无效分段："+relative);
    }
    public static BattleTableEffectCatalog Load()=>ReadFile(RuntimePath);
    public static BattleTableEffectCatalog ReadFile(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        return ReadStream(stream);
    }
    public static BattleTableEffectCatalog ReadStream(Stream stream)
    {
        using var book=new ExcelPackage(stream);
        var result=new BattleTableEffectCatalog();
        var resources=book.Workbook.Worksheets["Resources"];
        // Compatibility with pre-migration tables. No source-directory fallback.
        if(resources?.Dimension==null)return result;
        var h=ExcelTableHeaders.For(resources);
        h.Require("ResourceKey","Name","Category","OwnerID","OwnerName","Player","Lifetime","Anchor","Clip","DurationSeconds","Speed","Opacity","Intensity","FadeInSeconds","FadeOutSeconds");
        string Text(int row,string key)=>h.Optional(key)>0?Convert.ToString(h.Cell(row,key).Value,CultureInfo.InvariantCulture)?.Trim()??"":"";
        float Number(int row,string key,float fallback,float min,float max)
        {
            string text=Text(row,key);if(text.Length==0)return fallback;
            if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||float.IsNaN(value)||float.IsInfinity(value)||value<min||value>max)
                throw new InvalidDataException("特效资源参数无效："+Text(row,"ResourceKey")+"/"+key);
            return value;
        }
        for(int row=h.DataStartRow;row<=resources.Dimension.End.Row;row++)
        {
            string key=Text(row,"ResourceKey");if(key.Length==0)continue;
            string player=Text(row,"Player"),lifetime=Text(row,"Lifetime");
            if(player!="Legacy"&&player!="Sequence"&&player!="Sprite"&&player!="StatePulse")throw new InvalidDataException("未知特效Player："+key);
            if(lifetime!="State"&&lifetime!="OneShot")throw new InvalidDataException("特效Lifetime须为State或OneShot："+key);
            if(!Enum.TryParse(Text(row,"Anchor"),out BattleTableEffectAnchor anchor)||!Enum.IsDefined(typeof(BattleTableEffectAnchor),anchor))
                throw new InvalidDataException("特效Anchor无效："+key);
            var item=new BattleTableEffectResource {Key=key,Name=Text(row,"Name"),Category=Text(row,"Category"),OwnerName=Text(row,"OwnerName"),
                OwnerID=(int)Number(row,"OwnerID",0,0,int.MaxValue),Player=player,LegacyKind=Text(row,"LegacyKind"),Clip=Text(row,"Clip"),
                Selectable=Number(row,"Selectable",1,0,1)>0,
                ValueStatusID=(int)Number(row,"ValueStatusID",0,0,int.MaxValue),ValueMember=Text(row,"ValueMember").Length>0?Text(row,"ValueMember"):"Value",
                State=lifetime=="State",Anchor=anchor,RangeParts=Text(row,"RangeParts"),CountMode=Text(row,"CountMode"),ShaderResource=Text(row,"ShaderResource"),ActorPattern=Text(row,"Actors"),ResponseResourceKey=Text(row,"ResponseResourceKey"),
                ClipStart=Number(row,"ClipStart",0,0,120),ClipEnd=Number(row,"ClipEnd",0,0,120),
                PrimaryPort=(int)Number(row,"PrimaryPort",0,0,4),Duration=Number(row,"DurationSeconds",1,.01f,120),
                LoopStart=Number(row,"LoopStart",0,0,120),LoopEnd=Number(row,"LoopEnd",0,0,120),Contact=Number(row,"ContactSeconds",0,0,120),
                Speed=Number(row,"Speed",1,.05f,10),RangeScale=Number(row,"RangeScale",1,.1f,10),PartScale=Number(row,"PartScale",1,.1f,10),
                Opacity=Number(row,"Opacity",1,0,1),Intensity=Number(row,"Intensity",1,0,10),FadeIn=Number(row,"FadeInSeconds",.03f,0,10),FadeOut=Number(row,"FadeOutSeconds",.2f,0,10)};
            if(item.Category!="Character"&&item.Category!="Enemy"&&item.Category!="Common")throw new InvalidDataException("特效Category无效："+key);
            if(item.Name.Length==0||item.OwnerName.Length==0||result.Resources.ContainsKey(key))throw new InvalidDataException("特效资源缺名称或ResourceKey重复："+key);
            if(player=="Sequence")
            {
                ValidateRelative(item.Clip);
                if(item.State&&!(item.LoopEnd>item.LoopStart))throw new InvalidDataException("持续特效须配置有效循环区间："+key);
            }
            else if(player=="Legacy"&&(!Enum.TryParse(item.LegacyKind,out BattleEffectKind legacy)||Enum.GetName(typeof(BattleEffectKind),legacy)!=item.LegacyKind))
                throw new InvalidDataException("旧特效适配类型无效："+key);
            if(player=="Sprite")ValidateRelative(item.Clip);
            if(item.RangeParts.Length>0)new Regex(item.RangeParts,RegexOptions.CultureInvariant);
            if(item.ActorPattern.Length>0)new Regex(item.ActorPattern,RegexOptions.CultureInvariant);
            string contacts=Text(row,"ContactList");
            if(contacts.Length>0)
            {
                var parsed=new List<float>();foreach(string part in contacts.Split(';'))
                {if(!float.TryParse(part,NumberStyles.Float,CultureInfo.InvariantCulture,out float time)||time<item.ClipStart||time>item.Duration)throw new InvalidDataException("特效接触列表无效："+key);parsed.Add(time);}
                item.Contacts=parsed.ToArray();
            }
            result.Resources.Add(key,item);
            if(player=="Sequence"&&!result.clipDefinitions.ContainsKey(item.Clip))result.clipDefinitions.Add(item.Clip,item);
        }
        foreach(var item in result.Resources.Values)
        {
            if(!string.IsNullOrEmpty(item.ResponseResourceKey)&&(!result.Resources.TryGetValue(item.ResponseResourceKey,out var state)||!state.State||state.Player=="Legacy"))
                throw new InvalidDataException("轻亮响应须引用已注册的持续资源："+item.Key);
            if(item.Player=="StatePulse"&&(item.State||string.IsNullOrEmpty(item.ResponseResourceKey)||item.Selectable))
                throw new InvalidDataException("StatePulse须引用持续资源并禁用独立预览："+item.Key);
        }
        ReadBindings(book,"ByStatusID","StatusID",true,result.Statuses,result);
        ReadBindings(book,"ByEffectID","EffectID",false,result.Effects,result);
        var eventSheet=book.Workbook.Worksheets["ByEvent"];
        if(eventSheet?.Dimension!=null)
        {
            var e=ExcelTableHeaders.For(eventSheet);e.Require("Event","ResourceKey","Enabled");
            string T(int row,string key)=>e.Optional(key)>0?e.Cell(row,key).Text.Trim():"";
            var unique=new HashSet<string>(StringComparer.Ordinal);
            for(int row=e.DataStartRow;row<=eventSheet.Dimension.End.Row;row++)
            {
                string kind=T(row,"Event"),key=T(row,"ResourceKey");if(kind.Length==0)continue;
                string enabled=T(row,"Enabled");
                if((kind!="ShieldBroken"&&kind!="Summoned"&&kind!="Death")||!result.Resources.TryGetValue(key,out var visual)||visual.State||
                    (enabled!="0"&&enabled!="1"))throw new InvalidDataException("公共特效事件配置无效："+kind+"/"+key);
                int ID(string name){string s=T(row,name);if(s.Length==0)return 0;if(!int.TryParse(s,out int n)||n<=0)throw new InvalidDataException(name+"须为正整数");return n;}
                int summoner=ID("WhenSummonerID"),subject=ID("WhenSubjectID");string element=T(row,"WhenElement");
                if(!unique.Add(kind+"/"+element+"/"+summoner+"/"+subject))throw new InvalidDataException("公共特效事件条件重复："+kind);
                result.Events.Add(new BattleTableEffectEventBinding{Event=kind,ResourceKey=key,Enabled=enabled=="1",Element=element,SummonerID=summoner,SubjectID=subject});
            }
        }
        var parameters=book.Workbook.Worksheets["Parameters"];
        if(parameters?.Dimension!=null)
        {
            var p=ExcelTableHeaders.For(parameters);p.Require("ResourceKey","Key","Value");
            string P(int row,string key)=>Convert.ToString(p.Cell(row,key).Value,CultureInfo.InvariantCulture)?.Trim()??"";
            for(int row=p.DataStartRow;row<=parameters.Dimension.End.Row;row++)
            {
                string key=P(row,"ResourceKey");if(key.Length==0)continue;
                if(!result.Resources.TryGetValue(key,out var resource)||!float.TryParse(P(row,"Value"),NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||float.IsNaN(value)||float.IsInfinity(value)||resource.Options.ContainsKey(P(row,"Key")))
                    throw new InvalidDataException("特效参数资源、键或值无效："+key+"/"+P(row,"Key"));
                resource.Options.Add(P(row,"Key"),value);
            }
        }
        var variants=book.Workbook.Worksheets["Variants"];
        if(variants?.Dimension!=null)
        {
            var v=ExcelTableHeaders.For(variants);v.Require("ResourceKey","Selector","Value","Clip");
            string V(int row,string key)=>Convert.ToString(v.Cell(row,key).Value,CultureInfo.InvariantCulture)?.Trim()??"";
            for(int row=v.DataStartRow;row<=variants.Dimension.End.Row;row++)
            {
                string key=V(row,"ResourceKey");if(key.Length==0)continue;
                if(!result.Resources.TryGetValue(key,out var resource))throw new InvalidDataException("变体引用未知ResourceKey："+key);
                string clip=V(row,"Clip");ValidateRelative(clip);
                if(V(row,"Selector")=="Count")
                {
                    if(!int.TryParse(V(row,"Value"),out int count)||count<0||resource.CountVariants.ContainsKey(count))throw new InvalidDataException("Count变体重复或无效："+key);
                    resource.CountVariants.Add(count,clip);
                }
                else if(V(row,"Selector")=="Element")
                {
                    string element=V(row,"Value");if(resource.ElementVariants.ContainsKey(element))throw new InvalidDataException("元素变体重复："+key);
                    resource.ElementVariants.Add(element,clip);
                }
                else if(V(row,"Selector")=="SourceStatus")
                {
                    if(!int.TryParse(V(row,"Value"),out int status)||status<=0||resource.SourceStatusVariants.ContainsKey(status))throw new InvalidDataException("来源状态变体重复或无效："+key);
                    resource.SourceStatusVariants.Add(status,clip);
                }
                else if(V(row,"Selector")=="SourceValue")
                {
                    string[] parts=V(row,"Value").Split(':');
                    if(parts.Length!=3||!int.TryParse(parts[0],out int id)||id<=0||parts[1].Length==0||!float.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out float minimum)||float.IsNaN(minimum)||float.IsInfinity(minimum))throw new InvalidDataException("来源数值变体无效："+key);
                    string member=id.ToString(CultureInfo.InvariantCulture)+":"+parts[1];
                    foreach(var prior in resource.SourceValueVariants)if(prior.Key==member&&prior.Minimum==minimum)throw new InvalidDataException("来源数值变体重复："+key);
                    resource.SourceValueVariants.Add(new BattleTableEffectValueVariant{Key=member,Minimum=minimum,Clip=clip});
                }
                else throw new InvalidDataException("未知特效变体选择器："+key);
            }
        }
        return result;
    }
    private static void ReadBindings(ExcelPackage book,string name,string idKey,bool state,Dictionary<int,BattleTableEffectBinding> target,BattleTableEffectCatalog catalog)
    {
        var sheet=book.Workbook.Worksheets[name];if(sheet?.Dimension==null)throw new InvalidDataException("缺少特效对应子表："+name);
        var h=ExcelTableHeaders.For(sheet);h.Require(idKey,"Enabled");
        string Text(int row,string key)=>h.Optional(key)>0?Convert.ToString(h.Cell(row,key).Value,CultureInfo.InvariantCulture)?.Trim()??"":"";
        for(int row=h.DataStartRow;row<=sheet.Dimension.End.Row;row++)
        {
            string text=Text(row,idKey);if(text.Length==0)continue;
            if(!int.TryParse(text,out int id)||id<=0||target.ContainsKey(id))throw new InvalidDataException(name+"主键重复或无效："+text);
            string enabled=Text(row,"Enabled"),key=Text(row,"ResourceKey");if(key.Length==0)key=Text(row,"EffectKind");
            if(enabled!="0"&&enabled!="1")throw new InvalidDataException(name+" Enabled须为0或1："+id);
            if(!catalog.Resources.TryGetValue(key,out var resource)||resource.State!=state)throw new InvalidDataException(name+"资源不存在或生命周期不符："+id+"/"+key);
            if(Text(row,"ResolverHook").Length>0)throw new InvalidDataException("ResolverHook尚未实现，请留空："+id);
            string condition=Text(row,"WhenSourceHasStatus");int statusID=0;
            if(condition.Length>0&&(!int.TryParse(condition,out statusID)||statusID<0))throw new InvalidDataException("来源状态条件须为数字ID："+id);
            var binding=new BattleTableEffectBinding{ID=id,ResourceKey=key,Enabled=enabled=="1",Trigger=Text(row,"Trigger"),SourceStatusID=statusID,Element=Text(row,"WhenElement"),CountDecrease=Text(row,"OnCountDecrease"),CountIncrease=Text(row,"OnCountIncrease")};
            binding.Overlays=Text(row,"OverlayResources").Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries);
            foreach(string overlay in binding.Overlays)if(!state||!catalog.Resources.TryGetValue(overlay,out var visual)||!visual.State)throw new InvalidDataException("状态附加资源无效："+overlay);
            foreach(string transition in new[]{binding.CountDecrease,binding.CountIncrease})if(!string.IsNullOrEmpty(transition)&&(!catalog.Resources.TryGetValue(transition,out var visual)||visual.State))throw new InvalidDataException("Count变化须引用单次资源："+id+"/"+transition);
            foreach(string transition in new[]{binding.CountDecrease,binding.CountIncrease})if(!string.IsNullOrEmpty(transition)&&catalog.Resources[transition].Player=="StatePulse"&&catalog.Resources[transition].ResponseResourceKey!=key)
                throw new InvalidDataException("Count轻亮须绑定同一状态主体："+id+"/"+transition);
            target.Add(id,binding);
        }
    }
}
