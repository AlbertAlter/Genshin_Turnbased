using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Optional click adapter for the existing action visuals. Uses the same commands as keyboard input.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Action Buttons")]
public sealed class BattleUiActionButtons : MonoBehaviour
{
    public BattlefieldActionStateView view;
    public BattleInputController input;
    private readonly List<GameObject> hitAreas = new List<GameObject>();
    private bool started;
    private void Start() { started = true; Build(); }
    private void OnEnable() { if (started) Build(); }
    private void Build()
    {
        if (view == null || input == null) return;
        foreach (var action in view.actions)
        {
            if (action?.Icon == null) continue;
            var parent = action.StateBackground != null ? action.StateBackground.transform : action.Icon.transform;
            var area = new GameObject("ActionClickTarget", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = area.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var graphic = area.GetComponent<Image>(); graphic.color = Color.clear; graphic.raycastTarget = true;
            var button = area.GetComponent<Button>(); button.transition = Selectable.Transition.None;
            int slot = action.slot, skill = action.skillType;
            button.onClick.AddListener(() => { if (slot > 0) input.UIEnterBurstBySlot(slot - 1); else input.UIEnterSelectSkill(skill); });
            hitAreas.Add(area);
        }
    }
    private void OnDisable()
    {
        foreach (var area in hitAreas) if (area != null) { area.SetActive(false); Destroy(area); }
        hitAreas.Clear();
    }
}
