using System.Collections;
using UnityEngine;

/// <summary>Only scene assembly and isolated data; thaw, frost, shake and AP feedback use the shared modules.</summary>
public sealed class FreezeEffectsPreview : MonoBehaviour, IBattleUiFreezeSource, IBattleUiControlReleaseSource, IBattleUiApSource
{
    public enum ControlMode { Frozen, Airborne, FrozenThenAirborne, AirborneThenFrozen }
    public ControlMode controlMode;
    public Font controlsFont;
    public int characterId = 1009;
    public UICardFactory cardFactory;
    public RectTransform cardMount;
    [SerializeField] private Canvas previewCanvas;
    public UICardFreeze freeze;
    public BattleUiShields shields;
    public BattleUiFreezeInteraction interaction;
    public BattlefieldApView apView;
    public BattleEntity previewEntity;
    private ActionPointManager ap;
    private UICardView card;
    private StatusInstance airborne;
    private BattleUiCardBinding shieldCard;
    private bool showShield = true;
    private int shieldElement;
    private static readonly string[] ShieldKeys = { "None", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro" };
    private static readonly string[] ShieldLabels = { "无元素", "火", "水", "雷", "冰", "风", "岩", "草" };
    private static readonly string[] ControlLabels = { "冻结", "击飞", "先冻结再击飞", "先击飞再冻结" };
    private bool started, ready;
    private string error;

    private void Awake() { ap = new ActionPointManager(); ap.ResetAP(); }
    private IEnumerator Start() { started = true; yield return Assemble(); }
    private void OnEnable() { if (started) StartCoroutine(Assemble()); }
    private IEnumerator Assemble()
    {
        ready = false; error = null;
        if (cardFactory == null || cardMount == null || freeze == null || interaction == null
            || interaction.airborneStars == null || apView == null || previewEntity == null)
        { Fail("冻结预览缺少场景显式绑定的公共模块。"); yield break; }
        if (card == null || card.Released) card = cardFactory.Request(cardMount, BattleSide.Ally, characterId);
        while (!card.Ready && card.Error == null) yield return null;
        if (card.Error != null) { Fail(card.Error); yield break; }
        if (!freeze.Ready) yield return freeze.Reload();
        if (!freeze.Ready) { Fail(freeze.Error ?? "冻结资源未就绪。"); yield break; }
        if (!freeze.Bound) freeze.Bind(card);
        apView.Bind(this); ready = true;
        interaction.airborneStars.transform.SetAsLastSibling();
        ResetControls();
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
    public AllyControlState GetControl() => isActiveAndEnabled && ready
        ? AllyControlRelease.GetOldest(previewEntity) : default;
    public bool CanClickControl => isActiveAndEnabled && ready && GetControl().Exists;
    public AllyControlClickResult ClickControl() => CanClickControl
        ? AllyControlRelease.Click(previewEntity, ap, out _) : AllyControlClickResult.Ignored;
    public BattleUiApState GetAP() => new BattleUiApState { Identity = ap,
        Available = isActiveAndEnabled && ready, Current = ap?.CurrentAP ?? 0, Maximum = ap?.MaxAP ?? 100 };
    private void LateUpdate()
    {
        if (card != null && !card.Released)
            card.SetAnimationFrozen(this, FrozenReactionHandler.IsFrozen(previewEntity)
                || AllyControlRelease.GetOldest(previewEntity).Exists);
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
    public void ResetControls()
    {
        if (!ready || previewEntity == null) return;
        ClearControls();
        interaction.Refresh();
        switch (controlMode)
        {
            case ControlMode.Frozen: ResetFreeze(); break;
            case ControlMode.Airborne: ApplyAirborne(); break;
            case ControlMode.FrozenThenAirborne: ResetFreeze(); ApplyAirborne(); break;
            case ControlMode.AirborneThenFrozen: ApplyAirborne(); ResetFreeze(); break;
        }
        interaction.Refresh();
    }
    private void ApplyAirborne()
    {
        // Isolated preview data only. Click priority, progress, AP and removal use AllyControlRelease.
        airborne = new StatusInstance { StatusID2 = AirborneStatusHandler.CharacterStatusID,
            StackCount = 1, RemainingPhaseCount = 3, AddInPhase = (int)TurnPhase.AllyAction,
            IsActive = true, ApplyOrder = ++BattleEntity._applyOrderCounter, Host = previewEntity };
        airborne.RecordSource(previewEntity);
        previewEntity.StatusDict.Add(airborne);
    }
    private void ClearControls()
    {
        if (previewEntity == null) return;
        ReactionStateSystem.RemoveEntityState(previewEntity, ReactionType.Frozen);
        if (airborne != null && previewEntity.HasStatusInstance(airborne))
            previewEntity.RemoveStatusInstance(airborne, "PreviewReset");
        airborne = null;
    }
    public void RestoreAP() => ap?.ResetAP();
    public void EmptyAP() { if (ap != null) ap.ConsumeAP(ap.CurrentAP); }
    private void Fail(string message) { error = message; Debug.LogError("FreezeEffectsPreview: " + message, this); }
    private void OnGUI()
    {
        var matrix = GUI.matrix; var font = GUI.skin.font; bool enabledBefore = GUI.enabled;
        GUI.matrix = ProjectUiCanvas.GetGuiMatrix(previewCanvas);
        if (controlsFont != null) GUI.skin.font = controlsFont;
        var label = new GUIStyle(GUI.skin.label) { fontSize = 25 };
        var button = new GUIStyle(GUI.skin.button) { fontSize = 23 };
        GUILayout.BeginArea(new Rect(60, 35, 1800, 200));
        GUILayout.Label("点击卡牌或按空格：冻结点击开裂，击飞每次少一颗星；前两次免费，第三次成功解除才扣10 AP。", label);
        GUI.enabled = ready && isActiveAndEnabled;
        int selectedMode = GUILayout.Toolbar((int)controlMode, ControlLabels, button, GUILayout.Width(1200));
        if (selectedMode != (int)controlMode) { controlMode = (ControlMode)selectedMode; ResetControls(); }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("重新施加当前组合", button, GUILayout.Width(300))) ResetControls();
        if (GUILayout.Button("恢复 AP", button, GUILayout.Width(250))) RestoreAP();
        if (GUILayout.Button("AP 归零（验证不足）", button, GUILayout.Width(350))) EmptyAP();
        GUILayout.EndHorizontal(); GUI.enabled = enabledBefore;
        var control = GetControl();
        string progress = !ready ? "加载公共卡牌与冰层资源…" : !control.Exists ? "控制已解除，可重新施加"
            : control.Airborne != null ? $"当前处理：击飞 {control.Clicks}/3，剩余 {AllyControlRelease.RequiredClicks - control.Clicks} 颗星"
            : $"当前处理：冻结 {control.Clicks}/3";
        if (control.Exists && control.Clicks == AllyControlRelease.RequiredClicks - 1 && ap.CurrentAP < AllyControlRelease.APCost)
            progress += "；AP不足，保留当前进度，恢复AP后再点一下。";
        GUILayout.Label(error ?? progress, label);
        GUI.enabled = enabledBefore && ready && shieldCard != null && shields.Ready;
        GUILayout.BeginHorizontal();
        showShield = GUILayout.Toggle(showShield, showShield ? "护盾：开" : "护盾：关", button, GUILayout.Width(160));
        shieldElement = GUILayout.Toolbar(shieldElement, ShieldLabels, button, GUILayout.Width(700));
        if (showShield && GUILayout.Button("重播填充扫光", button, GUILayout.Width(210)))
            shields.PlaySweep(shieldCard.side, shieldCard.slot, true);
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
        ClearControls();
        interaction?.Refresh();
    }
    private void OnDestroy() { if (card != null && cardFactory != null) cardFactory.Release(card); }
}
