using System;
using UnityEngine;

/// <summary>Subclass and implement the desired IBattleSkill…Track contract. Bind the scene player explicitly.</summary>
public abstract class BattleSkillTrackBehaviour : MonoBehaviour, IBattleSkillTrack
{
    public BattlefieldPresentationPlayer player;

    protected virtual void OnEnable() { if (player != null) player.RegisterView(this); }
    protected virtual void Start() { if (player != null) player.RegisterView(this); }
    protected virtual void OnDisable()
    {
        if (player != null) player.UnregisterView(this);
        Cancel(-1);
    }
    public abstract void Present(long playbackID, BattlePresentationCue cue, Action<Exception> completed);
    public abstract void Cancel(long playbackID);
}
