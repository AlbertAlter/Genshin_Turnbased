using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the generated BattleField1 action visuals to the current battle state.
/// Freeze has visual priority over cooldown; normal state hides both indicators.
/// </summary>
public sealed class BattlefieldActionStateView : MonoBehaviour
{
    private sealed class ActionVisual
    {
        public StreamingSpriteReference Icon;
        public Image DisabledOverlay;
        public Image StateBackground;
        public TMP_Text Cooldown;
        public string ResolvedIcon;
        public bool IsBurst;
    }

    private sealed class StateStyle
    {
        public float IconOpacity;
        public float SkillBackgroundOpacity;
        public float BurstBackgroundOpacity;
        public bool ShowDisabledOverlay;
        public bool ShowCooldownTurns;
    }

    private readonly List<ActionVisual> _skills = new List<ActionVisual>();
    private readonly List<ActionVisual> _bursts = new List<ActionVisual>();
    private BattleInputController _input;
    private readonly Dictionary<string, StateStyle> _styles = new Dictionary<string, StateStyle>();

    private void Start()
    {
        LoadStateStyles();
        CacheVisuals();
    }

    private void Update()
    {
        if (_input == null) _input = FindObjectOfType<BattleInputController>();

        BattleManager battle = BattleManager.Instance;
        if (battle == null) return;
        CharacterBattleController active = battle.GetActiveAlly();
        for (int index = 0; index < _skills.Count; index++)
            RefreshAction(_skills[index], active, index);
        for (int index = 0; index < _bursts.Count; index++)
            RefreshAction(_bursts[index], battle.GetAllyBySlot(index), 3);
    }

    private void CacheVisuals()
    {
        Transform skillContainer = FindTransform("技能");
        if (skillContainer != null)
        {
            IEnumerable<Transform> groups = Enumerable.Range(0, skillContainer.childCount)
                .Select(skillContainer.GetChild)
                .Where(child => child.name.StartsWith("技能"))
                .OrderBy(child => FirstNumber(child.name));
            foreach (Transform group in groups)
                _skills.Add(BuildVisual(group, "CooldownTurns", "SkillStateBackground", false));
        }

        Transform allyContainer = FindTransform("角色栏");
        if (allyContainer != null)
        {
            IEnumerable<Transform> groups = Enumerable.Range(0, allyContainer.childCount)
                .Select(allyContainer.GetChild)
                .Where(child => child.name.StartsWith("角色"))
                .OrderBy(child => FirstNumber(child.name));
            foreach (Transform group in groups)
                _bursts.Add(BuildVisual(group, "BurstCooldown", "BurstStateBackground", true));
        }
    }

    private static ActionVisual BuildVisual(
        Transform group,
        string cooldownKey,
        string backgroundKey,
        bool isBurst)
    {
        PsdUiBinding[] bindings = group.GetComponentsInChildren<PsdUiBinding>(true);
        PsdUiBinding icon = bindings.FirstOrDefault(item =>
            item.bindingKey == "SkillIcon" || item.bindingKey == "BurstIcon");
        PsdUiBinding overlay = bindings.FirstOrDefault(item => item.bindingKey == "DisabledOverlay");
        PsdUiBinding cooldown = bindings.FirstOrDefault(item => item.bindingKey == cooldownKey);
        PsdUiBinding background = bindings.FirstOrDefault(item => item.bindingKey == backgroundKey);
        return new ActionVisual
        {
            Icon = icon != null
                ? icon.GetComponent<StreamingSpriteReference>() ?? icon.GetComponentInChildren<StreamingSpriteReference>(true)
                : null,
            DisabledOverlay = overlay != null
                ? overlay.GetComponent<Image>() ?? overlay.GetComponentInChildren<Image>(true)
                : null,
            StateBackground = background != null
                ? background.GetComponent<Image>() ?? background.GetComponentInChildren<Image>(true)
                : null,
            Cooldown = cooldown != null ? cooldown.GetComponent<TMP_Text>() : null,
            IsBurst = isBurst
        };
    }

