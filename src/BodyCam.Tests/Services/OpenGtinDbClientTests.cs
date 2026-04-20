using BodyCam.Services.Barcode;
using FluentAssertions;

namespace BodyCam.Tests.Services;

public class OpenGtinDbClientTests
{
    [Fact]
    public void Parse_ValidResponse_ReturnsProductInfo()
    {
        var text = """
            error=0
            ---
            name=Natürliches Mineralwasser
            detailname=Bad Vilbeler RIED Quelle
            vendor=H. Kroner GmbH & CO. KG
            maincat=Getränke, Alkohol
            origin=Deutschland
            contents=0
            ---
            """;

        var result = OpenGtinDbClient.Parse("4006420012345", text);

        result.Should().NotBeNull();
        result!.Barcode.Should().Be("4006420012345");
        result.Source.Should().Be("opengtindb");
        result.Name.Should().Be("Natürliches Mineralwasser — Bad Vilbeler RIED Quelle");
        result.Brand.Should().Be("H. Kroner GmbH & CO. KG");
        result.Category.Should().Be("Getränke, Alkohol");
        result.Origins.Should().Be("Deutschland");
    }

    [Fact]
    public void Parse_ErrorResponse_ReturnsNull()
    {
        var text = "error=1\n";

        var result = OpenGtinDbClient.Parse("1234567890123", text);

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyName_ReturnsNull()
    {
        var text = """
            error=0
            ---
            vendor=SomeVendor
            ---
            """;

        var result = OpenGtinDbClient.Parse("1234567890123", text);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(8, "Gluten-free")]
    [InlineData(256, "Vegan")]
    [InlineData(128, "Vegetarian")]
    [InlineData(264, "Gluten-free, Vegan")]       // 8 + 256
    [InlineData(384, "Vegetarian, Vegan")]         // 128 + 256
    [InlineData(1, "Lactose-free")]
    [InlineData(0, null)]
    public void BuildLabels_ParsesBitmask(int bits, string? expected)
    {
        OpenGtinDbClient.BuildLabels(bits).Should().Be(expected);
    }

    [Fact]
    public void Parse_WithContentsBitmask_SetsLabels()
    {
        var text = """
            error=0
            ---
            name=Organic Oats
            contents=288
            ---
            """;

        var result = OpenGtinDbClient.Parse("1234567890123", text);

        result.Should().NotBeNull();
        result!.Labels.Should().Be("Organic, Vegan"); // 32 + 256
    }

    [Fact]
    public void Parse_NameOnly_NoDetailName()
    {
        var text = """
            error=0
            ---
            name=Simple Product
            ---
            """;

        var result = OpenGtinDbClient.Parse("1234567890123", text);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Simple Product");
    }
}
