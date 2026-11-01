using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

/// <summary>Changes only existing numeric Value cells in a Parameters worksheet; preserves all other XLSX parts.</summary>
public static class ParameterWorkbookPatch
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static byte[] Update(byte[] original, IReadOnlyDictionary<string, float> values)
    {
        using var memory = new MemoryStream(); memory.Write(original, 0, original.Length); memory.Position = 0;
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
        {
            XmlDocument Load(string name)
            {
                var entry = zip.GetEntry(name) ?? throw new InvalidDataException("缺少工作簿部件：" + name);
                var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
                using var stream = entry.Open(); document.Load(stream); return document;
            }
            var workbook = Load("xl/workbook.xml");
            var sheet = workbook.GetElementsByTagName("sheet", Main).OfType<XmlElement>()
                .SingleOrDefault(s => s.GetAttribute("name") == "Parameters")
                ?? throw new InvalidDataException("缺少Parameters子表。");
            string relationshipId = sheet.GetAttribute("id", Rel);
            var relationship = Load("xl/_rels/workbook.xml.rels").DocumentElement.ChildNodes.OfType<XmlElement>()
                .Single(e => e.GetAttribute("Id") == relationshipId);
            if (relationship.GetAttribute("TargetMode") == "External") throw new InvalidDataException("Parameters不能引用外部工作表。");
            string part = new Uri(new Uri("http://workbook/xl/workbook.xml"), relationship.GetAttribute("Target")).AbsolutePath.TrimStart('/');
            var document = Load(part);
            var ns = new XmlNamespaceManager(document.NameTable); ns.AddNamespace("x", Main);
            var strings = zip.GetEntry("xl/sharedStrings.xml") != null
                ? Load("xl/sharedStrings.xml").GetElementsByTagName("si", Main).OfType<XmlElement>().Select(e => e.InnerText).ToArray()
                : Array.Empty<string>();
            string Text(XmlElement cell)
            {
                if (cell == null) return "";
                string v = cell.SelectSingleNode("x:v", ns)?.InnerText ?? "";
                if (cell.GetAttribute("t") == "s") return strings[int.Parse(v, CultureInfo.InvariantCulture)];
                return cell.GetAttribute("t") == "inlineStr" ? cell.SelectSingleNode("x:is", ns)?.InnerText ?? "" : v;
            }
            if (Text((XmlElement)document.SelectSingleNode("//x:c[@r='A1']", ns)) != "Key" ||
                Text((XmlElement)document.SelectSingleNode("//x:c[@r='B1']", ns)) != "Value")
                throw new InvalidDataException("Parameters前两列必须为Key、Value。");
            var changed = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement row in document.SelectNodes("/x:worksheet/x:sheetData/x:row", ns))
            {
                string rowId = row.GetAttribute("r");
                string key = Text((XmlElement)row.SelectSingleNode($"x:c[@r='A{rowId}']", ns)).Trim();
                if (!values.TryGetValue(key, out float number)) continue;
                if (float.IsNaN(number) || float.IsInfinity(number) || !changed.Add(key))
                    throw new InvalidDataException("参数重复或数值无效：" + key);
                var cell = (XmlElement)row.SelectSingleNode($"x:c[@r='B{rowId}']", ns)
                    ?? throw new InvalidDataException("缺少Value单元格：" + key);
                foreach (XmlNode child in cell.ChildNodes.Cast<XmlNode>().ToArray())
                    if (child.LocalName == "f" || child.LocalName == "v" || child.LocalName == "is") cell.RemoveChild(child);
                cell.SetAttribute("t", "n");
                var value = document.CreateElement(cell.Prefix, "v", Main);
                value.InnerText = number.ToString("R", CultureInfo.InvariantCulture); cell.PrependChild(value);
            }
            if (changed.Count != values.Count) throw new InvalidDataException("工作表缺少要写回的参数。");
            zip.GetEntry(part).Delete();
            using var output = zip.CreateEntry(part, CompressionLevel.Optimal).Open();
            using var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
            document.Save(writer);
        }
        return memory.ToArray();
    }
}
