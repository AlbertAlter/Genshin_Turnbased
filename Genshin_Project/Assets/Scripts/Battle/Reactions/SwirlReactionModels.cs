using System;
using System.Collections.Generic;

[Serializable]
public sealed class SwirlPreparedBatch
{
    internal readonly List<SwirlTargetSnapshot> Targets = new List<SwirlTargetSnapshot>();
    public long EffectExecutionID;
}

[Serializable]
public sealed class SwirlBatchResult
{
    public readonly ReactionResult Reaction = new ReactionResult();
    public readonly List<BattleEntity> DirectTargets = new List<BattleEntity>();
}

internal enum SwirlAuraKind { Normal, Frozen, Burning }

internal sealed class SwirlAuraAtom
{
    public string Element;
    public SwirlAuraKind Kind;
    public float Amount;
    public float SpreadAmount;
}

internal sealed class SwirlTargetSnapshot
{
    public ReactionContext Context;
    public BattleEntity Target;
    public BattleSide Side;
    public int SlotIndex;
    public bool WasAlive;
    public readonly List<SwirlAuraAtom> SpreadAtoms = new List<SwirlAuraAtom>();
}