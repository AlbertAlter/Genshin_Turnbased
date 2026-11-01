using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Reusable ice layers and shattering. Borrows the full-card clip; never changes combat state or AP.</summary>
[DisallowMultipleComponent]
public sealed class UICardFreeze : MonoBehaviour
{
    public const string ArtFolder = "UI/Froze/";
    public const string ArtRoot = "art_assets/" + ArtFolder;
    public static readonly string[] TextureFiles = { "Ice_01_Intact.png", "Ice_02_Cracked.png", "Ice_03_Fractured.png", "Ice_Surface.png" };
    public bool loadOnStart = true;
    public bool Ready { get; private set; }
    public string Error { get; private set; }
    public bool IsShattering { get; private set; }
    public bool Bound => front != null && iceLayer != null;
    public bool Available => isActiveAndEnabled && Ready && Bound && iceLayer.gameObject.activeInHierarchy;
    public int CurrentStage => stage;
    private readonly Sprite[] sprites = new Sprite[TextureFiles.Length];
    private sealed class SpriteLease
    {
        public readonly Sprite[] Items = new Sprite[TextureFiles.Length];
        public int Users = 1;
    }
    private SpriteLease spriteLease;
    private Image iceLayer;
    private UICardView cardView;
    private float crack1, crack2;
    private UICardFreezeStyle style;
    private UIFrozenShardGraphic shards;
    private RectTransform front;
    private Material shardMaterial;
    private UICardFrameView appearance;
    private UICardDissolveSnapshot.CaptureSession backdrop;
    private Texture backdropSource;
    private Color backdropTint;
    private Rect backdropRect;
    private Coroutine transition;
    private int stage = -1;
    private int generation;
    private bool started;

    private IEnumerator Start() { started = true; if (loadOnStart) yield return Reload(); }
    private void OnEnable() { if (started && loadOnStart && !Ready) StartCoroutine(Reload()); }

    public IEnumerator Reload()
    {
        int request = ++generation;
        Ready = false; Error = null; Hide(); ReleaseSprites();
        try { style = UICardFreezeStyle.Load(); }
        catch (Exception ex) { Fail(ex.Message); yield break; }
        var loadingSprites = new SpriteLease(); spriteLease = loadingSprites;
        ConfigureMaterial();
        for (int i = 0; i < sprites.Length; i++)
        {
            int index = i;
            yield return StreamingAssetSpriteLoader.Load(ArtRoot + TextureFiles[i], sprite =>
            {
                if (request == generation && isActiveAndEnabled)
                    loadingSprites.Items[index] = sprites[index] = sprite;
                else { Destroy(sprite.texture); Destroy(sprite); }
            }, message => { if (request == generation && isActiveAndEnabled) Fail(message); });
            if (request != generation || !isActiveAndEnabled) yield break;
            if (Error != null) yield break;
        }
        Ready = true;
        if (iceLayer != null) iceLayer.sprite = sprites[0];
        ConfigureMaterial();
    }

    /// <summary>Explicitly share one loaded ice texture set across multiple card views.</summary>
    public bool UseResourcesFrom(UICardFreeze source)
    {
        if (source == null || !source.isActiveAndEnabled || !source.Ready || source.spriteLease == null) return false;
        if (SharesResourcesWith(source)) return true;
        ++generation; StopAllCoroutines(); transition = null;
        Hide(); ReleaseSprites();
        spriteLease = source.spriteLease; ++spriteLease.Users;
        Array.Copy(spriteLease.Items, sprites, sprites.Length);
        style = source.style; Ready = true; Error = null;
        if (iceLayer != null) iceLayer.sprite = sprites[0];
        ConfigureMaterial(); return true;
    }
    public bool SharesResourcesWith(UICardFreeze source) => source != null && spriteLease != null &&
        ReferenceEquals(spriteLease, source.spriteLease);

