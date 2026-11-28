using System.Collections.Generic;

/// <summary>A scene may supply combat data, recorded data or manually authored preview values.</summary>
public interface IBattleUiAuraSource
{
    IReadOnlyList<string> GetAuras(BattleSide side, int slot);
}

/// <summary>可选的槽位身份查询，避免替换实体后继续绘制旧动画。</summary>
public interface IBattleUiAuraIdentitySource
{
    object GetAuraOwner(BattleSide side, int slot);
}
