using Drive.Domain.Items;

namespace Drive.Domain.Tests;

public sealed class ItemNameTests
{
    [Fact]
    public void Create_TrimsAndNormalizesUnicode()
    {
        // "Báo cáo" viết bằng ký tự tổ hợp (a + dấu sắc rời) và dựng sẵn phải cho cùng khoá.
        ItemName decomposed = ItemName.Create("  Báo cáo  ");
        ItemName precomposed = ItemName.Create("BÁO CÁO");

        decomposed.Value.ShouldBe("Báo cáo");
        decomposed.Key.ShouldBe(precomposed.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("tab\there")]
    [InlineData(".")]
    [InlineData("..")]
    public void Create_InvalidName_Throws(string name)
    {
        Should.Throw<DriveException.InvalidName>(() => ItemName.Create(name));
    }

    [Fact]
    public void Create_EnforcesMaxLength()
    {
        Should.NotThrow(() => ItemName.Create(new string('a', 255)));
        Should.Throw<DriveException.InvalidName>(() => ItemName.Create(new string('a', 256)));
    }

    [Theory]
    [InlineData("report.pdf", true, 1, "report (1).pdf")]
    [InlineData("report.pdf", true, 12, "report (12).pdf")]
    [InlineData("Ảnh", false, 2, "Ảnh (2)")]
    [InlineData(".bashrc", true, 1, ".bashrc (1)")]
    [InlineData("archive.tar.gz", true, 1, "archive.tar (1).gz")]
    public void WithSuffix_KeepsExtension(string name, bool isFile, int n, string expected)
    {
        ItemName.Create(name).WithSuffix(n, isFile).Value.ShouldBe(expected);
    }

    [Fact]
    public void WithSuffix_TruncatesToStayWithinMaxLength()
    {
        ItemName name = ItemName.Create(new string('a', 251) + ".pdf");

        ItemName renamed = name.WithSuffix(1, keepExtension: true);

        renamed.Value.Length.ShouldBe(ItemName.MaxLength);
        renamed.Value.ShouldEndWith(" (1).pdf");
    }
}
