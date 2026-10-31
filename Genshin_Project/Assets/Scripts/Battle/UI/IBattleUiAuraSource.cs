using System.Collections.Generic;

/// <summary>A scene may supply combat data, recorded data or manually authored preview values.</summary>
public interface IBattleUiAuraSource
{
    IReadOnlyList<string> GetAuras(BattleSide side, int slot);
}
