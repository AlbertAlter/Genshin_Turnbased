using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Resolves the icon configured in a character workbook's Skills/Icon cell.
/// Shared normal/charged icons live in Skill_Icon/#Normal. Character-specific
/// S (skills), T (passives) and C (constellations) share the character's numeric folder.
/// </summary>
public static class CharacterSkillIconResolver
{
    public const string RelativeFolder = "art_assets/Skill_Icon/#Normal";

    public static bool TryGetRelativePath(string configuredIcon, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(configuredIcon)) return false;

        string value = configuredIcon.Trim().Trim('"').Replace('\\', '/').TrimStart('/');
        if (value.StartsWith("art_assets/", StringComparison.OrdinalIgnoreCase))
            value = value.Substring("art_assets/".Length);

        if (Path.IsPathRooted(value) || value.Contains(":")) return false;
        if (!Path.HasExtension(value)) value += ".png";
        if (value.Contains("/"))
        {
            relativePath = "art_assets/" + value;
        }
        else
        {
            relativePath = TryGetOwner(value, out string owner, out _)
                ? $"art_assets/Skill_Icon/{owner}/{value}"
                : RelativeFolder + "/" + value;
        }
        if (value.Split('/').Contains("..")) return false;
        if (TrySharedAttack(relativePath, out string sharedName))
            relativePath = RelativeFolder + "/" + sharedName + ".png";
        return true;
    }

    private static bool TrySharedAttack(string path, out string sharedName)
    {
        sharedName = null;
        if (!path.StartsWith(RelativeFolder + "/", StringComparison.OrdinalIgnoreCase)) return false;
        string name = path.Substring(RelativeFolder.Length + 1);
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
        name = name.Substring(0, name.Length - 4);
        if (name.Length < 3 || name[0] < '0' || name[0] > '4' || name[1] != '_'
            || (name[2] != '1' && name[2] != '2')) return false;
        // Older Skills/Icon cells included the element used by the baked disc.
        if (name.Length > 3 && (name[3] != '_' || !HasElementDisc(name.Substring(4)))) return false;
        sharedName = name.Substring(0, 3);
        return true;
    }

    private static bool TryGetOwner(string file, out string owner, out char family)
    {
        string name = Path.GetFileNameWithoutExtension(file);
        int split = name.IndexOf('_');
        owner = split >= 4 ? name.Substring(0, split) : string.Empty;
        family = split >= 4 && name.Length == split + 4 ? char.ToUpperInvariant(name[split + 1]) : '\0';
        return (family == 'S' || family == 'T' || family == 'C') && owner.All(char.IsDigit)
            && char.IsDigit(name[split + 2]) && char.IsDigit(name[split + 3]);
    }

    public static string ElementBackground(string configuredIcon, string element)
    {
        if (!HasElementDisc(element) || !TryGetRelativePath(configuredIcon, out string path)) return null;
        bool skill = TryGetOwner(Path.GetFileName(path), out _, out char family) && family == 'S';
        return skill || TrySharedAttack(path, out _) ? $"art_assets/Skill_Icon/#Others/{element}_BG.png" : null;
    }

    private static bool HasElementDisc(string element)
    {
        switch (element)
        {
            case "Pyro": case "Hydro": case "Electro": case "Cryo":
            case "Geo": case "Anemo": case "Dendro":
                return true;
            default: return false;
        }
    }

    public static IEnumerator LoadSprite(
        string configuredIcon,
        Action<Sprite> onLoaded,
        Action<string> onError = null)
    {
        if (!TryGetRelativePath(configuredIcon, out string relativePath))
        {
            onError?.Invoke("Skills/Icon is empty or invalid, so the skill icon cannot be loaded.");
            yield break;
        }

        yield return StreamingAssetSpriteLoader.Load(relativePath, onLoaded, onError);
    }
}
