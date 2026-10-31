using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public sealed class CardAnimationCatalog { public CardAnimationEntry[] clips; }
[Serializable] public sealed class CardAnimationEntry { public int characterId; public string clipPath, staticPath; }
[Serializable] public sealed class CardAnimationData
{
    public string source, sourceFormat;
    public int width, height;
    public string[] pages;
    public Frame[] frames;
    [Serializable] public sealed class Frame { public int page, x, y; public float seconds; }
}

/// <summary>Explicitly owned atlas set, shared by players. Runtime reads StreamingAssets only.</summary>
public sealed class StreamingCardAnimationClip : IDisposable
{
    public CardAnimationData Data { get; private set; }
    public CardAnimationTimeline Timeline { get; private set; }
    public Sprite[] Frames { get; private set; }
    public long TextureBytes { get; private set; }
    public bool Ready => Frames != null;
    public string Error { get; private set; }
    private readonly List<Texture2D> textures = new List<Texture2D>();
    private UnityWebRequest request;
    private bool disposed;

    public IEnumerator Load(string path)
    {
        if (disposed || Data != null) throw new InvalidOperationException("Use a new clip for each load.");
        if (!StreamingAssetSpriteLoader.TryBuildRequestUrl(path, out var url))
        { Error = "Invalid animation path: " + path; yield break; }
        request = UnityWebRequest.Get(url);
        yield return request.SendWebRequest();
        if (disposed) yield break;
        if (request.result != UnityWebRequest.Result.Success) Error = request.error;
        else
        {
            try
            {
                Data = JsonUtility.FromJson<CardAnimationData>(request.downloadHandler.text);
                if (Data == null || Data.width <= 0 || Data.height <= 0 || Data.pages == null || Data.pages.Length == 0 ||
                    Data.frames == null || Data.frames.Length == 0) throw new ArgumentException("Invalid animation manifest.");
                var durations = new float[Data.frames.Length];
                for (int i = 0; i < durations.Length; i++) durations[i] = Data.frames[i].seconds;
                Timeline = new CardAnimationTimeline(durations);
            }
            catch (Exception error) { Error = error.Message; }
        }
        request.Dispose(); request = null;
        if (Error != null) yield break;
        foreach (string page in Data.pages)
        {
            if (!StreamingAssetSpriteLoader.TryBuildRequestUrl(page, out url))
            { Error = "Invalid animation page: " + page; yield break; }
            request = UnityWebRequestTexture.GetTexture(url, true); // No retained CPU pixel copy.
            yield return request.SendWebRequest();
            if (disposed) yield break;
            if (request.result != UnityWebRequest.Result.Success) Error = request.error;
            else
            {
                var texture = DownloadHandlerTexture.GetContent(request);
                texture.name = page; texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
                textures.Add(texture); TextureBytes += (long)texture.width * texture.height * 4;
            }
            request.Dispose(); request = null;
            if (Error != null) yield break;
        }
        try
        {
            Frames = new Sprite[Data.frames.Length];
            for (int i = 0; i < Frames.Length; i++)
            {
                var frame = Data.frames[i];
                if (frame.page < 0 || frame.page >= textures.Count || frame.x < 0 || frame.y < 0)
                    throw new ArgumentException("Invalid animation frame bounds.");
                var texture = textures[frame.page];
                if (frame.x + Data.width > texture.width || frame.y + Data.height > texture.height)
                    throw new ArgumentException("Animation frame exceeds its atlas.");
                Frames[i] = Sprite.Create(texture, new Rect(frame.x, frame.y, Data.width, Data.height),
                    new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            }
        }
        catch (Exception error) { Error = error.Message; Dispose(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (request != null) { request.Abort(); request.Dispose(); request = null; }
        if (Frames != null) foreach (var sprite in Frames) if (sprite != null) UnityEngine.Object.Destroy(sprite);
        Frames = null;
        foreach (var texture in textures) if (texture != null) UnityEngine.Object.Destroy(texture);
        textures.Clear(); TextureBytes = 0;
    }
}
