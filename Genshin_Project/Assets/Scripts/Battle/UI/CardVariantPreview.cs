using System.Collections;
using UnityEngine;

/// <summary>Isolated preview data and controls. Card rendering, switching and shake live in public modules.</summary>
public sealed class CardVariantPreview : MonoBehaviour
{
    public Font controlsFont;
    public UICardFactory cardFactory;
    public UIHitShakeController shake;
    public UICardVariantSwitcher switcher;
    public RectTransform directMount, shakeMount;
    public int enemyId = 2004, firstVariant = 1, secondVariant = 2;
    private UICardView directCard, shakenCard;
    private HitShakeStyle settings;
    private UICardVariantStyle fadeSettings;
    private bool started, ready, heavy = true, paused;
    private float speed = 1;
    private uint sequence;
    private string error;
    private IEnumerator Start() { started = true; yield return Assemble(); }
    private void OnEnable() { if (started) StartCoroutine(Assemble()); }
    private IEnumerator Assemble()
    {
        ready = false; error = null;
        if (cardFactory == null || shake == null || switcher == null || directMount == null || shakeMount == null)
        { Fail("缺少显式绑定的公共组件或卡牌挂载点。"); yield break; }
        try { settings = HitShakeStyle.Load(); fadeSettings = UICardVariantStyle.Load(); }
        catch (System.Exception exception) { Fail(exception.Message); yield break; }
        shake.AutomaticClock = false;
        switcher.AutomaticClock = false;
        directCard = cardFactory.Request(directMount, BattleSide.Enemy, enemyId, false);
        shakenCard = cardFactory.Request(shakeMount, BattleSide.Enemy, enemyId, false);
        while (!directCard.Ready || !shakenCard.Ready)
        {
            if (directCard.Error != null || shakenCard.Error != null)
            { Fail(directCard.Error ?? shakenCard.Error); yield break; }
            yield return null;
        }
        foreach (int variant in new[] { firstVariant, secondVariant })
        {
            yield return cardFactory.PreloadVariant(shakenCard, variant);
            if (!cardFactory.HasVariant(shakenCard, variant))
            { Fail($"缺少敌人 {enemyId} 的差分 {variant:D2}，请导入对应美术资源。"); yield break; }
        }
        switcher.Bind(cardFactory, shakenCard, firstVariant);
        ready = true; ResetCards();
    }
    private HitShakeStyle.Profile Profile => settings.Get(heavy ? "CardHeavy" : "CardNormal");
    private void Play()
    {
        if (!ready || paused || switcher.Pending) return;
        int target = switcher.CurrentVariant == firstVariant ? secondVariant : firstVariant;
        if (switcher.FadeTo(target, fadeSettings))
        {
            cardFactory.TrySetVariant(directCard, target);
        }
    }
    private void ResetCards()
    {
        if (!ready) return;
        shake.Cancel(1);
        switcher.SetImmediate(firstVariant);
        cardFactory.TrySetVariant(directCard, firstVariant);
    }
    private void Update()
    {
        if (ready && Input.GetKeyDown(KeyCode.Space)) Play();
        if (ready && Input.GetKeyDown(KeyCode.H)) SimulateHit();
    }
    private void SimulateHit()
    {
        if (!ready || paused) return;
        uint seed = ++sequence;
        shake.PulseCard(1, directCard.Appearance.Root, Profile, seed);
        shake.PulseCard(1, shakenCard.Appearance.Root, Profile, seed);
    }
    private void LateUpdate()
    {
        if (!ready || paused) return;
        float delta = Time.unscaledDeltaTime * speed;
        shake.Advance(delta); switcher.Advance(delta);
    }
    private void Fail(string message) { error = message; Debug.LogError("CardVariantPreview: " + message, this); }
    private void OnGUI()
    {
        var matrix = GUI.matrix; var font = GUI.skin.font; bool enabledBefore = GUI.enabled;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1));
        if (controlsFont != null) GUI.skin.font = controlsFont;
        var label = new GUIStyle(GUI.skin.label) { fontSize = 25 };
        var button = new GUIStyle(GUI.skin.button) { fontSize = 24 };
        GUILayout.BeginArea(new Rect(70, 35, 1780, 245));
        GUILayout.Label("敌人卡面差分：左侧直接切图，右侧交叉渐变", label);
        GUI.enabled = ready;
        GUILayout.BeginHorizontal();
        GUI.enabled = ready && !paused && !switcher.Pending;
        if (GUILayout.Button("切换差分（空格）", button, GUILayout.Width(330))) Play();
        GUI.enabled = ready;
        if (GUILayout.Button("重置为点燃", button, GUILayout.Width(230))) ResetCards();
        if (GUILayout.Button("模拟受击（H）", button, GUILayout.Width(230))) SimulateHit();
        if (GUILayout.Button(heavy ? "重击抖动" : "普攻抖动", button, GUILayout.Width(220))) heavy = !heavy;
        if (GUILayout.Button(paused ? "继续" : "暂停", button, GUILayout.Width(180))) paused = !paused;
        if (GUILayout.Button(speed == 1 ? "速度：1×" : "速度：0.25×", button, GUILayout.Width(230))) speed = speed == 1 ? .25f : 1;
        GUILayout.EndHorizontal();
        GUI.enabled = enabledBefore;
        GUILayout.Label(error ?? (ready ? $"{enemyId}：{firstVariant:D2} ⇄ {secondVariant:D2}；当前右侧：{switcher.CurrentVariant:D2}；渐变时长：{fadeSettings.Duration:F2}s；进度：{switcher.Progress:P0}" : "加载公共卡牌与差分…"), label);
        if (switcher != null && switcher.Error != null) GUILayout.Label(switcher.Error, label);
        GUILayout.Label("空格只切换差分；H 只模拟受击。可同时播放，切换自身不触发或取消抖动。", label);
        GUILayout.EndArea();
        GUI.enabled = enabledBefore; GUI.skin.font = font; GUI.matrix = matrix;
    }
    private void OnDisable()
    {
        StopAllCoroutines(); ready = false;
        if (switcher != null) switcher.Cancel();
        if (shake != null) shake.Cancel(1);
        if (cardFactory != null) { cardFactory.Release(directCard); cardFactory.Release(shakenCard); }
        directCard = shakenCard = null;
    }
}
