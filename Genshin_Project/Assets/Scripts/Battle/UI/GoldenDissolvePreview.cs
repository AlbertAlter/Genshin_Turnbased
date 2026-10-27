using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Isolated preview scene bootstrap. No DataManager, battle, source Charts reads, or scene lookups.</summary>
public sealed class GoldenDissolvePreview : MonoBehaviour
{
    private const string DefaultCardPath = "art_assets/Enemy/2001_01.png";
    private const string DefaultFramePath = "art_assets/UI/BattleField1/UI_components/AvatarFrame/FrameOutline.png";
    private const string DefaultInnerShadowPath = "art_assets/UI/BattleField1/UI_components/AvatarFrame/FrameInnerShadow.png";
    private const string DefaultOuterShadowPath = "art_assets/UI/BattleField1/UI_components/AvatarFrame/FrameShadow.png";
    public string cardPath = DefaultCardPath;
    public string framePath = DefaultFramePath;
    public string innerShadowPath = DefaultInnerShadowPath;
    public string outerShadowPath = DefaultOuterShadowPath;
    public bool autoReplay = true;
    public bool newSeedEachPlay = true;
    public int fixedSeed = 12345;

    private UIGoldenDissolveStyle settings;
    private UIGoldenDissolveCard effect;
    private Image background;
    private RectTransform cardRoot;
    private readonly List<Sprite> ownedSprites = new List<Sprite>();
    private string error;
    private bool ready, paused;
    private float loopElapsed;
    private uint playIndex;

    private void OnValidate() => MigrateLegacyPaths();

    private void MigrateLegacyPaths()
    {
        // Already-open scenes may retain serialized defaults after the scene file changes on disk.
        // Migrate only the four obsolete preview paths; preserve custom image selections.
        cardPath = MigrateLegacyPath(cardPath, "art_assets/Character_Whole/1009_03.png", DefaultCardPath);
        framePath = MigrateLegacyPath(framePath, "art_assets/Character_Bare/Frame/01.png", DefaultFramePath);
        innerShadowPath = MigrateLegacyPath(innerShadowPath,
            "art_assets/UI/BattleField1/UI_components/card/CardInnerShadow.png", DefaultInnerShadowPath);
        outerShadowPath = MigrateLegacyPath(outerShadowPath,
            "art_assets/UI/BattleField1/UI_components/card/CardShadow.png", DefaultOuterShadowPath);
    }

    private static string MigrateLegacyPath(string path, string legacyPath, string replacement)
        => string.Equals(path?.Trim().Replace('\\', '/').TrimStart('/'), legacyPath, StringComparison.Ordinal)
            ? replacement : path;

    private IEnumerator Start()
    {
        MigrateLegacyPaths();
        if (!TryReadSettings()) yield break;
        var paths = new[] { cardPath, framePath, innerShadowPath, outerShadowPath };
        var sprites = new Sprite[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            int index = i;
            yield return StreamingAssetSpriteLoader.Load(paths[i], sprite => { sprites[index] = sprite; ownedSprites.Add(sprite); }, Fail);
            if (!string.IsNullOrEmpty(error) || sprites[i] == null) yield break;
        }
        if (!TryBuild(sprites)) yield break;
        ready = true;
        Replay(); // No opening delay: play as soon as the four existing art layers have loaded.
    }

    private bool TryReadSettings()
    {
        try { settings = UIGoldenDissolveStyle.Load(); error = null; return true; }
        catch (Exception exception) { Fail("Cannot load GoldenDissolve.xlsx: " + exception.Message); return false; }
    }