    public void Bind(UICardView card)
    {
        if (card == null || card.Released) throw new ArgumentException("冻结视图需要有效的公共卡牌句柄。");
        Bind(card.Appearance);
        cardView = card;
    }
    public void Bind(UICardFrameView card)
    {
        if (card == null || card.FreezeLayer == null || card.FrontEffects == null)
            throw new ArgumentException("冻结视图需要已就绪的公共卡牌外观。");
        ClearBinding(); front = card.FrontEffects; appearance = card;
        var shader = Resources.Load<Shader>("UI/FrozenShards");
        if (shader == null) throw new InvalidOperationException("缺少 FrozenShards Shader。");
        shardMaterial = new Material(shader) { name = "FrozenShards_Instance", hideFlags = HideFlags.HideAndDontSave };
        shardMaterial.SetTexture("_ClipTex", card.FreezeClip);
        ConfigureMaterial();
        iceLayer = Stretch("FrozenIce", card.FreezeLayer).gameObject.AddComponent<Image>();
        iceLayer.sprite = sprites[0]; iceLayer.raycastTarget = false; iceLayer.color = new Color(1, 1, 1, 0);
        iceLayer.material = shardMaterial;
        shards = Stretch("FreezeFragments", front).gameObject.AddComponent<UIFrozenShardGraphic>();
        shards.material = shardMaterial; shards.raycastTarget = false; shards.gameObject.SetActive(false);
    }

    public bool ShowStage(int value, bool animate = true)
    {
        if (!isActiveAndEnabled || !Ready || !Bound) return false;
        value = Mathf.Clamp(value, 0, 2);
        cardView?.SetAnimationFrozen(this, true);
        if (!EnsureBackdrop()) return false;
        if (stage == value && !IsShattering) return true;
        CancelTransition(); IsShattering = false; shards.gameObject.SetActive(false); stage = value;
        SetAlpha(1);
        if (!animate || style.CrackRevealSeconds <= 0)
        { SetCracks(stage >= 1 ? 1 : 0, stage >= 2 ? 1 : 0); }
        else transition = StartCoroutine(RevealCracks());
        return true;
    }

    private IEnumerator RevealCracks()
    {
        float target1 = stage >= 1 ? 1 : 0, target2 = stage >= 2 ? 1 : 0;
        while (crack1 != target1 || crack2 != target2)
        {
            float step = Delta / style.CrackRevealSeconds;
            SetCracks(Mathf.MoveTowards(crack1, target1, step), Mathf.MoveTowards(crack2, target2, step));
            yield return null;
        }
        SetCracks(stage >= 1 ? 1 : 0, stage >= 2 ? 1 : 0);
        transition = null;
    }

    public bool Shatter()
    {
        if (!isActiveAndEnabled || !Ready || !Bound) return false;
        CancelTransition();
        SetAlpha(0); SetCracks(1, 1);
        cardView?.SetAnimationFrozen(this, false);
        stage = -1; IsShattering = true;
        shards.Configure(sprites[0].texture, style); shards.gameObject.SetActive(true);
        transition = StartCoroutine(BreakIce()); return true;
    }

    private IEnumerator BreakIce()
    {
        float elapsed = 0;
        while (elapsed < style.ShatterSeconds)
        { shards.SetProgress(elapsed / style.ShatterSeconds); elapsed += Delta; yield return null; }
        shards.gameObject.SetActive(false); IsShattering = false; transition = null;
        ReleaseBackdrop();
    }

