using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reads runtime tables below StreamingAssets (including Charts/UI) and copies the
/// referenced files from repository art_assets into StreamingAssets/art_assets.
/// </summary>
public static class ReferencedArtAssetsSync
{
    private static readonly HashSet<string> ImageExtensions = new HashSet<string>(
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".tif", ".tiff", ".gif", ".webp", ".svg", ".exr", ".hdr" },
        StringComparer.OrdinalIgnoreCase);

    [MenuItem("Tools/UI 管线/按配表同步所需美术资源")]
    public static void SyncReferencedImages()
    {
        string dataRoot = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "Data"));
        string sourceRoot = ArtAssetsImageSync.GetSourceRoot();
        string targetRoot = ArtAssetsImageSync.GetTargetRoot();
        if (!Directory.Exists(sourceRoot))
        {
            Debug.LogError("art_assets 美术源目录不存在：" + sourceRoot);
            return;
        }

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        if (Directory.Exists(dataRoot)) CollectDirectImageReferences(dataRoot, required, warnings);
        else warnings.Add("StreamingAssets/Data 不存在；本次只按 Charts/UI 收集美术资源：" + dataRoot);
        CollectSkillIcons(dataRoot, sourceRoot, required, warnings);
        CollectUiTableReferences(sourceRoot, required, warnings);
        CollectStageBackgrounds(sourceRoot, required, warnings);
        CharacterCardArtManifest cardManifest = CollectCharacterCards(dataRoot, sourceRoot, required, warnings);
        EnemyArtManifest enemyManifest = CollectEnemyArt(dataRoot, sourceRoot, required, warnings);

        string[] paths = required.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        int copied = 0;
        int skipped = 0;
        bool cancelled = false;
        var missing = new List<string>();
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int index = 0; index < paths.Length; index++)
            {
                string relativePath = paths[index];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "按配表同步所需美术资源", relativePath,
                        paths.Length == 0 ? 1f : (float)index / paths.Length))
                {
                    cancelled = true;
                    break;
                }

                string sourcePath = SafeCombine(sourceRoot, relativePath);
                string targetPath = SafeCombine(targetRoot, relativePath);
                if (!File.Exists(sourcePath))
                {
                    missing.Add(relativePath);
                    continue;
                }
                if (!NeedsCopy(sourcePath, targetPath))
                {
                    skipped++;
                    continue;
                }

                string directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.Copy(sourcePath, targetPath, true);
                File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));
                copied++;
            }

            if (!cancelled)
            {
                WriteCharacterCardManifest(targetRoot, cardManifest);
                WriteEnemyArtManifest(targetRoot, enemyManifest);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        foreach (string warning in warnings) Debug.LogWarning(warning);
        foreach (string path in missing) Debug.LogWarning("配表引用的美术资源不存在：art_assets/" + path);
        Debug.Log(
            $"按配表同步美术资源完成：识别 {paths.Length}，复制/更新 {copied}，跳过 {skipped}，" +
            $"缺失 {missing.Count}，角色表 {cardManifest.characters.Length}，有图片的敌人 {enemyManifest.enemies.Length}。" +
            (cancelled ? "同步已取消，角色和敌人图片清单未改动。" : string.Empty));
    }

    private static void CollectStageBackgrounds(
        string sourceRoot,
        HashSet<string> result,
        List<string> warnings)
    {
        string stageRoot = Path.Combine(Application.dataPath, "Resources", "Stages");
        if (!Directory.Exists(stageRoot)) return;
        foreach (string stagePath in Directory.EnumerateFiles(stageRoot, "*.json"))
        {
            try
            {
                StageSetupData stage = JsonUtility.FromJson<StageSetupData>(File.ReadAllText(stagePath));
                int battleId = Mathf.Max(1, stage?.BattleID ?? 1);
                string relativePath = $"BattleBG/{battleId}_BG.png";
                if (File.Exists(SafeCombine(sourceRoot, relativePath))) result.Add(relativePath);
                else warnings.Add($"关卡 {Path.GetFileName(stagePath)} 的战斗背景不存在：art_assets/{relativePath}");
            }
            catch (Exception exception)
            {
                warnings.Add($"无法读取关卡背景 {Path.GetFileName(stagePath)}：{exception.Message}");
            }
        }
    }

    internal static EnemyArtManifest CollectEnemyArt(
        string dataRoot,
        string sourceRoot,
        HashSet<string> result,
        List<string> warnings)
    {
        string workbookPath = Path.Combine(dataRoot, "EnemyAttributes.xlsx");
        var entries = new List<EnemyArtEntry>();
        if (!File.Exists(workbookPath))
        {
            warnings.Add("StreamingAssets 中不存在 EnemyAttributes.xlsx：" + workbookPath);
            return new EnemyArtManifest { enemies = entries.ToArray() };
        }

        try
        {
            using var package = new ExcelPackage(new FileInfo(workbookPath));
            ExcelWorksheet sheet = package.Workbook.Worksheets["Enemy_Main"];
            int idColumn = FindColumn(sheet, "EnemyID");
            if (sheet?.Dimension == null || idColumn <= 0)
            {
                warnings.Add("EnemyAttributes.xlsx/Enemy_Main 缺少 EnemyID 表头。");
                return new EnemyArtManifest { enemies = entries.ToArray() };
            }

            var seen = new HashSet<int>();
            for (int row = 2; row <= sheet.Dimension.End.Row; row++)
            {
                if (!int.TryParse(sheet.Cells[row, idColumn].Text.Trim(), out int enemyId) ||
                    enemyId <= 0 || !seen.Add(enemyId))
                    continue;

                string relativePath = $"Enemy/{enemyId}_01.png";
                if (!File.Exists(SafeCombine(sourceRoot, relativePath)))
                    continue;

                result.Add(relativePath);
                string iconPath = $"Enemy_Icon/{enemyId}_01.png";
                if (File.Exists(SafeCombine(sourceRoot, iconPath))) result.Add(iconPath);
                entries.Add(new EnemyArtEntry
                {
                    enemyId = enemyId,
                    artPath = "art_assets/" + relativePath
                });
            }
        }
        catch (Exception exception)
        {
            warnings.Add("Failed to collect enemy art from runtime EnemyAttributes.xlsx: " + exception.Message);
        }

        return new EnemyArtManifest { enemies = entries.OrderBy(entry => entry.enemyId).ToArray() };
    }

    internal static CharacterCardArtManifest CollectCharacterCards(
        string dataRoot,
        string sourceRoot,
        HashSet<string> result,
        List<string> warnings)
    {
        string characterTableRoot = Path.Combine(dataRoot, "Characters");
        var entries = new List<CharacterCardArtEntry>();
        string framePath = FindFirstFrame(sourceRoot, result, warnings);
        if (!Directory.Exists(characterTableRoot))
            return new CharacterCardArtManifest { framePath = framePath, characters = entries.ToArray() };

        foreach (string workbookPath in Directory.EnumerateFiles(characterTableRoot, "*.xlsx")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(workbookPath);
            if (fileName.StartsWith("0_", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("~$", StringComparison.Ordinal))
                continue;

            if (!TryReadCharacterId(workbookPath, out int characterId))
            {
                warnings.Add("Cannot read CharacterID from runtime character workbook: " + fileName);
                continue;
            }

            string relativeCardPath = $"Character_Whole/{characterId}_01.png";
            string sourceCardPath = SafeCombine(sourceRoot, relativeCardPath);
            if (!File.Exists(sourceCardPath))
            {
                warnings.Add($"Character {characterId} has no card image: art_assets/{relativeCardPath}");
                continue;
            }

            result.Add(relativeCardPath);
            entries.Add(new CharacterCardArtEntry
            {
                characterId = characterId,
                cardPath = "art_assets/" + relativeCardPath
            });
        }

        return new CharacterCardArtManifest
        {
            framePath = framePath,
            characters = entries.OrderBy(entry => entry.characterId).ToArray()
        };
    }

    internal static void CollectDirectImageReferences(
        string dataRoot,
        HashSet<string> result,
        List<string> warnings)
    {
        foreach (string workbookPath in Directory.EnumerateFiles(dataRoot, "*.xlsx", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(workbookPath).StartsWith("~$", StringComparison.Ordinal)) continue;
            try
            {
                using var package = new ExcelPackage(new FileInfo(workbookPath));
                foreach (ExcelWorksheet sheet in package.Workbook.Worksheets)
                {
                    if (sheet.Dimension == null) continue;
                    for (int row = 1; row <= sheet.Dimension.End.Row; row++)
                    for (int column = 1; column <= sheet.Dimension.End.Column; column++)
                    foreach (string candidate in SplitReferences(sheet.Cells[row, column].Text))
                    {
                        // Bare names such as 2271_01.png are table values, not files at
                        // art_assets root. Dedicated collectors resolve their directory.
                        if (!candidate.StartsWith("art_assets/", StringComparison.OrdinalIgnoreCase) &&
                            !candidate.StartsWith("art_assets\\", StringComparison.OrdinalIgnoreCase) &&
                            !candidate.Contains("/") && !candidate.Contains("\\"))
                            continue;
                        if (TryNormalizeImagePath(candidate, out string relativePath)) result.Add(relativePath);
                    }
                }
            }
            catch (Exception exception)
            {
                warnings.Add($"Failed to scan art references in {workbookPath}: {exception.Message}");
            }
        }
    }

    internal static void CollectSkillIcons(
        string dataRoot,
        string sourceRoot,
        HashSet<string> result,
        List<string> warnings)
    {
        string characterRoot = Path.Combine(dataRoot, "Characters");
        if (!Directory.Exists(characterRoot)) return;

        Dictionary<string, List<string>> artByIconId = BuildImageIndex(sourceRoot);
        foreach (string workbookPath in Directory.EnumerateFiles(characterRoot, "*.xlsx"))
        {
            string fileName = Path.GetFileName(workbookPath);
            if (fileName.StartsWith("0_", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("~$", StringComparison.Ordinal))
                continue;
            try
            {
                using var package = new ExcelPackage(new FileInfo(workbookPath));
                ExcelWorksheet sheet = package.Workbook.Worksheets["Skills"];
                int iconColumn = FindColumn(sheet, "Icon");
                if (sheet?.Dimension == null || iconColumn <= 0)
                {
                    warnings.Add(fileName + "/Skills is missing the Icon header.");
                    continue;
                }

                for (int row = 2; row <= sheet.Dimension.End.Row; row++)
                {
                    string configuredIcon = sheet.Cells[row, iconColumn].Text.Trim();
                    if (string.IsNullOrWhiteSpace(configuredIcon)) continue;
                    if (configuredIcon.Contains("/") || configuredIcon.Contains("\\"))
                    {
                        string explicitPath = configuredIcon;
                        if (!Path.HasExtension(explicitPath)) explicitPath += ".png";
                        if (TryNormalizeImagePath(explicitPath, out string normalized)) result.Add(normalized);
                        else warnings.Add("Invalid Skills/Icon relative path: " + configuredIcon);
                        continue;
                    }

                    string iconId = Path.GetFileNameWithoutExtension(configuredIcon);
                    if (!artByIconId.TryGetValue(iconId, out List<string> candidates))
                    {
                        warnings.Add("Cannot find Skills/Icon in art_assets: " + configuredIcon);
                        continue;
                    }
                    List<string> skillCandidates = candidates
                        .Where(path => path.StartsWith("Skill_Icon/", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (skillCandidates.Count == 1) result.Add(skillCandidates[0]);
                    else if (candidates.Count == 1) result.Add(candidates[0]);
                    else warnings.Add("Skills/Icon is ambiguous; configure a relative path: " + configuredIcon);
                }
            }
            catch (Exception exception)
            {
                warnings.Add($"Failed to read skill icons from {workbookPath}: {exception.Message}");
            }
        }
    }

    private static string FindFirstFrame(
        string sourceRoot,
        HashSet<string> result,
        List<string> warnings)
    {
        string frameRoot = Path.Combine(sourceRoot, "Character_Whole", "Frame");
        if (!Directory.Exists(frameRoot))
        {
            warnings.Add("Character card frame folder does not exist: " + frameRoot);
            return string.Empty;
        }

        string firstFrame = Directory.EnumerateFiles(frameRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (firstFrame == null)
        {
            warnings.Add("Character card frame folder contains no images: " + frameRoot);
            return string.Empty;
        }

        string relativePath = GetRelativePath(sourceRoot, firstFrame);
        result.Add(relativePath);
        return "art_assets/" + relativePath;
    }

    private static bool TryReadCharacterId(string workbookPath, out int characterId)
    {
        characterId = 0;
        try
        {
            using var package = new ExcelPackage(new FileInfo(workbookPath));
            ExcelWorksheet sheet = package.Workbook.Worksheets["Attributes"];
            int idColumn = FindColumn(sheet, "CharacterID");
            if (sheet?.Dimension != null && idColumn > 0)
            {
                for (int row = 2; row <= sheet.Dimension.End.Row; row++)
                {
                    if (int.TryParse(sheet.Cells[row, idColumn].Text.Trim(), out characterId) && characterId > 0)
                        return true;
                }
            }
        }
        catch
        {
            return false;
        }

        string prefix = Path.GetFileNameWithoutExtension(workbookPath).Split('_')[0];
        return int.TryParse(prefix, out characterId) && characterId > 0;
    }

    private static Dictionary<string, List<string>> BuildImageIndex(string sourceRoot)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (!ImageExtensions.Contains(Path.GetExtension(sourcePath))) continue;
            string iconId = Path.GetFileNameWithoutExtension(sourcePath);
            if (!result.TryGetValue(iconId, out List<string> candidates))
            {
                candidates = new List<string>();
                result[iconId] = candidates;
            }
            candidates.Add(GetRelativePath(sourceRoot, sourcePath));
        }
        return result;
    }

    private static void WriteCharacterCardManifest(string targetRoot, CharacterCardArtManifest manifest)
    {
        string manifestPath = SafeCombine(targetRoot, "Character_Whole/card_art_manifest.json");
        string directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
    }

    private static void WriteEnemyArtManifest(string targetRoot, EnemyArtManifest manifest)
    {
        string manifestPath = SafeCombine(targetRoot, "Enemy/enemy_art_manifest.json");
        string directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
    }

    private static int FindColumn(ExcelWorksheet sheet, string name)
    {
        if (sheet?.Dimension == null) return -1;
        for (int column = 1; column <= sheet.Dimension.End.Column; column++)
        {
            if (string.Equals(sheet.Cells[1, column].Text.Trim(), name, StringComparison.OrdinalIgnoreCase))
                return column;
        }
        return -1;
    }

    private static IEnumerable<string> SplitReferences(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        foreach (string part in value.Split(new[] { ';', '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries))
            yield return part.Trim().Trim('"');
    }

    private static bool TryNormalizeImagePath(string value, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string normalized = value.Trim().Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("art_assets/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("art_assets/".Length);
        if (!ImageExtensions.Contains(Path.GetExtension(normalized)) || Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(segment => segment == ".."))
            return false;
        relativePath = normalized;
        return true;
    }

    /// <summary>
    /// UI tables are deliberately read from StreamingAssets, never from repository Charts.
    /// Fixed paths add one file; placeholder paths such as {CharacterID} expand against
    /// the source art tree so runtime bindings can select an ID without an editor resync.
    /// </summary>
    internal static void CollectUiTableReferences(
        string sourceRoot,
        HashSet<string> result,
        List<string> warnings)
    {
        string uiRoot = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "Charts", "UI"));
        if (!Directory.Exists(uiRoot))
        {
            warnings.Add("StreamingAssets UI 配表目录不存在：" + uiRoot +
                         "。请先执行 Tools/UI 管线/同步 UI 配表到 StreamingAssets。");
            return;
        }

        string[] allImages = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path)))
            .Select(path => GetRelativePath(sourceRoot, path))
            .ToArray();

        foreach (string csvPath in Directory.EnumerateFiles(uiRoot, "*.csv", SearchOption.TopDirectoryOnly))
        {
            string[] lines = File.ReadAllLines(csvPath, new UTF8Encoding(true));
            if (lines.Length == 0) continue;
            List<string> headers = ParseCsvLine(lines[0]);
            int pathColumn = headers.FindIndex(header =>
                string.Equals(header.Trim(), "StreamingAssetPath", StringComparison.OrdinalIgnoreCase));
            if (pathColumn < 0)
            {
                warnings.Add("UI 配表缺少 StreamingAssetPath 列：" + csvPath);
                continue;
            }

            for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                if (string.IsNullOrWhiteSpace(lines[lineIndex])) continue;
                List<string> values = ParseCsvLine(lines[lineIndex]);
                if (pathColumn >= values.Count) continue;
                string configured = values[pathColumn].Trim();
                if (string.IsNullOrEmpty(configured) || configured == "-") continue;

                // Enemy candidates are not expanded from the directory. CollectEnemyArt
                // is authoritative: EnemyAttributes/Enemy_Main ID + existing Enemy/{ID}_01.png.
                if (string.Equals(configured, "art_assets/Enemy/{EnemyID}_01.png",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!configured.Contains("{"))
                {
                    if (TryNormalizeImagePath(configured, out string fixedPath)) result.Add(fixedPath);
                    else warnings.Add($"UI 配表第 {lineIndex + 1} 行资源路径无效：{configured}");
                    continue;
                }

                string relativePattern = configured.Replace('\\', '/').TrimStart('/');
                if (relativePattern.StartsWith("art_assets/", StringComparison.OrdinalIgnoreCase))
                    relativePattern = relativePattern.Substring("art_assets/".Length);
                string regexText = "^" + Regex.Replace(
                    Regex.Escape(relativePattern), @"\\\{[^{}]+\\\}", ".+") + "$";
                var regex = new Regex(regexText, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                string[] matches = allImages.Where(path => regex.IsMatch(path)).ToArray();
                if (matches.Length == 0)
                    warnings.Add($"UI 配表第 {lineIndex + 1} 行通配资源没有匹配文件：{configured}");
                foreach (string match in matches) result.Add(match);
            }
        }
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int index = 0; index < line.Length; index++)
        {
            char current = line[index];
            if (current == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (current == ',' && !quoted)
            {
                values.Add(cell.ToString());
                cell.Length = 0;
            }
            else cell.Append(current);
        }
        values.Add(cell.ToString());
        return values;
    }

    private static string GetRelativePath(string root, string path)
    {
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(path);
        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path escapes art_assets: " + path);
        return normalizedPath.Substring(normalizedRoot.Length).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string SafeCombine(string root, string relativePath)
    {
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string combined = Path.GetFullPath(Path.Combine(
            normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Relative art path escapes its root: " + relativePath);
        return combined;
    }

    private static bool NeedsCopy(string sourcePath, string targetPath)
    {
        if (!File.Exists(targetPath)) return true;
        var source = new FileInfo(sourcePath);
        var target = new FileInfo(targetPath);
        return source.Length != target.Length || source.LastWriteTimeUtc != target.LastWriteTimeUtc;
    }
}
