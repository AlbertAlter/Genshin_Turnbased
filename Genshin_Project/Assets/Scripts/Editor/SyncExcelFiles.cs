using UnityEngine;
using UnityEditor;
using System.IO;

public class SyncExcelFiles
{
    [MenuItem("Tools/Sync Charts to StreamingAssets")]
    public static void Sync()
    {
        string projectDir = Directory.GetParent(Application.dataPath).FullName;
        string rootDir = Directory.GetParent(projectDir).FullName;
        string sourceDir = Path.Combine(rootDir, "Charts");

        string targetDir = Path.Combine(Application.streamingAssetsPath, "Data");

        if (!Directory.Exists(sourceDir))
        {
            Debug.LogError("Source folder does not exist: " + sourceDir);
            return;
        }

        // Clear entire target
        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, true);

        Directory.CreateDirectory(targetDir);

        // Recursively find all xlsx, including subdirectories
        string[] files = Directory.GetFiles(sourceDir, "*.xlsx", SearchOption.AllDirectories);
        int count = 0;

        foreach (string file in files)
        {
            string relPath = file.Substring(sourceDir.Length + 1);
            string destPath = Path.Combine(targetDir, relPath);

            string destDir = Path.GetDirectoryName(destPath);
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            File.Copy(file, destPath, true);
            count++;
        }

        AssetDatabase.Refresh();
        Debug.Log("Sync done: copied " + count + " files to " + targetDir);
    }
}
