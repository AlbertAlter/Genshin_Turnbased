using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class ProjectSettingsView
{
    private BattleShortcut? capturing;
    private int captureStartedFrame;
    private void SelectPage(bool keys) { CancelCapture(); showingKeys = keys; Draw(); }
    private void DrawKeys()
    {
        Label(L(capturing.HasValue ? "ui_keys_capture_hint" : "ui_keys_hint"), 20, -10, 1580, 23);
        var definitions = BattleKeyMap.Definitions;
        for (int i = 0; i < definitions.Count; i++)
        {
            var d = definitions[i]; float x = i < 7 ? 20 : 870, y = -100 - i % 7 * 72;
            Label(L(d.LabelKey), x, y, 490, 25);
            view.Button(view.Root, capturing == d.Action ? L("ui_keys_waiting") : ProjectKeyBindings.Label(d.Action),
                new Vector2(0,1), x + 510, y - 2, 250, 54, () => BeginCapture(d.Action), capturing == d.Action);
        }
        view.Button(view.Root, L("btn_settings_reset_keys"), Vector2.zero, 20, 5, 400, 56,
            () => { CancelCapture(); ProjectKeyBindings.RestoreDefaults(); Draw(); view.Notice(L("tip_keys_reset")); });
    }
    private void BeginCapture(BattleShortcut action)
    {
        if (!ProjectKeyBindings.BeginCapture(this)) return;
        // 录入Enter或Space时不再向刚点击的设置按钮发送Submit。
        EventSystem.current?.SetSelectedGameObject(null);
        capturing = action; captureStartedFrame = Time.frameCount; Draw();
    }
    private void CancelCapture() { ProjectKeyBindings.EndCapture(this); capturing = null; }
    private void Update()
    {
        if (!capturing.HasValue || Time.frameCount <= captureStartedFrame) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { CancelCapture(); Draw(); return; }
        // 本轮支持单个键盘键，不录入组合键、鼠标或手柄按键。
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftAlt)
            || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
            || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)) return;
        foreach (var key in ProjectKeyBindings.CaptureKeys)
        {
            if (!Input.GetKeyDown(key)) continue;
            if (!ProjectKeyBindings.TrySet(capturing.Value, key, out var conflict, out string error))
            {
                string name = conflict.HasValue ? L(BattleKeyMap.Definitions.First(d => d.Action == conflict.Value).LabelKey) : "";
                view.Notice(L(error, name)); return;
            }
            CancelCapture(); Draw(); view.Notice(L("tip_keys_saved")); return;
        }
    }
    private void OnDisable() => CancelCapture();
    private void OnApplicationFocus(bool focused) { if (!focused && capturing.HasValue) { CancelCapture(); Draw(); } }
}
