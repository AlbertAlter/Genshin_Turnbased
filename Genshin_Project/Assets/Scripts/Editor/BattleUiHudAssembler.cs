using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>Explicit first-time scene assembly only. Never called by art/animation updates.</summary>
public static class BattleUiHudAssembler
{
    public static void Configure(GameObject runtime, Canvas canvas)
    {
        var source = runtime.GetComponent<BattleUiCombatHudSource>() ?? runtime.AddComponent<BattleUiCombatHudSource>();
        source.battle = runtime.GetComponent<BattleManager>() ?? runtime.AddComponent<BattleManager>();
        source.input = runtime.GetComponent<BattleInputController>() ?? runtime.AddComponent<BattleInputController>();
        source.switcher = runtime.GetComponent<BattlefieldSwitchController>();
        var loader = runtime.GetComponent<BattlefieldStageViewLoader>();
        loader.canvas = canvas;
        loader.characterCards = canvas.transform.Find("character");
        loader.characterRoster = canvas.transform.Find("角色栏");
        loader.enemyCards = canvas.transform.Find("Enemy");
        loader.enemyRoster = canvas.transform.Find("敌人栏");
        foreach (var bar in canvas.GetComponentsInChildren<BattlefieldHealthBar>(true)) bar.dataSource = source;
        var ap = runtime.GetComponent<BattlefieldApView>();
        var bindings = canvas.GetComponentsInChildren<PsdUiBinding>(true);
        TMP_Text Label(string key) => bindings.FirstOrDefault(b => b.bindingKey == key)?.GetComponent<TMP_Text>();
        if (ap != null)
        {
            ap.dataSource = source;
            ap.value = Label("APValue"); ap.loss = Label("APSpendHint"); ap.gain = Label("APGainHint");
        }
        var actions = runtime.GetComponent<BattlefieldActionStateView>();
        if (actions != null)
        {
            actions.dataSource = source;
            actions.ConfigureInitialVisuals(canvas.transform.Find("技能"), loader.characterRoster);
        }
        var layout = runtime.GetComponent<BattleUiLayout>();
        source.input.selectionLayout = layout;
        foreach (var card in layout.cards.Where(c => c.side == BattleSide.Enemy))
        {
            var group = card.root.Find("ElementalShield") as RectTransform;
            if (group != null && card.root.GetComponent<BattleUiEnemyShieldBar>() == null)
                ConfigureShield(card, group, source);
        }
    }

    private static void ConfigureShield(BattleUiCardBinding card, RectTransform group, BattleUiCombatHudSource source)
    {
        if (group.GetSiblingIndex() < card.art.GetSiblingIndex())
            group.SetSiblingIndex(card.art.GetSiblingIndex());
        foreach (Transform child in group) child.gameObject.SetActive(false);
        var model = Child(group, "ShieldAmountModel").gameObject.AddComponent<RoundedHealthFill>();
        model.enabled = false; model.raycastTarget = false;
        var stack = group.gameObject.AddComponent<PsdUiLayerStack>();
        stack.baseIndex = 1;
        string[] files = { "01_DropShadow", "02_BaseShape", "03_UpperSoftLight", "04_LowerSoftLight", "05_InnerShadow" };
        Vector4[] bounds = { new Vector4(0,0,180,22), new Vector4(3,6,172,14),
            new Vector4(8,14,163,4), new Vector4(9,5,163,4), new Vector4(4,6,172,14) };
        stack.layers = new PsdHealthLayer[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            var layer = Child(group, "PSD_Shield_" + files[i]).gameObject.AddComponent<PsdHealthLayer>();
            layer.relativePath = "art_assets/UI/BattleField1/UI_components/EnemyShield/" + files[i] + ".png";
            layer.layerBounds = bounds[i]; layer.baseBounds = bounds[1];
            layer.canvasSize = new Vector2(180,22); layer.padding = new Vector4(3,6,5,2);
            layer.caps = new Vector2(10,12); layer.blendMode = i == 1 ? 0 : i == 2 || i == 3 ? 2 : 1;
            layer.clippedToBase = i == 2 || i == 3; layer.followsHealth = true;
            layer.normalizeWithBase = i == 4;
            layer.healthSource = model; layer.vectorBase = true; layer.contentOffset = Vector2.zero;
            layer.sourceBaseExtent = new Vector4(.997366f,-.014563f,171.99998f,13.99999f);
            layer.raycastTarget = false; stack.layers[i] = layer;
        }
        var view = card.root.gameObject.AddComponent<BattleUiEnemyShieldBar>();
        view.slot = card.slot; view.dataSource = source; view.visibilityRoot = group.gameObject;
        view.fill = model; view.layerStack = stack;
        // Reload editor preview now that serialized references are complete.
        stack.enabled = false; stack.enabled = true;
    }

    private static RectTransform Child(RectTransform parent, string name)
    {
        var rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }
}
