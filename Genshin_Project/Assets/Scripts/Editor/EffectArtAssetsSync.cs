using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>Publishes UE effect exports as native Unity assets. Never copies Blender sources or changes scenes.</summary>
public static class EffectArtAssetsSync
{
    private static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { ".fbx", ".obj", ".mtl", ".png", ".tga", ".jpg", ".jpeg", ".tif", ".tiff", ".exr", ".hdr" };

    public static string Publish(string sourceRoot,string targetRoot)
        =>Publish(sourceRoot,targetRoot,Path.Combine(UnityEngine.Application.streamingAssetsPath,"Data","UE","Clips"));
    public static string Publish(string sourceRoot, string targetRoot, string clipTargetRoot)
    {
        string source = Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string target = Path.GetFullPath(targetRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("特效导入源目录与目标目录不能重叠。");
        if (!Directory.Exists(source)) return "UE特效目录不存在，本次跳过：" + source;
        string clips = Path.GetFullPath(clipTargetRoot);

        int copied = 0, skipped = 0;
        foreach (string file in ExportFiles(source).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string relative = file.Substring(source.Length + 1);
            bool sequence=file.EndsWith(".vfx.bytes",StringComparison.OrdinalIgnoreCase);
            string root=sequence?clips:target;
            string destination = Path.GetFullPath(Path.Combine(root, relative));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("特效导出路径越过目标目录：" + relative);
            if (File.Exists(destination))
            {
                var from = new FileInfo(file); var to = new FileInfo(destination);
                if (from.Length == to.Length && from.LastWriteTimeUtc == to.LastWriteTimeUtc) { skipped++; continue; }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(file, destination, true);
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(file));
            copied++;
        }
        // Sequence payloads reference their exported PNGs by relative path; sprites use the same published root.
        foreach(string file in ExportFiles(source).Where(f=>f.EndsWith(".png",StringComparison.OrdinalIgnoreCase)))
        {
            string destination=Path.Combine(clips,file.Substring(source.Length+1));
            var from=new FileInfo(file);var to=new FileInfo(destination);
            if(to.Exists&&from.Length==to.Length&&from.LastWriteTimeUtc==to.LastWriteTimeUtc)continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(file,destination,true);File.SetLastWriteTimeUtc(destination,from.LastWriteTimeUtc);copied++;
        }
        return $"UE特效导入完成：复制/更新 {copied}，跳过 {skipped}。目标 {target}；保留现有.meta，不删除资产或重建场景。";
    }

    private static IEnumerable<string> ExportFiles(string root)
    {
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            // Source trees may contain authoring links; publication only walks physical folders.
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (string file in Walk(directory, false)) yield return file;
        }
    }

    private static IEnumerable<string> Walk(string directory, bool inExports)
    {
        inExports |= string.Equals(Path.GetFileName(directory), "Exports", StringComparison.OrdinalIgnoreCase);
        if (inExports)
            foreach (string file in Directory.EnumerateFiles(directory))
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 && (Extensions.Contains(Path.GetExtension(file))||file.EndsWith(".vfx.bytes",StringComparison.OrdinalIgnoreCase)))
                    yield return file;
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (string file in Walk(child, inExports)) yield return file;
        }
    }
}
