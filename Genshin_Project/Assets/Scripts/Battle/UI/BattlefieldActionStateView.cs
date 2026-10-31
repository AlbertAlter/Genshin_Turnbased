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
    [System.Serializable]
    public sealed class ActionVisual
    {
        public GameObject Root;
        public StreamingSpriteReference Icon;
        public Image DisabledOverlay;
        public Image StateBackground;
        public TMP_Text Cooldown;
        [System.NonSerialized] public string ResolvedIcon;
        public bool IsBurst;
        public int slot, skillType;
        [System.NonSerialized] public float DisabledAlpha;
        [System.NonSerialized] public float CooldownAlpha;
        public float DisabledMaxAlpha = 1;
        public float CooldownMaxAlpha = 1;
        [System.NonSerialized] public object IndicatorEntity;
    }

    private sealed class StateStyle
    {
        public float IconOpacity;
        public float SkillBackgroundOpacity;
        public float BurstBackgroundOpacity;
        public bool ShowDisabledOverlay;
        public bool ShowCooldownTurns;
    }

    public ActionVisual[] actions = System.Array.Empty<ActionVisual>();
    public MonoBehaviour dataSource;
    private IBattleUiActionSource boundSource;
    private readonly Dictionary<string, StateStyle> _styles = new Dictionary<string, StateStyle>();
    private float indicatorFadeIn = .12f;
    private float indicatorFadeOut = .1f;

    private void Start()
    {
        LoadStateStyles();
        if (boundSource == null) boundSource = dataSource as IBattleUiActionSource;
        foreach (var visual in actions)
        {
            if (visual == null) continue;
            if (visual.DisabledOverlay != null) visual.DisabledMaxAlpha = visual.DisabledOverlay.color.a;
            if (visual.Cooldown != null) visual.CooldownMaxAlpha = visual.Cooldown.color.a;
        }
    }

    private void Update()
    {
        if (boundSource == null) return;
        foreach (var visual in actions)
            if (visual != null) RefreshAction(visual, boundSource.GetAction(visual.slot, visual.skillType));
    }

    public void Bind(IBattleUiActionSource source) => boundSource = source;

    // Editor initial assembly only. Runtime views never discover scene objects by names.
    public void ConfigureInitialVisuals(Transform skillContainer, Transform allyContainer)
    {
        var configured = new List<ActionVisual>();
        if (skillContainer != null)
        {
            IEnumerable<Transform> groups = Enumerable.Range(0, skillContainer.childCount)
                .Select(skillContainer.GetChild)
                .Where(child => child.name.StartsWith("技能"))
                .OrderBy(child => FirstNumber(child.name));
            foreach (Transform group in groups)
            {
                var visual = BuildVisual(group, "CooldownTurns", "SkillStateBackground", false);
                visual.slot = 0; visual.skillType = configured.Count;
                configured.Add(visual);
            }
        }

        if (allyContainer != null)
        {
            IEnumerable<Transform> groups = Enumerable.Range(0, allyContainer.childCount)
                .Select(allyContainer.GetChild)
                .Where(child => child.name.StartsWith("角色"))
                .OrderBy(child => FirstNumber(child.name));
            int slot = 1;
            foreach (Transform group in groups)
            {
                var visual = BuildVisual(group, "BurstCooldown", "BurstStateBackground", true);
                visual.slot = slot++; visual.skillType = 3;
                configured.Add(visual);
            }
        }
        actions = configured.ToArray();
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
        var visual = new ActionVisual
        {
            Root = isBurst ? null : group.gameObject,
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
        if (visual.DisabledOverlay != null) visual.DisabledMaxAlpha=visual.DisabledOverlay.color.a;
        if (visual.Cooldown != null) visual.CooldownMaxAlpha=visual.Cooldown.color.a;
        return visual;
    }

    public void RefreshAction(ActionVisual visual, BattleUiActionState data)
    {
        if (visual == null) return;
        if (_styles.Count == 0) LoadStateStyles();
        bool exists = data.Visible;
        if (visual.Root != null) visual.Root.SetActive(exists);
        bool frozen = exists && data.Frozen;
        int cooldown = exists ? data.Cooldown : 0;
        // 可莉/魈等仍有剩余次数时可以继续施放，充能计时不等于按钮不可用。
        string state = frozen ? "Freeze" : cooldown > 0 && !data.HasAvailableCharge ? "Cooldown" : "Normal";
        StateStyle style = _styles[state];

        if (visual.Icon != null) SetAlpha(visual.Icon.GetComponent<Image>(), exists ? style.IconOpacity : 0f);
        if (visual.StateBackground != null)
            SetAlpha(visual.StateBackground,
                exists ? (visual.IsBurst ? style.BurstBackgroundOpacity : style.SkillBackgroundOpacity) : 0f);
        object entity=exists ? data.Identity : null;
        if (!ReferenceEquals(visual.IndicatorEntity, entity))
        {
            // Never fade the previous character's cooldown number over the new character.
            visual.IndicatorEntity=entity;
            visual.DisabledAlpha=visual.CooldownAlpha=0;
        }
        bool showCooldown=exists && style.ShowCooldownTurns && cooldown>0;
        if (showCooldown && visual.Cooldown != null) visual.Cooldown.text=cooldown.ToString();
        FadeIndicator(visual.DisabledOverlay,exists && style.ShowDisabledOverlay,
            ref visual.DisabledAlpha,visual.DisabledMaxAlpha);
        FadeIndicator(visual.Cooldown,showCooldown,ref visual.CooldownAlpha,visual.CooldownMaxAlpha);

        string configuredIcon = exists ? data.Icon : string.Empty;
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
        var parameters = BattlefieldAnimationParameters.Load();
        indicatorFadeIn=parameters.Get("ActionIndicator.FadeInDuration",.12f);
        indicatorFadeOut=parameters.Get("ActionIndicator.FadeOutDuration",.1f);
        foreach (var state in _styles.Keys.ToArray())
        {
            var style = _styles[state];
            style.IconOpacity=parameters.Get(state+".IconOpacity",style.IconOpacity*255,0,255)/255;
            style.SkillBackgroundOpacity=parameters.Get(state+".SkillBackgroundOpacity",style.SkillBackgroundOpacity*255,0,255)/255;
            style.BurstBackgroundOpacity=parameters.Get(state+".BurstBackgroundOpacity",style.BurstBackgroundOpacity*255,0,255)/255;
        }
    }

    public void ReloadAnimationParameters() => LoadStateStyles();

    private void FadeIndicator(Graphic graphic,bool show,ref float alpha,float maxAlpha)
    {
        if (graphic == null) return;
        float duration=show ? indicatorFadeIn : indicatorFadeOut;
        alpha=duration<=0 ? (show ? 1 : 0) : Mathf.MoveTowards(alpha,show ? 1 : 0,Time.unscaledDeltaTime/duration);
        bool visible=show || alpha>0;
        if (graphic.gameObject.activeSelf != visible) graphic.gameObject.SetActive(visible);
        graphic.enabled=visible;
        graphic.raycastTarget=false;
        Color color=graphic.color;color.a=alpha*maxAlpha;graphic.color=color;
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

    private void OnDisable()
    {
        foreach (var visual in actions)
        {
            if (visual == null) continue;
            visual.IndicatorEntity = null;
            visual.DisabledAlpha = visual.CooldownAlpha = 0;
            if (visual.DisabledOverlay != null) visual.DisabledOverlay.gameObject.SetActive(false);
            if (visual.Cooldown != null) visual.Cooldown.gameObject.SetActive(false);
        }
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
