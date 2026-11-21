using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>One back command for the visible close button and Escape. The owner resolves its own child pages first.</summary>
public sealed class UIPageNavigation : MonoBehaviour
{
    public const string CloseIconPath = "art_assets/UI/Wish/WishCloseIcon.png";
    private static int escapeFrame = -1;
    private Action leave;
    private Func<bool> tryBack, blocked, showButton;
    private Button close;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInput() => escapeFrame = -1;

    public static UIPageNavigation Create(RectTransform parent, Action leave, Func<bool> tryBack = null,
        Func<bool> blocked = null, Func<bool> showButton = null, Vector2? buttonInset = null)
    {
        var root = new GameObject("Page navigation", typeof(RectTransform), typeof(UIPageNavigation));
        var rect = (RectTransform)root.transform; rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        root.layer = parent.gameObject.layer;
        var navigation = root.GetComponent<UIPageNavigation>();
        navigation.leave = leave; navigation.tryBack = tryBack; navigation.blocked = blocked; navigation.showButton = showButton;
        var button = new GameObject("Close / Escape", typeof(RectTransform), typeof(Image), typeof(Button));
        var area = (RectTransform)button.transform; area.SetParent(rect, false); button.layer = root.layer;
        area.anchorMin = area.anchorMax = area.pivot = Vector2.one;
        area.anchoredPosition = -(buttonInset ?? new Vector2(30, 25)); area.sizeDelta = new Vector2(44, 44);
        var face = button.GetComponent<Image>(); face.color = new Color32(113, 103, 88, 255);
        var rim = button.AddComponent<Outline>(); rim.effectColor = new Color32(231, 217, 190, 115); rim.effectDistance = Vector2.one;
        navigation.close = button.GetComponent<Button>(); navigation.close.targetGraphic = face;
        navigation.close.navigation = new Navigation { mode = Navigation.Mode.None };
        navigation.close.onClick.AddListener(navigation.RequestBack);
        var icon = new GameObject("Close icon", typeof(RectTransform), typeof(Image), typeof(StreamingSpriteReference));
        var iconRect = (RectTransform)icon.transform; iconRect.SetParent(area, false); icon.layer = root.layer;
        iconRect.anchorMin = Vector2.zero; iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = Vector2.one * 12; iconRect.offsetMax = -Vector2.one * 12;
        icon.GetComponent<Image>().raycastTarget = false;
        icon.GetComponent<StreamingSpriteReference>().SetResolvedPath(CloseIconPath);
        navigation.RefreshButton(); return navigation;
    }

    private void LateUpdate()
    {
        // Views can rebuild their content after this component is bound.
        transform.SetAsLastSibling(); RefreshButton();
        if (!Input.GetKeyDown(KeyCode.Escape) || escapeFrame == Time.frameCount || blocked?.Invoke() == true) return;
        escapeFrame = Time.frameCount;
        if (!DismissInput()) RequestBack();
    }

    private void RefreshButton()
    {
        if (close == null) return;
        bool visible = isActiveAndEnabled && showButton?.Invoke() != false;
        if (close.gameObject.activeSelf != visible) close.gameObject.SetActive(visible);
        close.interactable = blocked?.Invoke() != true;
    }

    public void RequestBack()
    {
        if (!isActiveAndEnabled || blocked?.Invoke() == true) return;
        if (tryBack?.Invoke() != true) leave?.Invoke();
    }

    private static bool DismissInput()
    {
        var events = EventSystem.current;
        var field = events != null && events.currentSelectedGameObject != null
            ? events.currentSelectedGameObject.GetComponentInParent<TMP_InputField>() : null;
        if (field != null)
        {
            field.DeactivateInputField(); events.SetSelectedGameObject(null); return true;
        }
        return false;
    }

    private void OnEnable() => RefreshButton();
    private void OnDisable() { if (close != null) close.gameObject.SetActive(false); }
    private void OnDestroy() { if (close != null) close.onClick.RemoveListener(RequestBack); }
}
