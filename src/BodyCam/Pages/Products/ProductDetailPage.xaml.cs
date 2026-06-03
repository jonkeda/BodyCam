using BodyCam.ViewModels;

namespace BodyCam.Pages.Products;

public partial class ProductDetailPage : ContentPage
{
    public const string Route = "product-detail";

    public ProductDetailPage(ProductDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
