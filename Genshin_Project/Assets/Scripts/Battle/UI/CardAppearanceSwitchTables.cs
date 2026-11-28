using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>One published workbook and one cached read for both sides; no per-character or per-enemy code branches.</summary>
public sealed class CardAppearanceSwitchTables
{
    public const string FileName = "CardAppearanceSwitch.xlsx";
    public CharacterCardSwitchTable Characters { get; private set; }
    public EnemyAppearanceTable Enemies { get; private set; }
    public static CardAppearanceSwitchTables Load() => CampaignUiTables.Read(ReadPublished);
    private static CardAppearanceSwitchTables ReadPublished()
    {
        var tables = ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName));
        string art = Path.Combine(Application.streamingAssetsPath, "art_assets");
        tables.Characters.ValidatePublishedReferences(art);
        tables.Enemies.ValidatePublishedReferences(art);
        return tables;
    }
    public static CardAppearanceSwitchTables ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        return new CardAppearanceSwitchTables
        {
            Characters = CharacterCardSwitchTable.ReadSheet(package.Workbook.Worksheets[CharacterCardSwitchTable.SheetName], "CharacterID"),
            Enemies = EnemyAppearanceTable.ReadSheet(package.Workbook.Worksheets[EnemyAppearanceTable.SheetName])
        };
    }
}
