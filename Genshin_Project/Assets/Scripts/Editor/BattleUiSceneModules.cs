using UnityEngine;
using System.Linq;

/// <summary>Explicit default modules for initial battle scene construction. Never used by daily UI sync.</summary>
public static class BattleUiSceneModules
{
    public static void Configure(GameObject runtime, BattleUiLayout layout)
    {
        T Module<T>() where T : Component => runtime.GetComponent<T>() ?? runtime.AddComponent<T>();
        var player = Module<BattlefieldPresentationPlayer>();
        player.selectionPresentation = runtime.GetComponent<BattlefieldSelectionPresentation>();
        var prompt = Module<BattleActionPromptView>();
        prompt.player = player; prompt.layout = layout; prompt.font = ProjectUiFont.LoadOrCreateOutlined();
        prompt.framePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/AdaptiveCharacterFrame.prefab").GetComponent<UIAdaptiveFrame>();
        var rise = Module<BattleUiStatusRiseView>(); rise.player = player; rise.layout = layout; rise.battle = runtime.GetComponent<BattleManager>();
        var voice = Module<CharacterBattleVoice>();
        voice.player = player; voice.battle = runtime.GetComponent<BattleManager>();
        var popups = Module<BattlefieldDamagePopupController>(); popups.layout = layout;
        if (popups.numberFont == null) popups.numberFont = ProjectUiFont.LoadOrCreate();
        Module<BattlefieldHitShakeView>().layout = layout;
        var effects = Module<BattleUiHitEffects>(); effects.layout = layout;
        var hits = Module<BattleUiHitResultView>(); hits.player = player; hits.layout = layout; hits.effects = effects;
        var cards = Module<BattleUiAnimatedCards>(); cards.layout = layout; cards.battle = runtime.GetComponent<BattleManager>();
        cards.factory = Module<UICardFactory>();
        var shields = Module<BattleUiShields>(); shields.layout = layout;
        var personal = Module<BattleUiPersonalShieldSource>(); personal.battle = cards.battle; personal.view = shields;
        var overall = Module<BattleUiOverallShields>(); overall.membraneStyle = shields;
        var overallSource = Module<BattleUiOverallShieldSource>();
        overallSource.battle = cards.battle; overallSource.layout = layout; overallSource.view = overall;
        var frozen = Module<BattleUiFrozenCards>();
        frozen.cards = cards; frozen.input = runtime.GetComponent<BattleInputController>();
        frozen.resources = Module<UICardFreeze>(); frozen.resources.loadOnStart = true;
        frozen.shake = Module<UIHitShakeController>();
        frozen.cardPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/CardFreeze.prefab")
            ?.GetComponent<BattleUiFreezeInteraction>();
        ConfigureAudio(runtime);
        Module<BattleUiEnemyAppearance>().cards = cards;
        cards.legacySkinObjects = layout.cards.Where(c => c?.root != null)
            .SelectMany(c => c.root.GetComponentsInChildren<StreamingSpriteReference>(true))
            .Where(a => a.relativePath == UICardFactory.FramePath || a.relativePath == UICardFactory.InnerShadowPath ||
                a.relativePath == UICardFactory.OuterShadowPath)
            .Select(a => a.gameObject).Distinct().ToArray();
        var buttons = Module<BattleUiActionButtons>();
        buttons.view = runtime.GetComponent<BattlefieldActionStateView>();
        buttons.input = runtime.GetComponent<BattleInputController>();
        buttons.switcher = runtime.GetComponent<BattlefieldSwitchController>();
        buttons.canvas = layout.canvas; buttons.projectFrame = prompt.framePrefab;
        buttons.hintFont = ProjectUiFont.LoadOrCreate();
        var controls = layout.canvas.GetComponentsInChildren<PsdUiBinding>(true);
        buttons.switchRoot = controls.FirstOrDefault(b => b.bindingKey == "SwitchIcon")?.transform.parent as RectTransform;
        buttons.cancelRoot = controls.FirstOrDefault(b => b.bindingKey == "CancelIcon")?.transform.parent as RectTransform;
        var endPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/BattleEndTurnButton.prefab");
        if (buttons.endTurnRoot == null)
            buttons.endTurnRoot = (RectTransform)((GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(endPrefab, layout.canvas.transform)).transform;
        RectTransform Disc(RectTransform root) => root?.GetComponentsInChildren<PsdUiBinding>(true)
            .FirstOrDefault(b => b.bindingKey == "DeepCircle")?.transform as RectTransform;
        buttons.switchDisc = Disc(buttons.switchRoot);
        buttons.cancelDisc = Disc(buttons.cancelRoot);
        buttons.endTurnDisc = buttons.endTurnRoot.Find("OuterStroke") as RectTransform;
    }

    private static void ConfigureAudio(GameObject runtime)
    {
        // Initial assembly only. Keep the persistent audio engine separate from scene-owned battle objects.
        var roots = runtime.scene.GetRootGameObjects();
        if (!roots.SelectMany(root => root.GetComponentsInChildren<AkInitializer>(true)).Any())
        {
            var global = new GameObject("WwiseGlobal");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(global, runtime.scene);
            global.AddComponent<AkInitializer>().InitializationSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<AkWwiseInitializationSettings>(
                "Assets/Wwise/ScriptableObjects/AkWwiseInitializationSettings.asset");
        }
        if (!roots.SelectMany(root => root.GetComponentsInChildren<AkAudioListener>(true)).Any())
        {
            var emitter = runtime.GetComponent<AkGameObj>() ?? runtime.AddComponent<AkGameObj>();
            emitter.isEnvironmentAware = false;
            (runtime.GetComponent<AkAudioListener>() ?? runtime.AddComponent<AkAudioListener>()).isDefaultListener = true;
        }
    }
}
