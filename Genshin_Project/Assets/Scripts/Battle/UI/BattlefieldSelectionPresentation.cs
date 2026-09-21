using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presents target and character-switch selection as one coherent entity visual.
/// Relevant entities keep both their battlefield cards and complete HUD panels
/// above the dimmer; CardOverGlow is reserved for the current selectable target.
/// </summary>
public sealed class BattlefieldSelectionPresentation : MonoBehaviour
{
    private const float DimmerFadeInDuration = 0.15f;
    private const float TargetTransitionDuration = 0.15f;

    private sealed class EntityVisual
    {
        public BattleSide Side;
        public int Position;
        public Transform Card;
        public Transform Info;
        public readonly List<Image> Glows = new List<Image>();
        public readonly List<Graphic> Graphics = new List<Graphic>();
        public readonly Dictionary<Graphic, Color> BaseColors = new Dictionary<Graphic, Color>();
        public readonly Dictionary<Image, float> GlowBaseAlphas = new Dictionary<Image, float>();
        public float Brightness = 1f;
        public float GlowLevel;
    }

    private sealed class LiftedVisual
    {
        public Transform Transform;
        public Transform Parent;
        public int SiblingIndex;
        public EntityVisual Entity;
    }

    private sealed class EntityTransition
    {
        public EntityVisual Entity;
        public float StartBrightness;
        public float TargetBrightness;
        public float StartGlow;
        public float TargetGlow;
        public float Elapsed;
        public bool RestoreAfter;
    }

    private readonly List<EntityVisual> _entities = new List<EntityVisual>();
    private readonly List<Transform> _entityContainers = new List<Transform>();
    private readonly List<Transform> _originalCanvasOrder = new List<Transform>();
    private readonly List<LiftedVisual> _lifted = new List<LiftedVisual>();
    private readonly HashSet<EntityVisual> _currentlyLifted = new HashSet<EntityVisual>();
    private readonly HashSet<EntityVisual> _currentlyGlowing = new HashSet<EntityVisual>();
    private readonly Dictionary<EntityVisual, EntityTransition> _entityTransitions =
        new Dictionary<EntityVisual, EntityTransition>();

    private BattleInputController _input;
    private BattlefieldSwitchController _switch;
    private Canvas _canvas;
    private Image _dimmer;
    private RectTransform _foreground;
    private bool _presenting;
    private bool _dimmerAlphaCached;
    private bool _dimmerFadingIn;
    private float _dimmerVisibleAlpha;
    private float _dimmerFadeElapsed;

    private void Start()
    {
        CacheHierarchy();
        SetAllGlows(false);
        if (_dimmer != null) _dimmer.enabled = false;
    }

    private void LateUpdate()
    {
        if (_canvas == null || _dimmer == null) CacheHierarchy();
        if (_input == null) _input = FindObjectOfType<BattleInputController>();
        if (_switch == null) _switch = FindObjectOfType<BattlefieldSwitchController>();

        bool selectingTarget = _input != null && _input.IsSelecting;
        bool selectingSwitch = _switch != null && _switch.IsSwitching;
        if ((!selectingTarget && !selectingSwitch) || _dimmer == null)
        {
            if (_presenting) EndPresentation();
            SetAllGlows(false);
            return;
        }

        bool beganPresentation = !_presenting;
        if (beganPresentation) BeginPresentation();
        UpdateDimmerFadeIn();

        HashSet<EntityVisual> visible = BuildVisibleSet();
        HashSet<EntityVisual> glowing = BuildGlowingSet(selectingSwitch);
        if (beganPresentation)
            ApplyInitialSelection(visible, glowing);
        else
            TransitionToSelection(visible, glowing);
        UpdateEntityTransitions(Time.unscaledDeltaTime);
    }

    private void OnDisable()
    {
        if (_presenting) EndPresentation();
        SetAllGlows(false);
    }

