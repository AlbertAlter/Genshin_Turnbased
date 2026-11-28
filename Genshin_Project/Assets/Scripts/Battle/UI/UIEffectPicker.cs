using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared metadata-only filter and selector for skill VFX and card-face authoring.</summary>
public sealed class UIEffectPicker
{
    public sealed class Entry { public string Key, Label, Detail, Category, Type, OwnerKey, OwnerLabel, SearchText; }
    private IReadOnlyList<Entry> entries = Array.Empty<Entry>();
    private readonly List<Entry> results = new List<Entry>(), owners = new List<Entry>();
    private string[] categories, types;
    private int category, type, highlighted;
    private string owner = "", query = "";
    private Vector2 resultScroll, ownerScroll;
    private bool focusSearch;
    private GUIStyle text, small, button, input, row;
    public bool CloseRequested { get; private set; }
    public void SetEntries(IReadOnlyList<Entry> values, string[] categoryLabels, string[] typeLabels)
    { entries = values; categories = categoryLabels; types = typeLabels; Refresh(); }
    public void Open(string current)
    {
        focusSearch = true; CloseRequested = false;
        for (int i = 0; i < results.Count; i++) if (results[i].Key == current) { highlighted = i; resultScroll.y = i * 64; break; }
    }
    public static bool Matches(string text, string search)
    {
        foreach (string token in (search ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            if ((text ?? "").IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }
    private void Refresh()
    {
        results.Clear(); owners.Clear(); resultScroll = Vector2.zero; highlighted = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (category != 0 && e.Category != categories[category]) continue;
            if (seen.Add(e.OwnerKey)) owners.Add(e);
            if (owner.Length > 0 && e.OwnerKey != owner || type != 0 && e.Type != types[type]) continue;
            if (Matches(e.SearchText, query)) results.Add(e);
        }
    }
    public bool Draw(Rect bounds, Font font, string current, out string selection)
    {
        selection = current; CloseRequested = false;
        bool enter = Event.current.type == EventType.KeyDown && string.IsNullOrEmpty(Input.compositionString)
            && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
        if (text == null || text.font != font)
        {
            text = new GUIStyle(GUI.skin.label) { font = font, fontSize = 20 };
            small = new GUIStyle(text) { fontSize = 16, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 19 };
            input = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 20 };
            row = new GUIStyle(button) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(12, 10, 6, 5) };
        }
        Color old = GUI.color; GUI.color = new Color(.075f, .085f, .095f, 1);
        GUI.DrawTexture(bounds, Texture2D.whiteTexture); GUI.color = old; GUI.BeginGroup(bounds);
        GUI.Label(new Rect(16, 10, bounds.width - 130, 32), "选择效果", text);
        if (GUI.Button(new Rect(bounds.width - 94, 8, 78, 34), "关闭", button)) CloseRequested = true;
        int next = GUI.SelectionGrid(new Rect(16, 54, bounds.width - 32, 38), category, categories, categories.Length, button);
        if (next != category) { category = next; owner = ""; ownerScroll = Vector2.zero; Refresh(); }
        GUI.Label(new Rect(16, 104, bounds.width - 32, 26), "名称 / 来源编号 / 英文名；空格组合关键词", small);
        GUI.SetNextControlName("UIEffectPicker.Search");
        string nextQuery = GUI.TextField(new Rect(16, 134, bounds.width - 126, 36), query, input);
        if (nextQuery != query) { query = nextQuery; Refresh(); }
        if (GUI.Button(new Rect(bounds.width - 100, 134, 84, 36), "清空", button)) { query = ""; Refresh(); focusSearch = true; }
        if (focusSearch) { GUI.FocusControl("UIEffectPicker.Search"); focusSearch = false; }
        next = GUI.SelectionGrid(new Rect(16, 182, bounds.width - 32, 34), type, types, types.Length, button);
        if (next != type) { type = next; Refresh(); }
        GUI.Label(new Rect(16, 228, 180, 27), "来源", text);
        GUI.Label(new Rect(214, 228, bounds.width - 230, 27), "匹配 " + results.Count + " 项 / 共 " + entries.Count + " 项", text);
        float height = bounds.height - 318, width = bounds.width - 230;
        ownerScroll = GUI.BeginScrollView(new Rect(16, 262, 184, height), ownerScroll, new Rect(0, 0, 162, (owners.Count + 1) * 44));
        if (GUI.Button(new Rect(0, 0, 160, 38), "全部来源", button)) { owner = ""; Refresh(); }
        for (int i = 0; i < owners.Count; i++)
            if (GUI.Button(new Rect(0, (i + 1) * 44, 160, 38), (owner == owners[i].OwnerKey ? "当前 · " : "") + owners[i].OwnerLabel, button))
            { owner = owners[i].OwnerKey; Refresh(); break; }
        GUI.EndScrollView();
        resultScroll = GUI.BeginScrollView(new Rect(214, 262, width, height), resultScroll, new Rect(0, 0, width - 22, Mathf.Max(height, results.Count * 64)));
        bool accepted = false;
        int first = Mathf.Max(0, Mathf.FloorToInt(resultScroll.y / 64));
        int last = Mathf.Min(results.Count, Mathf.CeilToInt((resultScroll.y + height) / 64) + 1);
        for (int i = first; i < last; i++)
        {
            var e = results[i]; Color bg = GUI.backgroundColor;
            if (e.Key == current) GUI.backgroundColor = new Color(.65f, .8f, .75f, 1);
            if (GUI.Button(new Rect(0, i * 64, width - 26, 60), (e.Key == current ? "当前 · " : "") + e.Label, row))
            { selection = e.Key; accepted = true; }
            GUI.backgroundColor = bg; GUI.Label(new Rect(12, i * 64 + 32, width - 50, 23), e.Detail, small);
        }
        if (results.Count == 0) GUI.Label(new Rect(12, 12, width - 45, 100), "没有匹配项。可清空关键词，或调整来源和类型。", small);
        GUI.EndScrollView();
        if (GUI.Button(new Rect(16, bounds.height - 44, 150, 32), "重置筛选", button))
        { category = type = 0; owner = query = ""; ownerScroll = Vector2.zero; Refresh(); focusSearch = true; }
        GUI.Label(new Rect(184, bounds.height - 44, bounds.width - 200, 32), "Enter 选择匹配项；Esc 关闭", small);
        if (enter && results.Count > 0)
        { selection = results[Mathf.Clamp(highlighted, 0, results.Count - 1)].Key; accepted = true; if (Event.current.type != EventType.Used) Event.current.Use(); }
        GUI.EndGroup(); if (accepted || CloseRequested) GUI.FocusControl(null); return accepted;
    }
}
