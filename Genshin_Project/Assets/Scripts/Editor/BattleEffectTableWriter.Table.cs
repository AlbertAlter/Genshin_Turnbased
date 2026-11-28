using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEngine;

public static partial class BattleEffectTableWriter
{
    private static string SaveTable(BattleTableEffectResource resource)
    {
        string source=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Charts/UE",BattleTableEffectCatalog.FileName));
        string target=BattleTableEffectCatalog.RuntimePath;
        bool existed=File.Exists(target),opened=false,saved=false;
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        try
        {
            using var from=new FileStream(source,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
            using var to=new FileStream(target,existed?FileMode.Open:FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
            opened=true;byte[] before=Read(from),published=Read(to);
            byte[] edited=PatchTable(before,resource);
            using(var check=new MemoryStream(edited))BattleTableEffectCatalog.ReadStream(check);
            try{Write(from,edited);Write(to,edited);saved=true;}
            catch{Write(from,before);Write(to,published);throw;}
        }
        finally{if(!saved&&opened&&!existed)File.Delete(target);}
        AssetDatabase.ImportAsset("Assets/StreamingAssets/Data/UE/"+BattleTableEffectCatalog.FileName,ImportAssetOptions.ForceUpdate);
        return "已写回并发布："+resource.Key;
    }
    public static byte[] PatchTable(byte[] original,BattleTableEffectResource resource)
    {
        const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var values=new Dictionary<string,float>(StringComparer.Ordinal){
            {"Speed",resource.Speed},{"RangeScale",resource.RangeScale},{"PartScale",resource.PartScale},{"Opacity",resource.Opacity},
            {"Intensity",resource.Intensity},{"FadeInSeconds",resource.FadeIn},{"FadeOutSeconds",resource.FadeOut}};
        using var memory=new MemoryStream();memory.Write(original,0,original.Length);memory.Position=0;
        using(var zip=new ZipArchive(memory,ZipArchiveMode.Update,true))
        {
            XmlDocument Load(string name){var d=new XmlDocument{PreserveWhitespace=true,XmlResolver=null};using var s=zip.GetEntry(name).Open();d.Load(s);return d;}
            string[] strings=zip.GetEntry("xl/sharedStrings.xml")==null?Array.Empty<string>():Load("xl/sharedStrings.xml").GetElementsByTagName("si",ns).OfType<XmlElement>().Select(x=>x.InnerText).ToArray();
            string Text(XmlElement c){if(c==null)return "";string v=c.GetElementsByTagName("v",ns).OfType<XmlElement>().FirstOrDefault()?.InnerText??"";return c.GetAttribute("t")=="s"?strings[int.Parse(v,CultureInfo.InvariantCulture)]:c.GetAttribute("t")=="inlineStr"?c.InnerText:v;}
            var wb=Load("xl/workbook.xml");var relationships=Load("xl/_rels/workbook.xml.rels");
            foreach(string name in new[]{"Resources","Parameters"})
            {
                var sheet=wb.GetElementsByTagName("sheet",ns).OfType<XmlElement>().Single(s=>s.GetAttribute("name")==name);
                var relationship=relationships.DocumentElement.ChildNodes.OfType<XmlElement>().Single(r=>r.GetAttribute("Id")==sheet.GetAttribute("id",rel));
                string part=new Uri(new Uri("http://workbook/xl/workbook.xml"),relationship.GetAttribute("Target")).AbsolutePath.TrimStart('/');
                var document=Load(part);var rows=document.GetElementsByTagName("row",ns).OfType<XmlElement>().ToArray();
                string Column(XmlElement c)=>new string(c.GetAttribute("r").TakeWhile(char.IsLetter).ToArray());
                var heads=rows.Single(r=>r.GetAttribute("r")=="1").ChildNodes.OfType<XmlElement>().Where(c=>c.LocalName=="c").ToDictionary(c=>Text(c).Trim(),Column,StringComparer.Ordinal);
                var written=new HashSet<string>(StringComparer.Ordinal);
                foreach(var row in rows.Skip(1))
                {
                    var cells=row.ChildNodes.OfType<XmlElement>().Where(c=>c.LocalName=="c").ToDictionary(Column,StringComparer.Ordinal);
                    string T(string head)=>heads.TryGetValue(head,out var col)&&cells.TryGetValue(col,out var cell)?Text(cell).Trim():"";
                    if(T("ResourceKey")!=resource.Key)continue;
                    void Put(string head,float number,string id)
                    {
                        if(float.IsNaN(number)||float.IsInfinity(number)||!written.Add(id))throw new InvalidDataException("参数重复或非有限数值："+id);
                        if(!heads.TryGetValue(head,out var col)||!cells.TryGetValue(col,out var cell))throw new InvalidDataException("没有对应数值单元格："+head);
                        foreach(XmlNode child in cell.ChildNodes.Cast<XmlNode>().ToArray())if(child.LocalName=="v"||child.LocalName=="is"||child.LocalName=="f")cell.RemoveChild(child);
                        cell.SetAttribute("t","n");var v=document.CreateElement(cell.Prefix,"v",ns);v.InnerText=number.ToString("R",CultureInfo.InvariantCulture);cell.PrependChild(v);
                    }
                    if(name=="Resources")foreach(var pair in values)Put(pair.Key,pair.Value,pair.Key);
                    else if(resource.Options.TryGetValue(T("Key"),out float value))Put("Value",value,T("Key"));
                }
                if(written.Count!=(name=="Resources"?values.Count:resource.Options.Count))throw new InvalidDataException("源表缺少要写回的资源参数："+resource.Key);
                zip.GetEntry(part).Delete();using var output=zip.CreateEntry(part,System.IO.Compression.CompressionLevel.Optimal).Open();
                using var writer=XmlWriter.Create(output,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=false});document.Save(writer);
            }
        }
        return memory.ToArray();
    }
}
