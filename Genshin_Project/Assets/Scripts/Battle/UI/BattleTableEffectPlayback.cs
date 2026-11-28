using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One data-driven mesh/motion player used by formal battle and authoring. No skill/character switches.</summary>
internal sealed class BattleTableEffectPlayback : IDisposable
{
    private readonly RectTransform space,behind,above;
    private readonly Shader shader;
    private readonly Dictionary<(int,Texture),BattleEffectMeshBatch> batches=new Dictionary<(int,Texture),BattleEffectMeshBatch>();
    private readonly List<RectTransform> targets=new List<RectTransform>();
    private readonly Vector3[] corners=new Vector3[4];
    private readonly Vector4[] regions=new Vector4[5];
    private readonly Dictionary<long,float[]> weights=new Dictionary<long,float[]>();
    private BattleEffectSequenceLease lease;
    private BattleTableEffectResource resource;
    private RectTransform caster;
    private Material material;
    private Regex rangeParts,actors;
    private float stopTime=-1,delay;
    private int count=-1;
    private string element,clip;
    private IReadOnlyList<int> sourceStatuses;
    private IReadOnlyDictionary<string,float> sourceValues;
    private readonly List<(float Start,float Duration,float Gain)> pulses=new List<(float,float,float)>();
    private Texture2D spriteTexture;
    private string spritePath;
    private sealed class SpriteNode {public RawImage Image;public UICardFrameView Frame;}
    private readonly List<SpriteNode> spriteNodes=new List<SpriteNode>();
    public bool Playing {get;private set;}
    public float Elapsed {get;private set;}
    public float Duration=>resource==null?0:(resource.ClipEnd>resource.ClipStart?resource.ClipEnd-resource.ClipStart:resource.Duration)/resource.Speed;
    public bool Sustained=>resource!=null&&resource.State;
    private BattleEffectContactGate contact;
    public BattleTableEffectPlayback(RectTransform space,RectTransform behind,RectTransform above,Shader shader)
    {this.space=space;this.behind=behind;this.above=above;this.shader=shader;}
    public void Play(BattleTableEffectResource definition,IReadOnlyList<RectTransform> actualTargets,RectTransform actualCaster,float startDelay,bool gated,int visualCount=-1,string visualElement=null,IReadOnlyList<int> sourceStatuses=null,IReadOnlyDictionary<string,float> sourceValues=null)
    {
        Clear();resource=definition.Copy();caster=actualCaster;count=visualCount<0?(int)resource.Option("DefaultCount",-1):visualCount;element=visualElement;
        this.sourceStatuses=sourceStatuses;
        this.sourceValues=sourceValues;
        if(!BattleEffectRange.TryCalculate(space,actualTargets,out _,corners,true))throw new ArgumentException("特效需要1～5个实际卡位。");
        targets.AddRange(actualTargets);
        if(resource.Anchor==BattleTableEffectAnchor.Source&&caster==null)throw new ArgumentException("施放者挂载的特效缺少实际施放者。");
        rangeParts=string.IsNullOrEmpty(resource.RangeParts)?null:new Regex(resource.RangeParts,RegexOptions.CultureInvariant);
        actors=string.IsNullOrEmpty(resource.ActorPattern)?null:new Regex(resource.ActorPattern,RegexOptions.CultureInvariant);
        if(material==null)material=new Material(shader){name="Table VFX/"+definition.Key};
        var canvas=space.GetComponentInParent<Canvas>();if(canvas!=null)canvas.additionalShaderChannels|=AdditionalCanvasShaderChannels.Normal|AdditionalCanvasShaderChannels.TexCoord1|AdditionalCanvasShaderChannels.TexCoord2;
        if(resource.Player=="Sprite")BuildSprites();
        else SelectClip();
        contact=gated?new BattleEffectContactGate(resource.Contact/resource.Speed):null;
        Playing=true;stopTime=-1;Seek(0);delay=Math.Max(0,startDelay);
        if(delay>0)ClearGeometry();
    }
    private void SelectClip()
    {
        string next=resource.ResolveClip(count,element,sourceStatuses,sourceValues);
        if(clip==next&&lease!=null)return;
        var acquired=BattleEffectSequenceLease.Acquire(next);
        ClearBatches();lease?.Dispose();lease=acquired;clip=next;weights.Clear();
    }
    public void SetVariables(int visualCount,string visualElement)
    {
        if(count==visualCount&&element==visualElement)return;count=visualCount;element=visualElement;if(resource.Player=="Sequence")SelectClip();
    }
    public void SetTargets(IReadOnlyList<RectTransform> value)
    {
        bool equal=value!=null&&value.Count==targets.Count;
        if(equal)for(int i=0;i<targets.Count;i++)if(value[i]!=targets[i]){equal=false;break;}
        if(equal)return;
        ReleaseSprites();targets.Clear();if(value!=null)targets.AddRange(value);if(targets.Count==0)Stop();else if(resource.Player=="Sprite")BuildSprites();
    }
    public void Advance(float seconds)
    {
        if(!Playing||seconds<0)return;
        if(delay>0){float wait=Math.Min(delay,seconds);delay-=wait;seconds-=wait;if(seconds<=0)return;}
        if(stopTime>=0)stopTime+=seconds;
        Seek(contact!=null?contact.Limit(Elapsed+seconds):Elapsed+seconds);
    }
    public void ConfirmContact(){if(contact==null||!contact.Waiting)return;contact.Confirm();Seek(contact.Contact);}
    public void Pulse(float gain,float seconds)
    {if(Playing&&Sustained&&seconds>0&&gain>0)pulses.Add((Elapsed,seconds,gain));}
    public void Seek(float seconds)
    {
        if(resource==null||(resource.Player=="Sequence"&&lease==null))return;Elapsed=Math.Max(0,seconds);delay=0;
        if((!Sustained&&Elapsed>=Duration)||(stopTime>=0&&stopTime>=resource.FadeOut)){ReleaseSprites();ClearGeometry();Playing=false;return;}
        if(!BattleEffectRange.TryCalculate(space,targets,out var range,corners,true)){Clear();return;}
        Playing=true;
        foreach(var batch in batches.Values)batch.Begin();
        if(Sustained&&resource.CountMode=="Variants"&&count==0&&!resource.CountVariants.ContainsKey(0)){foreach(var batch in batches.Values)batch.End();return;}
        float fade=resource.Opacity*(resource.FadeIn>0?Mathf.Clamp01(Elapsed/resource.FadeIn):1);
        if(stopTime>=0)fade*=1-Mathf.Clamp01(stopTime/Math.Max(.0001f,resource.FadeOut));
        float intensity=resource.Intensity;
        float pulse=1;
        for(int i=pulses.Count-1;i>=0;i--)
        {
            var p=pulses[i];float age=Elapsed-p.Start;
            if(age>=p.Duration){pulses.RemoveAt(i);continue;}
            pulse=Math.Max(pulse,BattleTableEffectResource.PulseMultiplier(age,p.Duration,p.Gain));
        }
        intensity*=pulse;
        if(resource.Player=="Sprite")
        {
            foreach(var node in spriteNodes)
            {node.Image.color=new Color(intensity,intensity,intensity,fade);if(resource.Option("HideCardArt",0)>0)node.Frame?.SetArtHidden(this,fade);}
            return;
        }
        var d=lease.Data.Document;float sample=BattleEffectSequenceTime.Sample(Elapsed+resource.ClipStart/resource.Speed,resource.Speed,Sustained,resource.LoopStart,resource.LoopEnd,d.duration);
        float frame=Mathf.Clamp(sample*d.fps,0,d.times.Length-1);int a=(int)frame,b=Math.Min(a+1,d.times.Length-1);float blend=frame-a;
        material.SetFloat("_Intensity",intensity);material.SetFloat("_EffectTime",Elapsed);
        Vector3 sourcePivot=V(d.sourcePivot);
        Vector3 holderPivot=new Vector3(resource.Option("PivotX",sourcePivot.x),resource.Option("PivotY",sourcePivot.y),resource.Option("PivotZ",sourcePivot.z));
        var casterPoint=caster!=null?Center(caster):range.Center;
        bool holder=resource.Anchor==BattleTableEffectAnchor.Holder||resource.Anchor==BattleTableEffectAnchor.Source;
        Vector2 holderCenter=resource.Anchor==BattleTableEffectAnchor.Source?casterPoint:range.Center;
        if(resource.Anchor==BattleTableEffectAnchor.Field)SetRegions(range);
        for(int ti=0;ti<d.tracks.Length;ti++)
        {
            var t=d.tracks[ti];if(actors!=null&&!actors.IsMatch(t.name))continue;
            var native=lease.Data.Meshes[t.mesh];float alpha=Mathf.Lerp(t.colors[a*4+3],t.colors[b*4+3],blend);
            if(alpha*fade<=.0001f)continue;
            var pose=new Matrix4x4();for(int row=0;row<4;row++)for(int col=0;col<4;col++)pose[row,col]=Mathf.Lerp(t.poses[a*16+row*4+col],t.poses[b*16+row*4+col],blend);
            Color color=new Color(Mathf.Lerp(t.colors[a*4],t.colors[b*4],blend),Mathf.Lerp(t.colors[a*4+1],t.colors[b*4+1],blend),Mathf.Lerp(t.colors[a*4+2],t.colors[b*4+2],blend),1);
            var authored=d.meshes[t.mesh];
            if(authored.ignoreObjectTint)color=Color.white;
            var parameters=new Vector4(authored.noise,t.parameters!=null&&t.parameters.Length==d.times.Length*2?Mathf.Lerp(t.parameters[a*2],t.parameters[b*2],blend):-1,Mathf.Lerp(t.colors[a*4],t.colors[b*4],blend),authored.revealAxis);
            float[] morph=null;
            if(native.Morphs.Length>0)
            {
                if(!weights.TryGetValue(ti,out morph)){morph=new float[native.Morphs.Length];weights.Add(ti,morph);}
                for(int j=0;j<morph.Length;j++)morph[j]=Mathf.Lerp(t.weights[a*morph.Length+j],t.weights[b*morph.Length+j],blend);
            }
            int copies=resource.Anchor==BattleTableEffectAnchor.EachTarget&&t.port>=0?targets.Count:1;
            if(resource.Anchor==BattleTableEffectAnchor.EachTarget&&t.port>=0&&t.port!=resource.PrimaryPort)continue;
            if(resource.Anchor==BattleTableEffectAnchor.Group&&resource.Option("WholeGroup",0)<=0&&t.port>=targets.Count)continue;
            for(int copy=0;copy<copies;copy++)
            {
                var matrix=pose;Vector2 origin=range.Center;Vector3 pivot=V(t.pivot);Vector3 local=matrix.GetColumn(3);
                if(holder){local-=holderPivot;origin=holderCenter;}
                else if((resource.Anchor==BattleTableEffectAnchor.Group||resource.Anchor==BattleTableEffectAnchor.Field)&&resource.Option("WholeGroup",0)>0)
                {
                    local-=holderPivot;origin=range.Center;
                    local.x*=range.HalfSize.x/range.PixelsPerUnit/Math.Max(.1f,resource.Option("ReferenceHalfWidth",.985f));
                    local.y*=range.HalfSize.y/range.PixelsPerUnit/Math.Max(.1f,resource.Option("ReferenceHalfHeight",1.69f));
                }
                else if(resource.Anchor==BattleTableEffectAnchor.EachTarget||resource.Anchor==BattleTableEffectAnchor.Group)
                {
                    Vector2 target=t.port<0?casterPoint:Center(targets[resource.Anchor==BattleTableEffectAnchor.EachTarget?copy:Mathf.Min(t.port,targets.Count-1)]);
                    // Canonical source/target blending retargets flights without putting reference card positions into a scene.
                    if(t.port>=0&&caster!=null)
                    {
                        Vector3 line=pivot-sourcePivot;float along=line.sqrMagnitude>1e-6f?Mathf.Clamp01(Vector3.Dot(local-sourcePivot,line)/line.sqrMagnitude):1;
                        var actualSource=new Vector3((casterPoint.x-target.x)/range.PixelsPerUnit,(casterPoint.y-target.y)/range.PixelsPerUnit,0);
                        Vector3 flatLine=new Vector3(line.x,line.y,0),actualLine=-actualSource;
                        Quaternion direction=flatLine.sqrMagnitude>1e-6f&&actualLine.sqrMagnitude>1e-6f?Quaternion.FromToRotation(flatLine,actualLine):Quaternion.identity;
                        local=direction*(local-Vector3.Lerp(sourcePivot,pivot,along))+actualSource*(1-along);
                        var translated=matrix.GetColumn(3);matrix.SetColumn(3,new Vector4(0,0,0,1));matrix=Matrix4x4.Rotate(direction)*matrix;matrix.SetColumn(3,translated);
                    }
                    else local-=t.port<0?sourcePivot:pivot;
                    origin=target;
                }
                else // A field uses one common world sheet; the shader exposes only the actual supplied regions.
                {origin=Vector2.zero;}
                local.x*=resource.RangeScale;local.y*=resource.RangeScale;
                matrix.SetColumn(3,new Vector4(local.x,local.y,local.z,1));
                if(rangeParts!=null&&rangeParts.IsMatch(t.name))matrix=matrix*Matrix4x4.Scale(new Vector3(range.HalfSize.x/range.PixelsPerUnit/.985f,range.HalfSize.y/range.PixelsPerUnit/1.69f,1));
                matrix=matrix*Matrix4x4.Scale(Vector3.one*resource.PartScale);
                if(holder)
                {
                    var card=resource.Anchor==BattleTableEffectAnchor.Source?caster:targets[0];
                    var frameView=card!=null?card.GetComponent<UICardFrameView>():null;
                    Append(native,morph,matrix,origin,range.PixelsPerUnit,color,alpha*fade,t.mesh,1,frameView?.BackEffects??behind,parameters);
                    Append(native,morph,matrix,origin,range.PixelsPerUnit,color,alpha*fade,t.mesh,-1,frameView?.FrontEffects??above,parameters);
                }
                else Append(native,morph,matrix,origin,range.PixelsPerUnit,color,alpha*fade,t.mesh,0,resource.Anchor==BattleTableEffectAnchor.Field?behind:above,parameters);
            }
        }
        foreach(var batch in batches.Values)batch.End();
    }
    private void SetRegions(BattleEffectRange range)
    {
        for(int i=0;i<targets.Count;i++){targets[i].GetWorldCorners(corners);Vector2 lo=space.InverseTransformPoint(corners[0]),hi=space.InverseTransformPoint(corners[2]);Vector2 center=(lo+hi)*.5f/range.PixelsPerUnit;Vector2 half=(hi-lo)*.5f/range.PixelsPerUnit;regions[i]=new Vector4(center.x,center.y,half.x*1.15f,half.y*1.12f);}
        material.SetFloat("_RegionCount",targets.Count);material.SetVectorArray("_Regions",regions);
    }
    private void Append(BattleEffectMeshSource mesh,float[] morph,Matrix4x4 pose,Vector2 origin,float ppu,Color color,float alpha,int meshIndex,int side,RectTransform mount,Vector4 parameters)
    {
        Texture texture=lease.Data.Textures[meshIndex];var key=(mount.GetInstanceID(),texture);
        if(!batches.TryGetValue(key,out var batch)){batch=new BattleEffectMeshBatch(mount,material,space,texture);batches.Add(key,batch);batch.Begin();}
        batch.Append(mesh,morph,pose,origin,ppu,color,alpha,lease.Data.Document.meshes[meshIndex].noise,0,depthSide:side,authoredParameters:parameters);
    }
    private Vector2 Center(RectTransform rect){rect.GetWorldCorners(corners);return space.InverseTransformPoint((corners[0]+corners[2])*.5f);}
    private static Vector3 V(float[] p)=>new Vector3(p[0],p[1],p[2]);
    public void Stop(bool immediate=false){contact=null;delay=0;if(immediate||resource==null||resource.FadeOut<=0)Clear();else if(Playing)stopTime=0;}
    private void ClearGeometry(){foreach(var batch in batches.Values){batch.Begin();batch.End();}}
    private void ClearBatches(){foreach(var batch in batches.Values)batch.Dispose();batches.Clear();}
    private void BuildSprites()
    {
        string path=BattleTableEffectCatalog.ClipPath(resource.Clip);
        if(spriteTexture==null||spritePath!=path)
        {
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="Table sprite/"+resource.Key};
            try{if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),true))throw new InvalidDataException("特效图片解码失败："+path);}
            catch{UnityEngine.Object.Destroy(texture);throw;}
            if(spriteTexture!=null)UnityEngine.Object.Destroy(spriteTexture);spriteTexture=texture;spritePath=path;
        }
        foreach(var target in targets)
        {
            var owner=resource.Anchor==BattleTableEffectAnchor.Source&&caster!=null?caster:target;
            var frame=owner.GetComponent<UICardFrameView>();var mount=frame?.FrontEffects??owner;
            var image=new GameObject("Table sprite",typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage)).GetComponent<RawImage>();
            image.rectTransform.SetParent(mount,false);image.rectTransform.anchorMin=Vector2.zero;image.rectTransform.anchorMax=Vector2.one;image.rectTransform.offsetMin=image.rectTransform.offsetMax=Vector2.zero;
            image.raycastTarget=false;image.texture=spriteTexture;image.color=Color.clear;spriteNodes.Add(new SpriteNode{Image=image,Frame=frame});
        }
    }
    private void ReleaseSprites()
    {foreach(var node in spriteNodes){node.Frame?.SetArtHidden(this,0);if(node.Image!=null)UnityEngine.Object.Destroy(node.Image.gameObject);}spriteNodes.Clear();}
    public void Clear(){ReleaseSprites();Playing=false;Elapsed=0;stopTime=-1;delay=0;contact=null;sourceStatuses=null;sourceValues=null;pulses.Clear();targets.Clear();ClearGeometry();}
    public void ReleaseIdleResources(){Clear();ClearBatches();lease?.Dispose();lease=null;if(spriteTexture!=null)UnityEngine.Object.Destroy(spriteTexture);spriteTexture=null;spritePath=null;}
    public void Dispose(){ReleaseIdleResources();if(material!=null)UnityEngine.Object.Destroy(material);material=null;}
}
