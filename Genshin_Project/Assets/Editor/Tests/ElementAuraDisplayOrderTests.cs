#if ROGUELIKE_SAVE_TESTS
using NUnit.Framework;

public sealed class ElementAuraDisplayOrderTests
{
    [Test]
    public void RefreshDoesNotReorderAndRemovalClosesTheGap()
    {
        var state = new ElementAuraDisplayOrder();
        state.Synchronize(new[] { "Hydro", "Electro", "Dendro" });
        state.Synchronize(new[] { "Electro", "Dendro", "Hydro" });
        CollectionAssert.AreEqual(new[] { "Hydro", "Electro", "Dendro" }, state.Elements);
        state.Synchronize(new[] { "Dendro", "Electro" });
        CollectionAssert.AreEqual(new[] { "Electro", "Dendro" }, state.Elements);
        state.Synchronize(new[] { "Hydro", "Dendro", "Electro" });
        CollectionAssert.AreEqual(new[] { "Electro", "Dendro", "Hydro" }, state.Elements);
    }

    [Test]
    public void SameElementPoolsShareOneIconAndNoTwoIconLimitExists()
    {
        var state = new ElementAuraDisplayOrder();
        state.Synchronize(new[] { "Hydro", "Hydro", "Electro", "Dendro", "Cryo" });
        CollectionAssert.AreEqual(new[] { "Hydro", "Electro", "Dendro", "Cryo" }, state.Elements);
        state.Synchronize(new string[0]);
        Assert.IsEmpty(state.Elements);
    }
}
#endif
