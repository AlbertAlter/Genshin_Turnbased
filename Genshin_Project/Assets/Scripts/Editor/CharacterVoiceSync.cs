using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CharacterVoiceSync
{
    [MenuItem("Tools/Audio/同步角色战斗语音")]
    public static void Sync()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        Debug.Log(Publish(root, Application.streamingAssetsPath));
        AssetDatabase.Refresh();
    }

    public static string Publish(string root, string streaming)
    {
        string sourceTable = Path.Combine(root, "Charts", CharacterVoiceTable.FileName);
        string art = Path.Combine(root, "soundtrack", "Character_Battle_Voice_CN");
        string destination = Path.Combine(streaming, CharacterVoiceTable.AudioFolder);
        string temp = Path.Combine(root, "codex_worktable", "voice_sync_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string tableCopy = Path.Combine(temp, CharacterVoiceTable.FileName);
            File.Copy(sourceTable, tableCopy);
            var table = CharacterVoiceTable.Read(tableCopy);
            string manifest = Path.Combine(destination, "published.txt");
            string[] previous = File.Exists(manifest) ? File.ReadAllLines(manifest) : Array.Empty<string>();
            foreach (string item in previous)
            {
                string[] parts = item.Split('/');
                if (parts.Length != 2 || !int.TryParse(parts[0], out int actor) || actor <= 0 ||
                    parts[0] != actor.ToString() || parts[1] != Path.GetFileName(parts[1]) ||
                    parts[1].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !parts[1].EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("语音发布清单含无效路径：" + item);
            }
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folders = new Dictionary<int, string>();
            var updates = new List<string>();
            foreach (var entry in table.Files)
            {
                if (!folders.TryGetValue(entry.actor, out string folder))
                {
                    var matches = Directory.GetDirectories(art, entry.actor + "_*");
                    if (matches.Length != 1) throw new InvalidDataException($"角色 {entry.actor} 的语音目录不唯一或不存在");
                    folders.Add(entry.actor, folder = matches[0]);
                }
                string source = Path.Combine(folder, entry.clip + ".wav");
                string rel = entry.actor + "/" + entry.clip + ".wav";
                if (!expected.Add(rel)) throw new InvalidDataException("语音文件名大小写冲突：" + rel);
                if (!File.Exists(source)) throw new FileNotFoundException("配表引用的语音不存在", source);
                string target = Path.Combine(destination, rel);
                var original = new FileInfo(source);
                if (File.Exists(target) && new FileInfo(target).Length == original.Length && File.GetLastWriteTimeUtc(target) == original.LastWriteTimeUtc)
                    continue;
                string staged = Path.Combine(temp, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(staged));
                File.Copy(source, staged); updates.Add(rel);
            }
            foreach (string rel in updates) Install(Path.Combine(temp, rel), Path.Combine(destination, rel));
            string runtimeTable = Path.Combine(streaming, "Data", CharacterVoiceTable.FileName);
            if (!File.Exists(runtimeTable) || !File.ReadAllBytes(runtimeTable).SequenceEqual(File.ReadAllBytes(tableCopy))) Install(tableCopy, runtimeTable);
            int removed = 0;
            foreach (string item in previous)
                if (!expected.Contains(item))
                {
                    string file = Path.Combine(destination, item);
                    File.Delete(file); File.Delete(file + ".meta"); removed++;
                }
            var next = expected.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (!File.Exists(manifest) || !previous.SequenceEqual(next))
            {
                string staged = Path.Combine(temp, "published.txt");
                File.WriteAllLines(staged, next); Install(staged, manifest);
            }
            return $"角色语音同步完成，共引用 {expected.Count} 个 WAV，更新 {updates.Count}，移除 {removed}。";
        }
        finally { Directory.Delete(temp, true); }
    }

    private static void Install(string staged, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        if (File.Exists(target)) File.Replace(staged, target, null);
        else File.Move(staged, target);
    }
}
