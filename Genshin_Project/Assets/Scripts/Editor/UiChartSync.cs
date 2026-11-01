using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Publishes battlefield and route UI tables to their runtime locations.</summary>
public static class UiChartSync
{
    private static string SourceRoot()
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Charts", "UI", "Battlefield1"));
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("UI 配表源目录不存在：" + source);
        return source;
    }

    public static void SyncAnimation()
    {
        string sourceRoot = SourceRoot();
        string source = Path.Combine(sourceRoot, BattlefieldAnimationParameters.FileName);
        BattlefieldAnimationParameters.ReadFile(source);
        string directory = Path.Combine(Application.streamingAssetsPath, "Charts", "UI");
        Directory.CreateDirectory(directory);
        File.Copy(source, Path.Combine(directory, BattlefieldAnimationParameters.FileName), true);
        SyncRouteStyles();
        SyncDamagePopup();
        SyncHitEffects();
        AssetDatabase.Refresh();
    }
    public static void Sync()
    {
        string sourceRoot = SourceRoot();
        string targetRoot = Path.Combine(Application.streamingAssetsPath, "Charts", "UI");
        Publish(sourceRoot, targetRoot);
        SyncRouteStyles();
        SyncDamagePopup();
        SyncHitEffects();
    }

    [MenuItem("Tools/UI/同步命中特效配表")]
    public static void SyncHitEffects()
    {
        string sourceRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Charts", "UI"));
        string timelineRoot = Path.Combine(sourceRoot, BattleTimelineSettings.Folder);
        string main = Path.Combine(timelineRoot, BattleTimelineSettings.FileName);
        BattleTimelineSettings.ReadFile(main);
        string source = Path.Combine(timelineRoot, UIHitEffectStyle.FileName);
        string colors = Path.Combine(sourceRoot, ElementColorPalette.FileName);
        string shake = Path.Combine(timelineRoot, HitShakeStyle.FileName);
        string textureTable = Path.Combine(timelineRoot, HitEffectTextureStyle.FileName);
        string templates = Path.Combine(timelineRoot, BattleSkillPresentationTemplates.FileName);
        string prompt = Path.Combine(timelineRoot, BattleActionStyle.FileName);
        string enemyActions = Path.Combine(timelineRoot, EnemyActionTemplates.FileName);
        string effects = Path.Combine(timelineRoot, BattlePresentationConfiguration.EffectFileName);
        BattleActionStyle.ReadFile(prompt);
        BattlePresentationConfiguration.ReadEffects(effects);
        EnemyActionTemplates.ReadFile(enemyActions, BattleSkillPresentationTemplates.ReadFile(templates));
        UIHitEffectStyle.ReadFile(source);
        ElementColorPalette.ReadFile(colors);
        HitShakeStyle.ReadFile(shake);
        var textureStyle = HitEffectTextureStyle.ReadFile(textureTable);
        string artSourceRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art_assets", "UI", "SlashComponents"));
        // Validate every reference before publishing any new table or art.
        foreach (string file in textureStyle.Files)
            if (!File.Exists(Path.Combine(artSourceRoot, file))) throw new FileNotFoundException("缺少命中特效贴图：" + file);
        string directory = Path.Combine(Application.streamingAssetsPath, "Data", "UI", BattleTimelineSettings.Folder);
        Directory.CreateDirectory(directory);
        foreach (string file in new[] { main, source, shake, textureTable, templates, enemyActions, prompt, effects })
        {
            string target = Path.Combine(directory, Path.GetFileName(file));
            File.Copy(file, target, true);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
        }
        File.Copy(colors, Path.Combine(Application.streamingAssetsPath, "Data", "UI", ElementColorPalette.FileName), true);
        string artTargetRoot = Path.Combine(Application.streamingAssetsPath, "art_assets", "UI", "SlashComponents");
        Directory.CreateDirectory(artTargetRoot);
        foreach (string file in textureStyle.Files)
        {
            string artSource = Path.Combine(artSourceRoot, file), artTarget = Path.Combine(artTargetRoot, file);
            File.Copy(artSource, artTarget, true);
            File.SetLastWriteTimeUtc(artTarget, File.GetLastWriteTimeUtc(artSource));
        }
        SyncShieldEffects();
        SyncCardTransitions();
        AssetDatabase.Refresh();
    }

    // Uses the existing UI publishing action; no new menu or scene regeneration.
    public static void SyncCardTransitions()
    {
        string sourceRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Charts", "UI"));
        string entrance = Path.Combine(sourceRoot, UICardEntranceStyle.FileName);
        string exit = Path.Combine(sourceRoot, UIGoldenDissolveStyle.FileName);
        string variant = Path.Combine(sourceRoot, UICardVariantStyle.FileName);
        string appearance = Path.Combine(sourceRoot, EnemyAppearanceTable.FileName);
        UICardEntranceStyle.ReadFile(entrance); UIGoldenDissolveStyle.ReadFile(exit);
        UICardVariantStyle.ReadFile(variant);
        EnemyAppearanceTable.ReadFile(appearance).ValidateRuntimeReferences(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art_assets")));
        string target = Path.Combine(Application.streamingAssetsPath, "Data", "UI"); Directory.CreateDirectory(target);
        foreach (string source in new[] { entrance, exit, variant, appearance })
        {
            string destination = Path.Combine(target, Path.GetFileName(source));
            File.Copy(source, destination, true); File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
        }
    }

    // Shared by the existing art/config/hit-effect menus; never changes scene transforms.
    public static void SyncShieldEffects()
    {
        string repository = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string source = Path.Combine(repository, "Charts", "UI", UIShieldStyle.FileName);
        var style = UIShieldStyle.ReadFile(source);
        foreach (string relative in style.ArtPaths)
            if (!File.Exists(Path.Combine(repository, relative)))
                throw new FileNotFoundException("缺少护盾独立PSD图层：" + relative);
        string target = Path.Combine(Application.streamingAssetsPath, "Data", "UI", UIShieldStyle.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.Copy(source, target, true);
        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        foreach (string relative in style.ArtPaths)
        {
            string artSource = Path.Combine(repository, relative);
            string artTarget = Path.Combine(Application.streamingAssetsPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(artTarget));
            if (File.Exists(artTarget) && new FileInfo(artSource).Length == new FileInfo(artTarget).Length &&
                File.GetLastWriteTimeUtc(artSource) == File.GetLastWriteTimeUtc(artTarget)) continue;
            File.Copy(artSource, artTarget, true);
            File.SetLastWriteTimeUtc(artTarget, File.GetLastWriteTimeUtc(artSource));
        }
    }

    public static void SyncDamagePopup()
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Charts", "UI",
            BattleTimelineSettings.Folder, BattlefieldDamagePopupStyle.FileName));
        BattlefieldDamagePopupStyle.ReadFile(source);
        string main = Path.Combine(Path.GetDirectoryName(source), BattleTimelineSettings.FileName);
        BattleTimelineSettings.ReadFile(main);
        string statusSource = Path.Combine(Path.GetDirectoryName(source), BattlefieldStatusPopupStyle.FileName);
        BattlefieldStatusPopupStyle.ReadFile(statusSource);
        string colorSource = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), "..", ElementColorPalette.FileName));
        ElementColorPalette.ReadFile(colorSource);
        string effectSource = Path.Combine(Path.GetDirectoryName(source), BattlePresentationConfiguration.EffectFileName);
        BattlePresentationConfiguration.ReadEffects(effectSource);
        string directory = Path.Combine(Application.streamingAssetsPath, "Data", "UI", BattleTimelineSettings.Folder);
        Directory.CreateDirectory(directory);
        File.Copy(main, Path.Combine(directory, BattleTimelineSettings.FileName), true);
        string target = Path.Combine(directory, BattlefieldDamagePopupStyle.FileName);
        File.Copy(source, target, true);
        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        string statusTarget = Path.Combine(directory, BattlefieldStatusPopupStyle.FileName);
        File.Copy(statusSource, statusTarget, true);
        File.SetLastWriteTimeUtc(statusTarget, File.GetLastWriteTimeUtc(statusSource));
        string colorTarget = Path.Combine(Application.streamingAssetsPath, "Data", "UI", ElementColorPalette.FileName);
        File.Copy(colorSource, colorTarget, true);
        File.SetLastWriteTimeUtc(colorTarget, File.GetLastWriteTimeUtc(colorSource));
        string effectTarget = BattleTimelineSettings.RuntimePath(BattlePresentationConfiguration.EffectFileName);
        File.Copy(effectSource, effectTarget, true);
        File.SetLastWriteTimeUtc(effectTarget, File.GetLastWriteTimeUtc(effectSource));
        AssetDatabase.Refresh();
    }

    public static void SyncRouteStyles()
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Charts", "UI",
            "Rouge_Route", Roguelike.Tests.RouteGeneration.RouteMapStyle.FileName));
        Roguelike.Tests.RouteGeneration.RouteMapStyle.ReadFile(source);
        string target = Path.Combine(Application.streamingAssetsPath,
            Roguelike.Tests.RouteGeneration.RouteMapStyle.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.Copy(source, target, true);
        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        AssetDatabase.Refresh();
    }

    // Shared publishing implementation for the menu and isolated validation builds.
    public static void Publish(string sourceRoot, string targetRoot)
    {
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException("UI 配表源目录不存在：" + sourceRoot);
        }

        Directory.CreateDirectory(targetRoot);
        int copied = 0;
        int removed = 0;
        var published = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string sourcePath in Directory.EnumerateFiles(sourceRoot, "*.csv", SearchOption.TopDirectoryOnly))
        {
            string targetPath = Path.Combine(targetRoot, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, targetPath, true);
            File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));
            published.Add(Path.GetFileName(sourcePath));
            copied++;
        }
        foreach (string targetPath in Directory.EnumerateFiles(targetRoot, "*.csv", SearchOption.TopDirectoryOnly))
        {
            if (published.Contains(Path.GetFileName(targetPath))) continue;
            File.Delete(targetPath);
            removed++;
        }
        AssetDatabase.Refresh();
        Debug.Log($"UI 配表已发布到 StreamingAssets/Charts/UI：{copied} 个文件，移除过期表 {removed} 个。");
    }
}
