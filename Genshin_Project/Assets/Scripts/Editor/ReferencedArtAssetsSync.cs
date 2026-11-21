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
    private const string PreferredCharacterVideoFolder = "Character_AnimatedwithBG";

    private static readonly HashSet<string> ImageExtensions = new HashSet<string>(
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".tif", ".tiff", ".gif", ".webp", ".svg", ".exr", ".hdr" },
        StringComparer.OrdinalIgnoreCase);

    public static bool SyncReferencedImages()
    {
        string dataRoot = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "Data"));
        string sourceRoot = ArtAssetsImageSync.GetSourceRoot();
        string targetRoot = ArtAssetsImageSync.GetTargetRoot();
        if (!Directory.Exists(sourceRoot))
        {
            Debug.LogError("art_assets 美术源目录不存在：" + sourceRoot);
            return false;
        }

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        if (Directory.Exists(dataRoot)) CollectDirectImageReferences(dataRoot, required, warnings);
        else warnings.Add("StreamingAssets/Data 不存在；本次只按 Charts/UI 收集美术资源：" + dataRoot);
        CollectSkillIcons(dataRoot, sourceRoot, required, warnings);
        required.Add(BattleRosterStatusBar.Background);
        // Status glyphs can also be chosen by the shared attribute/category resolver.
        string statusIcons = SafeCombine(sourceRoot, "Status_Icon");
        if (Directory.Exists(statusIcons))
            foreach (string icon in Directory.EnumerateFiles(statusIcons, "*.png"))
                required.Add(GetRelativePath(sourceRoot, icon));
        foreach (string element in new[] { "Anemo", "Cryo", "Dendro", "Electro", "Geo", "Hydro", "Pyro" })
            required.Add($"Skill_Icon/#Others/{element}_BG.png");
        CollectUiTableReferences(sourceRoot, required, warnings);
        CollectElementAuraIcons(required, warnings);
        CollectAppearanceArt(sourceRoot, required);
        CollectWishArt(dataRoot, sourceRoot, required, warnings);
        CollectItemListArt(required);
        required.Add(UIProjectMenuBackground.EmblemPath.Substring("art_assets/".Length));
        required.Add(RoguelikeShopView.EmblemArtPath.Substring("art_assets/".Length));
        CollectRosterArt(dataRoot, sourceRoot, required, warnings);
        required.Add("Title_Menu.png");
        foreach (string shortcut in new[] { "WishIcon", "CharacterIcon", "BackpackIcon", "PartySetupIcon" })
            required.Add("UI/Icon/" + shortcut + ".png");
        required.Add("UI/BattleField1/UI_components/CharacterPage/角色边框_1536x864@1x.png");
        required.Add("UI/BattleField1/UI_components/CharacterPage/HealthValueIcon.png");
        required.Add("UI/BattleField1/UI_components/CharacterPage/ShieldValueIcon.png");
        required.Add(UIPageNavigation.CloseIconPath.Substring("art_assets/".Length));
        required.Add("Others/PaimonEmblem_ProjectWhite.png");
        required.Add("UI/Status/status-name-background.svg");
        // Card overlays can be serialized in existing scenes without UI-table rows.
        required.Add("UI/BattleField1/UI_components/card/CardShadow.png");
        required.Add("UI/BattleField1/UI_components/card/CardInnerShadow.png");
        required.Add("UI/BattleField1/UI_components/card/CardSelectionBorder.png");
        required.Add("UI/BattleField1/UI_components/NextTurn.png");
        required.Add("UI/BattleField1/UI_components/Button/Keycaps/SPACE_BG.png");
        foreach (string file in BattleActionPromptView.LayerFiles) required.Add("UI/ActionPrompt/" + file);
        foreach (string file in UICardFreeze.TextureFiles)
            required.Add(UICardFreeze.ArtFolder + file);
        foreach (var file in new[] { "01_DropShadow", "02_BaseShape", "03_UpperSoftLight", "04_LowerSoftLight", "05_InnerShadow" })
            required.Add("UI/BattleField1/UI_components/EnemyShield/" + file + ".png");
        CollectStageBackgrounds(sourceRoot, required, warnings);
        CollectCardAnimations(sourceRoot, required, warnings);
        CollectCardVideos(sourceRoot, required, warnings);
        var sourceOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var characterVideos = CollectAnimatedVideos(sourceRoot, UICardVideoCatalog.CharacterFolder, required, warnings, sourceOverrides);
        var enemyVideos = CollectAnimatedVideos(sourceRoot, "Enemy_Animated", required, warnings, sourceOverrides);
        CollectRouteNodeIcons(required, warnings);
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

                string sourcePath = SafeCombine(sourceRoot,
                    sourceOverrides.TryGetValue(relativePath, out string source) ? source : relativePath);
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
                WriteVideoCatalog(targetRoot, UICardVideoCatalog.CharacterManifestPath, UICardVideoCatalog.CharacterFolder, characterVideos);
                WriteVideoCatalog(targetRoot, UICardVideoCatalog.EnemyManifestPath, "Enemy_Animated", enemyVideos);
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
        return !cancelled;
    }

    private static void CollectAppearanceArt(string sourceRoot, HashSet<string> result)
    {
        foreach (string folder in new[] { "CardBack", "Character_Bare/Frame" })
        {
            string directory = SafeCombine(sourceRoot, folder);
            if (!Directory.Exists(directory)) continue;
            foreach (string path in Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly))
                result.Add(GetRelativePath(sourceRoot, path));
        }
    }

    private static void CollectWishArt(string dataRoot, string sourceRoot, HashSet<string> result, List<string> warnings)
    {
        string uiRoot = SafeCombine(sourceRoot, "UI/Wish");
        if (Directory.Exists(uiRoot))
            foreach (string image in Directory.EnumerateFiles(uiRoot, "*.png"))
                result.Add(GetRelativePath(sourceRoot, image));

        string path = Path.Combine(dataRoot, "Roguelike", RoguelikeWishCatalog.WorkbookName);
        if (!File.Exists(path)) return;
        try
        {
            var pools = RoguelikeWishCatalog.Load(path).Pools;
            foreach (string image in new[] { RoguelikeWishTile.WeaponBackdropPath(3),
                RoguelikeWishTile.WeaponBackdropPath(4), RoguelikeWishTile.WeaponBackdropPath(5) })
                result.Add(image.Substring("art_assets/".Length));
            var weapons = new HashSet<int>(pools.Where(pool => pool.Featured.Kind == RoguelikeWishKind.Weapon)
                .SelectMany(pool => pool.FourStars.Select(prize => prize.Id).Append(pool.Featured.Id)));
            using var book = new ExcelPackage(new FileInfo(Path.Combine(dataRoot, "WeaponAttributes.xlsx")));
            var sheet = book.Workbook.Worksheets["Attributes"] ?? throw new InvalidDataException("武器表缺少 Attributes。");
            var headers = ExcelTableHeaders.For(sheet);
            headers.Require("WeaponID", "Star");
            for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 1); row++)
                if (headers.Cell(row, "Star").Text.Trim() == "3" && int.TryParse(headers.Cell(row, "WeaponID").Text, out int id))
                    weapons.Add(id);
            foreach (int id in weapons)
            {
                result.Add(UICardFactory.WeaponArtFolder.Substring("art_assets/".Length) + $"{id}_01.png");
                result.Add(RoguelikeWishTile.WeaponIconFolder.Substring("art_assets/".Length) + $"{id}_01.png");
            }
        }
        catch (Exception error) { warnings.Add("祈愿美术资源未完整收集：" + error.Message); }
    }
    private static void CollectItemListArt(HashSet<string> result)
    {
        foreach (string path in new[] { UIItemListIcon.IconMaskPath, UIItemListIcon.ValueBarPath, UIItemListIcon.SelectionOutlinePath })
            result.Add(path.Substring("art_assets/".Length));
        for (int rarity = 1; rarity <= 5; rarity++)
            result.Add(UIItemListIcon.GetRarityBackdropPath(rarity).Substring("art_assets/".Length));
        string characterList = RoguelikeRosterView.CharacterListArtPath.Substring("art_assets/".Length);
        foreach (string asset in new[]
        {
            "Shapes/TileMask.png", "Shapes/FilterBox.png", "Shapes/TitleDiamond.png",
            "Shapes/DropdownChevron.png", "Shapes/RoundedMask.png", "Shapes/CircleMask.png",
            "Rarity/background_purple.png", "Rarity/background_gold.png", "Details/Lock.png", "Details/Unlock.png",
            "Details/flower.png", "Details/plume.png", "Details/sands.png", "Details/goblet.png", "Details/circlet.png"
        })
            result.Add(characterList + asset);
        foreach (string element in new[] { "Anemo", "Cryo", "Dendro", "Electro", "Geo", "Hydro", "Pyro" })
        {
            result.Add(characterList + $"Details/element{element}.png");
            result.Add(characterList + $"Details/disc{element}.png");
        }
    }

    private static void CollectRosterArt(string dataRoot, string sourceRoot, HashSet<string> result, List<string> warnings)
    {
        string path = Path.Combine(dataRoot, RoguelikeItemCatalog.WorkbookName);
        if (!File.Exists(path)) return;
        try
        {
            using var book = new ExcelPackage(new FileInfo(path));
            foreach (var sheet in book.Workbook.Worksheets)
            {
                if (sheet.Name == "UpgradeMaterial")
                {
                    var materials = ExcelTableHeaders.For(sheet);
                    materials.Require("ItemID", "Icon");
                    for (int row = materials.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 1); row++)
                    {
                        string itemId = materials.Cell(row, "ItemID").Text.Trim();
                        if (itemId.Length == 0 || !itemId.All(char.IsDigit) ||
                            !string.IsNullOrWhiteSpace(materials.Cell(row, "Icon").Text)) continue;
                        string icon = $"ItemIcon/{itemId}.png";
                        if (File.Exists(SafeCombine(sourceRoot, icon))) result.Add(icon);
                    }
                    continue;
                }
                if (sheet.Name != "Weapon" && sheet.Name != "Artifact" && sheet.Name != "Constellation") continue;
                var columns = ExcelTableHeaders.For(sheet);
                columns.Require("ItemID", "ReferenceID", "Slot");
                for (int row = columns.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 1); row++)
                {
                    if (string.IsNullOrWhiteSpace(columns.Cell(row, "ItemID").Text) ||
                        !int.TryParse(columns.Cell(row, "ReferenceID").Text, out int id)) continue;
                    if (sheet.Name == "Weapon") result.Add($"Weapon_Icon/{id}_01.png");
                    else if (sheet.Name == "Constellation")
                    {
                        string portrait = $"Character_Icon/{id}_04.png";
                        result.Add(File.Exists(SafeCombine(sourceRoot, portrait)) ? portrait : $"Character_Icon/{id}_02.png");
                    }
                    else if (int.TryParse(columns.Cell(row, "Slot").Text, out int slot)) result.Add($"Artifact_Icon/{id}{slot}.png");
                }
            }
        }
        catch (Exception error) { warnings.Add("角色与背包装备图标未完整收集：" + error.Message); }
    }

    private static void CollectCardAnimations(string sourceRoot, HashSet<string> result, List<string> warnings)
    {
        const string catalog = "Character_Animation/catalog.json";
        if (!File.Exists(SafeCombine(sourceRoot, catalog))) return;
        try
        {
            // Generated art metadata, not a gameplay table. Copy only catalog-referenced exports.
            var entries = JsonUtility.FromJson<CardAnimationCatalog>(File.ReadAllText(SafeCombine(sourceRoot, catalog)));
            if (entries?.clips == null) throw new InvalidDataException("Animation catalog is empty.");
            result.Add(catalog);
            foreach (var entry in entries.clips)
            {
                string clip = AnimationArtPath(entry.clipPath);
                result.Add(clip); result.Add(AnimationArtPath(entry.staticPath));
                var data = JsonUtility.FromJson<CardAnimationData>(File.ReadAllText(SafeCombine(sourceRoot, clip)));
                if (data?.pages == null) throw new InvalidDataException("Animation has no pages: " + clip);
                foreach (string page in data.pages) result.Add(AnimationArtPath(page));
            }
        }
        catch (Exception error) { warnings.Add("角色动图清单未完整加载：" + error.Message); }
    }

    [Serializable] private sealed class CardVideoCatalog { public CardVideoEntry[] clips; }
    [Serializable] private sealed class CardVideoEntry { public string videoPath; public string posterPath; }

    internal static UICardVideoCatalog.PublishedCatalog CollectAnimatedVideos(
        string sourceRoot, string folder, HashSet<string> result, List<string> warnings,
        Dictionary<string, string> sourceOverrides)
    {
        bool character = folder == UICardVideoCatalog.CharacterFolder;
        if (!character && folder != "Enemy_Animated")
            throw new ArgumentException("Unknown animated card folder: " + folder);
        var videos = new HashSet<string>(StringComparer.Ordinal);
        // Both sources publish to the existing canonical path. Source priority is resolved only here.
        foreach (string sourceFolder in character ? new[] { PreferredCharacterVideoFolder, folder } : new[] { folder })
        {
            string directory = SafeCombine(sourceRoot, sourceFolder);
            if (!Directory.Exists(directory)) continue;
            foreach (string source in Directory.EnumerateFiles(directory, "*.mp4", SearchOption.TopDirectoryOnly))
            {
                string file = Path.GetFileName(source);
                if (!UICardVideoCatalog.TryGetIdentity(file, out int id, out int variant))
                { warnings.Add("跳过命名无效的动态卡面：" + sourceFolder + "/" + file); continue; }
                if (!videos.Add(file)) continue;
                string destination = folder + "/" + file;
                result.Add(destination);
                if (sourceFolder != folder) sourceOverrides.Add(destination, sourceFolder + "/" + file);
                // Reuse normal static art while the decoder prepares or fails.
                string fallback = character ? $"Character_Bare/{id}_{variant:D2}.png" :
                    AnimationArtPath(UICardVideoPlayer.PosterPathForEnemy(id, variant));
                if (File.Exists(SafeCombine(sourceRoot, fallback))) result.Add(fallback);
                else warnings.Add("动态卡面缺少静态回退：" + fallback);
            }
        }
        return new UICardVideoCatalog.PublishedCatalog { videos = videos.OrderBy(file => file, StringComparer.Ordinal).ToArray() };
    }

    private static void WriteVideoCatalog(string targetRoot, string path, string folder, UICardVideoCatalog.PublishedCatalog catalog)
    {
        catalog.videos = catalog.videos.Where(file => File.Exists(SafeCombine(targetRoot, folder + "/" + file))).ToArray();
        string target = SafeCombine(targetRoot, AnimationArtPath(path));
        string json = JsonUtility.ToJson(catalog, true) + "\n";
        if (File.Exists(target) && File.ReadAllText(target) == json) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.WriteAllText(target, json, new UTF8Encoding(false));
    }

    private static void CollectCardVideos(string sourceRoot, HashSet<string> result, List<string> warnings)
    {
        const string catalog = "Character_Video/catalog.json";
        if (!File.Exists(SafeCombine(sourceRoot, catalog))) return;
        try
        {
            // Generated art manifest: only rendered movies/posters, never production skeletons or atlases.
            var data = JsonUtility.FromJson<CardVideoCatalog>(File.ReadAllText(SafeCombine(sourceRoot, catalog)));
            if (data?.clips == null) throw new InvalidDataException("Card video catalog is empty.");
            result.Add(catalog);
            foreach (var entry in data.clips)
            {
                string video = AnimationArtPath(entry.videoPath), poster = AnimationArtPath(entry.posterPath);
                if (!video.StartsWith("Character_Video/", StringComparison.Ordinal) ||
                    !video.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                    !poster.StartsWith("Character_Video/", StringComparison.Ordinal) ||
                    !poster.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unexpected card video resource: " + video);
                result.Add(video); result.Add(poster);
            }
        }
        catch (Exception error) { warnings.Add("角色视频清单未完整加载：" + error.Message); }
    }

    private static string AnimationArtPath(string path)
    {
        path = path?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("art_assets/", StringComparison.Ordinal) ||
            path.Contains("..") || path.Contains(":")) throw new InvalidDataException("Invalid animation art path: " + path);
        return path.Substring("art_assets/".Length);
    }

    private static void CollectElementAuraIcons(HashSet<string> result, List<string> warnings)
    {
        try
        {
            var palette = ElementColorPalette.Load();
            foreach (string path in palette.AuraIconPaths)
                if (TryNormalizeImagePath(path, out string relative)) result.Add(relative);
        }
        catch (Exception error) { warnings.Add("元素栏图标配表未加载：" + error.Message); }
    }

    private static void CollectRouteNodeIcons(HashSet<string> result, List<string> warnings)
    {
        string published = Path.Combine(Application.streamingAssetsPath,
            Roguelike.Tests.RouteGeneration.RouteMapStyle.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var style = new Roguelike.Tests.RouteGeneration.RouteMapStyle();
        try { style = Roguelike.Tests.RouteGeneration.RouteMapStyle.ReadFile(published); }
        catch (Exception exception) { warnings.Add("Route art table unavailable; collecting default icons. " + exception.Message); }
        foreach (string path in style.ArtPaths())
        {
            if (TryNormalizeImagePath(path, out string relative)) result.Add(relative);
        }
    }

    private static void CollectStageBackgrounds(
        string sourceRoot,
        HashSet<string> result,
        List<string> warnings)
    {
        string stageRoot = Path.Combine(Application.dataPath, "Resources", "Stages");
        if (!Directory.Exists(stageRoot)) return;
        foreach (string stagePath in Directory.EnumerateFiles(stageRoot, "*.json", SearchOption.AllDirectories))
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
            ExcelWorksheet sheet = package.Workbook.Worksheets["Coeff"] ?? package.Workbook.Worksheets["Enemy_Main"];
            int idColumn = FindColumn(sheet, "EnemyID");
            if (sheet?.Dimension == null || idColumn <= 0)
            {
                warnings.Add("EnemyAttributes.xlsx/Coeff 缺少 EnemyID 表头。");
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
                // Publish existing numbered variants for valid enemies alongside the default card.
                foreach (string variantPath in Directory.EnumerateFiles(SafeCombine(sourceRoot, "Enemy"), $"{enemyId}_*.png"))
                {
                    string suffix = Path.GetFileNameWithoutExtension(variantPath).Substring(enemyId.ToString().Length + 1);
                    if (int.TryParse(suffix, out int variant) && variant > 0 && suffix == variant.ToString("D2"))
                        result.Add("Enemy/" + Path.GetFileName(variantPath));
                }
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

            string listPortrait = RoguelikeRosterView.CharacterListPortraitPath(characterId).Substring("art_assets/".Length);
            string transparentPortrait = $"Character_Icon/{characterId}_02.png";
            if (File.Exists(SafeCombine(sourceRoot, listPortrait))) result.Add(listPortrait);
            else if (File.Exists(SafeCombine(sourceRoot, transparentPortrait))) result.Add(transparentPortrait);
            string skills = SafeCombine(sourceRoot, $"Skill_Icon/{characterId}");
            if (Directory.Exists(skills))
                foreach (string pattern in new[] { $"{characterId}_S??.png", $"{characterId}_T??.png", $"{characterId}_C??.png" })
                    foreach (string icon in Directory.EnumerateFiles(skills, pattern))
                        result.Add($"Skill_Icon/{characterId}/{Path.GetFileName(icon)}");

            string relativeCardPath = $"Character_Bare/{characterId}_01.png";
            string sourceCardPath = SafeCombine(sourceRoot, relativeCardPath);
            if (!File.Exists(sourceCardPath))
            {
                warnings.Add($"Character {characterId} has no card image: art_assets/{relativeCardPath}");
                continue;
            }

            result.Add(relativeCardPath);
            // Publish all authored character forms, keeping the default manifest
            // entry on 01. Runtime adapters preload exact variants through the factory.
            string cardFolder = SafeCombine(sourceRoot, "Character_Bare");
            foreach (string cardFile in Directory.EnumerateFiles(cardFolder, $"{characterId}_*.png", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(cardFile);
                var match = Regex.Match(name, @"^[0-9]+_([0-9]{2,})\.png$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int variant) && variant > 0 &&
                    name == $"{characterId}_{variant:D2}.png") result.Add("Character_Bare/" + name);
            }
            string avatar = CharacterCardArtManifest.PortraitFile(characterId);
            if (File.Exists(SafeCombine(sourceRoot, avatar))) result.Add(avatar);
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
                    if (CharacterSkillIconResolver.TryGetRelativePath(configuredIcon, out string resolved)
                        && TryNormalizeImagePath(resolved, out string relative)
                        && File.Exists(Path.Combine(sourceRoot, relative)))
                    {
                        result.Add(relative);
                        continue;
                    }
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
        string frameRoot = Path.Combine(sourceRoot, "Character_Bare", "Frame");
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
