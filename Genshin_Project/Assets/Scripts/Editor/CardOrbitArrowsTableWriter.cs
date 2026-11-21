using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit preview write-back, using the existing narrow OOXML patcher.</summary>
[InitializeOnLoad]
public static class CardOrbitArrowsTableWriter
{
    static CardOrbitArrowsTableWriter() => StatusEffectsPreview.WriteTable = Save;
    private static string Save(IReadOnlyDictionary<string, float> numbers)
    {
        string repository = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        string source = Path.Combine(repository, "Charts", "UI", "StatusPopUp", UICardOrbitArrowStyle.FileName);
        string target = Path.Combine(Application.streamingAssetsPath, "Data", "UI", "StatusPopUp", UICardOrbitArrowStyle.FileName);
        UICardOrbitArrowStyle.ReadFile(source).WithNumbers(numbers);
        byte[] output = ParameterWorkbookPatch.Update(File.ReadAllBytes(source), numbers);
        using (var a = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var b = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        { a.Write(output, 0, output.Length); a.SetLength(output.Length); b.Write(output, 0, output.Length); b.SetLength(output.Length); }
        AssetDatabase.ImportAsset("Assets/StreamingAssets/Data/UI/StatusPopUp/" + UICardOrbitArrowStyle.FileName);
        return "已写回 Charts 参数表并发布运行时表。";
    }
}
