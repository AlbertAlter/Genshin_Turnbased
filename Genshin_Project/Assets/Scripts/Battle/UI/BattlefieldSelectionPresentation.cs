using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Selection dims cards in place. Scene sibling order owns overlap in every input mode.</summary>
public sealed class BattlefieldSelectionPresentation : MonoBehaviour
{
    private BattleInputController _input;
    private BattlefieldSwitchController _switch;
    private Image _dimmer;
    private float _inSeconds, _outSeconds, _targetSeconds, _phase, _dimmerAlpha;
    private bool _presenting;
    private readonly List<EntityVisual> _entities = new List<EntityVisual>();

    private sealed class EntityVisual
    {
        public BattleSide Side;
        public int Position;
        public Transform Card, Info;
        public Image Border;
        public int BorderIndex;
        public float BorderAlpha, Glow, Focus;
        public readonly Dictionary<CanvasRenderer, Color> Colors = new Dictionary<CanvasRenderer, Color>();
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
        var binding = FindObjectsOfType<PsdUiBinding>(true).FirstOrDefault(b => b.bindingKey == "ScreenDimmer");
        if (binding == null) return;
        _dimmer = binding.GetComponent<Image>() ?? binding.GetComponentInChildren<Image>(true);
        if (_dimmer == null) return;
        _dimmerAlpha = _dimmer.color.a;
        _dimmer.enabled = false;
        var canvas = binding.GetComponentInParent<Canvas>().transform;
        AddSide(BattleSide.Ally, canvas.Find("character"), "CharacterCard", canvas.Find("角色栏"), "角色");
        AddSide(BattleSide.Enemy, canvas.Find("Enemy"), "EnemyCard", canvas.Find("敌人栏"), "敌人");
        _input = GetComponent<BattleInputController>() ?? FindObjectOfType<BattleInputController>();
        _switch = GetComponent<BattlefieldSwitchController>() ?? FindObjectOfType<BattlefieldSwitchController>();
    }

    private void AddSide(BattleSide side, Transform cardRoot, string cardPrefix, Transform infoRoot, string infoPrefix)
    {
        var cards = OrderedGroups(cardRoot, cardPrefix);
        var infos = OrderedGroups(infoRoot, infoPrefix);
        for (int i = 0; i < cards.Count; ++i)
        {
            var card = cards[i];
            var glow = card.GetComponentsInChildren<PsdUiBinding>(true).FirstOrDefault(b => b.bindingKey == "CardOverGlow");
            var border = glow != null ? glow.GetComponent<Image>() : null;
            if (border != null) border.enabled = false;
            _entities.Add(new EntityVisual { Side = side, Position = i + 1, Card = card,
                Info = i < infos.Count ? infos[i] : null, Border = border,
                BorderIndex = border != null ? border.transform.GetSiblingIndex() : 0,
                BorderAlpha = border != null ? border.color.a : 0 });
        }
    }

    private static float Step(float value, bool on, float seconds, float dt) =>
        seconds <= 0 ? (on ? 1f : 0f) : Mathf.MoveTowards(value, on ? 1f : 0f, dt / seconds);

    private void LateUpdate()
    {
        if (_dimmer == null) return;
        bool swap = _switch != null && _switch.IsSwitching;
        bool select = _input != null && _input.IsSelecting;
        bool active = swap || select;
        if (!active && !_presenting) return;
        var caster = select ? _input.PendingAlly?.Entity : null;
        float dt = Time.unscaledDeltaTime;
        _phase = Step(_phase, active, active ? _inSeconds : _outSeconds, dt);
        float darkness = Mathf.SmoothStep(0, _dimmerAlpha, _phase);
        var dimColor = _dimmer.color; dimColor.a = darkness;
        _dimmer.color = dimColor; _dimmer.enabled = darkness > 0;
        foreach (var entity in _entities)
        {
            bool target = swap ? entity.Side == BattleSide.Ally && entity.Position == _switch.SelectedSlot + 1 :
                select && _input.Selector.Side == entity.Side && _input.Selector.ShowsPosition(entity.Position);
            bool focus = (target && (swap || _input.Selector.HasOccupant(entity.Position))) ||
                (caster != null && entity.Side == BattleSide.Ally && entity.Position == caster.SlotPosition);
            if (!_presenting)
            {
                Capture(entity, entity.Card); Capture(entity, entity.Info);
                entity.Focus = focus ? 1f : 0f;
            }
            // On cancel, keep the current focus while the dimmer fades back to normal.
            if (active) entity.Focus = Step(entity.Focus, focus, _targetSeconds, dt);
            entity.Glow = Step(entity.Glow, target, active ? _targetSeconds : _outSeconds, dt);
            if (select && !_input.Selector.TargetsField && _input.Selector.Side == entity.Side &&
                !_input.Selector.HasOccupant(entity.Position)) entity.Glow = 0;
            SetBorder(entity, Mathf.SmoothStep(0, 1, entity.Glow));
            Tint(entity, 1 - darkness * (1 - Mathf.SmoothStep(0, 1, entity.Focus)));
        }
        _presenting = true;
        if (!active && _phase == 0 && _entities.All(e => e.Glow == 0)) Clear();
    }

    private static void Capture(EntityVisual entity, Transform root)
    {
        if (root == null) return;
        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
            if (graphic != entity.Border)
                entity.Colors[graphic.canvasRenderer] = graphic.canvasRenderer.GetColor();
    }

    private static void Tint(EntityVisual entity, float factor)
    {
        foreach (var pair in entity.Colors)
        {
            if (pair.Key == null) continue;
            var color = pair.Value;
            color.r *= factor; color.g *= factor; color.b *= factor;
            color.a = pair.Key.GetAlpha();
            pair.Key.SetColor(color);
        }
    }

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
            Tint(entity, 1); entity.Colors.Clear();
            entity.Glow = entity.Focus = 0; SetBorder(entity, 0);
        }
        _phase = 0; _presenting = false;
        if (_dimmer != null) _dimmer.enabled = false;
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
