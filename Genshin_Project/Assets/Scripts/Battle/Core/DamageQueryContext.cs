using System;
using System.Threading;

/// <summary>
/// 正在计算的真实伤害元数据。DamageType只描述伤害，绝不由倍率SkillType或主动ActionType推断。
/// 优先使用本次效果实例（包括运行时改写）；非执行期查询才按效果ID读取已加载定义。
/// 不发布声明、不记录全局最后伤害，嵌套调用结束后恢复原上下文。
/// </summary>
public static class DamageQueryContext
{
    private static readonly AsyncLocal<Frame> Current = new AsyncLocal<Frame>();
    private sealed class Frame : IDisposable
    {
        internal readonly Frame Previous;
        internal readonly string EffectID, DamageType, SkillID;
        private bool disposed;
        internal Frame(string effect, string damageType, string skill)
        {
            Previous = Current.Value;
            EffectID = effect;
            DamageType = damageType;
            SkillID = skill;
            Current.Value = this;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Current.Value = Previous;
        }
    }

    public static IDisposable Enter(string effectID, string damageType, string skillID = "")
        => new Frame(effectID, damageType, skillID);

    public static string ResolveType(string effectID)
    {
        var frame = Current.Value;
        // 空DamageType也是实际结果，不能回退到共享表、倍率分类或外层伤害。
        if (frame != null && frame.EffectID == effectID) return frame.DamageType;
        var dm = DataManager.Instance;
        if (dm == null || string.IsNullOrEmpty(effectID)) return null;
        if (dm.SkillEffectDict.TryGetValue(effectID, out var skill))
            return skill.EffectType == "Damage" ? skill.DamageType : null;
        if (dm.StatusEffectDict.TryGetValue(effectID, out var status))
            return status.EffectType == "Damage" ? status.DamageType : null;
        if (dm.EnemySkillEffectDict.TryGetValue(effectID, out var enemy))
            return enemy.EffectType == "Damage" ? enemy.DamageType : null;
        return null; // 未知效果不能凭SkillType猜成普攻或战技。
    }

    public static string ResolveSkillID(string effectID, string fallback)
    {
        var frame = Current.Value;
        return frame != null && frame.EffectID == effectID ? frame.SkillID : fallback;
    }
}
