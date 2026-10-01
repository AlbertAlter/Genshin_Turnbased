using System.Collections;
using UnityEngine;

/// <summary>Seven editable PSD layer objects; no composed fill/background texture is consumed.</summary>
[ExecuteAlways]
public sealed class PsdHealthLayers : MonoBehaviour
{
    public BattlefieldHealthBar bar;
    public bool bossStyle;
    public PsdHealthLayer[] layers = new PsdHealthLayer[0];
    private Sprite[] sprites;
    private Coroutine loading;
    private bool Ally => bar != null && bar.side == BattleSide.Ally;

    // Coordinates are taken from the exported layer manifests, in bottom-left canvas pixels.
    private static readonly Vector4[] EnemyBounds = {
        new Vector4(0,0,176,19), new Vector4(2,4,172,15), new Vector4(3,7,168,11),
        new Vector4(3,6,169,11), new Vector4(3,6,169,11), new Vector4(7,11,162,4), new Vector4(6,4,162,5) };
    private static readonly Vector4[] AllyBounds = {
        new Vector4(0,0,227,22), new Vector4(1,3,225,20), new Vector4(3,7,219,14),
        new Vector4(3,6,220,15), new Vector4(3,6,220,15), new Vector4(8,13,212,5), new Vector4(8,6,212,6) };
    private static readonly Vector4[] BossBounds = {
        new Vector4(0,0,843,20), new Vector4(2,4,839,16), new Vector4(4,6,835,13),
        new Vector4(4,6,835,12), new Vector4(4,6,835,12), new Vector4(4,12,836,4), new Vector4(8,5,830,4) };

    private void OnEnable()
    {
        if (bar == null) bar = GetComponent<BattlefieldHealthBar>();
        if (Application.isPlaying) EnsureLayers();
        if (layers.Length == 7) ReloadLayers();
    }

    // Called once by art import/initial scene construction, or at runtime for an unmigrated scene.
    // Existing layer RectTransforms are never reset on subsequent imports.
    public void EnsureLayers()
    {
        if (bar == null) bar = GetComponent<BattlefieldHealthBar>();
        if (bar == null || bar.fill == null) return;
        string root = "art_assets/UI/BattleField1/UI_components/HP/" +
            (bossStyle ? "BossHealth_Layers/" : Ally ? "CharacterHealth_Layers/" : "EnemyHealth_Layers/HealthBar/");
        string[] files = {"01_DropShadow", "02_OuterStroke", "03_BaseShape", "04_UpperShadow", "05_LowerShadow", "06_UpperSoftLight", "07_LowerSoftLight"};
        var result = new PsdHealthLayer[7];
        for (int i=0; i<7; i++)
        {
            Transform child = transform.Find("PSD_" + files[i]);
            if (child == null)
            {
                child = new GameObject("PSD_" + files[i], typeof(RectTransform), typeof(CanvasRenderer), typeof(PsdHealthLayer)).transform;
                child.SetParent(transform, false);
                var rect = (RectTransform)child;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
                var layer = child.GetComponent<PsdHealthLayer>();
                layer.relativePath = root + files[i] + ".png";
                layer.layerBounds = (Ally ? AllyBounds : EnemyBounds)[i];
                layer.baseBounds = (Ally ? AllyBounds : EnemyBounds)[2];
                layer.canvasSize = Ally ? new Vector2(227,23) : new Vector2(176,19);
                layer.padding = new Vector4(3,7,5,Ally ? 2 : 1);
                layer.caps = Ally ? new Vector2(10,12) : new Vector2(8.5f,10.5f);
                layer.blendMode = i==0 ? 1 : i<3 ? 0 : i<5 ? 1 : 2;
                layer.clippedToBase = i>=3;
                layer.followsHealth = i>=2;
                layer.healthSource = bar.fill;
                layer.raycastTarget = false;
                if (bossStyle)
                {
                    layer.layerBounds=BossBounds[i]; layer.baseBounds=BossBounds[2];
                    layer.canvasSize=new Vector2(843,20); layer.padding=new Vector4(4,6,4,1);
                    layer.caps=new Vector2(10.5f,10.5f); layer.contentOffset=new Vector2(0,-.5f);
                }
            }
            result[i] = child.GetComponent<PsdHealthLayer>();
        }
        layers = result;
        // Retain the old component solely as the animated HP fraction model.
        bar.fill.enabled = false;
        bar.fill.psdTexturePath = "";
        var frame = GetComponent<PsdHealthBarFrame>();
        if (frame != null) { frame.enabled=false; frame.psdCompositeFill=false; frame.psdFillPath=""; }
    }

    public void ReloadLayers()
    {
        if (loading != null) StopCoroutine(loading);
        loading = null;
        ReleaseSprites();
        if (layers.Length != 7) return;
        sprites = new Sprite[7];
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            for (int i=0; i<layers.Length; i++)
            {
                if (layers[i] == null || !StreamingAssetSpriteLoader.TryBuildRequestUrl(layers[i].relativePath, out string url)) return;
                var uri = new System.Uri(url);
                if (!uri.IsFile || !System.IO.File.Exists(uri.LocalPath))
                { Debug.LogError("Missing individual PSD layer; import UI art: " + layers[i].relativePath,this); return; }
                var texture = new Texture2D(2,2,TextureFormat.RGBA32,false) {hideFlags=HideFlags.HideAndDontSave};
                if (!texture.LoadImage(System.IO.File.ReadAllBytes(uri.LocalPath))) { DestroyImmediate(texture); return; }
                texture.wrapMode=TextureWrapMode.Clamp; texture.filterMode=FilterMode.Bilinear;
                sprites[i]=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));
                sprites[i].hideFlags=HideFlags.HideAndDontSave;
            }
            BindAll();
            return;
        }
#endif
        if (isActiveAndEnabled) loading=StartCoroutine(LoadLayers());
    }

    private IEnumerator LoadLayers()
    {
        for (int i=0; i<layers.Length; i++)
        {
            int index=i;
            if (layers[i] == null) yield break;
            yield return StreamingAssetSpriteLoader.Load(layers[i].relativePath, sprite=>sprites[index]=sprite,
                error=>Debug.LogError(error,this));
            if (sprites[i] == null) yield break;
        }
        BindAll();
        loading=null;
    }

    private void BindAll()
    {
        Shader shader=Resources.Load<Shader>("UI/PsdHealthLayer");
        if (shader == null) { Debug.LogError("Missing UI/PsdHealthLayer shader",this); return; }
        for (int i=0; i<layers.Length; i++)
            layers[i].Bind(sprites[i],sprites[2].texture,shader,sprites[3],sprites[4],layers[3].layerBounds);
    }

    private void OnDisable() { if (loading != null) StopCoroutine(loading); loading=null; }
    private void OnDestroy() { ReleaseSprites(); }
    private void ReleaseSprites()
    {
        if (sprites == null) return;
        foreach (var sprite in sprites)
        {
            if (sprite == null) continue;
            if (Application.isPlaying) { Destroy(sprite.texture); Destroy(sprite); }
            else { DestroyImmediate(sprite.texture); DestroyImmediate(sprite); }
        }
        sprites=null;
    }
}
