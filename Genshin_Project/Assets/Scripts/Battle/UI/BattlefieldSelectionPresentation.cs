using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Selection dims cards in place. Scene sibling order owns overlap in every input mode.</summary>
public sealed class BattlefieldSelectionPresentation : MonoBehaviour
{
    private BattleInputController _input;
    private BattlefieldSwitchController _switch;
    private BattleManager _battle;
    private const float ReserveBrightness = 1f - 38f / 255f; // BattleField1.psd, character/矩形 504.
    private Image _dimmer;
    private float _inSeconds, _outSeconds, _targetSeconds, _phase, _dimmerAlpha;
    private bool _presenting;
    private TaskCompletionSource<bool> _timelineExit;
    private float _timelineExitSeconds;
    private readonly List<EntityVisual> _entities = new List<EntityVisual>();
    private readonly List<SkillButton> _skills = new List<SkillButton>();

    private sealed class SkillButton
    {
        public Transform Root;
        public int Skill;
        public float Dim;
        public readonly GroupTint Tint = new GroupTint();
    }

    private sealed class EntityVisual
    {
        public BattleSide Side;
        public int Position;
        public Transform Card, Info;
        public Image Border;
        public int BorderIndex;
        public float BorderAlpha, Glow, Focus;
        public readonly GroupTint CardTint = new GroupTint(), InfoTint = new GroupTint();
    }

    // Renderer RGB is independent of Graphic.color (art/element colors) and alpha fades.
    // Discover new video, shield and status graphics while dimmed without allocating each frame.
    private sealed class GroupTint
    {
        private readonly Dictionary<Graphic, Color> originals = new Dictionary<Graphic, Color>();
        private readonly List<Graphic> graphics = new List<Graphic>();

        public void Apply(Transform root, float factor, Graphic excluded = null)
        {
            if (root == null || factor == 1f) { Restore(); return; }
            root.GetComponentsInChildren(true, graphics);
            foreach (var graphic in graphics)
            {
                if (graphic == null || graphic == excluded || !graphic.TryGetComponent<CanvasRenderer>(out var renderer)) continue;
                if (!originals.TryGetValue(graphic, out var color))
                    originals.Add(graphic, color = renderer.GetColor());
                Set(renderer, color, factor);
            }
            graphics.Clear();
            foreach (var graphic in originals.Keys)
                if (graphic == null || !graphic.transform.IsChildOf(root)) graphics.Add(graphic);
            foreach (var graphic in graphics)
            {
                if (graphic != null) Set(graphic.GetComponent<CanvasRenderer>(), originals[graphic], 1f);
                originals.Remove(graphic);
            }
            graphics.Clear();
        }

        public void Restore()
        {
            foreach (var pair in originals)
                if (pair.Key != null) Set(pair.Key.GetComponent<CanvasRenderer>(), pair.Value, 1f);
            originals.Clear();
        }

        private static void Set(CanvasRenderer renderer, Color baseColor, float factor)
        {
            if (renderer == null) return;
            Color current = renderer.GetColor();
            Color color = new Color(baseColor.r * factor, baseColor.g * factor, baseColor.b * factor, current.a);
            if (current != color) renderer.SetColor(color);
        }
    }

    public void ReloadAnimationParameters()
    {
        var table = BattlefieldAnimationParameters.Load();
        _inSeconds = table.Get("Selection.DimmerFadeInDuration", .15f);
        _outSeconds = table.Get("Selection.DimmerFadeOutDuration", .15f);
        _targetSeconds = table.Get("Selection.TargetTransitionDuration", .15f);
    }