    private void RefreshAction(ActionVisual visual, CharacterBattleController ally, int skillType)
    {
        if (visual == null) return;
        bool exists = ally != null && ally.Entity != null;
        bool frozen = exists && (FrozenReactionHandler.IsFrozen(ally.Entity) || ally.IsButtonFrozen(skillType));
        int cooldown = exists ? ally.GetCooldownRemaining(skillType) : 0;
        string state = frozen ? "Freeze" : cooldown > 0 ? "Cooldown" : "Normal";
        StateStyle style = _styles[state];

        if (visual.Icon != null) SetAlpha(visual.Icon.GetComponent<Image>(), style.IconOpacity);
        if (visual.StateBackground != null)
            SetAlpha(visual.StateBackground,
                visual.IsBurst ? style.BurstBackgroundOpacity : style.SkillBackgroundOpacity);
        if (visual.DisabledOverlay != null)
            visual.DisabledOverlay.enabled = style.ShowDisabledOverlay;
        if (visual.Cooldown != null)
        {
            bool showCooldown = style.ShowCooldownTurns && cooldown > 0;
            visual.Cooldown.gameObject.SetActive(showCooldown);
            if (showCooldown) visual.Cooldown.text = cooldown.ToString();
        }

        string configuredIcon = exists ? ally.GetBoundSkillIcon(skillType) : string.Empty;
        if (visual.Icon == null || visual.ResolvedIcon == configuredIcon) return;
        visual.ResolvedIcon = configuredIcon;
        if (CharacterSkillIconResolver.TryGetRelativePath(configuredIcon, out string path))
            visual.Icon.SetResolvedPath(path);
        else
            visual.Icon.SetResolvedPath(string.Empty);
    }

    private void LoadStateStyles()
    {
        _styles.Clear();
        AddStyle("Normal", 255, 255, 255, false, false);
        AddStyle("Freeze", 64, 255, 0, true, false);
        AddStyle("Cooldown", 64, 255, 128, false, true);
        string path = Path.Combine(Application.streamingAssetsPath, "Charts", "UI", "BattleField1_state_styles.csv");
        if (!File.Exists(path)) return;
        foreach (string line in File.ReadAllLines(path).Skip(1))
        {
            string[] cells = line.Split(',');
            if (cells.Length < 6 || string.IsNullOrWhiteSpace(cells[0])) continue;
            if (!int.TryParse(cells[1], out int icon) ||
                !int.TryParse(cells[2], out int skillBackground) ||
                !int.TryParse(cells[3], out int burstBackground))
                continue;
            bool.TryParse(cells[4], out bool showDisabled);
            bool.TryParse(cells[5], out bool showCooldown);
            AddStyle(cells[0].Trim(), icon, skillBackground, burstBackground, showDisabled, showCooldown);
        }
    }

    private void AddStyle(
        string state,
        int iconOpacity,
        int skillBackgroundOpacity,
        int burstBackgroundOpacity,
        bool showDisabled,
        bool showCooldown)
    {
        _styles[state] = new StateStyle
        {
            IconOpacity = Mathf.Clamp01(iconOpacity / 255f),
            SkillBackgroundOpacity = Mathf.Clamp01(skillBackgroundOpacity / 255f),
            BurstBackgroundOpacity = Mathf.Clamp01(burstBackgroundOpacity / 255f),
            ShowDisabledOverlay = showDisabled,
            ShowCooldownTurns = showCooldown
        };
    }

    private static void SetAlpha(Image image, float alpha)
    {
        if (image == null) return;
        Color color = image.color;
        color.a = alpha;
        image.color = color;
    }

    private static Transform FindTransform(string objectName)
    {
        return FindObjectsOfType<Transform>(true).FirstOrDefault(item => item.name == objectName);
    }

    private static int FirstNumber(string value)
    {
        int result = 0;
        bool found = false;
        foreach (char character in value)
        {
            if (character >= '0' && character <= '9')
            {
                found = true;
                result = result * 10 + character - '0';
            }
            else if (found) break;
        }
        return found ? result : int.MaxValue;
    }
}
