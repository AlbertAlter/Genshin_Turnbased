using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Publishes the numbered themes referenced by character wish banners.</summary>
internal static class WishCharacterThemeSync
{
    public static void Publish(string repository, string streaming)
    {
        string source = Path.Combine(repository, "soundtrack", "BGM1", "角色展示曲");
        string table = Path.Combine(streaming, "Data", "Roguelike", RoguelikeWishCatalog.WorkbookName);
        if (!Directory.Exists(source) || !File.Exists(table))
        {
            Debug.LogWarning("未同步祈愿角色曲，缺少角色展示曲目录或已发布的 Wishlist.xlsx。");
            return;
        }

        var featured = RoguelikeWishCatalog.Load(table).Pools.Select(pool => pool.Featured)
            .Where(prize => prize.Kind == RoguelikeWishKind.Character)
            .GroupBy(prize => prize.Id).ToDictionary(group => group.Key, group => group.First().Name);
        var tracks = new Dictionary<int, string>();
        foreach (string file in Directory.EnumerateFiles(source))
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension != ".mp3" && extension != ".wav") continue;
            string name = Path.GetFileNameWithoutExtension(file);
            int separator = name.IndexOf(' ');
            if (separator < 1 || !int.TryParse(name.Substring(0, separator),
                    NumberStyles.None, CultureInfo.InvariantCulture, out int id) || !featured.ContainsKey(id))
                continue;
            if (tracks.ContainsKey(id)) throw new InvalidDataException($"角色 {id} 有多首展示曲，无法确定祈愿曲目。");
            tracks.Add(id, file);
        }

        string destination = Path.Combine(streaming, RoguelikeWishMusic.AudioFolder);
        int updated = 0;
        foreach (var track in tracks.OrderBy(entry => entry.Key))
        {
            string id = track.Key.ToString(CultureInfo.InvariantCulture);
            string extension = Path.GetExtension(track.Value).ToLowerInvariant();
            string target = Path.Combine(destination, id + extension);
            var original = new FileInfo(track.Value);
            var published = new FileInfo(target);
            if (!published.Exists || published.Length != original.Length || published.LastWriteTimeUtc != original.LastWriteTimeUtc)
            {
                Directory.CreateDirectory(destination);
                File.Copy(original.FullName, target, true);
                File.SetLastWriteTimeUtc(target, original.LastWriteTimeUtc);
                updated++;
            }
            // Keep the runtime lookup unambiguous if a track changes format.
            string previousFormat = Path.Combine(destination, id + (extension == ".mp3" ? ".wav" : ".mp3"));
            if (File.Exists(previousFormat))
            {
                File.Delete(previousFormat);
                File.Delete(previousFormat + ".meta");
            }
        }
        string[] missing = featured.Where(prize => !tracks.ContainsKey(prize.Key))
            .OrderBy(prize => prize.Key).Select(prize => $"{prize.Key} {prize.Value}").ToArray();
        if (missing.Length > 0) Debug.LogWarning("以下五星角色暂无展示曲：" + string.Join("、", missing));
        Debug.Log($"祈愿角色曲同步完成，共匹配 {tracks.Count} 首，更新 {updated} 首。");
    }
}
