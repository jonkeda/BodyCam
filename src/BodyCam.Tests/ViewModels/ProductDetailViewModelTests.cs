using BodyCam.Services.Barcode;
using BodyCam.ViewModels;
using FluentAssertions;

namespace BodyCam.Tests.ViewModels;

public sealed class ProductDetailViewModelTests
{
    [Fact]
    public void SetProduct_ProjectsHeaderAndSections()
    {
        var vm = new ProductDetailViewModel();

        vm.SetProduct(new ProductInfo
        {
            Barcode = "4006420012345",
            Source = "openfoodfacts",
            Name = "Mineral Water",
            Brand = "TestBrand",
            Quantity = "1 L",
            ImageUrl = "https://example.com/product.jpg",
            Category = "Water",
            NutriScoreGrade = "a",
            EnergyKcal = 0,
            Fat = 0,
            Allergens = "None",
            IngredientsText = "Water",
            LowestPrice = 1.25m,
            HighestPrice = 1.75m,
            Currency = "EUR"
        });

        vm.HasProduct.Should().BeTrue();
        vm.HasNoProduct.Should().BeFalse();
        vm.ProductName.Should().Be("Mineral Water");
        vm.BrandLine.Should().Be("TestBrand - 1 L");
        vm.HasBrandLine.Should().BeTrue();
        vm.HasImage.Should().BeTrue();
        vm.Title.Should().Be("Mineral Water");

        vm.Sections.Select(section => section.Title)
            .Should()
            .Contain(["Identity", "Food", "Nutrition", "Warnings", "Ingredients", "Pricing"]);

        vm.Sections.Single(section => section.Title == "Pricing")
            .Rows
            .Single()
            .Value
            .Should()
            .Be("1.25 - 1.75 EUR");
    }

    [Fact]
    public void ApplyQueryAttributes_WithProduct_UpdatesProduct()
    {
        var vm = new ProductDetailViewModel();
        var product = new ProductInfo
        {
            Barcode = "123",
            Source = "test",
            Name = "Provided Product"
        };

        vm.ApplyQueryAttributes(new Dictionary<string, object>
        {
            ["product"] = product
        });

        vm.Product.Should().Be(product);
        vm.ProductName.Should().Be("Provided Product");
    }

    [Fact]
    public void SetProduct_WithoutName_UsesBarcodeAsDisplayName()
    {
        var vm = new ProductDetailViewModel();

        vm.SetProduct(new ProductInfo
        {
            Barcode = "123",
            Source = "test"
        });

        vm.ProductName.Should().Be("123");
        vm.Title.Should().Be("123");
    }
}
