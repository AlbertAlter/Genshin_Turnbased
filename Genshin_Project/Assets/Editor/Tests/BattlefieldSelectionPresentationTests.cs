using System;
using System.Collections.Generic;
using System.Reflection;
using GenshinTurnBased.Tests.EditMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class BattlefieldSelectionPresentationTests
{
    private readonly List<GameObject> _roots = new List<GameObject>();
    private EditorSceneTestScope _scenes;

    [SetUp]
    public void SetUp()
    {
        _scenes = new EditorSceneTestScope();
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            for (int index = _roots.Count - 1; index >= 0; index--)
                if (_roots[index] != null) UnityEngine.Object.DestroyImmediate(_roots[index]);
        }
        finally
        {
            _roots.Clear();
            _scenes?.Dispose();
            _scenes = null;
        }
    }

    [Test]
    public void ReserveShadeSurvivesFocusCancelAndSwitch_SkillBrightnessFollowsNextPhase()
    {
        var canvas = NewRoot("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        var dimmer = NewChild(canvas.transform, "Dimmer", typeof(Image), typeof(PsdUiBinding));
        dimmer.GetComponent<PsdUiBinding>().bindingKey = "ScreenDimmer";
        dimmer.GetComponent<Image>().color = new Color(0, 0, 0, .5f);
        var runtime = NewRoot("Battle");
        var battle = runtime.AddComponent<BattleManager>();
        var input = runtime.AddComponent<BattleInputController>();
        var switcher = runtime.AddComponent<BattlefieldSwitchController>();
        var layout = input.selectionLayout = runtime.AddComponent<BattleUiLayout>();
        layout.canvas = canvas;
        var allies = new CharacterBattleController[2];
        var cards = new Image[2];
        layout.cards = new BattleUiCardBinding[2];
        for (int i = 0; i < allies.Length; i++)
        {
            var unit = NewRoot("Unit" + i);
            var ally = allies[i] = unit.AddComponent<CharacterBattleController>();
            ally.Entity = unit.AddComponent<BattleEntity>();
            ally.Entity.CurrentHP = 100;
            ally.Entity.SlotPosition = i + 1;
            ally.IsActive = i == 0;
            battle.RegisterAlly(ally);
            var card = cards[i] = NewChild(canvas.transform, "Card" + i, typeof(Image)).GetComponent<Image>();
            layout.cards[i] = new BattleUiCardBinding { side = BattleSide.Ally, slot = i + 1, root = card.rectTransform, art = card.rectTransform };
        }

        var actions = runtime.AddComponent<BattlefieldActionStateView>();
        actions.actions = new BattlefieldActionStateView.ActionVisual[3];
        for (int i = 0; i < actions.actions.Length; i++)
            actions.actions[i] = new BattlefieldActionStateView.ActionVisual
            {
                Root = NewChild(canvas.transform, "Key" + i, typeof(Image)), skillType = i
            };
        var first = new SkillMainData { SkillID = 1, SkillID2 = "First", SkillPhase = 1 };
        var second = new SkillMainData { SkillID = 2, SkillID2 = "Second", SkillPhase = 1 };
        SetPrivateField(allies[0], "_boundSkills", new[] { null, first.SkillID2, null, null });
        SetPrivateField(allies[0], "_skillById2", new Dictionary<string, SkillMainData> { { "First", first }, { "Second", second } });
        SetPrivateField(battle, "<CurrentPhase>k__BackingField", TurnPhase.AllyAction);
        var view = runtime.AddComponent<BattlefieldSelectionPresentation>();
        InvokeLifecycle(view, "Start");
        foreach (string key in new[] { "_inSeconds", "_outSeconds", "_targetSeconds" }) SetPrivateField(view, key, 0f);
        void Refresh() => InvokeLifecycle(view, "LateUpdate");
        void Expect(Graphic graphic, float brightness) =>
            Assert.That(graphic.canvasRenderer.GetColor().r, Is.EqualTo(brightness).Within(.005f), graphic.name);
        void Keys(float q, float w, float e)
        {
            Expect(actions.actions[0].Root.GetComponent<Image>(), q);
            Expect(actions.actions[1].Root.GetComponent<Image>(), w);
            Expect(actions.actions[2].Root.GetComponent<Image>(), e);
        }

        const float reserve = 217f / 255f;
        Refresh(); Expect(cards[0], 1); Expect(cards[1], reserve); Keys(1, 1, 1);
        SetPrivateField(input, "_pendingSkill", 1);
        SetPrivateField(input, "_pendingAlly", allies[0]);
        SetPrivateField(input, "_selecting", true);
        SetPrivateField(input.Selector, "_aliveProvider", (Func<int, bool>)(pos => true));
        input.Selector.Side = BattleSide.Ally;
        input.Selector.CurrentSelection = new List<int> { 1 };
        Refresh(); Expect(cards[0], 1); Expect(cards[1], reserve * .5f); Keys(.5f, 1, .5f);
        Assert.That(allies[0].GetBoundSkillID(1), Is.EqualTo("First"));
        input.Selector.CurrentSelection = new List<int> { 2 };
        var aura = NewChild(cards[1].transform, "Aura", typeof(Image)).GetComponent<Image>();
        Refresh(); Expect(cards[0], .5f); Expect(cards[1], reserve); Expect(aura, reserve);
        Assert.That(cards[1].transform.parent, Is.EqualTo(canvas.transform));
        allies[0].AdvanceSkillPhase(1);
        Refresh(); Keys(.5f, .5f, .5f);
        SetPrivateField(input, "_waitingForResolution", true);
        var exit = view.WaitForSelectionExit(0, System.Threading.CancellationToken.None);
        Assert.That(exit.IsCompleted, Is.False, "正式时间轴须等待选择画面实际复原");
        Assert.That(input.IsSelecting, Is.True, "退出显示不撤销已确认的选择数据");
        Assert.That(input.IsSelectionVisible, Is.False);
        Refresh();
        Assert.That(exit.IsCompleted, Is.True);
        Expect(cards[0], 1); Expect(cards[1], reserve); Keys(1, 1, 1);
        SetPrivateField(input, "_waitingForResolution", false);
        input.UICancelSelection();
        Refresh(); Expect(cards[1], reserve); Keys(1, 1, 1);

        SetPrivateField(switcher, "_selectedSlot", 1);
        SetPrivateField(switcher, "_switching", true);
        Refresh(); Expect(cards[0], .5f); Expect(cards[1], reserve);
        SetPrivateField(switcher, "_switching", false);
        allies[0].IsActive = false; allies[1].IsActive = true;
        Refresh(); Expect(cards[0], reserve); Expect(cards[1], 1); Expect(aura, 1);
        view.enabled = false;
        Expect(cards[0], 1);
    }

    [Test]
    public void GeneratedScene_HasPresenterAndTargetGlowsDefaultOff()
    {
        Scene scene = _scenes.OpenPreview("Assets/Scene/BattleField1.unity");

        Assert.That(EditorSceneTestScope.GetComponents<BattlefieldSelectionPresentation>(scene), Is.Not.Empty);
        Assert.That(EditorSceneTestScope.GetComponents<BattlefieldSwitchController>(scene), Is.Not.Empty);
        List<PsdUiBinding> bindings = EditorSceneTestScope.GetComponents<PsdUiBinding>(scene);
        var glows = new List<PsdUiBinding>();
        PsdUiBinding dimmerBinding = null;
        foreach (PsdUiBinding binding in bindings)
        {
            if (binding.bindingKey == "CardOverGlow") glows.Add(binding);
            if (binding.bindingKey == "ScreenDimmer") dimmerBinding = binding;
        }

        Assert.That(glows, Has.Count.EqualTo(9));
        foreach (PsdUiBinding glowBinding in glows)
        {
            Image glow = glowBinding.GetComponent<Image>() ?? glowBinding.GetComponentInChildren<Image>(true);
            StreamingSpriteReference reference = glowBinding.GetComponent<StreamingSpriteReference>() ??
                                                 glowBinding.GetComponentInChildren<StreamingSpriteReference>(true);
            Assert.That(glow, Is.Not.Null);
            Assert.That(glow.enabled, Is.False);
            Assert.That(reference, Is.Not.Null);
            Assert.That(reference.revealOnLoad, Is.False);
        }

        Assert.That(dimmerBinding, Is.Not.Null);
        Assert.That(dimmerBinding.GetComponent<Image>().enabled, Is.False);
    }

    private GameObject NewRoot(string name, params Type[] components)
    {
        var root = new GameObject(name, components);
        if (root.scene != _scenes.TestScene) SceneManager.MoveGameObjectToScene(root, _scenes.TestScene);
        _roots.Add(root);
        return root;
    }

    [Test]
    public void SceneScope_KeepsActiveSceneAndClosesOnlyItsOwnPreviewScene()
    {
        Scene original = SceneManager.GetActiveScene();
        Scene outer = _scenes.TestScene;
        Scene nested;
        GameObject nestedRoot;
        using (var scope = new EditorSceneTestScope())
        {
            nested = scope.TestScene;
            Assert.That(nested, Is.Not.EqualTo(original));
            Assert.That(nested, Is.Not.EqualTo(outer));
            Assert.That(UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(nested), Is.True);
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(original), "预览隔离不改变原活动场景");
            Assert.That(original.isLoaded, Is.True);
            nestedRoot = new GameObject("NestedSceneOwnedRoot");
            _roots.Add(nestedRoot);
            SceneManager.MoveGameObjectToScene(nestedRoot, nested);
        }
        Assert.That(original.isLoaded, Is.True);
        Assert.That(outer.IsValid() && outer.isLoaded, Is.True, "关闭内层不能关闭外层测试场景");
        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(original));
        Assert.That(nested.IsValid() && nested.isLoaded, Is.False);
        Assert.That(nestedRoot == null, Is.True, "内层场景拥有的对象随场景清理");
    }

    private static GameObject NewChild(Transform parent, string name, params Type[] components)
    {
        var requested = new List<Type> { typeof(RectTransform) };
        foreach (Type component in components)
            if (component != typeof(RectTransform)) requested.Add(component);
        var child = new GameObject(name, requested.ToArray());
        if (child.scene != parent.gameObject.scene)
            SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene);
        child.transform.SetParent(parent, false);
        return child;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }

    private static void InvokeLifecycle(BattlefieldSelectionPresentation presentation, string methodName)
    {
        MethodInfo method = typeof(BattlefieldSelectionPresentation).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(presentation, null);
    }
}
