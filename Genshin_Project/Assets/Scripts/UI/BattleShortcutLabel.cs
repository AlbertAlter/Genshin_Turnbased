using TMPro;
using UnityEngine;

/// <summary>只更新显式绑定的键帽文字；不移动场景节点。较长键名在原文字框内缩小。</summary>
public sealed class BattleShortcutLabel : MonoBehaviour
{
    public TMP_Text label;
    public BattleShortcut shortcut;
    private bool captured, wasAuto;
    private float minimum, maximum, baseSize;
    public void Bind(TMP_Text text, BattleShortcut action)
    {
        Restore(); label = text; shortcut = action; Apply();
    }
    private void OnEnable() { ProjectKeyBindings.Changed += Apply; Apply(); }
    private void OnDisable() { ProjectKeyBindings.Changed -= Apply; Restore(); }
    private void Apply()
    {
        if (!isActiveAndEnabled || label == null) return;
        if (!captured)
        { captured = true; wasAuto = label.enableAutoSizing; minimum = label.fontSizeMin; maximum = label.fontSizeMax; baseSize = label.fontSize; }
        label.enableAutoSizing = true; label.fontSizeMax = baseSize; label.fontSizeMin = Mathf.Min(10, baseSize);
        label.text = ProjectKeyBindings.Label(shortcut);
    }
    private void Restore()
    {
        if (captured && label != null) { label.enableAutoSizing = wasAuto; label.fontSizeMin = minimum; label.fontSizeMax = maximum; label.fontSize = baseSize; }
        captured = false;
    }
}
