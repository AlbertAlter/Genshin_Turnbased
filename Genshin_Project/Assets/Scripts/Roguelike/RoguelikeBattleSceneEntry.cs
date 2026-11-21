using UnityEngine;

/// <summary>Explicit campaign battle binding. Independently opened scenes retain their authored test entry.</summary>
public sealed class RoguelikeBattleSceneEntry : MonoBehaviour
{
    internal static RoguelikeBattleSceneEntry Current { get; private set; }
    [SerializeField] internal BattleSceneRuntime runtime;
    [SerializeField] internal RectTransform navigationMount;
    private void Awake()
    {
        Current = this;
        if (RoguelikeCampaignHost.Current != null && runtime != null) runtime.enabled = false;
    }
    private void OnDestroy() { if (Current == this) Current = null; }
}