    private float Delta => style.Unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
    private void ConfigureMaterial()
    {
        if (shardMaterial == null || style == null) return;
        shardMaterial.SetVector("_RevealCenter", new Vector4(.5f, style.OriginY, 0, 0));
        shardMaterial.SetFloat("_RevealFeatherPixels", style.CrackRevealFeatherPixels);
        shardMaterial.SetFloat("_SecondRevealEasePower", style.SecondRevealEasePower);
        shardMaterial.SetTexture("_SurfaceTex", sprites[3] != null ? sprites[3].texture : null);
        shardMaterial.SetTexture("_CrackTex1", sprites[1] != null ? sprites[1].texture : null);
        shardMaterial.SetTexture("_CrackTex2", sprites[2] != null ? sprites[2].texture : null);
        SetCracks(crack1, crack2);
    }
    private void SetCracks(float first, float second)
    {
        crack1 = first; crack2 = second;
        if (shardMaterial == null) return;
        shardMaterial.SetFloat("_Crack1Progress", first); shardMaterial.SetFloat("_Crack2Progress", second);
    }
    private void LateUpdate()
    {
        if (!Bound) { ReleaseBackdrop(); return; }
        if (!Ready || (stage < 0 && !IsShattering)) return;
        if (EnsureBackdrop()) backdrop.Render();
    }
    private bool EnsureBackdrop()
    {
        if (appearance == null || appearance.Surface == null) return false;
        Texture source = appearance.Surface.texture;
        Color tint = appearance.Surface.color;
        Rect rect = appearance.Root.rect;
        if (backdrop != null && source == backdropSource && tint == backdropTint && rect == backdropRect) return true;
        ReleaseBackdrop();
        try
        {
            var canvas = appearance.GetComponentInParent<Canvas>();
            int height = Mathf.CeilToInt(rect.height * (canvas != null ? canvas.scaleFactor : 1));
            backdrop = UICardDissolveSnapshot.BeginCapture(appearance.Root, appearance.SnapshotLayers, height,
                appearance.SnapshotClip, appearance.SnapshotClippedLayers);
            backdropSource = source; backdropTint = tint; backdropRect = rect;
            shardMaterial.SetTexture("_BackdropTex", backdrop.Texture);
            return true;
        }
        catch (Exception ex) { Ready = false; Fail("冻结混合背景：" + ex.Message); return false; }
    }
    private void ReleaseBackdrop()
    {
        if (shardMaterial != null) shardMaterial.SetTexture("_BackdropTex", null);
        backdrop?.Dispose(); backdrop = null; backdropSource = null;
    }
    private void SetAlpha(float alpha) { if (iceLayer != null) iceLayer.color = new Color(1, 1, 1, alpha); }
    private void CancelTransition() { if (transition != null) StopCoroutine(transition); transition = null; }
    public void Hide()
    {
        CancelTransition(); IsShattering = false; stage = -1;
        SetAlpha(0); SetCracks(0, 0);
        cardView?.SetAnimationFrozen(this, false);
        if (shards != null) shards.gameObject.SetActive(false);
        ReleaseBackdrop();
    }
    private void Fail(string message) { Error = message; Debug.LogError("Frozen card: " + message, this); }
    private static RectTransform Stretch(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }
    private void ReleaseSprites()
    {
        if (iceLayer != null) iceLayer.sprite = null;
        if (shardMaterial != null)
        {
            shardMaterial.SetTexture("_CrackTex1", null); shardMaterial.SetTexture("_CrackTex2", null);
            shardMaterial.SetTexture("_SurfaceTex", null);
        }
        if (spriteLease != null && --spriteLease.Users == 0)
        {
            foreach (var sprite in spriteLease.Items)
                if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
        }
        spriteLease = null; Array.Clear(sprites, 0, sprites.Length);
    }
    private void ClearBinding()
    {
        Hide();
        if (iceLayer != null) { iceLayer.gameObject.SetActive(false); Destroy(iceLayer.gameObject); iceLayer = null; }
        if (shards != null) { Destroy(shards.gameObject); shards = null; }
        if (shardMaterial != null) { Destroy(shardMaterial); shardMaterial = null; }
        front = null; cardView = null; appearance = null;
    }
    private void OnDisable()
    {
        generation++; StopAllCoroutines(); transition = null; Hide();
        if (!Ready) ReleaseSprites();
    }
    private void OnDestroy() { generation++; ClearBinding(); ReleaseSprites(); }
}
