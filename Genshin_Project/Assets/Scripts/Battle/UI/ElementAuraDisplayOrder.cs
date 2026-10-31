using System;
using System.Collections.Generic;

/// <summary>Presentation ordering only: refresh keeps position; removal and reapplication append anew.</summary>
public sealed class ElementAuraDisplayOrder
{
    private readonly List<string> elements = new List<string>();
    public IReadOnlyList<string> Elements => elements;

    public void Synchronize(IEnumerable<string> present)
    {
        var incoming = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string element in present)
            if (!string.IsNullOrEmpty(element) && unique.Add(element)) incoming.Add(element);
        elements.RemoveAll(element => !unique.Contains(element));
        foreach (string element in incoming)
            if (!elements.Contains(element)) elements.Add(element);
    }
}
