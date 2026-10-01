using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Loads a runtime-created Sprite from a path below StreamingAssets.</summary>
public static class StreamingAssetSpriteLoader
{
    public static IEnumerator Load(
        string relativePath,
        Action<Sprite> onLoaded,
        Action<string> onError = null)
    {
        if (!TryBuildRequestUrl(relativePath, out string requestUrl))
        {
            onError?.Invoke("Invalid StreamingAssets image path: " + relativePath);
            yield break;
        }

        using UnityWebRequest request = UnityWebRequestTexture.GetTexture(requestUrl, false);
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke($"Failed to load StreamingAssets image: {relativePath} ({request.error})");
            yield break;
        }

        Texture2D texture = DownloadHandlerTexture.GetContent(request);
        texture.name = Path.GetFileNameWithoutExtension(relativePath);
        // StreamingAssets textures bypass Unity's texture importer, so apply the
        // UI-safe sampling settings explicitly. Clamp prevents transparent edge
        // colours bleeding into frames, while bilinear keeps scaled portraits clean.
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = 1;
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect);
        sprite.name = texture.name;
        onLoaded?.Invoke(sprite);
    }

    internal static bool TryBuildRequestUrl(string relativePath, out string requestUrl)
    {
        requestUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        string normalized = relativePath.Trim().Replace('\\', '/').TrimStart('/');
        if (Path.IsPathRooted(normalized) || normalized.Contains(":") ||
            normalized.Split('/').Contains(".."))
            return false;

        string streamingRoot = Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/');
        string combined = streamingRoot + "/" + normalized;
        requestUrl = combined.Contains("://") ? combined : new Uri(combined).AbsoluteUri;
        requestUrl = requestUrl.Replace("#", "%23");
        return true;
    }
}
