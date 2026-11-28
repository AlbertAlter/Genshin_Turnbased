using UnityEngine;
using TMPro;
using System.Linq;

/// <summary>Explicit default modules for initial battle scene construction. Never used by daily UI sync.</summary>
public static class BattleUiSceneModules
{
    public static void Configure(GameObject runtime, BattleUiLayout layout)
    {
        T Module<T>() where T : Component => runtime.GetComponent<T>() ?? runtime.AddComponent<T>();
        var player = Module<BattlefieldPresentationPlayer>();
        var flow = Module<BattleUiFlowPresentation>();
        flow.source = runtime.GetComponent<BattleUiCombatHudSource>();
        flow.input = runtime.GetComponent<BattleInputController>(); flow.mount = (RectTransform)layout.canvas.transform;
        flow.visualRoots = new[] { runtime.transform, layout.canvas.transform };
        flow.viewPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/BattleFlowDisplay.prefab")?.GetComponent<UIBattleFlowView>();
        flow.resultPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/RewardPopup.prefab")?.GetComponent<UIRewardPopup>();
        var sceneRuntime = runtime.GetComponent<BattleSceneRuntime>();
        if (sceneRuntime != null) sceneRuntime.flowPresentation = flow;
        var tips = Module<BattleUiFloatingTips>();
        tips.battle = runtime.GetComponent<BattleManager>(); tips.canvas = layout.canvas;
        tips.font = ProjectUiFont.LoadOrCreate();
        var bottom = Module<BattleUiCardBottomStatus>(); bottom.battle = tips.battle;
        bottom.input = runtime.GetComponent<BattleInputController>();
        bottom.layout = layout; bottom.displayMount = (RectTransform)layout.canvas.transform; bottom.font = ProjectUiFont.LoadOrCreateOutlined();
        player.selectionPresentation = runtime.GetComponent<BattlefieldSelectionPresentation>();
        var auraSource = Module<BattleUiCombatAuraSource>();
        var auras = Module<BattlefieldElementAuraController>();
        auras.player = player; auras.layout = layout; auras.dataSource = auraSource;
        auras.Bind(layout, auraSource);
        var prompt = Module<BattleActionPromptView>();
        prompt.player = player; prompt.layout = layout; prompt.font = ProjectUiFont.LoadOrCreateOutlined();
        prompt.selectionInput = runtime.GetComponent<BattleInputController>();
        prompt.framePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/AdaptiveCharacterFrame.prefab").GetComponent<UIAdaptiveFrame>();
        var pause = Module<BattleUiPauseMenu>(); pause.battle = tips.battle; pause.input = bottom.input;
        pause.switcher = runtime.GetComponent<BattlefieldSwitchController>(); pause.mount = (RectTransform)layout.canvas.transform;
        pause.font = ProjectUiFont.LoadOrCreate(); pause.framePrefab = prompt.framePrefab;
        pause.settingsPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/ProjectSettingsPage.prefab")?.GetComponent<ProjectSettingsView>();
        var rise = Module<BattleUiStatusRiseView>(); rise.player = player; rise.layout = layout; rise.battle = runtime.GetComponent<BattleManager>();
        rise.arrowPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/StatusArrows.prefab").GetComponent<UICardOrbitArrows>();
        var voice = Module<CharacterBattleVoice>();
        voice.player = player; voice.battle = runtime.GetComponent<BattleManager>();
        var popups = Module<BattlefieldDamagePopupController>(); popups.layout = layout;
        var healing = Module<BattleUiHealEffects>(); healing.player = player; healing.layout = layout;
        if (popups.numberFont == null) popups.numberFont = ProjectUiFont.LoadOrCreate();
        if (popups.reactionFont == null) popups.reactionFont = ProjectUiFont.LoadOrCreate();
        var statusNames = Module<UIStatusNamePopup>(); statusNames.canvas = layout.canvas;
        statusNames.font = ProjectUiFont.LoadOrCreate(); popups.statusNames = statusNames;
        Module<BattlefieldAttackAnimationView>().layout = layout;
        var effects = Module<BattleUiHitEffects>(); effects.layout = layout;
        var hits = Module<BattleUiHitResultView>(); hits.player = player; hits.layout = layout; hits.effects = effects;
        var native = Module<BattleUiNativeEffects>(); native.player = player; native.layout = layout; native.battle = runtime.GetComponent<BattleManager>();
        var cards = Module<BattleUiAnimatedCards>(); cards.layout = layout; cards.battle = runtime.GetComponent<BattleManager>();
        healing.cards = cards;
        cards.factory = Module<UICardFactory>();
        var cardFace = Module<BattleUiCardFaceStatus>();
        cardFace.layout = layout; cardFace.battle = cards.battle; cardFace.targetedAssets = Module<UICardTargetAssets>();
        cardFace.talismanAssets = Module<UICardConductiveAssets>();
        cardFace.talismanAssets.preset = UICardConductiveAssets.GlyphPreset.QiqiTalisman;
        cardFace.displayMount = layout.canvas.transform as RectTransform;
        var shields = Module<BattleUiShields>(); shields.layout = layout;
        cardFace.membraneAssets = shields;
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
        Module<BattleUiCharacterAppearance>().cards = cards;
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
        var skillInfo = Module<BattleUiSkillInfo>();
        skillInfo.Bind(buttons, buttons.input, runtime.GetComponent<BattleManager>(), layout.canvas,
            UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/BattleSkillInfo.prefab")
                ?.GetComponent<BattleSkillInfoView>());
        var controls = layout.canvas.GetComponentsInChildren<PsdUiBinding>(true);
        ConfigureCharacterDetails(runtime, layout, cards.factory, prompt.framePrefab);
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
        ConfigureShortcutLabels(buttons);
    }

