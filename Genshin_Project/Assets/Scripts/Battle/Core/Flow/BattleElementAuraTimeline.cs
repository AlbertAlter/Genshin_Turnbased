using System;
using System.Collections.Generic;

/// <summary>可选的序列准备阶段。选择退出等待之前冻结历史快照；finally释放尚未派发的请求。</summary>
public interface IBattlePresentationSequenceView
{
    void PrepareSequence(BattlePresentationSequence sequence, IReadOnlyList<BattlePresentationCue> cues);
    void ReleaseSequence(long sequenceID);
}

/// <summary>关联真实接触/反应/附着事务。仅按声明因果和目标引用配对，禁止按时间或槽位猜测。</summary>
public static class BattleElementAuraTimeline
{
    public sealed class Moment
    {
        public BattlePresentationCue Anchor;
        public BattlePresentationResult Contact, Change;
        public readonly List<BattlePresentationResult> Reactions = new List<BattlePresentationResult>();
        public BattleEntity Target => (Contact ?? Change ?? Reactions[0]).Data.Subject;
        public BattlePresentationResult Identity => Contact ?? Change ?? Reactions[0];
    }
    public static IReadOnlyList<Moment> Build(IReadOnlyList<BattlePresentationCue> cues)
    {
        var results = new Dictionary<(long, long), BattlePresentationResult>();
        var moments = new List<Moment>();
        var contacts = new Dictionary<(long, long), Moment>();
        foreach (var cue in cues)
        {
            if (cue.IsAnticipation || cue.IsEnemyExit || cue.IsAttackStart) continue;
            var r = cue.Result;
            results[(r.Header.SessionId, r.Header.EventId)] = r;
            if (r.Data is ElementContactEventData && (cue.SkillTracks & BattleSkillTracks.ReactionIcons) != 0)
            {
                var m = new Moment { Anchor = cue, Contact = r }; moments.Add(m);
                contacts[(r.Header.SessionId, r.Header.EventId)] = m;
            }
        }
        Moment ContactFor(BattlePresentationResult result)
        {
            var seen = new HashSet<long>(); var search = new Queue<long>(result.Header.CauseEventIds);
            while (search.Count > 0)
            {
                long id = search.Dequeue(); if (!seen.Add(id)) continue;
                if (contacts.TryGetValue((result.Header.SessionId, id), out var found) &&
                    ReferenceEquals(found.Target, result.Data.Subject)) return found;
                if (results.TryGetValue((result.Header.SessionId, id), out var parent))
                    foreach (long cause in parent.Header.CauseEventIds) search.Enqueue(cause);
            }
            return null;
        }
        foreach (var cue in cues)
        {
            if (cue.IsAnticipation || cue.IsEnemyExit || cue.IsAttackStart ||
                (cue.SkillTracks & BattleSkillTracks.ReactionIcons) == 0) continue;
            var r = cue.Result;
            if (!(r.Data is ReactionEventData) && !(r.Data is AuraChangedEventData)) continue;
            var moment = ContactFor(r);
            // 一次事务的后续独立写回保留自己的锚点，不压成一张净变化快照。
            if (moment == null || r.Data is AuraChangedEventData && moment.Change != null)
            { moment = new Moment { Anchor = cue }; moments.Add(moment); }
            if (r.Data is AuraChangedEventData) moment.Change = r;
            else moment.Reactions.Add(r);
            if (r.Order < moment.Anchor.Result.Order) moment.Anchor = cue;
        }
        moments.Sort((a, b) => a.Anchor.Result.Order.CompareTo(b.Anchor.Result.Order));
        return moments.AsReadOnly();
    }
}
