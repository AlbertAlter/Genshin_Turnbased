using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Loads separate character-card and frame sprites from StreamingAssets.</summary>
public static class CharacterCardArtResolver
{
    public const string ManifestPath = "art_assets/Character_Whole/card_art_manifest.json";

    private static CharacterCardArtManifest _cachedManifest;

    public static IEnumerator Load(
        int characterId,
        Action<Sprite, Sprite> onLoaded,
        Action<string> onError = null)
    {
        if (_cachedManifest == null)
            yield return LoadManifest(onError);
        if (_cachedManifest == null) yield break;

        if (!_cachedManifest.TryGetCardPath(characterId, out string cardPath))
        {
            onError?.Invoke("Character card art is not listed in the runtime manifest: " + characterId);
            yield break;
        }
        if (string.IsNullOrWhiteSpace(_cachedManifest.framePath))
        {
            onError?.Invoke("The runtime character-card manifest has no frame path.");
            yield break;
        }

        Sprite card = null;
        Sprite frame = null;
        string loadError = null;
        yield return StreamingAssetSpriteLoader.Load(cardPath, value => card = value, value => loadError = value);
        if (loadError != null)
        {
            onError?.Invoke(loadError);
            yield break;
        }

        yield return StreamingAssetSpriteLoader.Load(
            _cachedManifest.framePath,
            value => frame = value,
            value => loadError = value);
        if (loadError != null)
        {
            onError?.Invoke(loadError);
            yield break;
        }

        onLoaded?.Invoke(card, frame);
    }

    public static void ClearCache()
    {
        _cachedManifest = null;
    }

    private static IEnumerator LoadManifest(Action<string> onError)
    {
        if (!StreamingAssetSpriteLoader.TryBuildRequestUrl(ManifestPath, out string requestUrl))
        {
            onError?.Invoke("Invalid character-card manifest path.");
            yield break;
        }

        using UnityWebRequest request = UnityWebRequest.Get(requestUrl);
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke($"Failed to load character-card manifest ({request.error}).");
            yield break;
        }

        try
        {
            _cachedManifest = JsonUtility.FromJson<CharacterCardArtManifest>(request.downloadHandler.text);
        }
        catch (Exception exception)
        {
            onError?.Invoke("Failed to parse character-card manifest: " + exception.Message);
        }
    }
}
