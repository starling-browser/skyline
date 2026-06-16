namespace Skyline.Interaction.Tests;

[TestClass]
public class BitmapFontTests
{
    [TestMethod]
    public void Rows_ReturnsAGlyphForKnownCharacters()
    {
        foreach (var c in "AzgQ7:·")
        {
            var rows = BitmapFont.Rows(c);
            Assert.AreEqual(BitmapFont.Height, rows.Length, $"'{c}' has seven rows");
            Assert.IsTrue(Array.Exists(rows, r => r != 0), $"'{c}' lights at least one pixel");
        }
    }

    [TestMethod]
    public void Rows_UnknownCharacter_IsBlank()
    {
        CollectionAssert.AreEqual(new byte[BitmapFont.Height], BitmapFont.Rows('~'));
    }

    [TestMethod]
    public void MeasureUnits_CountsColumnsAndGaps()
    {
        Assert.AreEqual(0, BitmapFont.MeasureUnits(""));
        Assert.AreEqual(BitmapFont.Width, BitmapFont.MeasureUnits("A"));
        Assert.AreEqual(BitmapFont.Width * 2 + 1, BitmapFont.MeasureUnits("Ao"));
    }
}
