using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Resolves the icon configured in a character workbook's Skills/Icon cell.
/// Shared normal/charged icons live in Skill_Icon/#Normal. Character-specific
/// talent IDs such as 1009_T01 live in the matching numeric character folder.
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
            int separator = value.IndexOf("_T", StringComparison.OrdinalIgnoreCase);
            string owner = separator > 0 ? value.Substring(0, separator) : string.Empty;
            bool characterSpecific = separator > 0 && owner.All(char.IsDigit) && owner.Length >= 4;
            relativePath = characterSpecific
                ? $"art_assets/Skill_Icon/{owner}/{value}"
                : RelativeFolder + "/" + value;
        }
        return !value.Split('/').Contains("..");
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
