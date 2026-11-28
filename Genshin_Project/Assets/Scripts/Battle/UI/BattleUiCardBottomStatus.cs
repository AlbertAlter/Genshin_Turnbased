using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Four newest object icons per real card/field slot, outside the card toward its nearest top/bottom edge.</summary>
[DisallowMultipleComponent]
public sealed class BattleUiCardBottomStatus : MonoBehaviour
{
    public BattleManager battle;
    public BattleInputController input;
    public BattleUiLayout layout;
    public RectTransform displayMount;
    public TMP_FontAsset font;
    private CardBottomStatusTable table;
    private UIIconMainColor mainColors;
    private readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();
    private readonly Dictionary<BattleUiCardBinding, RectTransform> bars = new Dictionary<BattleUiCardBinding, RectTransform>();
    private readonly Dictionary<BattleUiCardBinding, Image[]> images = new Dictionary<BattleUiCardBinding, Image[]>();
    private readonly Dictionary<BattleUiCardBinding, TMP_Text[]> numbers = new Dictionary<BattleUiCardBinding, TMP_Text[]>();
    private readonly Dictionary<BattleUiCardBinding, BattleStatusDescriptionView[]> descriptions = new();
    private readonly List<StatusInstance> statuses = new List<StatusInstance>();
    private readonly List<CardBottomStatusTable.Display> values = new List<CardBottomStatusTable.Display>();
    private readonly Dictionary<int, int> indices = new Dictionary<int, int>();
    private readonly Vector3[] corners = new Vector3[4];
    private readonly HashSet<BattleUiCardBinding> seen = new HashSet<BattleUiCardBinding>();
    private readonly List<BattleUiCardBinding> removed = new List<BattleUiCardBinding>();

