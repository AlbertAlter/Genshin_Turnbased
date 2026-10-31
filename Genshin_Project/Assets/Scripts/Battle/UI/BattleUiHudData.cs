/// <summary>Read-only display snapshots. Identity resets animation when the occupant/source changes.</summary>
public struct BattleUiMeterState
{
    public object Identity;
    public bool Visible;
    public float Current, Maximum;
    public string Element;
    public float Fraction => Maximum > 0 && !float.IsInfinity(Maximum) && !float.IsNaN(Current) && !float.IsInfinity(Current)
        ? UnityEngine.Mathf.Clamp01(Current / Maximum) : 0;
}

public struct BattleUiApState
{
    public object Identity;
    public bool Available;
    public int Current, Maximum;
    // Signed change: spending is negative, gaining is positive; null means no preview.
    public int? PreviewDelta;
}

public struct BattleUiActionState
{
    public object Identity;
    public bool Visible, Frozen;
    public bool HasAvailableCharge;
    public int Cooldown;
    public string Icon;
}

public interface IBattleUiHealthSource
{
    BattleUiMeterState GetHealth(BattleSide side, int slot);
}

public interface IBattleUiEnemyShieldSource
{
    BattleUiMeterState GetEnemyShield(int slot);
}

public interface IBattleUiApSource
{
    BattleUiApState GetAP();
}

public interface IBattleUiActionSource
{
    // slot == 0 selects the active character; positive slots are one-based.
    BattleUiActionState GetAction(int slot, int skillType);
}
