using GamHubApp.Models;

namespace GamHubApp.Controls;

public partial class DealPreview : ContentView
{
    public static readonly BindableProperty DealProperty = BindableProperty.Create(propertyName: nameof(Deal),
                                                                                       returnType: typeof(Deal),
                                                                                       declaringType: typeof(DealPreview),
                                                                                       defaultBindingMode: BindingMode.OneWay);
    public Deal Deal
    {
        get => (Deal)GetValue(DealProperty);
        set => SetValue(DealProperty, value);
    }

    /// <summary>
    /// Limit of character to display for the Title
    /// </summary>
    public static readonly BindableProperty TitleLimitProperty = BindableProperty.Create(propertyName: nameof(Deal),
                                                                                       returnType: typeof(int),
                                                                                       declaringType: typeof(DealPreview),
                                                                                       defaultBindingMode: BindingMode.OneWay,
                                                                                       defaultValue: int.MaxValue);
    public int TitleLimit
    {
        get => (int)GetValue(TitleLimitProperty);
        set => SetValue(TitleLimitProperty, value);
    }

    public DealPreview()
	{
		InitializeComponent();
        BindingContext = this;
	}
}