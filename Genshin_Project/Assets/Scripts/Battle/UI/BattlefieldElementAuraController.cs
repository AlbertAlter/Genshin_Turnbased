using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional aura display module. Layout and data are supplied by this scene.</summary>
[AddComponentMenu("Battle UI/Element Auras")]
public sealed class BattlefieldElementAuraController : MonoBehaviour
{
    public BattleUiLayout layout;
    [Tooltip("A component implementing IBattleUiAuraSource (combat or preview).")]
    public MonoBehaviour dataSource;
    private IBattleUiLayout boundLayout;
    private IBattleUiAuraSource boundSource;
    private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    public void Bind(IBattleUiLayout sceneLayout, IBattleUiAuraSource source)
    {
        ClearBars(); boundLayout = sceneLayout; boundSource = source;
        if (boundLayout != null) foreach (var card in boundLayout.Cards) card?.auraBar?.Initialize();
    }
    private void Awake() => Bind(layout, dataSource as IBattleUiAuraSource);
    private IEnumerator Start()
    {
        ElementColorPalette palette;
        try { palette = ElementColorPalette.Load(); }
        catch (Exception error) { Debug.LogError("元素栏配置读取失败：" + error.Message, this); yield break; }
        foreach (string element in ElementQuery.Elements)
        {
            if (!palette.TryGetAuraIconPath(element, out string path)) continue;
            yield return StreamingAssetSpriteLoader.Load(path, sprite => sprites.Add(element, sprite),
                error => Debug.LogError("元素栏图标加载失败：" + error, this));
        }
    }
    private void LateUpdate()
    {
        if (boundLayout == null) return;
        foreach (var card in boundLayout.Cards)
            if (card?.auraBar != null) card.auraBar.Present(boundSource?.GetAuras(card.side, card.slot), sprites);
    }
    private void ClearBars()
    {
        if (boundLayout != null) foreach (var card in boundLayout.Cards) card?.auraBar?.Present(null, sprites);
    }
    private void OnDisable() => ClearBars();
    private void OnDestroy()
    {
        foreach (var sprite in sprites.Values)
            if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
        sprites.Clear();
    }
}
