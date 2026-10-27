using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Roguelike.Tests.RouteGeneration
{
    public enum RouteLineMode { Procedural, BrushTexture }
    public enum RouteLineFitMode { AdjustGap, CenterPattern }

    public sealed class RouteMapStyle
    {
        public const string FileName = "Rouge_Route_styles.csv";
        public const string RelativePath = "Charts/UI/Rouge_Route/" + FileName;
        public RouteLineMode RenderMode = RouteLineMode.Procedural;
        public Color LineColor = new Color32(113, 103, 88, 255);
        public float LineOpacity = 0.9f;
        public float Width = 3f;
        public float DashLength = 12f;
        public float GapLength = 8f;
        public float LengthVariation = 0.1f;
        public float WidthVariation = 0.1f;
        public float GapVariation = 0.1f;
        public float AngleVariation = 2f;
        public float OffsetVariation = 0.5f;
        public float EdgeRoughness = 0.15f;
        public float EndRoundness = 1f;
        public float EndTaper = 0.1f;
        public int SamplesPerDash = 5;
        public float NodeGap = 4f;
        public RouteLineFitMode FitMode = RouteLineFitMode.AdjustGap;
        public int PatternSeed = 12345;
        public string[] BrushTextures = Array.Empty<string>();
        public bool UseIcons = true;
        public Color NodeColor = new Color32(113, 103, 88, 255);
        public float NodeOpacity = 1f;
        public float IconSize = 30f;
        public string BattleIcon = "art_assets/rouge/routeicon/Battle_v1.png";
        public string EventIcon = "art_assets/rouge/routeicon/Event_v1.png";
        public string EliteIcon = "art_assets/rouge/routeicon/Elite_v3.png";

        public string GetIconPath(RouteNodeType type)
        {
            switch (type)
            {
                case RouteNodeType.Battle: return BattleIcon;
                case RouteNodeType.Event: return EventIcon;
                case RouteNodeType.Elite: return EliteIcon;
                default: return null;
            }
        }

        public IEnumerable<string> ArtPaths()
        {
            if (UseIcons)
            {
                yield return BattleIcon;
                yield return EventIcon;
                yield return EliteIcon;
            }
            if (RenderMode == RouteLineMode.BrushTexture)
                foreach (string path in BrushTextures) yield return path;
        }

        public static IEnumerator Load(Action<RouteMapStyle> onLoaded)
        {
            StreamingAssetSpriteLoader.TryBuildRequestUrl(RelativePath, out string url);
            using UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();
            RouteMapStyle style = null;
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                    throw new IOException(request.error);
                style = Parse(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Route art parameters could not be loaded; using defaults. " + exception.Message);
            }
            onLoaded(style ?? new RouteMapStyle());
        }

        // The editor publication tool validates the source before copying it.
        public static RouteMapStyle ReadFile(string path) => Parse(File.ReadAllText(path));

        public static RouteMapStyle Parse(string csv)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            List<string[]> rows = ParseCsv(csv);
            if (rows.Count == 0 || rows[0].Length < 2 ||
                rows[0][0].TrimStart('\uFEFF') != "Key" || rows[0][1] != "Value")
                throw new InvalidDataException("Route styles must begin with Key,Value.");
            for (int i = 1; i < rows.Count; i++)
            {
                string[] row = rows[i];
                if (row.Length == 1 && string.IsNullOrWhiteSpace(row[0])) continue;
                if (row.Length < 2 || string.IsNullOrWhiteSpace(row[0]))
                    throw new InvalidDataException("Invalid route style row " + (i + 1));
                if (values.ContainsKey(row[0].Trim()))
                    throw new InvalidDataException("Duplicate route style key: " + row[0]);
                values.Add(row[0].Trim(), row[1].Trim());
            }
            string Get(string key)
            {
                if (!values.TryGetValue(key, out string value))
                    throw new InvalidDataException("Missing route style key: " + key);
                return value;
            }
            float Number(string key, float min, float max)
            {
                if (!float.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ||
                    float.IsNaN(number) || float.IsInfinity(number) || number < min || number > max)
                    throw new InvalidDataException("Invalid route style value: " + key);
                return number;
            }
            int Integer(string key, int min, int max)
            {
                if (!int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ||
                    number < min || number > max)
                    throw new InvalidDataException("Invalid route style integer: " + key);
                return number;
            }
            Color Rgb(string key)
            {
                string text = Get(key);
                if (text.Length != 7 || text[0] != '#' || !ColorUtility.TryParseHtmlString(text, out Color color))
                    throw new InvalidDataException("Expected #RRGGBB: " + key);
                return color;
            }
            string Art(string path)
            {
                string normalized = path.Replace('\\', '/');
                if (!normalized.StartsWith("art_assets/", StringComparison.Ordinal) ||
                    normalized.Contains(":") || Array.Exists(normalized.Split('/'), part => part == "..") ||
                    !normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Expected an art_assets/... PNG path: " + path);
                return normalized;
            }

            var result = new RouteMapStyle();
            if (!Enum.TryParse(Get("Line.RenderMode"), out result.RenderMode) || !Enum.IsDefined(typeof(RouteLineMode), result.RenderMode))
                throw new InvalidDataException("Unknown Line.RenderMode.");
            if (!Enum.TryParse(Get("Line.FitMode"), out result.FitMode) || !Enum.IsDefined(typeof(RouteLineFitMode), result.FitMode))
                throw new InvalidDataException("Unknown Line.FitMode.");
            result.LineColor = Rgb("Line.Color");
            result.LineOpacity = Number("Line.Opacity", 0, 1);
            result.Width = Number("Line.Width", 0.001f, float.MaxValue);
            result.DashLength = Number("Line.DashLength", 0.001f, float.MaxValue);
            result.GapLength = Number("Line.GapLength", 0.001f, float.MaxValue);
            result.LengthVariation = Number("Line.LengthVariation", 0, 0.5f);
            result.WidthVariation = Number("Line.WidthVariation", 0, 0.5f);
            result.GapVariation = Number("Line.GapVariation", 0, 0.5f);
            result.AngleVariation = Number("Line.AngleVariation", 0, 5);
            result.OffsetVariation = Number("Line.OffsetVariation", 0, 2);
            result.EdgeRoughness = Number("Line.EdgeRoughness", 0, result.Width * 0.25f);
            result.EndRoundness = Number("Line.EndRoundness", 0, 1);
            result.EndTaper = Number("Line.EndTaper", 0, 0.5f);
            result.SamplesPerDash = Integer("Line.SamplesPerDash", 3, 12);
            result.NodeGap = Number("Line.NodeGap", 0, float.MaxValue);
            result.PatternSeed = Integer("Line.PatternSeed", 0, int.MaxValue);
            string brushes = Get("Line.BrushTextures");
            result.BrushTextures = Array.ConvertAll(brushes.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), p => Art(p.Trim()));
            if (!bool.TryParse(Get("Node.UseIcons"), out result.UseIcons))
                throw new InvalidDataException("Node.UseIcons must be True or False.");
            result.NodeColor = Rgb("Node.Color");
            result.NodeOpacity = Number("Node.Opacity", 0, 1);
            result.IconSize = Number("Node.IconSize", 0.001f, float.MaxValue);
            result.BattleIcon = Art(Get("Node.BattleIcon"));
            result.EventIcon = Art(Get("Node.EventIcon"));
            result.EliteIcon = Art(Get("Node.EliteIcon"));
            return result;
        }

        private static List<string[]> ParseCsv(string csv)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (!quoted && (c == ',' || c == '\r' || c == '\n'))
                {
                    row.Add(cell.ToString()); cell.Clear();
                    if (c != ',')
                    {
                        rows.Add(row.ToArray()); row.Clear();
                        if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    }
                }
                else cell.Append(c);
            }
            if (quoted) throw new InvalidDataException("Unclosed CSV quote.");
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
            return rows;
        }
    }
}