    private void Start()
    {
        ReloadAnimationParameters();
        _input = GetComponent<BattleInputController>() ?? FindObjectOfType<BattleInputController>();
        _switch = GetComponent<BattlefieldSwitchController>() ?? FindObjectOfType<BattlefieldSwitchController>();
        _battle = GetComponent<BattleUiCombatHudSource>()?.battle ?? GetComponent<BattleManager>();
        var layout = _input != null ? _input.selectionLayout : null;
        var bindings = layout != null && layout.canvas != null
            ? layout.canvas.GetComponentsInChildren<PsdUiBinding>(true) : FindObjectsOfType<PsdUiBinding>(true);
        var binding = bindings.FirstOrDefault(b => b.bindingKey == "ScreenDimmer");
        if (binding != null) _dimmer = binding.GetComponent<Image>() ?? binding.GetComponentInChildren<Image>(true);
        if (_dimmer != null) { _dimmerAlpha = _dimmer.color.a; _dimmer.enabled = false; }
        var canvas = layout != null && layout.canvas != null ? layout.canvas : binding?.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        AddSide(BattleSide.Ally, canvas.transform.Find("character"), "CharacterCard", canvas.transform.Find("角色栏"), "角色", layout);
        AddSide(BattleSide.Enemy, canvas.transform.Find("Enemy"), "EnemyCard", canvas.transform.Find("敌人栏"), "敌人", layout);
        var actions = GetComponent<BattlefieldActionStateView>();
        if (actions != null)
            foreach (var action in actions.actions)
                if (action?.Root != null && action.slot == 0 && action.skillType >= 0 && action.skillType < 3)
                    _skills.Add(new SkillButton { Root = action.Root.transform, Skill = action.skillType });
    }

    private void AddSide(BattleSide side, Transform cardRoot, string cardPrefix, Transform infoRoot, string infoPrefix, BattleUiLayout layout)
    {
        var cards = layout != null ? layout.Cards.Where(c => c != null && c.side == side && c.root != null)
            .Select(c => (card: (Transform)c.root, position: c.slot)).ToList()
            : OrderedGroups(cardRoot, cardPrefix).Select((card, i) => (card, position: i + 1)).ToList();
        var infos = OrderedGroups(infoRoot, infoPrefix);
        foreach (var (card, position) in cards)
        {
            var glow = card.GetComponentsInChildren<PsdUiBinding>(true).FirstOrDefault(b => b.bindingKey == "CardOverGlow");
            var border = glow != null ? glow.GetComponent<Image>() : null;
            if (border != null) border.enabled = false;
            _entities.Add(new EntityVisual { Side = side, Position = position, Card = card,
                Info = position > 0 && position <= infos.Count ? infos[position - 1] : null, Border = border,
                BorderIndex = border != null ? border.transform.GetSiblingIndex() : 0,
                BorderAlpha = border != null ? border.color.a : 0 });
        }
    }

    private static float Step(float value, bool on, float seconds, float dt) =>
        seconds <= 0 ? (on ? 1f : 0f) : Mathf.MoveTowards(value, on ? 1f : 0f, dt / seconds);

