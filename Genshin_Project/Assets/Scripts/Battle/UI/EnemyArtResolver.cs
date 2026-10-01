using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Loads enemy art listed by EnemyAttributes.xlsx from StreamingAssets.</summary>
public static class EnemyArtResolver
{
    public const string ManifestPath = "art_assets/Enemy/enemy_art_manifest.json";
    private static EnemyArtManifest _cachedManifest;

    public static IEnumerator Load(int enemyId, Action<Sprite> onLoaded, Action<string> onError = null)
    {
        if (_cachedManifest == null) yield return LoadManifest(onError);
        if (_cachedManifest == null) yield break;
        if (!_cachedManifest.TryGetArtPath(enemyId, out string artPath))
        {
            onError?.Invoke("Enemy art is not listed in the runtime manifest: " + enemyId);
            yield break;
        }
        yield return StreamingAssetSpriteLoader.Load(artPath, onLoaded, onError);
    }

    public static void ClearCache() => _cachedManifest = null;

    private static IEnumerator LoadManifest(Action<string> onError)
    {
        if (!StreamingAssetSpriteLoader.TryBuildRequestUrl(ManifestPath, out string requestUrl))
        {
            onError?.Invoke("Invalid enemy-art manifest path.");
            yield break;
        }
        using UnityWebRequest request = UnityWebRequest.Get(requestUrl);
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke($"Failed to load enemy-art manifest ({request.error}).");
            yield break;
        }
        try
        {
            _cachedManifest = JsonUtility.FromJson<EnemyArtManifest>(request.downloadHandler.text);
        }
        catch (Exception exception)
        {
            onError?.Invoke("Failed to parse enemy-art manifest: " + exception.Message);
        }
    }
}
