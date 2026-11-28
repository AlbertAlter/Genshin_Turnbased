using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit editor write-back using the shared OOXML patcher; never saves scene layout.</summary>
[InitializeOnLoad]
public static class CardTargetIndicatorTableWriter
{
    static CardTargetIndicatorTableWriter()
    {
        CardFaceTargetedPreview.WriteTable = Save;
        CardFaceConductivePreview.WriteTable = style =>
        {
            var values = CardFaceConductiveTable.Values(style);
            var text = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in values) text.Add(pair.Key, pair.Value.ToString("R", CultureInfo.InvariantCulture));
            CardFaceConductiveTable.Parse(text);
            return SaveParameters(CardFaceConductiveTable.FileName, values, path => CardFaceConductiveTable.ReadFile(path));
        };
        CardFaceConductivePreview.WriteGlyphTable = (file, style) =>
        {
            if (file != CardFaceConductiveTable.FileName && file != "QiqiTalisman.xlsx")
                throw new ArgumentException("未注册的卡面符印参数表。");
            var values = CardFaceConductiveTable.Values(style);
            var raw = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in values) raw.Add(pair.Key, pair.Value.ToString("R", CultureInfo.InvariantCulture));
            CardFaceConductiveTable.Parse(raw);
            return SaveParameters(file, values, path => CardFaceConductiveTable.ReadFile(path));
        };
    }

    private static string Save(CardTargetIndicatorStyle style)
    {
        return SaveParameters(CardTargetIndicatorTable.FileName, CardTargetIndicatorTable.Values(style), path => CardTargetIndicatorTable.ReadFile(path));
    }

    private static string SaveParameters(string fileName, IReadOnlyDictionary<string, float> values, Action<string> validate)
    {
        string repository = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        string source = Path.Combine(repository, "Charts", "UI", CardTargetIndicatorTable.Folder, fileName);
        string target = Path.Combine(Application.streamingAssetsPath, "Data", "UI", CardTargetIndicatorTable.Folder, fileName);
        validate(source);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        bool targetExisted = File.Exists(target), openedTarget = false, saved = false;
        try
        {
            // Acquire both locks before changing either file. An Excel lock fails without a partial save.
            using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var targetStream = new FileStream(target, targetExisted ? FileMode.Open : FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None);
            openedTarget = true;
            byte[] sourceBefore = Read(sourceStream), targetBefore = Read(targetStream);
            byte[] edited = ParameterWorkbookPatch.Update(sourceBefore, values);
            try
            {
                Write(sourceStream, edited); Write(targetStream, edited);
                saved = true;
            }
            catch (Exception failure)
            {
                try { Write(sourceStream, sourceBefore); Write(targetStream, targetBefore); }
                catch (Exception recovery)
                { throw new IOException("写回未完成，且文件还原失败：" + recovery.Message, failure); }
                throw;
            }
        }
        finally { if (!saved && openedTarget && !targetExisted && File.Exists(target)) File.Delete(target); }
        AssetDatabase.ImportAsset("Assets/StreamingAssets/Data/UI/" + CardTargetIndicatorTable.Folder + "/" +
            fileName, ImportAssetOptions.ForceUpdate);
        return "已写回并发布：Charts/UI/CardFaceUI/" + fileName;
    }

    private static byte[] Read(Stream stream)
    {
        using var data = new MemoryStream(); stream.Position = 0; stream.CopyTo(data); return data.ToArray();
    }
    private static void Write(FileStream stream, byte[] bytes)
    {
        stream.Position = 0; stream.Write(bytes, 0, bytes.Length); stream.SetLength(bytes.Length); stream.Flush(true);
    }
}
