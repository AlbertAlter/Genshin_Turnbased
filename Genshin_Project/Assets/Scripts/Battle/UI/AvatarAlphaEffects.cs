using UnityEngine;
using UnityEngine.UI;

/// <summary>Effects follow the current Image alpha, not the avatar's diamond frame.</summary>
[ExecuteAlways, RequireComponent(typeof(Image))]
public sealed class AvatarAlphaEffects : BaseMeshEffect
{
    public float shadowDistance = 4;
    public float shadowSize = 8;
    [Range(0,100)] public float shadowSpread = 5;
    public float lightAngle = 135;
    public Color shadowColor = new Color(0,0,0,.28f);
    public float outlineWidth = 3;
    public Color outlineColor = new Color(113f/255,103f/255,88f/255,.75f);
    private Material ownedMaterial;
    private Material previousMaterial;

    protected override void OnEnable()
    {
        base.OnEnable();
        if (graphic == null) return;
        var shader=Resources.Load<Shader>("UI/AvatarAlphaEffects");
        if (shader == null) return;
        previousMaterial=graphic.material;
        ownedMaterial=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
        graphic.material=ownedMaterial;
        graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || ownedMaterial == null || mesh.currentVertCount != 4) return;
        var a=new UIVertex(); var b=new UIVertex();
        mesh.PopulateUIVertex(ref a,0); mesh.PopulateUIVertex(ref b,2);
        Vector2 size=b.position-a.position;
        if (size.x<=0 || size.y<=0) return;
        Vector2 uvSize=(Vector2)(b.uv0-a.uv0);
        float radians=lightAngle*Mathf.Deg2Rad;
        Vector2 offset=new Vector2(-Mathf.Cos(radians),-Mathf.Sin(radians))*shadowDistance;
        float padding=Mathf.Max(outlineWidth,shadowSize*2+shadowDistance)+1;
        ownedMaterial.SetVector("_SpriteUV",new Vector4(a.uv0.x,a.uv0.y,b.uv0.x,b.uv0.y));
        ownedMaterial.SetVector("_UvPerUnit",new Vector4(uvSize.x/size.x,uvSize.y/size.y,0,0));
        ownedMaterial.SetVector("_ShadowOffset",new Vector4(offset.x,offset.y,0,0));
        ownedMaterial.SetFloat("_ShadowSize",Mathf.Max(0,shadowSize));
        ownedMaterial.SetFloat("_Spread",Mathf.Clamp01(shadowSpread/100));
        ownedMaterial.SetFloat("_OutlineWidth",Mathf.Max(0,outlineWidth));
        ownedMaterial.SetColor("_ShadowColor",shadowColor);
        ownedMaterial.SetColor("_OutlineColor",outlineColor);
        // Expand drawing only: the RectTransform, layout, hit area and stencil stay unchanged.
        for (int i=0;i<4;i++)
        {
            var v=new UIVertex(); mesh.PopulateUIVertex(ref v,i);
            Vector2 delta=new Vector2(i<2 ? -padding : padding,i==0 || i==3 ? -padding : padding);
            v.position+=(Vector3)delta;
            v.uv0+=new Vector4(delta.x*uvSize.x/size.x,delta.y*uvSize.y/size.y,0,0);
            mesh.SetUIVertex(v,i);
        }
        // UI stencil materials are clones. Refresh parameters on an existing clone too.
        var rendering=graphic.materialForRendering;
        if (rendering != ownedMaterial)
        {
            foreach (string key in new[]{"_SpriteUV","_UvPerUnit","_ShadowOffset"}) rendering.SetVector(key,ownedMaterial.GetVector(key));
            foreach (string key in new[]{"_ShadowSize","_Spread","_OutlineWidth"}) rendering.SetFloat(key,ownedMaterial.GetFloat(key));
            rendering.SetColor("_ShadowColor",shadowColor); rendering.SetColor("_OutlineColor",outlineColor);
        }
    }

    protected override void OnDisable()
    {
        if (graphic != null && graphic.material == ownedMaterial) graphic.material=previousMaterial;
        if (ownedMaterial != null)
        {
            if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial);
        }
        ownedMaterial=null;
        base.OnDisable();
    }
}
