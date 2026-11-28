using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum BattleShortcut
{
    // 数值已用于场景键帽绑定，后续新增动作不可重排现有值。
    Normal = 0, Heavy = 1, Skill = 2, Burst1 = 3, Burst2 = 4, Burst3 = 5, Burst4 = 6,
    Previous = 7, Next = 8, Switch = 9, Confirm = 10, Cancel = 11, Pause = 12
}

/// <summary>纯按键映射及校验；不会执行战斗命令。界面、实际输入与键帽读取同一份映射。</summary>
public sealed class BattleKeyMap
{
    public readonly struct Definition
    {
        public readonly BattleShortcut Action;
        public readonly KeyCode DefaultKey;
        public readonly string LabelKey;
        public Definition(BattleShortcut action, KeyCode key, string label)
        { Action = action; DefaultKey = key; LabelKey = label; }
    }
    public static readonly IReadOnlyList<Definition> Definitions = Array.AsReadOnly(new[] {
        new Definition(BattleShortcut.Normal, KeyCode.Q, "ui_keys_normal"),
        new Definition(BattleShortcut.Heavy, KeyCode.W, "ui_keys_heavy"),
        new Definition(BattleShortcut.Skill, KeyCode.E, "ui_keys_skill"),
        new Definition(BattleShortcut.Burst1, KeyCode.Alpha1, "ui_keys_burst_1"),
        new Definition(BattleShortcut.Burst2, KeyCode.Alpha2, "ui_keys_burst_2"),
        new Definition(BattleShortcut.Burst3, KeyCode.Alpha3, "ui_keys_burst_3"),
        new Definition(BattleShortcut.Burst4, KeyCode.Alpha4, "ui_keys_burst_4"),
        new Definition(BattleShortcut.Previous, KeyCode.A, "ui_keys_previous"),
        new Definition(BattleShortcut.Next, KeyCode.D, "ui_keys_next"),
        new Definition(BattleShortcut.Switch, KeyCode.F, "ui_keys_switch"),
        new Definition(BattleShortcut.Confirm, KeyCode.Space, "ui_keys_confirm"),
        new Definition(BattleShortcut.Cancel, KeyCode.X, "ui_keys_cancel"),
        new Definition(BattleShortcut.Pause, KeyCode.Escape, "ui_keys_pause") });
    private readonly Dictionary<BattleShortcut, KeyCode> keys = new();
    public BattleKeyMap() => Reset();
    public KeyCode this[BattleShortcut action] => keys[action];
    public void Reset() { keys.Clear(); foreach (var d in Definitions) keys.Add(d.Action, d.DefaultKey); }
    public static bool IsKeyboardKey(KeyCode key)
    {
        if (!Enum.IsDefined(typeof(KeyCode), key) || key == KeyCode.None || (int)key >= (int)KeyCode.Mouse0) return false;
        switch (key)
        {
            case KeyCode.LeftShift: case KeyCode.RightShift: case KeyCode.LeftControl: case KeyCode.RightControl:
            case KeyCode.LeftAlt: case KeyCode.RightAlt: case KeyCode.LeftCommand: case KeyCode.RightCommand:
            case KeyCode.LeftWindows: case KeyCode.RightWindows: case KeyCode.AltGr:
            case KeyCode.CapsLock: case KeyCode.Numlock: case KeyCode.ScrollLock: return false;
            default: return true;
        }
    }
    public bool TryAssign(BattleShortcut action, KeyCode key, out BattleShortcut? conflict, out string errorKey)
    {
        conflict = null; errorKey = null;
        if (!keys.ContainsKey(action) || !IsKeyboardKey(key)) { errorKey = "tip_keys_unsupported"; return false; }
        if (key == KeyCode.Escape && action != BattleShortcut.Pause) { errorKey = "tip_keys_reserved"; return false; }
        foreach (var entry in keys)
            if (entry.Key != action && entry.Value == key)
            { conflict = entry.Key; errorKey = "tip_keys_conflict"; return false; }
        keys[action] = key; return true;
    }
    /// <summary>整份旧配置先验证再采用；损坏或冲突时保留默认值，不进行含糊的逐项覆盖。</summary>
    public bool TryLoad(IReadOnlyDictionary<BattleShortcut, KeyCode> saved)
    {
        var candidate = new Dictionary<BattleShortcut, KeyCode>();
        var seen = new HashSet<KeyCode>();
        foreach (var d in Definitions)
        {
            var key = saved != null && saved.TryGetValue(d.Action, out var value) ? value : d.DefaultKey;
            if (!IsKeyboardKey(key) || key == KeyCode.Escape && d.Action != BattleShortcut.Pause || !seen.Add(key)) return false;
            candidate.Add(d.Action, key);
        }
        keys.Clear(); foreach (var entry in candidate) keys.Add(entry.Key, entry.Value);
        return true;
    }
}

