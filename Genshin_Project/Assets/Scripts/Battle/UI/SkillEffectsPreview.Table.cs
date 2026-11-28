using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public sealed partial class SkillEffectsPreview
{
#if UNITY_EDITOR
    public static Func<BattleTableEffectResource,string> WriteTableParameters;
#endif
    private BattleTableEffectCatalog tableCatalog;
    private BattleTableEffectResource tableResource;
    private readonly Dictionary<string,BattleTableEffectResource> tableTuning=new Dictionary<string,BattleTableEffectResource>(StringComparer.Ordinal);
    private int tableCount=3;
    private string tableElement;
    private string CurrentTableKey=>tableResource?.Key??kind.ToString();
    private void LoadTablePicker()
    {
        tableCatalog=BattleTableEffectCatalog.Load();
        var entries=new List<UIEffectPicker.Entry>();
        foreach(var r in tableCatalog.Resources.Values)
        {
            if(!r.Selectable)continue;
            string category=r.Category=="Character"?"角色":r.Category=="Enemy"?"敌人":"公共";
            string type=r.State?"持续状态":"单次行动";
            entries.Add(new UIEffectPicker.Entry{Key=r.Key,Label=r.OwnerName+" · "+r.Name,Detail=r.Key+" · "+type,
                Category=category,Type=type,OwnerKey=r.Category+"/"+r.OwnerID,OwnerLabel=r.OwnerName,
                SearchText=r.OwnerName+" "+r.Name+" "+r.OwnerID+" "+r.Key+" "+category+" "+type});
        }
        effectPicker.SetEntries(entries,new[]{"全部","角色","敌人","公共"},new[]{"全部","持续状态","单次行动"});
    }
    private void SelectTable(string key)
    {
        if(!tableCatalog.Resources.TryGetValue(key,out var resource)){message="总表没有该资源："+key;return;}
        if(resource.Player=="Legacy"){SelectEffect((BattleEffectKind)Enum.Parse(typeof(BattleEffectKind),resource.LegacyKind));return;}
        if(tableResource!=null)tableTuning[tableResource.Key]=tableResource.Copy();
        fullPreview.Stop(true);ventiPreviewFlip?.Cancel(-1);player.Stop(true);burningAreaPlayer.Stop(true);
        fullAnimation=false;parameters=burningAreaParameters=null;
        kind=BattleEffectKind.Explosion;choosing=false;edits.Clear();scroll=Vector2.zero;error=message=null;
        tableResource=tableTuning.TryGetValue(key,out var tuned)?tuned.Copy():resource.Copy();
        if(tableResource.Anchor==BattleTableEffectAnchor.Holder){targetCount=1;manual=false;}
        if(!player.BindTableEffect(stage,behind,above,effectShader,tableResource)){Fail(player.Error);return;}
        player.automaticClock=false;ready=true;SetPaused(false);Replay();
    }
    private void ReplayTable()
    {
        player.SetTableParameters(tableResource);
        if(!player.TryPlayTable(selected,Caster(),0,false,tableCount,tableElement)){error=player.Error;loop=false;return;}
        error=null;SetPaused(paused);
    }
    private bool ReloadTable()
    {
        string key=tableResource.Key;
        try{LoadTablePicker();tableTuning.Remove(key);tableResource=null;SelectTable(key);message="已重读 Data/UE/"+BattleTableEffectCatalog.FileName;return true;}
        catch(Exception e){message="重读失败，保留当前配置："+e.Message;return false;}
    }
    private void SaveTable()
    {
#if UNITY_EDITOR
        try
        {
            foreach(var edit in edits)if(!float.TryParse(edit.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out float value)||float.IsNaN(value)||float.IsInfinity(value))throw new InvalidOperationException("请填写有效数值："+edit.Key);
            if(WriteTableParameters==null)throw new InvalidOperationException("缺少公共写回入口。");
            message=WriteTableParameters(tableResource);LoadTablePicker();
        }
        catch(Exception e){message="写回失败："+e.Message;}
#endif
    }
    private void DrawTableVariables()
    {
        GUI.enabled=ready&&!choosing;
        GUI.Label(new Rect(885,250,620,32),"按源动画播放；正式触发读取总表与实际战斗结果",label);
        if(!string.IsNullOrEmpty(tableResource.CountMode))
        {
            GUI.Label(new Rect(530,292,160,32),"示例 Count",label);
            string next=GUI.TextField(new Rect(700,292,105,32),tableCount.ToString(CultureInfo.InvariantCulture),field);
            if(int.TryParse(next,out int value)&&value>=0&&value<=999&&value!=tableCount){tableCount=value;player.SetTableVariables(tableCount,tableElement);player.Seek(player.Elapsed);}
        }
        if(tableResource.ElementVariants.Count>0)
        {
            var labels=new List<string>{"默认"};labels.AddRange(tableResource.ElementVariants.Keys);
            int current=tableElement==null?0:labels.IndexOf(tableElement);
            int next=GUI.SelectionGrid(new Rect(850,292,650,32),current,labels.ToArray(),labels.Count,button);
            if(next!=current){tableElement=next==0?null:labels[next];player.SetTableVariables(tableCount,tableElement);player.Seek(player.Elapsed);}
        }
    }
    private void DrawTableParameters()
    {
        var fields=new List<(string key,string name,float value,float min,float max)>{
            ("Speed","播放速度",tableResource.Speed,.05f,5),("RangeScale","分布范围",tableResource.RangeScale,.1f,5),
            ("PartScale","粒子大小",tableResource.PartScale,.1f,5),("Opacity","不透明度",tableResource.Opacity,0,1),
            ("Intensity","亮度",tableResource.Intensity,0,5),("FadeInSeconds","淡入秒数",tableResource.FadeIn,0,3),
            ("FadeOutSeconds","淡出秒数",tableResource.FadeOut,0,3)};
        foreach(var p in tableResource.Options)fields.Add(("Option/"+p.Key,p.Key,p.Value,-20,30));
        bool changed=false;float oldTime=player.Elapsed;bool hold=paused;
        scroll=GUI.BeginScrollView(new Rect(34,360,430,500),scroll,new Rect(0,0,404,fields.Count*72));
        for(int i=0;i<fields.Count;i++)
        {
            var p=fields[i];float y=i*72,value=p.value;
            GUI.Label(new Rect(5,y,285,30),p.name,label);
            string before=edits.TryGetValue(p.key,out var edit)?edit:value.ToString("0.###",CultureInfo.InvariantCulture);
            string after=GUI.TextField(new Rect(292,y,105,30),before,field);
            if(before!=after){edits[p.key]=after;if(float.TryParse(after,NumberStyles.Float,CultureInfo.InvariantCulture,out float n)&&!float.IsNaN(n)&&!float.IsInfinity(n)&&n>=p.min&&n<=p.max)value=n;}
            float slider=GUI.HorizontalSlider(new Rect(5,y+37,392,24),value,p.min,p.max);
            if(!Mathf.Approximately(slider,value)){value=slider;edits.Remove(p.key);}
            if(Mathf.Approximately(value,p.value))continue;changed=true;
            switch(p.key){case "Speed":tableResource.Speed=value;break;case "RangeScale":tableResource.RangeScale=value;break;
                case "PartScale":tableResource.PartScale=value;break;case "Opacity":tableResource.Opacity=value;break;
                case "Intensity":tableResource.Intensity=value;break;case "FadeInSeconds":tableResource.FadeIn=value;break;case "FadeOutSeconds":tableResource.FadeOut=value;break;
                default:tableResource.Options[p.key.Substring(7)]=value;break;}
        }
        GUI.EndScrollView();
        if(changed){tableTuning[tableResource.Key]=tableResource.Copy();Replay();player.Seek(oldTime);SetPaused(hold);}
    }
}
