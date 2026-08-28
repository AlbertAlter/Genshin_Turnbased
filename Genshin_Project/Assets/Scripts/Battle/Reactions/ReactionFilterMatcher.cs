using System;
using System.Collections.Generic;

/// <summary>统一解释表格中的反应名或七元素英文名筛选。</summary>
public static class ReactionFilterMatcher
{
    private static readonly string[] ElementNames =
    {
        "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo"
    };

    public static bool Matches(
        string configured,
        ReactionType reactionType,
        string displayName = null,
        IEnumerable<string> actualElements = null)
    {
        if (string.IsNullOrWhiteSpace(configured)) return false;

        string configuredName = GetConfiguredReactionName(reactionType);
        HashSet<string> involvedElements = GetInvolvedElements(reactionType, actualElements);
        foreach (string raw in configured.Split(';'))
        {
            string token = raw.Trim();
            if (token.Length == 0) continue;
            if (!string.IsNullOrEmpty(configuredName)
                && string.Equals(token, configuredName, StringComparison.Ordinal))
                return true;
            if (!string.IsNullOrEmpty(displayName)
                && string.Equals(token, displayName, StringComparison.Ordinal))
                return true;
            if (IsElementName(token) && involvedElements.Contains(token))
                return true;
        }
        return false;
    }

    public static bool Matches(
        string configured,
        ReactionType reactionType,
        string displayName,
        string actualElement)
    {
        return Matches(
            configured,
            reactionType,
            displayName,
            string.IsNullOrWhiteSpace(actualElement) ? null : new[] { actualElement });
    }

    public static string GetConfiguredReactionName(ReactionType reactionType)
    {
        switch (reactionType)
        {
            case ReactionType.Vaporize: return "蒸发";
            case ReactionType.Melt: return "融化";
            case ReactionType.Overloaded: return "超载";
            case ReactionType.Superconduct: return "超导";
            case ReactionType.ElectroCharged: return "感电";
            case ReactionType.Frozen: return "冻结";
            case ReactionType.Shatter: return "碎冰";
            case ReactionType.Swirl: return "扩散";
            case ReactionType.Crystallize: return "结晶";
            case ReactionType.Burning: return "燃烧";
            case ReactionType.Bloom: return "绽放";
            case ReactionType.Quicken: return "原激化";
            case ReactionType.Hyperbloom: return "超绽放";
            case ReactionType.Burgeon: return "烈绽放";
            case ReactionType.Spread: return "蔓激化";
            case ReactionType.Aggravate: return "超激化";
            default: return string.Empty;
        }
    }

    private static HashSet<string> GetInvolvedElements(
        ReactionType reactionType,
        IEnumerable<string> actualElements)
    {
        var elements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (actualElements != null)
        {
            foreach (string element in actualElements)
                if (IsElementName(element)) elements.Add(element.Trim());
        }

        switch (reactionType)
        {
            case ReactionType.Vaporize: Add(elements, "Pyro", "Hydro"); break;
            case ReactionType.Melt: Add(elements, "Pyro", "Cryo"); break;
            case ReactionType.Overloaded: Add(elements, "Pyro", "Electro"); break;
            case ReactionType.Superconduct: Add(elements, "Cryo", "Electro"); break;
            case ReactionType.ElectroCharged: Add(elements, "Hydro", "Electro"); break;
            case ReactionType.Frozen: Add(elements, "Hydro", "Cryo"); break;
            case ReactionType.Shatter: elements.Add("Cryo"); break;
            case ReactionType.Burning: Add(elements, "Pyro", "Dendro"); break;
            case ReactionType.Bloom: Add(elements, "Hydro", "Dendro"); break;
            case ReactionType.Hyperbloom: Add(elements, "Hydro", "Dendro", "Electro"); break;
            case ReactionType.Burgeon: Add(elements, "Hydro", "Dendro", "Pyro"); break;
            case ReactionType.Quicken:
            case ReactionType.Aggravate:
            case ReactionType.Spread:
                Add(elements, "Electro", "Dendro");
                break;
            case ReactionType.Swirl: elements.Add("Anemo"); break;
            case ReactionType.Crystallize: elements.Add("Geo"); break;
        }
        return elements;
    }

    private static bool IsElementName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (string element in ElementNames)
            if (string.Equals(value.Trim(), element, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void Add(HashSet<string> elements, params string[] values)
    {
        foreach (string value in values) elements.Add(value);
    }
}