    // 首次装配时确定原有键帽文字引用，运行时不按节点名称或初始字母猜测绑定。
    private static void ConfigureShortcutLabels(BattleUiActionButtons buttons)
    {
        var bindings = new System.Collections.Generic.List<BattleUiActionButtons.ShortcutVisual>();
        void Add(Transform root, BattleShortcut shortcut, TMP_Text excluded = null)
        {
            if (root == null) return;
            var text = root.GetComponentsInChildren<TMPro.TMP_Text>(true).FirstOrDefault(t => t != excluded);
            if (text != null) bindings.Add(new BattleUiActionButtons.ShortcutVisual { text = text, shortcut = shortcut });
        }
        foreach (var action in buttons.view.actions)
        {
            if (action?.Icon == null) continue;
            var root = action.Root != null ? action.Root.transform : action.Icon.transform.parent;
            var shortcut = action.slot > 0 ? (BattleShortcut)((int)BattleShortcut.Burst1 + action.slot - 1) : (BattleShortcut)action.skillType;
            Add(root, shortcut, action.Cooldown);
        }
        Add(buttons.switchRoot, BattleShortcut.Switch);
        Add(buttons.cancelRoot, BattleShortcut.Cancel);
        // 结束回合Prefab自己的键帽已绑定公共组件，避免重复管理同一个文字。
        if (buttons.endTurnRoot.GetComponentInChildren<BattleShortcutLabel>(true) == null)
            Add(buttons.endTurnRoot, BattleShortcut.Confirm);
        buttons.shortcutVisuals = bindings.ToArray();
    }

    private static void ConfigureCharacterDetails(GameObject runtime, BattleUiLayout layout, UICardFactory cards, UIAdaptiveFrame frame)
    {
        var details = runtime.GetComponent<BattleUiCharacterDetails>() ?? runtime.AddComponent<BattleUiCharacterDetails>();
        var serialized = new UnityEditor.SerializedObject(details);
        void Bind(string field, Object value) => serialized.FindProperty(field).objectReferenceValue = value;
        Bind("canvas", layout.canvas);
        Bind("battle", runtime.GetComponent<BattleManager>());
        Bind("input", runtime.GetComponent<BattleInputController>());
        Bind("switcher", runtime.GetComponent<BattlefieldSwitchController>());
        Bind("cardFactory", cards);
        Bind("framePrefab", frame);
        Bind("font", ProjectUiFont.LoadOrCreate());
        var slots = serialized.FindProperty("portraits");
        slots.arraySize = layout.cards.Where(card => card != null && card.side == BattleSide.Ally).Select(card => card.slot).DefaultIfEmpty(0).Max();
        for (int index = 0; index < slots.arraySize; index++) slots.GetArrayElementAtIndex(index).objectReferenceValue = null;
        var roster = runtime.GetComponent<BattlefieldStageViewLoader>()?.characterRoster;
        if (roster != null)
            foreach (Transform group in roster)
            {
                var health = group.GetComponentsInChildren<BattlefieldHealthBar>(true).FirstOrDefault(bar => bar.side == BattleSide.Ally);
                var portrait = group.GetComponentsInChildren<PsdUiBinding>(true).FirstOrDefault(binding => binding.bindingKey == "CharacterAvatar");
                if (health != null && portrait != null && health.slot > 0 && health.slot <= slots.arraySize)
                    slots.GetArrayElementAtIndex(health.slot - 1).objectReferenceValue = portrait.transform;
            }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var entry = runtime.GetComponent<BattleSceneRuntime>();
        if (entry != null) entry.characterDetails = details;
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
