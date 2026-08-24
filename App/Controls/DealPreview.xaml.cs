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

    public DealPreview()
	{
		InitializeComponent();
        BindingContext = this;
	}
}