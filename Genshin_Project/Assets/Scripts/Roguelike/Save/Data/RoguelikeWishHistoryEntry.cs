using System;

/// <summary>One awarded draw, recorded in the save's draw order.</summary>
[Serializable]
public sealed class RoguelikeWishHistoryEntry
{
    public int ChapterId;
    public string WishId;
    public string PoolTitle;
    public RoguelikeWishKind PoolKind;
    public int PrizeId;
    public string PrizeName;
    public RoguelikeWishKind PrizeKind;
    public int Stars;
    public string DrawnAtUtc;
}
