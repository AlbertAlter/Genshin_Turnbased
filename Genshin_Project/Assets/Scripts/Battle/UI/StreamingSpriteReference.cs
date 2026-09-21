using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A serialized reference to an image below StreamingAssets. Placeholder paths are
/// intentionally left for a battle presenter to resolve from runtime IDs.
/// </summary>
[RequireComponent(typeof(Image))]
public sealed class StreamingSpriteReference : MonoBehaviour
{
    public string relativePath;
    public bool allowCrop;
    public bool loadOnEnable = true;
    public bool revealOnLoad = true;

    private Coroutine request;

    private void OnEnable()
    {
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
    }

    public void SetResolvedPath(string path)
    {
        relativePath = path;
        Image image = GetComponent<Image>();
        image.sprite = null;
        image.enabled = false;
        if (!isActiveAndEnabled) return;
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == "-" || relativePath.Contains("{")) return;
        if (request != null) StopCoroutine(request);
        request = StartCoroutine(Load(relativePath));
    }

    private IEnumerator Load(string path)
    {
        yield return StreamingAssetSpriteLoader.Load(path, sprite =>
        {
            Image image = GetComponent<Image>();
            image.sprite = sprite;
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
