namespace BodyCam.Views;

public partial class SettingsCardView : ContentView
{
    public static readonly BindableProperty CardAutomationIdProperty =
        BindableProperty.Create(nameof(CardAutomationId), typeof(string), typeof(SettingsCardView),
            propertyChanged: (b, _, n) => ((SettingsCardView)b).CardButton.AutomationId = (string)n);

    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(SettingsCardView),
            propertyChanged: (b, _, n) => ((SettingsCardView)b).IconLabel.Text = (string)n);

    public static readonly BindableProperty CardTitleProperty =
        BindableProperty.Create(nameof(CardTitle), typeof(string), typeof(SettingsCardView),
            propertyChanged: (b, _, n) => ((SettingsCardView)b).TitleLabel.Text = (string)n);

    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(SettingsCardView),
            propertyChanged: (b, _, n) => ((SettingsCardView)b).DescriptionLabel.Text = (string)n);

    public string CardAutomationId
    {
        get => (string)GetValue(CardAutomationIdProperty);
        set => SetValue(CardAutomationIdProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string CardTitle
    {
        get => (string)GetValue(CardTitleProperty);
        set => SetValue(CardTitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public event EventHandler? CardClicked;

    public SettingsCardView()
    {
        InitializeComponent();
    }

    private void OnCardClicked(object? sender, EventArgs e)
    {
        CardClicked?.Invoke(this, e);
    }
}
