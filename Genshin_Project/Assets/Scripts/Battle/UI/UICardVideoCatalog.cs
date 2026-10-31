using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Shared published-video lookup for battle scenes and display-only previews.</summary>
public static class UICardVideoCatalog
{
    [Serializable] private sealed class Catalog { public Entry[] characters; }
    [Serializable] private sealed class Entry { public int id; public string status; }

    public static IEnumerator Load(HashSet<int> available, Action<string> onError)
    {
        available.Clear();
        if (!StreamingAssetSpriteLoader.TryBuildRequestUrl("art_assets/Character_Animated/manifest.json", out string url))
            yield break;
        using (var request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            { onError?.Invoke(request.error); yield break; }
            Catalog catalog = null;
            try { catalog = JsonUtility.FromJson<Catalog>(request.downloadHandler.text); }
            catch (Exception error) { onError?.Invoke(error.Message); }
            if (catalog?.characters == null) yield break;
            foreach (var item in catalog.characters)
                if (item != null && item.id > 0 && item.status == "exported") available.Add(item.id);
        }
    }
}
