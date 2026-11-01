using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Shared published-video lookup for battle scenes and display-only previews.</summary>
public static class UICardVideoCatalog
{
    public const string EnemyManifestPath = "art_assets/Enemy_Animated/manifest.json";
    [Serializable] private sealed class Catalog { public Entry[] characters; }
    [Serializable] private sealed class Entry { public int id; public string status; }
    // Enemy export IDs include numeric IDs and suffixed form keys. Identity comes
    // from the canonical media folder; do not deserialize that mixed JSON id as int.
    [Serializable] public sealed class EnemyCatalog { public EnemyEntry[] enemies; }
    [Serializable] public sealed class EnemyEntry
    {
        public int enemyID, variant;
        public string status, video, poster;
    }

    public static bool TryGetEnemyIdentity(EnemyEntry entry, out int enemyId, out int variant)
    {
        enemyId = variant = 0;
        if (entry == null || entry.status != "exported" || string.IsNullOrEmpty(entry.video)) return false;
        int slash = entry.video.IndexOf('/');
        if (slash <= 0) return false;
        string[] identity = entry.video.Substring(0, slash).Split('_');
        if (identity.Length > 2 || !int.TryParse(identity[0], out int id) || id <= 0) return false;
        int form = 1;
        if (identity.Length == 2 && (!int.TryParse(identity[1], out form) || form <= 1)) return false;
        if ((entry.enemyID != 0 && entry.enemyID != id) || (entry.variant != 0 && entry.variant != form)) return false;
        string folder = UICardVideoPlayer.EnemyFolder(id, form);
        if (entry.video != folder + "/idle_60fps.mp4" || entry.poster != folder + "/poster.png") return false;
        enemyId = id; variant = form; return true;
    }

    public static IEnumerator LoadEnemies(HashSet<string> available, Action<string> onError)
    {
        available.Clear();
        if (!StreamingAssetSpriteLoader.TryBuildRequestUrl(EnemyManifestPath, out string url)) yield break;
        using (var request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            { onError?.Invoke(request.error); yield break; }
            EnemyCatalog catalog = null;
            try { catalog = JsonUtility.FromJson<EnemyCatalog>(request.downloadHandler.text); }
            catch (Exception error) { onError?.Invoke(error.Message); }
            if (catalog?.enemies == null) yield break;
            foreach (var item in catalog.enemies)
            {
                if (item == null || item.status != "exported") continue;
                if (TryGetEnemyIdentity(item, out int id, out int variant))
                    available.Add(UICardVideoPlayer.VideoPathForEnemy(id, variant));
                else onError?.Invoke("敌人动态清单包含无效的编号、形态或路径：" + item.video);
            }
        }
    }

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
