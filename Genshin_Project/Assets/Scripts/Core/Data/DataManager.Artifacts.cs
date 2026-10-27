using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public partial class DataManager
{
    public static string ArtifactStatusKey(int statusID) => $"ST_Artifact_{statusID}";
    public static string ArtifactEffectKey(int effectID) => $"STE_Artifact_{effectID}";

    private void LoadArtifactStatusData()
    {
        const string fileName = "StatusData_Artifacts.xlsx";
        string path = Path.Combine(ChartsPath, fileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"{fileName} not found");
            return;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        ExcelWorksheet main = RequireArtifactSheet(package, "StatusData_Main_Artifacts");
        ExcelWorksheet actions = RequireArtifactSheet(package, "Status_Action_Artifacts");
        ExcelWorksheet effects = RequireArtifactSheet(package, "Status_Effect");
        // 保留现有表名的拼写，同时允许后续改成 Initiate。
        ExcelWorksheet initiate = package.Workbook.Worksheets["Artifacts_Initiate"]
            ?? RequireArtifactSheet(package, "Artifacts_Inititate");

        Dictionary<int, string> statusKeys = ReadArtifactKeys(main, "StatusID", "StatusID2", ArtifactStatusKey);
        Dictionary<int, string> effectKeys = ReadArtifactKeys(effects, "StatusEffectID", "StatusEffectID2", ArtifactEffectKey);
        string ResolveStatus(string value) => ResolveArtifactReference(
            CountStatusSystem.ResolvePublicStatusReference(value), statusKeys, ArtifactStatusKey);
        string ResolveEffect(string value) => ResolveArtifactReference(value, effectKeys, ArtifactEffectKey);

        LoadStatusMainSheet(main, id => statusKeys[id]);
        LoadStatusEffectSheet(effects, $"{fileName}/{effects.Name}", false,
            id => effectKeys[id], ResolveStatus);

        for (int row = GetDataStartRow(actions); row <= GetSheetMaxRow(actions); row++)
        {
            int id = SafeGetInt(actions.Cells[row, FindColumnByHeader(actions, "StatusID")].Value);
            string key = actions.Cells[row, FindColumnByHeader(actions, "StatusID2")].Text.Trim();
            if (key.Length == 0 && id <= 0) continue;
            key = ResolveStatus(key.Length > 0 ? key : id.ToString(CultureInfo.InvariantCulture));
            if (!StatusMainDict.ContainsKey(key))
                throw new InvalidDataException($"[{actions.Name}] row {row}: 状态不存在 {key}");

            StatusActionData action = ReadStatusAction(actions, row, true, $"{fileName}/{actions.Name}");
            action.StatusID2 = key;
            action.Param1 = ResolveArtifactReferenceList(action.Param1, ResolveEffect);
            if (!StatusActionDict.TryGetValue(key, out List<StatusActionData> list))
                StatusActionDict[key] = list = new List<StatusActionData>();
            list.Add(action);
        }

        LoadArtifactInitiateSheet(initiate, ResolveStatus);
        LogManager.Log(LogCategory.Data, $"Artifact statuses loaded, {ArtifactInitiateStatusDict.Count} sets");
    }

    private void LoadArtifactInitiateSheet(ExcelWorksheet sheet, Func<string, string> resolveStatusKey)
    {
        // 初始化槽按表头发现，不限定6个；行末备注不会被当成状态引用。
        var initiateColumns = ExcelTableHeaders.For(sheet).NumberedColumns("InitiateStatus");
        if (initiateColumns.Count == 0)
            throw new InvalidDataException($"[{sheet.Name}] 缺少 InitiateStatus 初始化列。");

        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            int setID = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "ArtifactSetID")].Value);
            if (setID <= 0) continue;
            int setNumber = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "SetNumber")].Value);
            if (setNumber != 2 && setNumber != 4)
                throw new InvalidDataException($"[{sheet.Name}] row {row}: SetNumber 必须是 2 或 4。");

            if (!ArtifactInitiateStatusDict.TryGetValue(setID, out Dictionary<int, List<string>> tiers))
                ArtifactInitiateStatusDict[setID] = tiers = new Dictionary<int, List<string>>();
            if (tiers.ContainsKey(setNumber))
                throw new InvalidDataException($"[{sheet.Name}] row {row}: 重复套装配置 {setID}/{setNumber}。");

            var statuses = new List<string>();
            foreach (int column in initiateColumns)
            {
                string reference = sheet.Cells[row, column].Text.Trim();
                if (reference.Length == 0 || reference == "0") continue;
                string key = resolveStatusKey(reference);
                if (!StatusMainDict.ContainsKey(key))
                    throw new InvalidDataException($"[{sheet.Name}] row {row}: 初始化状态不存在 {reference}。");
                if (!statuses.Contains(key)) statuses.Add(key);
            }
            tiers.Add(setNumber, statuses);
        }
    }

    /// <summary>
    /// 0/1 件无效果，2/3 件返回两件套，4 件及以上按两件套、四件套顺序返回。
    /// 返回新列表，同一状态只初始化一次；未填写的套装/档位返回空结果。
    /// </summary>
    public IReadOnlyList<string> GetArtifactInitiateStatuses(int artifactSetID, int equippedCount)
    {
        if (equippedCount < 0) throw new ArgumentOutOfRangeException(nameof(equippedCount));
        var result = new List<string>();
        if (!ArtifactInitiateStatusDict.TryGetValue(artifactSetID, out Dictionary<int, List<string>> tiers))
            return result;

        foreach (int threshold in new[] { 2, 4 })
        {
            if (equippedCount < threshold || !tiers.TryGetValue(threshold, out List<string> statuses)) continue;
            foreach (string key in statuses)
                if (!result.Contains(key)) result.Add(key);
        }
        return result;
    }

    private Dictionary<int, string> ReadArtifactKeys(ExcelWorksheet sheet, string idHeader,
        string keyHeader, Func<int, string> fallback)
    {
        var keys = new Dictionary<int, string>();
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            int id = SafeGetInt(ExcelTableHeaders.For(sheet).Cell(row, idHeader).Value);
            if (id <= 0) continue;
            string explicitKey = ExcelTableHeaders.For(sheet).Cell(row, keyHeader).Text.Trim();
            if (keys.ContainsKey(id))
                throw new InvalidDataException($"[{sheet.Name}] row {row}: 重复编号 {id}。");
            keys.Add(id, explicitKey.Length > 0 ? explicitKey : fallback(id));
        }
        return keys;
    }

    private static string ResolveArtifactReference(string value, Dictionary<int, string> keys,
        Func<int, string> fallback)
    {
        value = value?.Trim();
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) return value;
        return keys.TryGetValue(id, out string key) ? key : fallback(id);
    }

    private static string ResolveArtifactReferenceList(string value, Func<string, string> resolve)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        string[] parts = value.Split(';');
        for (int index = 0; index < parts.Length; index++) parts[index] = resolve(parts[index]);
        return string.Join(";", parts);
    }

    private static ExcelWorksheet RequireArtifactSheet(ExcelPackage package, string sheetName)
    {
        return package.Workbook.Worksheets[sheetName]
            ?? throw new InvalidDataException($"StatusData_Artifacts.xlsx 缺少工作表 {sheetName}。");
    }
}
