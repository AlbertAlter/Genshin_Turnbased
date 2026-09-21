using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Publishes repository Charts/UI tables to their runtime location.</summary>
public static class UiChartSync
{
    [MenuItem("Tools/UI 管线/同步 UI 配表到 StreamingAssets")]
    public static void Sync()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        DirectoryInfo repository = Directory.GetParent(projectRoot);
        if (repository == null) throw new DirectoryNotFoundException("Cannot locate repository root.");

        string sourceRoot = Path.Combine(repository.FullName, "Charts", "UI");
        string targetRoot = Path.Combine(Application.streamingAssetsPath, "Charts", "UI");
        if (!Directory.Exists(sourceRoot))
        {
            Debug.LogError("UI 配表源目录不存在：" + sourceRoot);
            return;
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
