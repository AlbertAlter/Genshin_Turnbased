using UnityEngine;
using System.Linq;

/// <summary>Explicit default modules for initial battle scene construction. Never used by daily UI sync.</summary>
public static class BattleUiSceneModules
{
    public static void Configure(GameObject runtime, BattleUiLayout layout)
    {
        T Module<T>() where T : Component => runtime.GetComponent<T>() ?? runtime.AddComponent<T>();
        var player = Module<BattlefieldPresentationPlayer>();
        var popups = Module<BattlefieldDamagePopupController>(); popups.layout = layout;
        if (popups.numberFont == null) popups.numberFont = ProjectUiFont.LoadOrCreate();
        Module<BattlefieldHitShakeView>().layout = layout;
        var effects = Module<BattleUiHitEffects>(); effects.layout = layout;
        var hits = Module<BattleUiHitResultView>(); hits.player = player; hits.layout = layout; hits.effects = effects;
        var cards = Module<BattleUiAnimatedCards>(); cards.layout = layout; cards.battle = runtime.GetComponent<BattleManager>();
        cards.factory = Module<UICardFactory>();
        Module<BattleUiEnemyAppearance>().cards = cards;
        cards.legacySkinObjects = layout.cards.Where(c => c?.root != null)
            .SelectMany(c => c.root.GetComponentsInChildren<StreamingSpriteReference>(true))
            .Where(a => a.relativePath == UICardFactory.FramePath || a.relativePath == UICardFactory.InnerShadowPath ||
                a.relativePath == UICardFactory.OuterShadowPath)
            .Select(a => a.gameObject).Distinct().ToArray();
        var buttons = Module<BattleUiActionButtons>();
        buttons.view = runtime.GetComponent<BattlefieldActionStateView>();
        buttons.input = runtime.GetComponent<BattleInputController>();
    }
}
