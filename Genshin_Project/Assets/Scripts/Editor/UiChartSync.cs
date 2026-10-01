using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Publishes Charts/UI/Battlefield1 tables to their runtime location.</summary>
public static class UiChartSync
{
    private static string SourceRoot()
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Charts", "UI", "Battlefield1"));
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("UI 配表源目录不存在：" + source);
        return source;
    }

    public static void SyncAnimation()
    {
        string sourceRoot = SourceRoot();
        string source = Path.Combine(sourceRoot, BattlefieldAnimationParameters.FileName);
        BattlefieldAnimationParameters.ReadFile(source);
        string directory = Path.Combine(Application.streamingAssetsPath, "Charts", "UI");
        Directory.CreateDirectory(directory);
        File.Copy(source, Path.Combine(directory, BattlefieldAnimationParameters.FileName), true);
        AssetDatabase.Refresh();
    }
    public static void Sync()
    {
        string sourceRoot = SourceRoot();
        string targetRoot = Path.Combine(Application.streamingAssetsPath, "Charts", "UI");
        Publish(sourceRoot, targetRoot);
    }

    // Shared publishing implementation for the menu and isolated validation builds.
    public static void Publish(string sourceRoot, string targetRoot)
    {
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException("UI 配表源目录不存在：" + sourceRoot);
        }

        Directory.CreateDirectory(targetRoot);
        int copied = 0;
        int removed = 0;
        var published = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string sourcePath in Directory.EnumerateFiles(sourceRoot, "*.csv", SearchOption.TopDirectoryOnly))
        {
            string targetPath = Path.Combine(targetRoot, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, targetPath, true);
            File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));
            published.Add(Path.GetFileName(sourcePath));
            copied++;
        }
        foreach (string targetPath in Directory.EnumerateFiles(targetRoot, "*.csv", SearchOption.TopDirectoryOnly))
        {
            if (published.Contains(Path.GetFileName(targetPath))) continue;
            File.Delete(targetPath);
            removed++;
        }
        AssetDatabase.Refresh();
        Debug.Log($"UI 配表已发布到 StreamingAssets/Charts/UI：{copied} 个文件，移除过期表 {removed} 个。");
    }
}