    private void LateUpdate()
    {
        bool swap = _switch != null && _switch.IsSwitching;
        bool select = _input != null && _input.IsSelectionVisible;
        bool active = swap || select;
        var caster = select && !swap && _input.Selector.TargetsField ? _input.PendingAlly?.Entity : null;
        float dt = Time.unscaledDeltaTime;
        float outSeconds = _timelineExit != null ? _timelineExitSeconds : _outSeconds;
        _phase = Step(_phase, active, active ? _inSeconds : outSeconds, dt);
        float darkness = Mathf.SmoothStep(0, _dimmerAlpha, _phase);
        if (_dimmer != null)
        {
            var dimColor = _dimmer.color; dimColor.a = darkness;
            _dimmer.color = dimColor; _dimmer.enabled = darkness > 0;
        }
        foreach (var button in _skills)
        {
            bool dim = select && !_input.CanAdvancePendingSkill(_battle, button.Skill);
            float duration = dim ? _inSeconds : select ? _targetSeconds : outSeconds;
            button.Dim = Step(button.Dim, dim, duration, dt);
            button.Tint.Apply(button.Root, 1 - _dimmerAlpha * Mathf.SmoothStep(0, 1, button.Dim));
        }
        bool hasGlow = false;
        foreach (var entity in _entities)
        {
            var ally = entity.Side == BattleSide.Ally && _battle != null ? _battle.GetAllyBySlot(entity.Position - 1) : null;
            bool reserve = ally?.Entity?.IsOnField == true && !ally.IsActive;
            float cardBrightness = reserve ? ReserveBrightness : 1f;
            if (!active && !_presenting)
            {
                entity.CardTint.Apply(entity.Card, cardBrightness, entity.Border);
                continue;
            }
            bool target = swap ? entity.Side == BattleSide.Ally && entity.Position == _switch.SelectedSlot + 1 :
                select && _input.Selector.Side == entity.Side && _input.Selector.ShowsPosition(entity.Position);
            bool focus = (target && (swap || _input.Selector.HasOccupant(entity.Position))) ||
                (caster != null && entity.Side == BattleSide.Ally && entity.Position == caster.SlotPosition);
            if (!_presenting)
            {
                entity.Focus = focus ? 1f : 0f;
            }
            // On cancel, keep the current focus while the dimmer fades back to normal.
            if (active) entity.Focus = Step(entity.Focus, focus, _targetSeconds, dt);
            entity.Glow = Step(entity.Glow, target, active ? _targetSeconds : outSeconds, dt);
            if (!swap && select && !_input.Selector.TargetsField && _input.Selector.Side == entity.Side &&
                !_input.Selector.HasOccupant(entity.Position)) entity.Glow = 0;
            SetBorder(entity, Mathf.SmoothStep(0, 1, entity.Glow));
            hasGlow |= entity.Glow > 0;
            float selectionBrightness = 1 - darkness * (1 - Mathf.SmoothStep(0, 1, entity.Focus));
            entity.CardTint.Apply(entity.Card, cardBrightness * selectionBrightness, entity.Border);
            entity.InfoTint.Apply(entity.Info, selectionBrightness);
        }
        _presenting = active || _phase > 0 || hasGlow;
        if (!_presenting && _skills.All(button => button.Dim == 0)) CompleteTimelineExit();
    }

    public async Task WaitForSelectionExit(float seconds, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (_input?.IsSelectionVisible == true || (_switch != null && _switch.IsSwitching)) return;
        if (!isActiveAndEnabled || (!_presenting && _phase == 0 && _skills.All(button => button.Dim == 0))) return;
        _timelineExitSeconds = seconds;
        var completion = _timelineExit ??= new TaskCompletionSource<bool>();
        using (cancellation.Register(() => completion.TrySetCanceled()))
            await completion.Task;
    }

    // Called after visuals have restored, or when the owning playback is skipped/reset.
    private void CompleteTimelineExit()
    {
        var completion = _timelineExit;
        _timelineExit = null;
        completion?.TrySetResult(true);
    }

    public void SkipSelectionExit() => Clear();

    private static void SetBorder(EntityVisual entity, float alpha)
    {
        var image = entity.Border;
        if (image == null || entity.Card == null) return;
        // Unoccupied slots can have an inactive card root. Expose only its hollow border.
        bool detached = alpha > 0 && !entity.Card.gameObject.activeInHierarchy;
        if (detached && image.transform.parent == entity.Card)
        {
            image.transform.SetParent(entity.Card.parent, true);
            image.transform.SetSiblingIndex(entity.Card.GetSiblingIndex());
        }
        else if (!detached && image.transform.parent != entity.Card)
        {
            image.transform.SetParent(entity.Card, true);
            image.transform.SetSiblingIndex(entity.BorderIndex);
        }
        var color = image.color; color.a = entity.BorderAlpha * alpha; image.color = color;
        image.enabled = alpha > 0 && image.sprite != null;
    }

    private void Clear()
    {
        foreach (var entity in _entities)
        {
            entity.CardTint.Restore(); entity.InfoTint.Restore();
            entity.Glow = entity.Focus = 0; SetBorder(entity, 0);
        }
        foreach (var button in _skills) { button.Tint.Restore(); button.Dim = 0; }
        _phase = 0; _presenting = false;
        if (_dimmer != null) _dimmer.enabled = false;
        CompleteTimelineExit();
    }
    private void OnDisable() => Clear();

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
