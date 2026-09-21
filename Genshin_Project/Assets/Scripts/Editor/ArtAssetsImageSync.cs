using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Legacy full-folder sync implementation retained as a path utility for
/// ReferencedArtAssetsSync. It intentionally has no menu entry: UI artists use
/// the single table-driven sync command instead.
/// </summary>
public static class ArtAssetsImageSync
{
    private const string SourceFolderName = "art_assets";
    private const string TargetFolderName = "art_assets";
    private static readonly HashSet<string> ImageExtensions = new HashSet<string>(
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".tif", ".tiff", ".gif", ".webp", ".svg", ".exr", ".hdr" },
        StringComparer.OrdinalIgnoreCase);

    internal static void SyncImages()
    {
        string sourceRoot = GetSourceRoot();
        string targetRoot = GetTargetRoot();
        if (!Directory.Exists(sourceRoot))
        {
            Debug.LogError("art_assets 美术源目录不存在：" + sourceRoot);
            return;
        }

        string[] sourceFiles = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(IsImageFile).ToArray();
        Directory.CreateDirectory(targetRoot);
        int copied = 0;
        int skipped = 0;
        int failed = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int index = 0; index < sourceFiles.Length; index++)
            {
                string sourcePath = sourceFiles[index];
                string relativePath = GetRelativePathUnderRoot(sourceRoot, sourcePath);
                if (EditorUtility.DisplayCancelableProgressBar(
                        "同步全部 art_assets 图片", relativePath,
                        sourceFiles.Length == 0 ? 1f : (float)index / sourceFiles.Length))
                    break;

                try
                {
                    string targetPath = GetSafeTargetPath(targetRoot, relativePath);
                    if (!NeedsCopy(sourcePath, targetPath))
                    {
                        skipped++;
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                    File.Copy(sourcePath, targetPath, true);
                    File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));
                    copied++;
                }
                catch (Exception exception)
                {
                    failed++;
                    Debug.LogError("同步图片失败：" + relativePath + Environment.NewLine + exception);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        Debug.Log($"全部 art_assets 图片同步完成：复制/更新 {copied}，跳过 {skipped}，失败 {failed}。" +
                  Environment.NewLine + "源目录：" + sourceRoot +
                  Environment.NewLine + "目标目录：" + targetRoot);
    }

    internal static string GetSourceRoot()
    {
        string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        DirectoryInfo repositoryRoot = Directory.GetParent(unityProjectRoot);
        if (repositoryRoot == null)
            throw new DirectoryNotFoundException("Cannot locate repository root from the Unity project.");
        return Path.GetFullPath(Path.Combine(repositoryRoot.FullName, SourceFolderName));
    }

    internal static string GetTargetRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, TargetFolderName));
    }

    private static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    private static string GetRelativePathUnderRoot(string rootPath, string filePath)
    {
        string root = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
        string file = Path.GetFullPath(filePath);
        if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Image is outside art_assets: " + file);
        return file.Substring(root.Length);
    }

    private static string GetSafeTargetPath(string targetRoot, string relativePath)
    {
        string root = EnsureTrailingSeparator(Path.GetFullPath(targetRoot));
        string target = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Target path escapes StreamingAssets/art_assets: " + relativePath);
        return target;
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static bool NeedsCopy(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath)) return true;
        var source = new FileInfo(sourcePath);
        var target = new FileInfo(targetPath);
        return source.Length != target.Length || source.LastWriteTimeUtc != target.LastWriteTimeUtc;
    }
}
