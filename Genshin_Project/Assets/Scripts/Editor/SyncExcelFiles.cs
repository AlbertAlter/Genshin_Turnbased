using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class SyncExcelFiles
{
    [MenuItem("Tools/Sync Charts to StreamingAssets")]
    public static void Sync()
    {
        var project = Directory.GetParent(Application.dataPath);
        string target = Path.Combine(Application.streamingAssetsPath, "Data");
        AssetDatabase.StartAssetEditing();
        try
        {
            int count = Publish(Path.Combine(project.Parent.FullName, "Charts"), target,
                Path.Combine(project.FullName, "Library", "ChartPublication"), candidate =>
                {
                    ValidateCandidate(candidate);
                    ConfirmRemovals(target, candidate);
                });
            Debug.Log($"配表已发布：{count} 本工作簿 → {target}");
        }
        catch (OperationCanceledException) { Debug.Log("配表发布已取消，未替换运行目录。"); }
        catch (Exception error) { Debug.LogError("配表发布失败：" + error.Message); }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }
    }

    internal static void ValidateCandidate(string candidate)
    {
        var errors = DataSourcePreflightValidator.ValidatePublication(candidate);
        if (errors.Count != 0) throw new InvalidDataException(string.Join("\n", errors));
    }

    private static void ConfirmRemovals(string target, string candidate)
    {
        if (!Directory.Exists(target)) return;
        var retired = new List<string>();
        foreach (string file in Directory.EnumerateFiles(target, "*.xlsx", SearchOption.AllDirectories))
        {
            string relative = file.Substring(target.Length + 1);
            if (!File.Exists(Path.Combine(candidate, relative))) retired.Add(relative);
        }
        if (retired.Count == 0) return;
        retired.Sort(StringComparer.Ordinal);
        Debug.Log("本次发布拟移除工作簿：\n" + string.Join("\n", retired));
        string preview = string.Join("\n", retired.GetRange(0, Math.Min(retired.Count, 10)));
        if (!EditorUtility.DisplayDialog("确认移除已发布配表", $"将移除 {retired.Count} 本工作簿（完整清单见 Console）：\n{preview}", "继续发布", "取消"))
            throw new OperationCanceledException();
    }

    internal static int Publish(string source, string target, string workspace, Action<string> validate)
    {
        if (validate == null) throw new ArgumentNullException(nameof(validate));
        source = Path.GetFullPath(source);
        target = Path.GetFullPath(target);
        workspace = Path.GetFullPath(workspace);
        if (Overlaps(source, target) || Overlaps(source, workspace) || Overlaps(target, workspace))
            throw new ArgumentException("配表源、运行目录及发布暂存目录必须相互独立。");
        string candidate = Path.Combine(workspace, "candidate");
        string previous = Path.Combine(workspace, "previous");
        Directory.CreateDirectory(workspace);
        RejectLink(workspace);
        using var publishLock = new FileStream(Path.Combine(workspace, "publish.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        if (Directory.Exists(previous))
        {
            if (Directory.Exists(target)) RemoveTree(previous);
            else Directory.Move(previous, target);
        }
        if (Directory.Exists(candidate)) RemoveTree(candidate);
        Directory.CreateDirectory(candidate);
        try
        {
            int count = 0;
            foreach (string file in FilesUnder(source))
            {
                string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!IsWorkbook(file) || Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)
                    || relative.Equals("EnemySkill.xlsx", StringComparison.OrdinalIgnoreCase)
                    || relative.Equals("StatusData_Enemy.xlsx", StringComparison.OrdinalIgnoreCase)) continue;
                Copy(file, Path.Combine(candidate, relative));
                count++;
            }
            if (count == 0) throw new InvalidDataException("没有可发布的工作簿。");
            bool hadTarget = Directory.Exists(target);
            if (hadTarget) PreserveAssets(target, candidate);
            validate(candidate);
            if (hadTarget) Directory.Move(target, previous);
            try { Directory.Move(candidate, target); }
            catch
            {
                if (hadTarget)
                {
                    try { Directory.Move(previous, target); }
                    catch (Exception recovery) { throw new IOException($"无法还原运行目录；上一版保留在 {previous}，请先恢复。", recovery); }
                }
                throw;
            }
            if (hadTarget) TryClean(previous);
            return count;
        }
        finally { TryClean(candidate); }
    }

    private static void PreserveAssets(string target, string candidate)
    {
        var metadata = new List<KeyValuePair<string, string>>();
        string Destination(string path) => Path.Combine(candidate, path.Substring(target.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (string file in FilesUnder(target, directory => Directory.CreateDirectory(Destination(directory))))
        {
            string destination = Destination(file);
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                metadata.Add(new KeyValuePair<string, string>(file, destination));
            else if (!IsWorkbook(file)) Copy(file, destination);
        }
        foreach (var pair in metadata)
        {
            string asset = Path.ChangeExtension(pair.Value, null);
            if (File.Exists(asset) || Directory.Exists(asset)) Copy(pair.Key, pair.Value);
        }
    }

    private static bool IsWorkbook(string path) => Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    private static bool Overlaps(string first, string second)
    {
        string left = first.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string right = second.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return left.StartsWith(right, StringComparison.OrdinalIgnoreCase) || right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
    }

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(source, destination);
    }

    private static IEnumerable<string> FilesUnder(string root, Action<string> visitDirectory = null)
    {
        RejectLink(root);
        visitDirectory?.Invoke(root);
        foreach (string file in Directory.EnumerateFiles(root)) { RejectLink(file); yield return file; }
        foreach (string directory in Directory.EnumerateDirectories(root))
            foreach (string file in FilesUnder(directory, visitDirectory)) yield return file;
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("配表发布目录不支持链接：" + path);
    }

    private static void RemoveTree(string path)
    {
        RejectLink(path);
        foreach (string directory in Directory.GetDirectories(path)) RemoveTree(directory);
        foreach (string file in Directory.GetFiles(path)) { RejectLink(file); File.Delete(file); }
        Directory.Delete(path);
    }

    private static void TryClean(string path)
    {
        try { if (Directory.Exists(path)) RemoveTree(path); }
        catch (IOException error) { Debug.LogWarning($"暂存目录待下次发布清理：{path}；{error.Message}"); }
        catch (UnauthorizedAccessException error) { Debug.LogWarning($"暂存目录待下次发布清理：{path}；{error.Message}"); }
    }
}
