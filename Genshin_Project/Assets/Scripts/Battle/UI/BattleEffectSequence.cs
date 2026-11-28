using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

/// <summary>Versioned offline motion data. Contains effect actors only, never reference cards or gameplay.</summary>
[Serializable] public sealed class BattleEffectSequenceDocument
{
    public int version;
    public float fps, duration;
    public float[] sourcePivot;
    public BattleEffectSequenceMesh[] meshes;
    public BattleEffectSequenceTrack[] tracks;
    public float[] times;
}
[Serializable] public sealed class BattleEffectSequenceMorph { public float[] v; }
[Serializable] public sealed class BattleEffectSequenceMesh
{
    public float[] v,n,c,uv;
    public int[] tri;
    public BattleEffectSequenceMorph[] shapes;
    public string texture;
    public float noise;
    public bool ignoreObjectTint;
    public int revealAxis;
}
[Serializable] public sealed class BattleEffectSequenceTrack
{
    public string name;
    public int mesh,port;
    public float[] pivot,poses,colors,weights,parameters;
}

internal sealed class BattleEffectSequenceLease : IDisposable
{
    internal sealed class Shared
    {
        public int References;
        public BattleEffectSequenceDocument Document;
        public BattleEffectMeshSource[] Meshes;
        public Texture2D[] Textures;
    }
    private static readonly Dictionary<string,Shared> cache=new Dictionary<string,Shared>(StringComparer.Ordinal);
    private readonly string path;
    internal readonly Shared Data;
    private bool disposed;
    private BattleEffectSequenceLease(string path,Shared data) {this.path=path;Data=data;data.References++;}
    public static BattleEffectSequenceLease Acquire(string relative)
    {
        string path=BattleTableEffectCatalog.ClipPath(relative);
        if(cache.TryGetValue(path,out var existing))return new BattleEffectSequenceLease(path,existing);
        var shared=new Shared();
        try
        {
            using(var stream=File.OpenRead(path))using(var zip=new GZipStream(stream,CompressionMode.Decompress))using(var reader=new StreamReader(zip))
                shared.Document=JsonUtility.FromJson<BattleEffectSequenceDocument>(reader.ReadToEnd());
            var d=shared.Document;
            if(d==null||d.version!=1||!(d.fps>0)||d.times==null||d.times.Length<2||d.meshes==null||d.tracks==null||d.tracks.Length==0)
                throw new InvalidDataException("特效动画版本或内容无效："+relative);
            shared.Meshes=new BattleEffectMeshSource[d.meshes.Length];shared.Textures=new Texture2D[d.meshes.Length];
            var textures=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
            for(int i=0;i<d.meshes.Length;i++)
            {
                var m=d.meshes[i];var vertices=Vectors(m.v);var normals=Vectors(m.n);
                if(vertices.Length==0||vertices.Length>60000||normals.Length!=vertices.Length||m.c.Length!=vertices.Length*4||m.uv.Length!=vertices.Length*2||m.tri.Length%3!=0)
                    throw new InvalidDataException("特效网格数组或分块无效："+relative+"/"+i);
                var colors=new Color[vertices.Length];var uv=new Vector2[vertices.Length];
                for(int j=0;j<vertices.Length;j++){colors[j]=new Color(m.c[j*4],m.c[j*4+1],m.c[j*4+2],m.c[j*4+3]);uv[j]=new Vector2(m.uv[j*2],m.uv[j*2+1]);}
                foreach(int index in m.tri)if(index<0||index>=vertices.Length)throw new InvalidDataException("特效网格索引越界："+relative);
                var morphs=new Vector3[m.shapes.Length][];
                for(int j=0;j<morphs.Length;j++){morphs[j]=Vectors(m.shapes[j].v);if(morphs[j].Length!=vertices.Length)throw new InvalidDataException("特效形态键长度不符："+relative);}
                shared.Meshes[i]=new BattleEffectMeshSource(vertices,normals,colors,uv,m.tri,morphs);
                if(!string.IsNullOrEmpty(m.texture))
                {
                    string image=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path),m.texture));
                    string root=Path.GetFullPath(Path.Combine(Application.streamingAssetsPath,"Data","UE","Clips"))+Path.DirectorySeparatorChar;
                    if(!image.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("特效纹理路径越界。");
                    if(!textures.TryGetValue(image,out var texture))
                    {
                        texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="VFX/"+Path.GetFileName(image),wrapMode=TextureWrapMode.Clamp};
                        if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(image),true)){UnityEngine.Object.Destroy(texture);throw new InvalidDataException("特效纹理解码失败："+image);}
                        textures.Add(image,texture);
                    }
                    shared.Textures[i]=texture;
                }
            }
            foreach(var t in d.tracks)
                if(t.mesh<0||t.mesh>=d.meshes.Length||t.poses.Length!=d.times.Length*16||t.colors.Length!=d.times.Length*4||t.weights.Length!=d.times.Length*shared.Meshes[t.mesh].Morphs.Length)
                    throw new InvalidDataException("特效动画通道长度不符："+relative+"/"+t.name);
            cache.Add(path,shared);return new BattleEffectSequenceLease(path,shared);
        }
        catch{DestroyTextures(shared);throw;}
    }
    internal static Vector3[] Vectors(float[] values)
    {
        if(values==null||values.Length%3!=0)throw new InvalidDataException("特效向量数组无效。");
        var result=new Vector3[values.Length/3];for(int i=0;i<result.Length;i++)result[i]=new Vector3(values[i*3],values[i*3+1],values[i*3+2]);return result;
    }
    private static void DestroyTextures(Shared shared)
    {
        if(shared.Textures==null)return;
        var seen=new HashSet<Texture2D>();foreach(var texture in shared.Textures)if(texture!=null&&seen.Add(texture))UnityEngine.Object.Destroy(texture);
    }
    public void Dispose(){if(disposed)return;disposed=true;if(--Data.References==0){cache.Remove(path);DestroyTextures(Data);}}
}

public static class BattleEffectSequenceTime
{
    public static float Sample(float elapsed,float speed,bool state,float start,float end,float duration)
    {
        float time=Math.Max(0,elapsed)*speed;
        if(state&&end>start&&time>=end)time=start+(time-start)%(end-start);
        return Math.Min(time,duration);
    }
}
