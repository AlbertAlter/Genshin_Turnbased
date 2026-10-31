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
    public void TargetSelection_LiftsCompleteTargetAndRestoresOriginalHierarchy()
    {
        GameObject canvasObject = NewRoot("BattleField1Canvas", typeof(RectTransform), typeof(Canvas));
        Transform canvas = canvasObject.transform;
        NewChild(canvas, "Background", typeof(Image));
        GameObject dimmerObject = NewChild(canvas, "Blackfold", typeof(Image), typeof(PsdUiBinding));
        dimmerObject.GetComponent<PsdUiBinding>().bindingKey = "ScreenDimmer";
        Image dimmer = dimmerObject.GetComponent<Image>();
        dimmer.enabled = false;

        Transform allyCards = NewChild(canvas, "character").transform;
        Transform enemyCards = NewChild(canvas, "Enemy").transform;
        Transform allyInfo = NewChild(canvas, "角色栏").transform;
        Transform enemyInfo = NewChild(canvas, "敌人栏").transform;
        Transform controls = NewChild(canvas, "技能").transform;
        Transform allyCard = CreateCard(allyCards, "CharacterCard1", out Image allyGlow);
        Transform enemyCard1 = CreateCard(enemyCards, "EnemyCard1", out Image enemyGlow1);
        Transform enemyCard2 = CreateCard(enemyCards, "EnemyCard2", out Image enemyGlow2);
        Transform allyPanel = NewChild(allyInfo, "角色1").transform;
        Transform enemyPanel1 = NewChild(enemyInfo, "敌人1").transform;
        Transform enemyPanel2 = NewChild(enemyInfo, "敌人2").transform;
        var originalOrder = new List<Transform>();
        for (int index = 0; index < canvas.childCount; index++) originalOrder.Add(canvas.GetChild(index));

        GameObject runtime = NewRoot("BattleRuntime");
        BattleInputController input = runtime.AddComponent<BattleInputController>();
        BattlefieldSwitchController switcher = runtime.AddComponent<BattlefieldSwitchController>();
        CharacterBattleController caster = runtime.AddComponent<CharacterBattleController>();
        caster.Entity = runtime.AddComponent<BattleEntity>();
        caster.Entity.Side = BattleSide.Ally;
        caster.Entity.SlotPosition = 1;
        BattlefieldSelectionPresentation presentation = runtime.AddComponent<BattlefieldSelectionPresentation>();
        SetSelecting(input, true);
        SetPrivateField(input, "_pendingAlly", caster);
        input.Selector.Side = BattleSide.Enemy;
        input.Selector.CurrentSelection = new List<int> { 1 };

        BindTestHierarchy(presentation, canvasObject.GetComponent<Canvas>(), dimmer, input, switcher);
        InvokeLifecycle(presentation, "LateUpdate");

        Assert.That(dimmer.enabled, Is.True);
        Assert.That(enemyCards.GetSiblingIndex(), Is.LessThan(dimmer.transform.GetSiblingIndex()));
        Assert.That(allyCards.GetSiblingIndex(), Is.LessThan(dimmer.transform.GetSiblingIndex()));
        Assert.That(enemyCard1.parent.name, Is.EqualTo("SelectionForeground"));
        Assert.That(enemyPanel1.parent.name, Is.EqualTo("SelectionForeground"));
        Assert.That(enemyCard2.parent, Is.EqualTo(enemyCards));
        Assert.That(enemyPanel2.parent, Is.EqualTo(enemyInfo));
        Assert.That(enemyGlow1.enabled, Is.True);
        Assert.That(enemyGlow2.enabled, Is.False);
        Assert.That(allyGlow.enabled, Is.False);
        Assert.That(allyCard.parent.name, Is.EqualTo("SelectionForeground"));
        Assert.That(allyPanel.parent.name, Is.EqualTo("SelectionForeground"));
        Assert.That(controls.GetSiblingIndex(), Is.GreaterThan(dimmer.transform.GetSiblingIndex()));

        input.Selector.CurrentSelection = new List<int> { 2 };
        SetPrivateField(presentation, "_dimmerFadingIn", false);
        Color settledDimmer = dimmer.color;
        settledDimmer.a = 0.5f;
        dimmer.color = settledDimmer;
        InvokeLifecycle(presentation, "LateUpdate");
        Assert.That(enemyCard1.parent.name, Is.EqualTo("SelectionForeground"),
            "旧目标应留在前景中完成淡入遮罩动画。");
        Assert.That(enemyCard2.parent.name, Is.EqualTo("SelectionForeground"),
            "新目标应先抬到前景，再从遮罩亮度淡出。");

        InvokeEntityTransition(presentation, 0.15f);
        Assert.That(enemyCard1.parent, Is.EqualTo(enemyCards));
        Assert.That(enemyPanel1.parent, Is.EqualTo(enemyInfo));
        Assert.That(enemyCard2.parent.name, Is.EqualTo("SelectionForeground"));
        Assert.That(enemyPanel2.parent.name, Is.EqualTo("SelectionForeground"));
        Assert.That(enemyGlow1.enabled, Is.False);
        Assert.That(enemyGlow2.enabled, Is.True);
        Assert.That(allyCard.parent.name, Is.EqualTo("SelectionForeground"));
        Assert.That(allyPanel.parent.name, Is.EqualTo("SelectionForeground"));

        SetSelecting(input, false);
        InvokeLifecycle(presentation, "LateUpdate");

        Assert.That(dimmer.enabled, Is.False);
        Assert.That(enemyCard1.parent, Is.EqualTo(enemyCards));
        Assert.That(enemyPanel1.parent, Is.EqualTo(enemyInfo));
        Assert.That(enemyGlow1.enabled, Is.False);
        for (int index = 0; index < originalOrder.Count; index++)
            Assert.That(canvas.GetChild(index), Is.EqualTo(originalOrder[index]));
    }

    [Test]
    public void SwitchSelection_LiftsOnlyCandidateWithWhiteGlow()
    {
        GameObject canvasObject = NewRoot("BattleField1Canvas", typeof(RectTransform), typeof(Canvas));
        Transform canvas = canvasObject.transform;
        NewChild(canvas, "Background", typeof(Image));
        GameObject dimmerObject = NewChild(canvas, "Blackfold", typeof(Image), typeof(PsdUiBinding));
        dimmerObject.GetComponent<PsdUiBinding>().bindingKey = "ScreenDimmer";
        Image dimmer = dimmerObject.GetComponent<Image>();
        dimmer.enabled = false;

        Transform allyCards = NewChild(canvas, "character").transform;
        NewChild(canvas, "Enemy");
        Transform allyInfo = NewChild(canvas, "角色栏").transform;
        NewChild(canvas, "敌人栏");
        Transform allyCard1 = CreateCard(allyCards, "CharacterCard1", out Image allyGlow1);
        Transform allyCard2 = CreateCard(allyCards, "CharacterCard2", out Image allyGlow2);
        Transform allyPanel1 = NewChild(allyInfo, "角色1").transform;
        Transform allyPanel2 = NewChild(allyInfo, "角色2").transform;

        GameObject runtime = NewRoot("BattleRuntime");
        BattleInputController input = runtime.AddComponent<BattleInputController>();
        BattlefieldSwitchController switcher = runtime.AddComponent<BattlefieldSwitchController>();
        BattlefieldSelectionPresentation presentation = runtime.AddComponent<BattlefieldSelectionPresentation>();
        SetPrivateField(switcher, "_switching", true);
        SetPrivateField(switcher, "_selectedSlot", 1);

        BindTestHierarchy(presentation, canvasObject.GetComponent<Canvas>(), dimmer, input, switcher);
        InvokeLifecycle(presentation, "LateUpdate");

        Assert.That(dimmer.enabled, Is.True);
        Assert.That(allyCard1.parent, Is.EqualTo(allyCards));
        Assert.That(allyPanel1.parent, Is.EqualTo(allyInfo));
        Assert.That(allyGlow1.enabled, Is.False);
        Assert.That(allyCard2.parent.name, Is.EqualTo("SelectionForeground"));
        Assert.That(allyPanel2.parent.name, Is.EqualTo("SelectionForeground"));
        Assert.That(allyGlow2.enabled, Is.True);
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

    private static void BindTestHierarchy(BattlefieldSelectionPresentation presentation, Canvas canvas,
        Image dimmer, BattleInputController input, BattlefieldSwitchController switcher)
    {
        // 行为单元测试显式绑定自身界面，不调用包含全局查找和资源读取的 Start。
        // 正式场景的组件及绑定结构由独立预览场景测试检查。
        SetPrivateField(presentation, "_canvas", canvas);
        SetPrivateField(presentation, "_dimmer", dimmer);
        SetPrivateField(presentation, "_input", input);
        SetPrivateField(presentation, "_switch", switcher);
        Transform allyCards = canvas.transform.Find("character");
        Transform enemyCards = canvas.transform.Find("Enemy");
        Transform allyInfo = canvas.transform.Find("角色栏");
        Transform enemyInfo = canvas.transform.Find("敌人栏");
        foreach (Transform container in new[] { allyCards, enemyCards, allyInfo, enemyInfo })
            InvokePrivate(presentation, "AddContainer", container);
        InvokePrivate(presentation, "AddSide", BattleSide.Ally, allyCards, "CharacterCard", allyInfo, "角色");
        InvokePrivate(presentation, "AddSide", BattleSide.Enemy, enemyCards, "EnemyCard", enemyInfo, "敌人");
        InvokePrivate(presentation, "SetAllGlows", false);
    }

    private static void InvokePrivate(BattlefieldSelectionPresentation presentation, string methodName,
        params object[] arguments)
    {
        MethodInfo method = typeof(BattlefieldSelectionPresentation).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(presentation, arguments);
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

    private static Transform CreateCard(Transform parent, string name, out Image glow)
    {
        Transform card = NewChild(parent, name, typeof(Image)).transform;
        GameObject glowObject = NewChild(card, "CardOverGlow", typeof(RectTransform), typeof(Image), typeof(PsdUiBinding));
        glowObject.GetComponent<PsdUiBinding>().bindingKey = "CardOverGlow";
        glow = glowObject.GetComponent<Image>();
        glow.enabled = true;
        return card;
    }

    private static void SetSelecting(BattleInputController input, bool value)
    {
        SetPrivateField(input, "_selecting", value);
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

    private static void InvokeEntityTransition(BattlefieldSelectionPresentation presentation, float deltaTime)
    {
        MethodInfo method = typeof(BattlefieldSelectionPresentation).GetMethod(
            "UpdateEntityTransitions",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(float) },
            null);
        Assert.That(method, Is.Not.Null);
        method.Invoke(presentation, new object[] { deltaTime });
    }
}