    private void OnEnable() => StartCoroutine(Load());
    private IEnumerator Load()
    {
        try
        {
            table = CardBottomStatusTable.Load();
            foreach (var rule in table.Rules.Values)
                if (!string.IsNullOrEmpty(rule.MainColor)) { mainColors = new UIIconMainColor(ElementColorPalette.Load()); break; }
        }
        catch (Exception error)
        {
            table = null; mainColors?.Dispose(); mainColors = null;
            Debug.LogError("卡底状态配置失败：" + error.Message, this); yield break;
        }
        var loaded = new HashSet<string>();
        foreach (var rule in table.Rules.Values)
            if (loaded.Add(rule.Icon)) yield return StreamingAssetSpriteLoader.Load(rule.Icon, sprite => icons.Add(rule.Icon, sprite), error => Debug.LogError(error, this));
    }
    private void LateUpdate()
    {
        if (table == null || layout == null || battle == null || displayMount == null || font == null) return;
        seen.Clear();
        foreach (var binding in layout.Cards)
        {
            if (binding?.art == null) continue;
            var position = battle.Field?.GetSlot(binding.side, binding.slot); if (position == null) continue;
            seen.Add(binding); statuses.Clear();
            if (position.StatusList != null) statuses.AddRange(position.StatusList);
            var entity = battle.GetEntityByPosition(binding.side, binding.slot);
            if (entity?.IsOnField == true) statuses.AddRange(entity.GetStatusList());
            try { table.Resolve(statuses, values, indices, position, entity?.IsOnField == true ? entity : null); }
            catch (Exception error)
            {
                Debug.LogError("卡底状态计数失败：" + error.Message, this);
                enabled = false; // Clear this module through OnDisable; do not repeat an invalid hook every frame.
                return;
            }
            int count = Math.Min(table.MaxVisible, values.Count);
            if (!bars.TryGetValue(binding, out var bar))
            { if (count == 0) continue; bar = Create(binding); }
            bar.gameObject.SetActive(count > 0 && battle.IsBattleRunning && !battle.IsBattleOver);
            if (count == 0 || !bar.gameObject.activeSelf) continue;
            binding.art.GetWorldCorners(corners);
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity); var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in corners) { Vector2 point = displayMount.InverseTransformPoint(corner); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            float size = (max.x - min.x) * table.IconSizeRatio, gap = (max.x - min.x) * table.SpacingRatio;
            bool upper = (min.y + max.y) * .5f > displayMount.rect.center.y;
            bar.anchoredPosition = new Vector2((min.x + max.x) * .5f - displayMount.rect.center.x,
                (upper ? max.y + (max.y - min.y) * table.EdgeGapRatio + size * .5f : min.y - (max.y - min.y) * table.EdgeGapRatio - size * .5f) - displayMount.rect.center.y);
            for (int i = 0; i < 4; i++)
            {
                var image = images[binding][i]; image.gameObject.SetActive(i < count);
                if (i >= count) continue;
                var value = values[i]; var rect = (RectTransform)image.transform;
                descriptions[binding][i].BindCardObject(value.Status, value.Status.Host, input, battle);
                rect.anchoredPosition = new Vector2((i - (count - 1) * .5f) * (size + gap), 0); rect.sizeDelta = Vector2.one * size;
                icons.TryGetValue(value.Rule.Icon, out var sprite); image.sprite = sprite; image.enabled = sprite != null;
                try { image.material = string.IsNullOrEmpty(value.Rule.MainColor) ? null : mainColors.GetMaterial(value.Rule.MainColor, value.Status.Host as BattleEntity); }
                catch (Exception error)
                {
                    Debug.LogError("卡底状态主色失败：" + error.Message, this);
                    enabled = false;
                    return;
                }
                var number = numbers[binding][i]; number.fontSize = size * table.NumberSizeRatio;
                number.gameObject.SetActive(sprite != null && value.Rule.ShowCount);
                number.text = value.Rule.ShowCount ? value.Count.ToString("0.##", CultureInfo.InvariantCulture) : "";
            }
        }
        removed.Clear(); foreach (var binding in bars.Keys) if (!seen.Contains(binding)) removed.Add(binding);
        foreach (var binding in removed) { Destroy(bars[binding].gameObject); bars.Remove(binding); images.Remove(binding); numbers.Remove(binding); descriptions.Remove(binding); }
    }
    private RectTransform Create(BattleUiCardBinding binding)
    {
        var root = new GameObject("CardBottomStatus_" + binding.side + "_" + binding.slot, typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(displayMount, false); root.anchorMin = root.anchorMax = root.pivot = Vector2.one * .5f;
        root.gameObject.layer = displayMount.gameObject.layer;
        var art = new Image[4]; var counts = new TMP_Text[4]; var details = new BattleStatusDescriptionView[4];
        for (int i = 0; i < 4; i++)
        {
            var rect = new GameObject("Object " + i, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>(); rect.SetParent(root, false);
            rect.gameObject.layer = root.gameObject.layer;
            art[i] = rect.GetComponent<Image>(); art[i].raycastTarget = true; art[i].preserveAspect = true;
            details[i] = rect.gameObject.AddComponent<BattleStatusDescriptionView>();
            var label = new GameObject("Count", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(rect, false); var textRect = (RectTransform)label.transform;
            label.gameObject.layer = root.gameObject.layer;
            textRect.anchorMin = new Vector2(.2f, -.07f); textRect.anchorMax = new Vector2(1.08f, .48f); textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            label.font = font; label.alignment = TextAlignmentOptions.BottomRight; label.raycastTarget = false; label.enableWordWrapping = false;
            label.fontStyle = FontStyles.Bold; label.color = Color.white; counts[i] = label;
            var shadow = label.gameObject.AddComponent<Shadow>(); shadow.effectColor = new Color(0,0,0,.85f); shadow.effectDistance = new Vector2(1,-1);
        }
        bars.Add(binding, root); images.Add(binding, art); numbers.Add(binding, counts); descriptions.Add(binding, details); return root;
    }
    private void OnDisable()
    {
        StopAllCoroutines(); foreach (var bar in bars.Values) if (bar != null) Destroy(bar.gameObject);
        bars.Clear(); images.Clear(); numbers.Clear(); descriptions.Clear();
        mainColors?.Dispose(); mainColors = null;
        foreach (var sprite in icons.Values) if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
        icons.Clear(); table = null;
    }
}
