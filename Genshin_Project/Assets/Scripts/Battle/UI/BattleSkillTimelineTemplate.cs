using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Battle UI/Skill Timeline Template")]
public sealed class BattleSkillTimelineTemplate : ScriptableObject
{
    public const string DefaultResource = "UI/DefaultSkillTimeline";
    [Tooltip("本模板参与播放的轨道。每种轨道由场景显式绑定实现组件。")]
    public BattleSkillTracks tracks = BattleSkillTracks.All;
    [Tooltip("每次施放只取一个语音点。无弹道时首个发射回落到首次命中，无命中时回落到起手。")]
    public BattleVoiceAnchor voiceAt = BattleVoiceAnchor.CastStart;

    public IReadOnlyList<BattlePresentationCue> Build(BattlePresentationSequence sequence,
        Func<BattlePresentationResult, BattlePresentationTiming> timing)
        => BattleSkillTimeline.Build(sequence, timing, tracks, voiceAt);
}
