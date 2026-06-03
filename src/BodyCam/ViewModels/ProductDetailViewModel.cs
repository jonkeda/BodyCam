using System.Collections.ObjectModel;
using System.Globalization;
using BodyCam.Mvvm;
using BodyCam.Services.Barcode;

namespace BodyCam.ViewModels;

public sealed class ProductDetailViewModel : ViewModelBase, IQueryAttributable
{
    private ProductInfo? _product;
    private string? _imageUrl;
    private string _productName = "Product";
    private string _brandLine = string.Empty;

    public ProductDetailViewModel()
    {
        Title = "Product";
    }

    public ProductInfo? Product
    {
        get => _product;
        private set
        {
            if (SetProperty(ref _product, value))
                Refresh();
        }
    }

    public string ProductName
    {
        get => _productName;
        private set => SetProperty(ref _productName, value);
    }

    public string BrandLine
    {
        get => _brandLine;
        private set
        {
            if (SetProperty(ref _brandLine, value))
                OnPropertyChanged(nameof(HasBrandLine));
        }
    }

    public string? ImageUrl
    {
        get => _imageUrl;
        private set
        {
            if (SetProperty(ref _imageUrl, value))
                OnPropertyChanged(nameof(HasImage));
        }
    }

    public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);
    public bool HasBrandLine => !string.IsNullOrWhiteSpace(BrandLine);
    public bool HasProduct => Product is not null;
    public bool HasNoProduct => Product is null;

    public ObservableCollection<ProductDetailSectionViewModel> Sections { get; } = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("product", out var product) && product is ProductInfo info)
            SetProduct(info);
    }

    public void SetProduct(ProductInfo product)
    {
        Product = product ?? throw new ArgumentNullException(nameof(product));
    }

    private void Refresh()
    {
        Sections.Clear();

        if (Product is not { } product)
        {
            Title = "Product";
            ProductName = "Product";
            BrandLine = string.Empty;
            ImageUrl = null;
            OnPropertyChanged(nameof(HasProduct));
            OnPropertyChanged(nameof(HasNoProduct));
            return;
        }

        ProductName = ProductBarcodeLookupWorkflow.ProductDisplayName(product);
        Title = ProductName;
        BrandLine = JoinNonEmpty(product.Brand, product.Quantity);
        ImageUrl = product.ImageUrl;

        AddSection("Identity",
            Row("Barcode", product.Barcode),
            Row("Source", product.Source),
            Row("Category", product.Category),
            Row("Origins", product.Origins));

        AddSection("Food",
            Row("Nutri-Score", product.NutriScoreGrade?.ToUpperInvariant()),
            Row("NOVA group", product.NovaGroup?.ToString(CultureInfo.InvariantCulture)),
            Row("Calories", FormatPer100g(product.EnergyKcal, "kcal")));

        AddSection("Nutrition",
            Row("Fat", FormatPer100g(product.Fat, "g")),
            Row("Saturated fat", FormatPer100g(product.SaturatedFat, "g")),
            Row("Sugars", FormatPer100g(product.Sugars, "g")),
            Row("Salt", FormatPer100g(product.Salt, "g")),
            Row("Proteins", FormatPer100g(product.Proteins, "g")),
            Row("Fiber", FormatPer100g(product.Fiber, "g")));

        AddSection("Warnings",
            Row("Allergens", product.Allergens),
            Row("Labels", product.Labels));

        AddSection("Ingredients",
            Row("Ingredients", product.IngredientsText));

        AddSection("Pricing",
            Row("Price range", FormatPriceRange(product)));

        AddSection("Description",
            Row("Description", product.Description));

        OnPropertyChanged(nameof(HasProduct));
        OnPropertyChanged(nameof(HasNoProduct));
    }

    private void AddSection(string title, params ProductDetailRowViewModel?[] rows)
    {
        var sectionRows = rows.Where(row => row is not null).Cast<ProductDetailRowViewModel>().ToList();
        if (sectionRows.Count == 0)
            return;

        var section = new ProductDetailSectionViewModel(title);
        foreach (var row in sectionRows)
            section.Rows.Add(row);

        Sections.Add(section);
    }

    private static ProductDetailRowViewModel? Row(string label, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : new ProductDetailRowViewModel(label, value.Trim());

    private static string? FormatPer100g(double? value, string unit) =>
        value is null
            ? null
            : $"{value.Value.ToString("0.##", CultureInfo.InvariantCulture)} {unit} per 100g";

    private static string? FormatPriceRange(ProductInfo product)
    {
        if (product.LowestPrice is null)
            return null;

        var currency = string.IsNullOrWhiteSpace(product.Currency) ? string.Empty : $" {product.Currency}";
        if (product.HighestPrice is null || product.HighestPrice == product.LowestPrice)
            return $"{product.LowestPrice.Value.ToString("0.##", CultureInfo.InvariantCulture)}{currency}";

        return $"{product.LowestPrice.Value.ToString("0.##", CultureInfo.InvariantCulture)} - {product.HighestPrice.Value.ToString("0.##", CultureInfo.InvariantCulture)}{currency}";
    }

    private static string JoinNonEmpty(params string?[] values) =>
        string.Join(" - ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
}

public sealed class ProductDetailSectionViewModel
{
    public ProductDetailSectionViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }
    public ObservableCollection<ProductDetailRowViewModel> Rows { get; } = [];
}

public sealed record ProductDetailRowViewModel(string Label, string Value);
