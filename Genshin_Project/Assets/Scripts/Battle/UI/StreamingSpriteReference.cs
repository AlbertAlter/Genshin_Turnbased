using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A serialized reference to an image below StreamingAssets. Placeholder paths are
/// intentionally left for a battle presenter to resolve from runtime IDs.
/// </summary>
[RequireComponent(typeof(Image))]
[ExecuteAlways]
public sealed class StreamingSpriteReference : MonoBehaviour
{
    public string relativePath;
    public bool allowCrop;
    public bool loadOnEnable = true;
    public bool revealOnLoad = true;
    // Saved in the scene at authoring time; importing art never changes this decision.
    public bool vectorCircle;
    [HideInInspector] public bool slotCenterAligned;
    private Material vectorMaterial;

    private void ApplyVectorMaterial(Image image)
    {
        if (!vectorCircle)
        {
            if (vectorMaterial != null && image.material == vectorMaterial) image.material = null;
            return;
        }
        var shader = Resources.Load<Shader>("UI/PsdHealthLayer");
        if (shader == null) return;
        if (vectorMaterial == null)
            vectorMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        vectorMaterial.SetFloat("_VectorCircle", 1);
        image.material = vectorMaterial;
    }

    private Coroutine request;
    private Sprite runtimeSprite;
    private void ReleaseRuntimeSprite()
    {
        if (runtimeSprite == null) return;
        var image = GetComponent<Image>();
        if (image != null && image.sprite == runtimeSprite) image.sprite = null;
        Destroy(runtimeSprite.texture); Destroy(runtimeSprite); runtimeSprite = null;
    }
#if UNITY_EDITOR
    private Sprite editorPreview;
    public void RefreshEditorPreview()
    {
        if (Application.isPlaying || !StreamingAssetSpriteLoader.TryBuildRequestUrl(relativePath, out string url)) return;
        var uri = new System.Uri(url);
        if (!uri.IsFile || !System.IO.File.Exists(uri.LocalPath)) return;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.hideFlags = HideFlags.HideAndDontSave;
        if (!texture.LoadImage(System.IO.File.ReadAllBytes(uri.LocalPath))) { DestroyImmediate(texture); return; }
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        ClearEditorPreview();
        editorPreview = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
        editorPreview.hideFlags = HideFlags.HideAndDontSave;
        var image = GetComponent<Image>();
        image.sprite = editorPreview;
        ApplyVectorMaterial(image);
        image.enabled = revealOnLoad;
    }

    private void ClearEditorPreview()
    {
        if (editorPreview == null) return;
        DestroyImmediate(editorPreview.texture);
        DestroyImmediate(editorPreview);
        editorPreview = null;
    }

#endif

    private void OnDestroy()
    {
        ReleaseRuntimeSprite();
#if UNITY_EDITOR
        ClearEditorPreview();
#endif
        if (vectorMaterial != null)
        {
            if (Application.isPlaying) Destroy(vectorMaterial); else DestroyImmediate(vectorMaterial);
        }
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (loadOnEnable) RefreshEditorPreview();
            return;
        }
#endif
        Image image = GetComponent<Image>();
        if (image.sprite == null) image.enabled = false;
        if (loadOnEnable && !string.IsNullOrWhiteSpace(relativePath) &&
            relativePath != "-" && !relativePath.Contains("{"))
            request = StartCoroutine(Load(relativePath));
    }

    private void OnDisable()
    {
        if (request != null) StopCoroutine(request);
        request = null;
        if (Application.isPlaying) ReleaseRuntimeSprite();
    }

    public void SetResolvedPath(string path)
    {
        if (request != null) StopCoroutine(request);
        request = null;
        if (Application.isPlaying) ReleaseRuntimeSprite();
        relativePath = path;
        Image image = GetComponent<Image>();
        image.sprite = null;
        image.enabled = false;
        if (!isActiveAndEnabled) return;
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == "-" || relativePath.Contains("{")) return;
        request = StartCoroutine(Load(relativePath));
    }

    private IEnumerator Load(string path)
    {
        yield return StreamingAssetSpriteLoader.Load(path, sprite =>
        {
            ReleaseRuntimeSprite();
            runtimeSprite = sprite;
            Image image = GetComponent<Image>();
            image.sprite = sprite;
            ApplyVectorMaterial(image);
            image.enabled = revealOnLoad;
            image.preserveAspect = !allowCrop;
            AspectRatioFitter fitter = GetComponent<AspectRatioFitter>();
            if (allowCrop && fitter != null && sprite.rect.height > 0f)
            {
                fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            }
            request = null;
        }, error =>
        {
            Debug.LogWarning(error, this);
            request = null;
        });
    }
}