    private bool TryBuild(Sprite[] sprites)
    {
        try
        {
            var cameraObject = new GameObject("GoldenDissolvePreviewCamera", typeof(Camera));
            cameraObject.transform.SetParent(transform, false);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = settings.PreviewBackgroundColor;
            camera.cullingMask = 0; // Only the overlay Canvas is drawn; no battle/world objects are rendered.
            var canvasObject = new GameObject("GoldenDissolvePreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
            var bgObject = new GameObject("PreviewBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bgRect = bgObject.GetComponent<RectTransform>();
            bgRect.SetParent(canvasObject.transform, false); bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
            background = bgObject.GetComponent<Image>(); background.color = settings.PreviewBackgroundColor; background.raycastTarget = false;
            var cardObject = new GameObject("CardPreview_Art_Frame_InnerShadow", typeof(RectTransform));
            cardRoot = cardObject.GetComponent<RectTransform>();
            cardRoot.SetParent(canvasObject.transform, false); cardRoot.anchorMin = cardRoot.anchorMax = cardRoot.pivot = new Vector2(.5f, .5f);
            cardRoot.sizeDelta = new Vector2(settings.PreviewCardHeight * sprites[0].rect.width / sprites[0].rect.height, settings.PreviewCardHeight);
            // Preserve the existing exported padding and baked opacity; do not darken the PSD shadow again.
            Vector2 shadowSize = Vector2.Scale(cardRoot.sizeDelta, new Vector2(sprites[3].rect.width / sprites[2].rect.width,
                sprites[3].rect.height / sprites[2].rect.height));
            Image shadow = CreateImage("OuterShadow_SeparateFade", sprites[3], shadowSize);
            Image art = CreateImage("CardArt", sprites[0], cardRoot.sizeDelta);
            Image inner = CreateImage("InnerShadow", sprites[2], cardRoot.sizeDelta);
            Image frame = CreateImage("CardFrame", sprites[1], cardRoot.sizeDelta);
            effect = cardObject.AddComponent<UIGoldenDissolveCard>();
            effect.cardRoot = cardRoot; effect.bodyLayers = new[] { art, inner, frame }; effect.outerShadows = new[] { shadow };
            Canvas.ForceUpdateCanvases();
            return true;
        }
        catch (Exception exception) { Fail("Cannot build death preview: " + exception.Message); return false; }
    }

    private Image CreateImage(string name, Sprite sprite, Vector2 size)
    {
        var node = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rect = node.GetComponent<RectTransform>();
        rect.SetParent(cardRoot, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        var image = node.GetComponent<Image>();
        image.sprite = sprite; image.color = Color.white; image.raycastTarget = false;
        return image;
    }

    private void Replay()
    {
        if (!ready) return;
        paused = false; loopElapsed = 0;
        cardRoot.sizeDelta = new Vector2(settings.PreviewCardHeight * ownedSprites[0].rect.width / ownedSprites[0].rect.height,
            settings.PreviewCardHeight);
        foreach (var layer in effect.bodyLayers) layer.rectTransform.sizeDelta = cardRoot.sizeDelta;
        foreach (var shadow in effect.outerShadows)
            shadow.rectTransform.sizeDelta = Vector2.Scale(cardRoot.sizeDelta, new Vector2(ownedSprites[3].rect.width / ownedSprites[2].rect.width,
                ownedSprites[3].rect.height / ownedSprites[2].rect.height));
        int seed = newSeedEachPlay ? unchecked((int)GoldenDissolveMath.Mix((uint)fixedSeed ^ ++playIndex)) : fixedSeed;
        if (!effect.Play(settings, seed)) Fail("Death preview could not start; see the Console for the capture error.");
    }

    private void Update()
    {
        if (!ready || !string.IsNullOrEmpty(error)) return;
        if (Input.GetKeyDown(KeyCode.R)) Replay();
        if (Input.GetKeyDown(KeyCode.Space)) paused = !paused;
        effect.Paused = paused;
        if (autoReplay && !paused && !effect.IsPlaying)
        {
            loopElapsed += settings.UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (loopElapsed >= settings.PreviewLoopPauseSeconds) Replay();
        }
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(16, 16, Mathf.Max(100, Screen.width - 32), 110));
        GUILayout.Label("Golden death preview | Card + frame + inner shadow dissolve together; outer shadow fades.");
        if (!string.IsNullOrEmpty(error)) GUILayout.Label(error);
        else if (!ready) GUILayout.Label("Loading card layers...");
        else
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Replay [R]")) Replay();
            if (GUILayout.Button(paused ? "Resume [Space]" : "Pause [Space]")) paused = !paused;
            autoReplay = GUILayout.Toggle(autoReplay, "Auto replay");
            newSeedEachPlay = GUILayout.Toggle(newSeedEachPlay, "New visual seed");
            if (GUILayout.Button("Reload runtime table") && TryReadSettings())
            {
                background.color = settings.PreviewBackgroundColor;
                Replay();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Body: {effect.BodyProgress:P0} | Effect: {effect.Elapsed:F2}s / {settings.TotalDuration:F2}s");
        }
        GUILayout.EndArea();
    }

    private void Fail(string message) { error = message; Debug.LogError(message, this); }

    private void OnDestroy()
    {
        if (effect != null) effect.ResetEffect();
        foreach (var sprite in ownedSprites)
        {
            if (sprite == null) continue;
            UICardDissolveSnapshot.Dispose(sprite.texture); UICardDissolveSnapshot.Dispose(sprite);
        }
        ownedSprites.Clear();
    }
}
