using System;
using System.Collections.Generic;
using System.IO;

/// <summary>Read-only, per-icon display counts. Never evaluates gameplay ScriptHooks or changes a status.</summary>
public static class CardBottomStatusCountHooks
{
    public readonly struct Context
    {
        public readonly int StatusID;
        public readonly FieldPosition Position;
        public readonly BattleEntity Occupant;
        public readonly StatusInstance LatestStatus;

        internal Context(int statusID, FieldPosition position, BattleEntity occupant, StatusInstance latest)
        { StatusID = statusID; Position = position; Occupant = occupant; LatestStatus = latest; }
    }

    private static readonly Dictionary<string, Func<Context, double>> Hooks = new Dictionary<string, Func<Context, double>>(StringComparer.Ordinal)
    {
        { "AlbedoSilver", context => {
            var owner = context.LatestStatus?.Host as BattleEntity;
            if (owner == null || owner.EntityID != 1034 || context.LatestStatus.Caster != owner)
                throw new InvalidOperationException("AlbedoSilver需要阿贝多本人持有的真实瑰银状态。");
            return AlbedoScriptHooks.CountSilver(owner);
        } },
        { "PyroAgentClones", context => {
            if (context.Position == null) throw new InvalidOperationException("PyroAgentClones需要实际场地位置。");
            return Enemy2051To2060Hooks.CountClones(context.Position);
        } }
    };

    /// <summary>Register before loading a referencing table; dispose when its owning module/session ends.</summary>
    public static IDisposable Register(string name, Func<Context, double> readCount)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim()) throw new ArgumentException("卡底计数钩子名不能为空或带首尾空格。", nameof(name));
        if (readCount == null) throw new ArgumentNullException(nameof(readCount));
        if (Hooks.ContainsKey(name)) throw new InvalidOperationException("卡底计数钩子重复：" + name);
        Hooks.Add(name, readCount);
        return new Registration(name, readCount);
    }

    public static void Validate(string name)
    {
        if (!string.IsNullOrEmpty(name) && !Hooks.ContainsKey(name))
            throw new InvalidDataException("未知卡底计数钩子：" + name);
    }

    internal static double Read(string name, Context context)
    {
        Validate(name);
        try
        {
            double value = Hooks[name](context);
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new InvalidOperationException("计数必须为有限非负数。");
            return value;
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"卡底状态{context.StatusID}计数钩子{name}失败：{error.Message}", error);
        }
    }

    private sealed class Registration : IDisposable
    {
        private string name;
        private readonly Func<Context, double> readCount;
        internal Registration(string name, Func<Context, double> readCount) { this.name = name; this.readCount = readCount; }
        public void Dispose()
        {
            if (name == null) return;
            if (Hooks.TryGetValue(name, out var current) && ReferenceEquals(current, readCount)) Hooks.Remove(name);
            name = null;
        }
    }
}