    private void CacheHierarchy()
    {
        PsdUiBinding dimmerBinding = FindObjectsOfType<PsdUiBinding>(true)
            .FirstOrDefault(binding => binding.bindingKey == "ScreenDimmer");
        if (dimmerBinding == null) return;

        _dimmer = dimmerBinding.GetComponent<Image>() ?? dimmerBinding.GetComponentInChildren<Image>(true);
        _canvas = dimmerBinding.GetComponentInParent<Canvas>();
        if (_canvas == null) return;

        _entities.Clear();
        _entityContainers.Clear();
        Transform allyCards = FindDirectChild(_canvas.transform, "character");
        Transform enemyCards = FindDirectChild(_canvas.transform, "Enemy");
        Transform allyInfo = FindDirectChild(_canvas.transform, "角色栏");
        Transform enemyInfo = FindDirectChild(_canvas.transform, "敌人栏");
        AddContainer(allyCards);
        AddContainer(enemyCards);
        AddContainer(allyInfo);
        AddContainer(enemyInfo);
        AddSide(BattleSide.Ally, allyCards, "CharacterCard", allyInfo, "角色");
        AddSide(BattleSide.Enemy, enemyCards, "EnemyCard", enemyInfo, "敌人");
    }

    private void AddSide(
        BattleSide side,
        Transform cardContainer,
        string cardPrefix,
        Transform infoContainer,
        string infoPrefix)
    {
        List<Transform> cards = OrderedGroups(cardContainer, cardPrefix);
        List<Transform> infos = OrderedGroups(infoContainer, infoPrefix);
        int count = Mathf.Max(cards.Count, infos.Count);
        for (int index = 0; index < count; index++)
        {
            var entity = new EntityVisual
            {
                Side = side,
                Position = index + 1,
                Card = index < cards.Count ? cards[index] : null,
                Info = index < infos.Count ? infos[index] : null
            };
            if (entity.Card != null)
            {
                foreach (PsdUiBinding binding in entity.Card.GetComponentsInChildren<PsdUiBinding>(true))
                {
                    if (binding.bindingKey != "CardOverGlow") continue;
                    Image glow = binding.GetComponent<Image>() ?? binding.GetComponentInChildren<Image>(true);
                    if (glow == null) continue;
                    entity.Glows.Add(glow);
                    entity.GlowBaseAlphas[glow] = glow.color.a;
                }
            }
            AddGraphics(entity, entity.Card);
            AddGraphics(entity, entity.Info);
            _entities.Add(entity);
        }
    }

    private void BeginPresentation()
    {
        if (_canvas == null || _dimmer == null) return;
        _originalCanvasOrder.Clear();
        for (int index = 0; index < _canvas.transform.childCount; index++)
            _originalCanvasOrder.Add(_canvas.transform.GetChild(index));

        EnsureForeground();
        foreach (Transform container in _entityContainers.OrderBy(item => item.GetSiblingIndex()).ToArray())
        {
            if (container != null && container.parent == _canvas.transform)
                container.SetSiblingIndex(_dimmer.transform.GetSiblingIndex());
        }
        _foreground.gameObject.SetActive(true);
        _foreground.SetAsLastSibling();
        BeginDimmerFadeIn();
        _presenting = true;
    }

    private void EndPresentation()
    {
        ResetEntityTransitions();
        RestoreLiftedVisuals();
        _currentlyLifted.Clear();
        _currentlyGlowing.Clear();
        _dimmerFadingIn = false;
        if (_dimmer != null)
        {
            if (_dimmerAlphaCached) SetDimmerAlpha(_dimmerVisibleAlpha);
            _dimmer.enabled = false;
        }
        if (_foreground != null) _foreground.gameObject.SetActive(false);

        for (int index = 0; index < _originalCanvasOrder.Count; index++)
        {
            Transform item = _originalCanvasOrder[index];
            if (item != null && _canvas != null && item.parent == _canvas.transform)
                item.SetSiblingIndex(index);
        }
        _originalCanvasOrder.Clear();
        _presenting = false;
    }

    private void BeginDimmerFadeIn()
    {
        if (_dimmer == null) return;
        if (!_dimmerAlphaCached)
        {
            _dimmerVisibleAlpha = _dimmer.color.a;
            _dimmerAlphaCached = true;
        }

        _dimmerFadeElapsed = 0f;
        _dimmerFadingIn = true;
        SetDimmerAlpha(0f);
        _dimmer.enabled = true;
    }

