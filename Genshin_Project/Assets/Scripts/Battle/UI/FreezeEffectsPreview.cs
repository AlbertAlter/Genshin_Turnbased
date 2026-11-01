using System.Collections;
using UnityEngine;

/// <summary>Only scene assembly and isolated data; thaw, frost, shake and AP feedback use the shared modules.</summary>
public sealed class FreezeEffectsPreview : MonoBehaviour, IBattleUiFreezeSource, IBattleUiApSource
{
    public Font controlsFont;
    public int characterId = 1009;
    public UICardFactory cardFactory;
    public RectTransform cardMount;
    public UICardFreeze freeze;
    public BattleUiShields shields;
    public BattleUiFreezeInteraction interaction;
    public BattlefieldApView apView;
    public BattleEntity previewEntity;
    private ActionPointManager ap;
    private UICardView card;
    private BattleUiCardBinding shieldCard;
    private bool showShield = true;
    private int shieldElement;
    private static readonly string[] ShieldKeys = { "None", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro" };
    private static readonly string[] ShieldLabels = { "无元素", "火", "水", "雷", "冰", "风", "岩", "草" };
    private bool started, ready;
    private string error;

    private void Awake() { ap = new ActionPointManager(); ap.ResetAP(); }
    private IEnumerator Start() { started = true; yield return Assemble(); }
    private void OnEnable() { if (started) StartCoroutine(Assemble()); }
    private IEnumerator Assemble()
    {
        ready = false; error = null;
        if (cardFactory == null || cardMount == null || freeze == null || interaction == null || apView == null || previewEntity == null)
        { Fail("冻结预览缺少场景显式绑定的公共模块。"); yield break; }
        if (card == null || card.Released) card = cardFactory.Request(cardMount, BattleSide.Ally, characterId);
        while (!card.Ready && card.Error == null) yield return null;
        if (card.Error != null) { Fail(card.Error); yield break; }
        if (!freeze.Ready) yield return freeze.Reload();
        if (!freeze.Ready) { Fail(freeze.Error ?? "冻结资源未就绪。"); yield break; }
        if (!freeze.Bound) freeze.Bind(card);
        apView.Bind(this); ready = true;
        ResetFreeze(); interaction.Refresh();
        if (shields == null || shields.layout == null || !shields.layout.TryGetCard(BattleSide.Ally, 1, out shieldCard))
        { Fail("护盾预览缺少卡牌布局绑定。"); yield break; }
        shieldCard.shieldLayer = card.Appearance.ShieldLayer;
    }
    public BattleUiFreezeState GetFreeze()
    {
        if (!isActiveAndEnabled || !ready) return default;
        ReactionStateSystem.TryGetEntityState(previewEntity, ReactionType.Frozen, out var state);
        return new BattleUiFreezeState { Instance = state, CanInteract = ready };
    }
    public FrozenThawClickResult ClickThaw() => !isActiveAndEnabled || !ready
        ? FrozenThawClickResult.Ignored : FrozenThawInteraction.Click(previewEntity, ap);
    public BattleUiApState GetAP() => new BattleUiApState { Identity = ap,
        Available = isActiveAndEnabled && ready, Current = ap?.CurrentAP ?? 0, Maximum = ap?.MaxAP ?? 100 };
    private void LateUpdate()
    {
        if (card != null && !card.Released)
            card.SetAnimationFrozen(this, FrozenReactionHandler.IsFrozen(previewEntity));
        if (ready && shieldCard != null && shields.Ready)
            shields.SetShield(shieldCard.side, shieldCard.slot, showShield, ShieldKeys[shieldElement]);
    }
    public void ResetFreeze()
    {
        if (!ready || previewEntity == null) return;
        ReactionStateSystem.RemoveEntityState(previewEntity, ReactionType.Frozen);
        ReactionStateSystem.SetEntityState(previewEntity, ReactionType.Frozen, 3,
            new ReactionSourceSnapshot { SourceEntity = previewEntity, SourceEntityID = previewEntity.EntityID,
                SourceSide = BattleSide.Ally, SourceKind = ReactionSourceKind.DerivedReaction },
            (int)TurnPhase.AllyAction, false, 3);
        interaction.Refresh();
    }
    public void RestoreAP() => ap?.ResetAP();
    public void EmptyAP() { if (ap != null) ap.ConsumeAP(ap.CurrentAP); }
    private void Fail(string message) { error = message; Debug.LogError("FreezeEffectsPreview: " + message, this); }
    private void OnGUI()
    {
        var matrix = GUI.matrix; var font = GUI.skin.font; bool enabledBefore = GUI.enabled;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1));
        if (controlsFont != null) GUI.skin.font = controlsFont;
        var label = new GUIStyle(GUI.skin.label) { fontSize = 25 };
        var button = new GUIStyle(GUI.skin.button) { fontSize = 23 };
        GUILayout.BeginArea(new Rect(60, 35, 1800, 200));
        GUILayout.Label("点击卡牌或按空格：前两下开裂，第三下成功解冻才扣10 AP；每次点击均使用公共受击震动。", label);
        GUI.enabled = ready && isActiveAndEnabled;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("重新冻结", button, GUILayout.Width(250))) ResetFreeze();
        if (GUILayout.Button("恢复 AP", button, GUILayout.Width(250))) RestoreAP();
        if (GUILayout.Button("AP 归零（验证不足）", button, GUILayout.Width(350))) EmptyAP();
        GUILayout.EndHorizontal(); GUI.enabled = enabledBefore;
        var state = GetFreeze();
        GUILayout.Label(error ?? (ready ? state.Instance != null
            ? $"解冻进度：{state.Instance.ThawClicks}/3" : "已解冻，可重新冻结" : "加载公共卡牌与冰层资源…"), label);
        if (interaction != null && interaction.LastResult == FrozenThawClickResult.InsufficientAP
            && ap.CurrentAP < FrozenReactionHandler.ThawAPCost)
            GUILayout.Label("AP不足，保留第二阶段裂纹。恢复AP后可再次点击完成解冻。", label);
        GUI.enabled = enabledBefore && ready && shieldCard != null && shields.Ready;
        GUILayout.BeginHorizontal();
        showShield = GUILayout.Toggle(showShield, showShield ? "护盾：开" : "护盾：关", button, GUILayout.Width(160));
        shieldElement = GUILayout.Toolbar(shieldElement, ShieldLabels, button, GUILayout.Width(700));
        GUILayout.EndHorizontal();
        if (shields != null && shields.Error != null) GUILayout.Label(shields.Error, label);
        GUILayout.EndArea();
        GUI.enabled = enabledBefore; GUI.skin.font = font; GUI.matrix = matrix;
    }
    private void OnDisable()
    {
        StopAllCoroutines(); ready = false;
        if (shieldCard != null && shields != null) shields.SetShield(shieldCard.side, shieldCard.slot, false);
        shieldCard = null;
        card?.SetAnimationFrozen(this, false);
        if (previewEntity != null) ReactionStateSystem.RemoveEntityState(previewEntity, ReactionType.Frozen);
    }
    private void OnDestroy() { if (card != null && cardFactory != null) cardFactory.Release(card); }
}
