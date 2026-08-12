using GamHubApp.ViewModels;

namespace GamHubApp.Views;

public partial class SettingsPage : ContentPage
{
	public SettingsPage(SettingsViewModel vm)
	{
		InitializeComponent();
		BindingContext = vm;
	}
    protected override void OnAppearing()
    {

        if (BindingContext is SettingsViewModel vm)
        {
            Task.Run(async () => 
            {
                await vm.InitialiseAsync();
            }).ConfigureAwait(false);
        }

        base.OnAppearing();
    }
}