    private void UpdateDimmerFadeIn()
    {
        if (!_dimmerFadingIn || _dimmer == null) return;
        _dimmerFadeElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(_dimmerFadeElapsed / DimmerFadeInDuration);
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        SetDimmerAlpha(Mathf.Lerp(0f, _dimmerVisibleAlpha, eased));
        if (progress >= 1f) _dimmerFadingIn = false;
    }

    private void SetDimmerAlpha(float alpha)
    {
        Color color = _dimmer.color;
        color.a = alpha;
        _dimmer.color = color;
    }

    private void ApplyInitialSelection(
        HashSet<EntityVisual> visible,
        HashSet<EntityVisual> glowing)
    {
        ResetEntityTransitions();
        RestoreLiftedVisuals();
        foreach (EntityVisual entity in visible.OrderBy(item => item.Side).ThenBy(item => item.Position))
            LiftEntity(entity);
        foreach (EntityVisual entity in _entities)
            SetEntityGlowLevel(entity, glowing.Contains(entity) ? 1f : 0f);

        _currentlyLifted.Clear();
        _currentlyLifted.UnionWith(visible);
        _currentlyGlowing.Clear();
        _currentlyGlowing.UnionWith(glowing);
    }

    private void TransitionToSelection(
        HashSet<EntityVisual> visible,
        HashSet<EntityVisual> glowing)
    {
        if (_currentlyLifted.SetEquals(visible) && _currentlyGlowing.SetEquals(glowing)) return;

        var affected = new HashSet<EntityVisual>(_currentlyLifted);
        affected.UnionWith(visible);
        affected.UnionWith(_currentlyGlowing);
        affected.UnionWith(glowing);

        foreach (EntityVisual entity in affected)
        {
            bool wasVisible = _currentlyLifted.Contains(entity);
            bool willBeVisible = visible.Contains(entity);
            bool wasGlowing = _currentlyGlowing.Contains(entity);
            bool willGlow = glowing.Contains(entity);
            if (wasVisible == willBeVisible && wasGlowing == willGlow) continue;

            if (willBeVisible && !IsEntityLifted(entity))
            {
                LiftEntity(entity);
                SetEntityBrightness(entity, CurrentDimmedBrightness());
            }

            StartOrRetargetTransition(
                entity,
                willBeVisible ? 1f : CurrentDimmedBrightness(),
                willGlow ? 1f : 0f,
                !willBeVisible);
        }

        _currentlyLifted.Clear();
        _currentlyLifted.UnionWith(visible);
        _currentlyGlowing.Clear();
        _currentlyGlowing.UnionWith(glowing);
    }

    private void StartOrRetargetTransition(
        EntityVisual entity,
        float targetBrightness,
        float targetGlow,
        bool restoreAfter)
    {
        if (entity == null) return;
        CaptureBaseColors(entity);
        _entityTransitions[entity] = new EntityTransition
        {
            Entity = entity,
            StartBrightness = entity.Brightness,
            TargetBrightness = targetBrightness,
            StartGlow = entity.GlowLevel,
            TargetGlow = targetGlow,
            Elapsed = 0f,
            RestoreAfter = restoreAfter
        };
    }

