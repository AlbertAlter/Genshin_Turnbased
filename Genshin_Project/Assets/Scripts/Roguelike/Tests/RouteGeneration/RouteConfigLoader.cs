using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Roguelike.Tests.RouteGeneration
{
    public sealed class RouteConfigLoader
    {
        private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace OfficeRelationsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        public IReadOnlyList<RouteStageConfig> Load(string workbookPath)
        {
            return ParseStageRows(LoadFirstWorksheetRows(workbookPath));
        }

        public RouteUiRuleConfig LoadRouteUiRules(string workbookPath)
        {
            List<Dictionary<string, string>> rows = LoadFirstWorksheetRows(workbookPath);
            Dictionary<string, float> values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<string, string> row in rows)
            {
                string id = Get(row, "ID");
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                string raw = Get(row, "Param(px)");
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    throw new InvalidDataException(id + " has an invalid Param(px): " + raw);
                values[id] = value;
            }

            return new RouteUiRuleConfig
            {
                CoordinateDistance = ReadRule(values, "CoordinateDistance"),
                RandomizeRadius = ReadRule(values, "RandomizeRadius"),
                NoCloserThan = ReadRule(values, "NoCloserThan"),
                StageWidth = ReadRule(values, "StageWidth"),
                MidlineHeight = ReadRule(values, "MidlineHeight"),
                AdditionalRouteTwoChance = ReadRule(values, "AdditionalRoute+2"),
                AdditionalRouteOneChance = ReadRule(values, "AdditionalRoute+1"),
                AdditionalRouteOneChanceWhenOnlyTwo = ReadRule(values, "AdditionalRoute+1(when only 2)")
            };
        }

        private static List<Dictionary<string, string>> LoadFirstWorksheetRows(string workbookPath)
        {
            if (string.IsNullOrWhiteSpace(workbookPath))
                throw new ArgumentException("Workbook path is empty.", nameof(workbookPath));
            if (!File.Exists(workbookPath))
                throw new FileNotFoundException("Workbook was not found.", workbookPath);

            // Excel/WPS may have the workbook open. Never request an exclusive lock and never write it back.
            using (FileStream stream = new FileStream(workbookPath, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                List<string> sharedStrings = ReadSharedStrings(archive);
                ZipArchiveEntry sheetEntry = FindFirstWorksheet(archive);
                return ReadRows(sheetEntry, sharedStrings);
            }
        }

        private static float ReadRule(IReadOnlyDictionary<string, float> values, string id)
        {
            if (!values.TryGetValue(id, out float value))
                throw new InvalidDataException("Missing required RouteUI rule: " + id);
            return value;
        }

        private static ZipArchiveEntry FindFirstWorksheet(ZipArchive archive)
        {
            XDocument workbook = LoadXml(archive, "xl/workbook.xml");
            XDocument relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            XElement firstSheet = workbook.Descendants(SpreadsheetNs + "sheet").FirstOrDefault();
            if (firstSheet == null)
                throw new InvalidDataException("Stage workbook contains no worksheet.");

            string relationshipId = (string)firstSheet.Attribute(OfficeRelationsNs + "id");
            XElement relationship = relationships.Descendants(PackageRelationsNs + "Relationship")
                .FirstOrDefault(x => string.Equals((string)x.Attribute("Id"), relationshipId, StringComparison.Ordinal));
            if (relationship == null)
                throw new InvalidDataException("The first worksheet relationship is missing.");

            string target = ((string)relationship.Attribute("Target") ?? string.Empty).Replace('\\', '/');
            string fullPath = target.StartsWith("/", StringComparison.Ordinal)
                ? target.TrimStart('/')
                : "xl/" + target.TrimStart('/');
            ZipArchiveEntry entry = archive.GetEntry(fullPath);
            if (entry == null)
                throw new InvalidDataException("The first worksheet data is missing: " + fullPath);
            return entry;
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
                return new List<string>();
            using (Stream entryStream = entry.Open())
            {
                XDocument document = XDocument.Load(entryStream);
                return document.Descendants(SpreadsheetNs + "si")
                    .Select(x => string.Concat(x.Descendants(SpreadsheetNs + "t").Select(t => t.Value)))
                    .ToList();
            }
        }

        private static List<Dictionary<string, string>> ReadRows(ZipArchiveEntry sheetEntry, IReadOnlyList<string> sharedStrings)
        {
            using (Stream stream = sheetEntry.Open())
            {
                XDocument document = XDocument.Load(stream);
                List<XElement> sourceRows = document.Descendants(SpreadsheetNs + "row").ToList();
                if (sourceRows.Count == 0)
                    throw new InvalidDataException("The stage worksheet is empty.");

                List<string> headers = ReadCellMap(sourceRows[0], sharedStrings)
                    .OrderBy(x => x.Key).Select(x => x.Value.Trim()).ToList();
                List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
                for (int rowIndex = 1; rowIndex < sourceRows.Count; rowIndex++)
                {
                    Dictionary<int, string> cells = ReadCellMap(sourceRows[rowIndex], sharedStrings);
                    Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int column = 0; column < headers.Count; column++)
                        row[headers[column]] = cells.TryGetValue(column, out string value) ? value.Trim() : string.Empty;
                    if (row.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
                        result.Add(row);
                }
                return result;
            }
        }

        private static Dictionary<int, string> ReadCellMap(XElement row, IReadOnlyList<string> sharedStrings)
        {
            Dictionary<int, string> result = new Dictionary<int, string>();
            foreach (XElement cell in row.Elements(SpreadsheetNs + "c"))
            {
                string reference = (string)cell.Attribute("r") ?? string.Empty;
                int column = ParseColumn(reference);
                string type = (string)cell.Attribute("t");
                string raw = cell.Element(SpreadsheetNs + "v")?.Value
                             ?? string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(x => x.Value));
                if (type == "s" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    && index >= 0 && index < sharedStrings.Count)
                    raw = sharedStrings[index];
                result[column] = raw ?? string.Empty;
            }
            return result;
        }

        private static IReadOnlyList<RouteStageConfig> ParseStageRows(IEnumerable<Dictionary<string, string>> rows)
        {
            Dictionary<int, RouteStageConfig> selected = new Dictionary<int, RouteStageConfig>();
            foreach (Dictionary<string, string> row in rows)
            {
                string stageId = Get(row, "StageID");
                if (!TryGetRouteStageNumber(stageId, out int stageNumber) || stageNumber < 101 || stageNumber > 120)
                    continue;
                RouteStageConfig config = new RouteStageConfig
                {
                    StageId = stageId,
                    MinEncounter = ReadInt(row, "MinEncounter", "MinEncouner"),
                    MaxEncounter = ReadInt(row, "MaxEncounter"),
                    BattleWeight = ReadInt(row, "Battle"),
                    EventWeight = ReadInt(row, "Event"),
                    EliteWeight = ReadInt(row, "Elite"),
                    BossWeight = ReadInt(row, "Boss")
                };
                if (config.MinEncounter < 1 || config.MaxEncounter > 6 || config.MinEncounter > config.MaxEncounter)
                    throw new InvalidDataException(stageId + " has an invalid encounter range; expected 1..6 and min <= max.");
                if (config.BattleWeight < 0 || config.EventWeight < 0 || config.EliteWeight < 0 || config.BossWeight < 0)
                    throw new InvalidDataException(stageId + " has a negative node-type weight.");
                if (config.TotalWeight <= 0)
                    throw new InvalidDataException(stageId + " has no positive node-type weight.");
                if (selected.ContainsKey(stageNumber))
                    throw new InvalidDataException("Duplicate stage row: " + stageId);
                selected.Add(stageNumber, config);
            }

            for (int stageNumber = 101; stageNumber <= 120; stageNumber++)
                if (!selected.ContainsKey(stageNumber))
                    throw new InvalidDataException("Missing required stage row: R" + stageNumber);
            return selected.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();
        }

        private static bool TryGetRouteStageNumber(string stageId, out int stageNumber)
        {
            stageNumber = 0;
            return !string.IsNullOrWhiteSpace(stageId)
                   && stageId.Length > 1
                   && (stageId[0] == 'R' || stageId[0] == 'r')
                   && int.TryParse(stageId.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out stageNumber);
        }

        private static string Get(IReadOnlyDictionary<string, string> row, string name)
        {
            return row.TryGetValue(name, out string value) ? value : string.Empty;
        }

        private static int ReadInt(IReadOnlyDictionary<string, string> row, params string[] names)
        {
            foreach (string name in names)
            {
                if (!row.TryGetValue(name, out string value))
                    continue;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                    return parsed;
                throw new InvalidDataException(name + " is not an integer: " + value);
            }
            throw new InvalidDataException("Missing required column: " + string.Join(" or ", names));
        }

        private static int ParseColumn(string cellReference)
        {
            int column = 0;
            int letters = 0;
            foreach (char character in cellReference)
            {
                if (!char.IsLetter(character))
                    break;
                column = column * 26 + (char.ToUpperInvariant(character) - 'A' + 1);
                letters++;
            }
            if (letters == 0)
                throw new InvalidDataException("Invalid cell reference: " + cellReference);
            return column - 1;
        }

        private static XDocument LoadXml(ZipArchive archive, string path)
        {
            ZipArchiveEntry entry = archive.GetEntry(path);
            if (entry == null)
                throw new InvalidDataException("Workbook entry is missing: " + path);
            using (Stream stream = entry.Open())
                return XDocument.Load(stream);
        }
    }
}
