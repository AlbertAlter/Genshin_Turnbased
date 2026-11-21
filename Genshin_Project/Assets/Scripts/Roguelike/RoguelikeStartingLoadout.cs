using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using UnityEngine;

public sealed class RoguelikeStartingLoadout
{
    private List<int> characters;
    private List<(int Number, int Slot, int Character)> parties;
    private List<(int Weapon, int? Owner)> weapons;
    private List<(int Set, int Star, ArtifactSlot Slot, int? Owner)> artifacts;
    private List<(string Id, int Quantity)> items;

    private RoguelikeStartingLoadout() { }

    internal static RoguelikeStartingLoadout ReadPublished() =>
        Read(Path.Combine(Application.streamingAssetsPath, "Data", "Roguelike", "StartingLoadout.xlsx"));

    public static RoguelikeProgressSnapshot Load(DataManager data) => data.GetStartingLoadout().Create(
        new DataManagerRoguelikeEquipmentCatalog(data), new DataManagerRoguelikeCharacterAcquisitionCatalog(data),
        data.GetItemCatalog(), () => new ArtifactGenerator(data.GetArtifactConfig()));

    internal static RoguelikeProgressSnapshot Load(string path, IRoguelikeEquipmentCatalog equipment,
        IRoguelikeCharacterAcquisitionCatalog characters, IRoguelikeItemCatalog items)
        => Read(path).Create(equipment, characters, items, () => ArtifactGenerator.CreateFromStreamingAssets());

    internal static RoguelikeStartingLoadout Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var book = new ExcelPackage(stream);
        List<T> Rows<T>(string sheetName, string[] fields, Func<Func<string, string>, T> parse)
        {
            var sheet = book.Workbook.Worksheets[sheetName] ?? throw new InvalidDataException("开局配置缺少 " + sheetName);
            var headers = ExcelTableHeaders.For(sheet);
            headers.Require(fields);
            var rows = new List<T>();
            for (int row = headers.DataStartRow; row <= sheet.Dimension.End.Row; row++)
            {
                string Cell(string field) => headers.Cell(row, field).Text.Trim();
                if (fields.All(field => Cell(field).Length == 0)) continue;
                try { rows.Add(parse(Cell)); }
                catch (Exception error) { throw new InvalidDataException($"开局配置 {sheetName} 第 {row} 行：{error.Message}", error); }
            }
            return rows;
        }
        int? Owner(string value) => value.Length == 0 ? (int?)null : int.Parse(value);
        return new RoguelikeStartingLoadout
        {
            characters = Rows("Characters", new[] { "CharacterID" }, c => int.Parse(c("CharacterID"))),
            parties = Rows("PartyPresets", new[] { "PresetID", "PartySlot", "CharacterID" }, c =>
                (int.Parse(c("PresetID")), int.Parse(c("PartySlot")), int.Parse(c("CharacterID")))),
            weapons = Rows("Weapons", new[] { "WeaponID", "EquippedCharacterID" }, c =>
                (int.Parse(c("WeaponID")), Owner(c("EquippedCharacterID")))),
            artifacts = Rows("Artifacts", new[] { "SetID", "Star", "Slot", "EquippedCharacterID" }, c =>
                (int.Parse(c("SetID")), int.Parse(c("Star")), (ArtifactSlot)Enum.Parse(typeof(ArtifactSlot), c("Slot"), true), Owner(c("EquippedCharacterID")))),
            items = Rows("Items", new[] { "ItemID", "Quantity" }, c => (c("ItemID"), int.Parse(c("Quantity"))))
        };
    }

    internal RoguelikeProgressSnapshot Create(IRoguelikeEquipmentCatalog equipment, IRoguelikeCharacterAcquisitionCatalog limits,
        IRoguelikeItemCatalog catalog, Func<ArtifactGenerator> createGenerator)
    {
        var draft = new RoguelikeInventoryTransaction(new RoguelikeProgressSnapshot { CurrentChapterId = 1 }, equipment, catalog);
        var owned = new HashSet<int>();
        var armed = new HashSet<int>();
        var artifactSlots = new HashSet<(int, ArtifactSlot)>();
        ArtifactGenerator generator = null;
        try
        {
            foreach (int id in characters)
            {
                if (!owned.Add(id) || !limits.TryGetInitialLimits(id, out float health, out float energy))
                    throw new InvalidDataException("开局配置重复或无效的角色 " + id);
                draft.AddCharacter(id, health, energy);
            }
            if (!parties.Any(p => p.Number == 1)) throw new InvalidDataException("缺少开局默认的 1 号编队。");
            foreach (var preset in parties.GroupBy(p => p.Number).OrderByDescending(p => p.Key))
            {
                var ordered = preset.OrderBy(p => p.Slot).ToList();
                if (preset.Key < 1 || preset.Key > 10 || ordered.Count > 4 ||
                    ordered.Where((p, index) => !owned.Contains(p.Character) || p.Slot != index + 1).Any())
                    throw new InvalidDataException($"预设 {preset.Key} 的位置或角色无效。");
                draft.SetParty(ordered.Select(p => p.Character).ToList(), preset.Key);
            }
            foreach (var weapon in weapons)
            {
                string instance = draft.AddWeapon(weapon.Weapon);
                if (weapon.Owner is int owner)
                {
                    if (!armed.Add(owner)) throw new InvalidDataException("角色重复装备武器：" + owner);
                    draft.EquipWeapon(owner, instance);
                }
            }
            foreach (var artifact in artifacts)
            {
                generator ??= createGenerator();
                string instance = draft.AddArtifact(artifact.Set, generator.Generate(artifact.Star, artifact.Slot));
                if (artifact.Owner is int owner)
                {
                    if (!artifactSlots.Add((owner, artifact.Slot))) throw new InvalidDataException("重复的圣遗物装备位置");
                    draft.EquipArtifact(owner, instance);
                }
            }
            draft.AddItems(items.Select(item => new RoguelikeItemStackData { ItemId = item.Id, Quantity = item.Quantity }).ToList());
            return draft.ExportSnapshot();
        }
        finally { draft.Close(); }
    }
}