/// <summary>用户级改键偏好，与游戏存档独立。统一键盘查询和录入期间的输入隔离。</summary>
public static class ProjectKeyBindings
{
    private const string Prefix = "Project.Keys.";
    private static BattleKeyMap map;
    private static object captureOwner;
    private static int suppressedFrame = -1;
    public static event Action Changed;
    public static bool IsCapturing => captureOwner != null;
    public static bool InputSuppressed => IsCapturing || suppressedFrame == Time.frameCount;
    public static readonly IReadOnlyList<KeyCode> CaptureKeys = Array.AsReadOnly(((KeyCode[])Enum.GetValues(typeof(KeyCode)))
        .Where(BattleKeyMap.IsKeyboardKey).Distinct().ToArray());
    private static BattleKeyMap Map
    {
        get
        {
            if (map != null) return map;
            map = new BattleKeyMap(); var saved = new Dictionary<BattleShortcut, KeyCode>();
            foreach (var d in BattleKeyMap.Definitions)
                if (PlayerPrefs.HasKey(Prefix + d.Action)) saved.Add(d.Action, (KeyCode)PlayerPrefs.GetInt(Prefix + d.Action));
            if (!map.TryLoad(saved)) Debug.LogWarning("改键配置无效，本次使用默认按键。");
            return map;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntime() { map = null; captureOwner = null; suppressedFrame = -1; Changed = null; }
    public static KeyCode Get(BattleShortcut action) => Map[action];
    public static bool Down(BattleShortcut action) => !InputSuppressed && Input.GetKeyDown(Get(action));
    public static bool TrySet(BattleShortcut action, KeyCode key, out BattleShortcut? conflict, out string errorKey)
    {
        if (!Map.TryAssign(action, key, out conflict, out errorKey)) return false;
        Save(); return true;
    }
    public static void RestoreDefaults() { Map.Reset(); Save(); }
    private static void Save()
    {
        foreach (var d in BattleKeyMap.Definitions) PlayerPrefs.SetInt(Prefix + d.Action, (int)Map[d.Action]);
        PlayerPrefs.Save(); suppressedFrame = Time.frameCount; Changed?.Invoke();
    }
    public static bool BeginCapture(object owner)
    {
        if (owner == null || captureOwner != null && !ReferenceEquals(captureOwner, owner)) return false;
        captureOwner = owner; suppressedFrame = Time.frameCount; return true;
    }
    public static void EndCapture(object owner)
    {
        if (!ReferenceEquals(captureOwner, owner)) return;
        captureOwner = null; suppressedFrame = Time.frameCount;
    }
    public static string Label(BattleShortcut action) => KeyLabel(Get(action));
    public static string KeyLabel(KeyCode key)
    {
        if ((int)key >= (int)KeyCode.Alpha0 && (int)key <= (int)KeyCode.Alpha9) return ((int)key - (int)KeyCode.Alpha0).ToString();
        if ((int)key >= (int)KeyCode.A && (int)key <= (int)KeyCode.Z) return key.ToString().ToUpperInvariant();
        switch (key)
        {
            case KeyCode.Keypad0: case KeyCode.Keypad1: case KeyCode.Keypad2: case KeyCode.Keypad3: case KeyCode.Keypad4:
            case KeyCode.Keypad5: case KeyCode.Keypad6: case KeyCode.Keypad7: case KeyCode.Keypad8: case KeyCode.Keypad9:
                return "NUM" + ((int)key - (int)KeyCode.Keypad0);
            case KeyCode.Space: return "SPACE"; case KeyCode.Escape: return "ESC";
            case KeyCode.Return: return "ENTER"; case KeyCode.KeypadEnter: return "NUM↵";
            case KeyCode.KeypadPeriod: return "NUM."; case KeyCode.KeypadDivide: return "NUM/";
            case KeyCode.KeypadMultiply: return "NUM*"; case KeyCode.KeypadMinus: return "NUM-";
            case KeyCode.KeypadPlus: return "NUM+"; case KeyCode.KeypadEquals: return "NUM=";
            case KeyCode.LeftArrow: return "←"; case KeyCode.RightArrow: return "→";
            case KeyCode.UpArrow: return "↑"; case KeyCode.DownArrow: return "↓";
            case KeyCode.Backspace: return "BS"; case KeyCode.PageUp: return "PGUP";
            case KeyCode.PageDown: return "PGDN"; case KeyCode.Insert: return "INS"; case KeyCode.Delete: return "DEL";
            default: return key.ToString().ToUpperInvariant();
        }
    }
}
