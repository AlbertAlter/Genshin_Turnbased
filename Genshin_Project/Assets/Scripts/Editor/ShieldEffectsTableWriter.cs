using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit Editor write-back; patches only tuned Value cells and publishes the same workbook.</summary>
[InitializeOnLoad]
public static class ShieldEffectsTableWriter
{
    static ShieldEffectsTableWriter() => ShieldPreviewPanel.WriteTable = Save;

    private static string Save(IReadOnlyDictionary<string, float> values)
    {
        string repository = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        string source = Path.Combine(repository, "Charts", "UI", UIShieldStyle.FileName);
        string target = Path.Combine(Application.streamingAssetsPath, "Data", "UI", UIShieldStyle.FileName);
        UIShieldStyle.ReadFile(source).WithNumbers(values);
        byte[] edited = ParameterWorkbookPatch.Update(File.ReadAllBytes(source), values);
        // 两个文件都可写后才开始保存；Excel占用时保留原文件并报告错误。
        using (var sourceStream = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var targetStream = new FileStream(target, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            sourceStream.Write(edited, 0, edited.Length); sourceStream.SetLength(edited.Length);
            targetStream.Write(edited, 0, edited.Length); targetStream.SetLength(edited.Length);
        }
        AssetDatabase.ImportAsset("Assets/StreamingAssets/Data/UI/" + UIShieldStyle.FileName, ImportAssetOptions.ForceUpdate);
        return "已写回Charts/UI/ShieldEffects.xlsx并发布运行时表。";
    }
}