    private void UpdateEntityTransitions(float deltaTime)
    {
        if (_entityTransitions.Count == 0) return;
        var completed = new List<EntityVisual>();
        foreach (EntityTransition transition in _entityTransitions.Values.ToArray())
        {
            transition.Elapsed += Mathf.Max(0f, deltaTime);
            float progress = Mathf.Clamp01(transition.Elapsed / TargetTransitionDuration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            float targetBrightness = transition.RestoreAfter
                ? CurrentDimmedBrightness()
                : transition.TargetBrightness;
            SetEntityBrightness(
                transition.Entity,
                Mathf.Lerp(transition.StartBrightness, targetBrightness, eased));
            SetEntityGlowLevel(
                transition.Entity,
                Mathf.Lerp(transition.StartGlow, transition.TargetGlow, eased));
            if (progress >= 1f) completed.Add(transition.Entity);
        }

        foreach (EntityVisual entity in completed)
        {
            if (!_entityTransitions.TryGetValue(entity, out EntityTransition transition)) continue;
            if (transition.RestoreAfter)
            {
                RestoreBaseColors(entity);
                RestoreEntity(entity);
            }
            else
            {
                SetEntityBrightness(entity, 1f);
                RestoreBaseColors(entity);
            }
            SetEntityGlowLevel(entity, transition.TargetGlow);
            _entityTransitions.Remove(entity);
        }
    }

    private float CurrentDimmedBrightness()
    {
        return _dimmer != null ? Mathf.Clamp01(1f - _dimmer.color.a) : 1f;
    }

    private HashSet<EntityVisual> BuildGlowingSet(bool selectingSwitch)
    {
        var glowing = new HashSet<EntityVisual>();
        if (selectingSwitch)
        {
            AddIfFound(glowing, BattleSide.Ally, _switch.SelectedSlot + 1);
            return glowing;
        }

        TargetSelector selector = _input != null ? _input.Selector : null;
        if (selector == null || selector.CurrentSelection == null) return glowing;
        foreach (int position in selector.CurrentSelection)
            AddIfFound(glowing, selector.Side, position);
        return glowing;
    }

    private HashSet<EntityVisual> BuildVisibleSet()
    {
        var visible = new HashSet<EntityVisual>();
        if (_switch != null && _switch.IsSwitching)
        {
            AddIfFound(visible, BattleSide.Ally, _switch.SelectedSlot + 1);
            return visible;
        }

        CharacterBattleController caster = _input.PendingAlly;
        if (caster != null && caster.Entity != null)
            AddIfFound(visible, BattleSide.Ally, caster.Entity.SlotPosition);

        TargetSelector selector = _input.Selector;
        if (selector != null && selector.CurrentSelection != null)
        {
            foreach (int position in selector.CurrentSelection)
                AddIfFound(visible, selector.Side, position);
        }
        return visible;
    }

    private void AddIfFound(HashSet<EntityVisual> destination, BattleSide side, int position)
    {
        EntityVisual entity = FindEntity(side, position);
        if (entity != null) destination.Add(entity);
    }

    private EntityVisual FindEntity(BattleSide side, int position)
    {
        return _entities.FirstOrDefault(item => item.Side == side && item.Position == position);
    }

    private void LiftEntity(EntityVisual entity)
    {
        if (entity == null || _foreground == null) return;
        Lift(entity.Card, entity);
        Lift(entity.Info, entity);
    }

    private void Lift(Transform item, EntityVisual entity)
    {
        if (item == null || item.parent == null || item.parent == _foreground) return;
        _lifted.Add(new LiftedVisual
        {
            Transform = item,
            Parent = item.parent,
            SiblingIndex = item.GetSiblingIndex(),
            Entity = entity
        });
        item.SetParent(_foreground, true);
    }

    private bool IsEntityLifted(EntityVisual entity)
    {
        return _lifted.Any(item => item.Entity == entity && item.Transform != null &&
                                   item.Transform.parent == _foreground);
    }

    private void RestoreEntity(EntityVisual entity)
    {
        List<LiftedVisual> matches = _lifted.Where(item => item.Entity == entity).ToList();
        foreach (LiftedVisual lifted in matches)
        {
            if (lifted.Transform != null && lifted.Parent != null)
                lifted.Transform.SetParent(lifted.Parent, true);
        }
        foreach (LiftedVisual lifted in matches.OrderBy(item => item.SiblingIndex))
        {
            if (lifted.Transform != null && lifted.Parent != null && lifted.Transform.parent == lifted.Parent)
                lifted.Transform.SetSiblingIndex(lifted.SiblingIndex);
        }
        _lifted.RemoveAll(item => item.Entity == entity);
    }

    private void RestoreLiftedVisuals()
    {
        foreach (LiftedVisual lifted in _lifted)
        {
            if (lifted.Transform != null && lifted.Parent != null)
                lifted.Transform.SetParent(lifted.Parent, true);
        }
        foreach (LiftedVisual lifted in _lifted.OrderBy(item => item.SiblingIndex))
        {
            if (lifted.Transform != null && lifted.Parent != null && lifted.Transform.parent == lifted.Parent)
                lifted.Transform.SetSiblingIndex(lifted.SiblingIndex);
        }
        _lifted.Clear();
    }

    private void SetAllGlows(bool visible)
    {
        foreach (EntityVisual entity in _entities) SetEntityGlowLevel(entity, visible ? 1f : 0f);
    }

    private static void SetEntityGlowLevel(EntityVisual entity, float level)
    {
        if (entity == null) return;
        entity.GlowLevel = Mathf.Clamp01(level);
        foreach (Image glow in entity.Glows)
        {
            if (glow == null) continue;
            float baseAlpha = entity.GlowBaseAlphas.TryGetValue(glow, out float alpha) ? alpha : 1f;
            Color color = glow.color;
            color.a = baseAlpha * entity.GlowLevel;
            glow.color = color;
            glow.enabled = entity.GlowLevel > 0.001f;
        }
    }

    private static void AddGraphics(EntityVisual entity, Transform root)
    {
        if (entity == null || root == null) return;
        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic == null || entity.Glows.Contains(graphic as Image) || entity.Graphics.Contains(graphic))
                continue;
            entity.Graphics.Add(graphic);
        }
    }

    private static void CaptureBaseColors(EntityVisual entity)
    {
        if (entity == null || entity.BaseColors.Count > 0) return;
        foreach (Graphic graphic in entity.Graphics)
            if (graphic != null) entity.BaseColors[graphic] = graphic.color;
    }

    private static void SetEntityBrightness(EntityVisual entity, float brightness)
    {
        if (entity == null) return;
        CaptureBaseColors(entity);
        entity.Brightness = Mathf.Clamp01(brightness);
        foreach (KeyValuePair<Graphic, Color> item in entity.BaseColors.ToArray())
        {
            if (item.Key == null) continue;
            Color color = item.Value;
            color.r *= entity.Brightness;
            color.g *= entity.Brightness;
            color.b *= entity.Brightness;
            item.Key.color = color;
        }
    }

    private static void RestoreBaseColors(EntityVisual entity)
    {
        if (entity == null) return;
        foreach (KeyValuePair<Graphic, Color> item in entity.BaseColors)
            if (item.Key != null) item.Key.color = item.Value;
        entity.BaseColors.Clear();
        entity.Brightness = 1f;
    }

    private void ResetEntityTransitions()
    {
        _entityTransitions.Clear();
        foreach (EntityVisual entity in _entities)
        {
            RestoreBaseColors(entity);
            SetEntityGlowLevel(entity, 0f);
        }
    }

    private void EnsureForeground()
    {
        if (_foreground != null) return;
        var foreground = new GameObject("SelectionForeground", typeof(RectTransform));
        _foreground = foreground.GetComponent<RectTransform>();
        _foreground.SetParent(_canvas.transform, false);
        _foreground.anchorMin = Vector2.zero;
        _foreground.anchorMax = Vector2.one;
        _foreground.offsetMin = Vector2.zero;
        _foreground.offsetMax = Vector2.zero;
        _foreground.pivot = new Vector2(0.5f, 0.5f);
    }

    private void AddContainer(Transform container)
    {
        if (container != null && !_entityContainers.Contains(container)) _entityContainers.Add(container);
    }

    private static Transform FindDirectChild(Transform parent, string name)
    {
        if (parent == null) return null;
        for (int index = 0; index < parent.childCount; index++)
        {
            Transform child = parent.GetChild(index);
            if (child.name == name) return child;
        }
        return null;
    }

    private static List<Transform> OrderedGroups(Transform container, string prefix)
    {
        if (container == null) return new List<Transform>();
        return Enumerable.Range(0, container.childCount)
            .Select(container.GetChild)
            .Where(child => child.name.StartsWith(prefix))
            .OrderBy(child => FirstNumber(child.name))
            .ToList();
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
