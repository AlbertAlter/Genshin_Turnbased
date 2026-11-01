using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Choose before playback, then attach a single sampled clip to the actual cue.</summary>
public static class CharacterVoicePlan
{
    private sealed class Candidates
    {
        public int Rank;
        // Equal-priority characters have equal chances regardless of how many times they were hit.
        public readonly Dictionary<int, (BattlePresentationCue cue, int speaker, CharacterVoiceAction action)> Targets
            = new Dictionary<int, (BattlePresentationCue, int, CharacterVoiceAction)>();
    }

    public static string Pick(int actor, IReadOnlyList<string> pool, Func<int, int> roll)
    {
        if (pool.Count == 0) return null;
        return actor + "/" + pool[pool.Count == 1 ? 0 : roll(pool.Count)] + ".wav";
    }

    public static void Prepare(IReadOnlyList<BattlePresentationCue> cues, CharacterVoiceTable table,
        IReadOnlyList<int> survivors, Func<int, int> roll)
    {
        var damageGroups = new Dictionary<(long, long), Candidates>();
        var deathGroups = new Dictionary<(long, long), Candidates>();
        var scopes = new Dictionary<(long, long), long>();
        var causes = new Dictionary<(long, long), long>();
        var releases = new HashSet<(long session, long status)>();
        foreach (var cue in cues)
        {
            if (!cue.IsAnticipation && cue.Result.Data is DamageEventData release && release.HitLanded &&
                !string.IsNullOrEmpty(release.ReleaseVoiceSkillId))
            {
                var source = release.Source;
                if (source.SourceSide == BattleSide.Ally && releases.Add((cue.Result.Header.SessionId, source.SourceStatusApplyOrder)))
                    Assign(cue, source.SourceEntityID, release.ReleaseVoiceSkillId, table.Skill(source.SourceEntityID, release.ReleaseVoiceSkillId));
            }
            if ((cue.SkillTracks & BattleSkillTracks.Voice) == 0 || cue.CastStart?.TargetSide != BattleSide.Ally) continue;
            var cast = (ActionEventData)cue.CastStart.Data;
            Assign(cue, cast.SubjectId, cast.SkillId, table.Skill(cast.SubjectId, cast.SkillId));
        }
        foreach (var cue in cues.Where(c => !c.IsAnticipation).OrderBy(c => c.Result.Order))
        {
            var r = cue.Result; var h = r.Header; var data = r.Data;
            long group = 0;
            foreach (var cause in h.CauseEventIds)
                if (causes.TryGetValue((h.SessionId, cause), out group)) break;
            long scope = h.ActionScopeId != 0 ? h.ActionScopeId : h.ActionExecutionId;
            if (group == 0 && scope != 0) scopes.TryGetValue((h.SessionId, scope), out group);
            if (data.Kind == BattleEventKind.ActionStarted && r.TargetSide == BattleSide.Enemy && scope != 0)
            {
                if (group == 0) group = scope;
                scopes[(h.SessionId, scope)] = group;
            }
            if (data is DamageEventData damage && r.TargetSide == BattleSide.Ally)
            {
                if (group == 0 && damage.FromIndependentEnemyStatus && scope != 0)
                { group = scope; scopes[(h.SessionId, scope)] = group; }
                if (group == 0 && damage.Source.HasSourceEntity && damage.Source.SourceSide == BattleSide.Enemy)
                    group = h.EffectScopeId != 0 ? h.EffectScopeId : h.ResolutionId;
                if (group != 0 && (damage.HitLanded || damage.ActualHPDamage > 0) && damage.HPAfter > 0)
                {
                    int actor = data.SubjectId;
                    bool low = damage.ActualHPDamage > 0 && 10d * damage.HPAfter < 3d * damage.TargetMaxHP;
                    if (low && data.SubjectWasOnField)
                        Offer(damageGroups, group, 4, cue, actor, CharacterVoiceAction.LowHP);
                    if (low && !data.SubjectWasOnField && damage.ActiveAllyIdAtHit > 0)
                        Offer(damageGroups, group, 3, cue, damage.ActiveAllyIdAtHit, CharacterVoiceAction.TeammateLowHP);
                    if (damage.TargetMaxHP > 0 && 10d * damage.ActualHPDamage >= damage.TargetMaxHP)
                        Offer(damageGroups, group, 2, cue, actor, CharacterVoiceAction.HardHit);
                    Offer(damageGroups, group, 1, cue, actor, CharacterVoiceAction.LightHit);
                }
            }
            if (group != 0) causes[(h.SessionId, h.EventId)] = group;
            if (data is DeathEventData && r.TargetSide == BattleSide.Ally)
                Offer(deathGroups, h.HitId != 0 ? h.HitId : h.ResolutionId, 1, cue, data.SubjectId, CharacterVoiceAction.Die);
            if (data is SwitchEventData sw && r.TargetSide == BattleSide.Ally && sw.Reason == BattleSwitchReason.Manual)
                Assign(cue, sw.IncomingId, nameof(CharacterVoiceAction.SwitchIn), table.Action(sw.IncomingId, CharacterVoiceAction.SwitchIn));
            if (data is BattleEndedEventData ended && ended.Victory)
            {
                var eligible = survivors.Where(id => table.Action(id, CharacterVoiceAction.Win).Count > 0).ToArray();
                if (eligible.Length > 0)
                {
                    int actor = eligible[eligible.Length == 1 ? 0 : roll(eligible.Length)];
                    Assign(cue, actor, nameof(CharacterVoiceAction.Win), table.Action(actor, CharacterVoiceAction.Win));
                }
            }
        }
        foreach (var list in damageGroups.Values.Concat(deathGroups.Values))
        {
            var values = list.Targets.Values.ToArray();
            var line = values[values.Length == 1 ? 0 : roll(values.Length)];
            Assign(line.cue, line.speaker, line.action.ToString(), table.Action(line.speaker, line.action));
        }

        void Assign(BattlePresentationCue cue, int actor, string key, IReadOnlyList<string> pool)
        {
            cue.CharacterVoiceKey = key;
            cue.CharacterVoicePath = Pick(actor, pool, roll);
            if (pool.Count == 0 && LogManager.IsEnabled(LogCategory.Voice))
                LogManager.Log(LogCategory.Voice, $"跳过 {actor} {key}，没有可用的 Soundtrack。");
        }

        void Offer(Dictionary<(long, long), Candidates> groups, long group, int rank, BattlePresentationCue cue,
            int speaker, CharacterVoiceAction action)
        {
            if (table.Action(speaker, action).Count == 0) return;
            var key = (cue.Result.Header.SessionId, group);
            if (!groups.TryGetValue(key, out var list)) groups.Add(key, list = new Candidates());
            if (rank < list.Rank) return;
            if (rank > list.Rank) { list.Targets.Clear(); list.Rank = rank; }
            if (!list.Targets.ContainsKey(cue.Result.Data.SubjectId))
                list.Targets.Add(cue.Result.Data.SubjectId, (cue, speaker, action));
        }
    }
}
