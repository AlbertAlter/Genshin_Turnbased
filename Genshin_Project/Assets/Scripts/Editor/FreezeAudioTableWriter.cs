using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Editor-only explicit write-back. Runtime readers never access Charts.</summary>
[InitializeOnLoad]
public static class FreezeAudioTableWriter
{
    static FreezeAudioTableWriter() => FreezeAudioPreviewPanel.WriteTable = Save;

    private static string Save(FreezeAudioSettings settings)
    {
        var values = settings.Values();
        string repository = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        string source = Path.Combine(repository, "Charts", "UI", FreezeAudioSettings.FileName);
        byte[] edited = ParameterWorkbookPatch.Update(File.ReadAllBytes(source), values);
        File.WriteAllBytes(source, edited);
        string target = Path.Combine(Application.streamingAssetsPath, "Data", "UI", FreezeAudioSettings.FileName);
        File.WriteAllBytes(target, edited);
        AssetDatabase.ImportAsset("Assets/StreamingAssets/Data/UI/" + FreezeAudioSettings.FileName, ImportAssetOptions.ForceUpdate);
        return "已写回Charts/UI/FreezeAudio.xlsx并发布运行时表。";
    }
}
