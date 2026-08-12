using GamHubApp.Core;
using GamHubApp.Models;
using GamHubApp.ViewModels;
using Plugin.StoreReview;
#if DEBUG
using System.Diagnostics;
#endif
#if ANDROID
using Android.Views;
#endif

namespace GamHubApp.Views;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class ArticlePage : ContentPage
{
    private const string TimeSpentKey = "timeSpentOnArticles";
#if DEBUG

        private const double TimeMaxArticles = 0;
        private const bool _isTest = true;
#else
        private const double TimeMaxArticles = 3;
        private const bool _isTest = false;
#endif
    private ArticleViewModel _vm;
    private uint _modalHeightStart = 0;
    private uint _modalWidthStart = 50;
    private Article _article;

    public ArticlePage(Article article)
    {
        InitializeComponent();
#if ANDROID
Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("DisableHWAccel", (handler, view) =>
{
    if (Android.OS.Build.Manufacturer.Contains("samsung", StringComparison.CurrentCultureIgnoreCase))
    {
        handler.PlatformView.SetLayerType(LayerType.Software, null);
    }
});
#endif
        BindingContext = _vm = new ArticleViewModel(_article = article);
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        // Stop the timer
        StopTimer();

        // Stop all text to speech
        _vm.StopTtS();

        try
        {
            _ = (App.Current as App).DataFetcher
                .RequestReward(_article)
                .ConfigureAwait(false);
        }
        catch (Exception ex) 
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
        }

        base.OnNavigatedFrom(args);
    }
    /// <summary>
    /// Stop the timer that count the time spent on reading article pages
    /// </summary>
    private async void StopTimer()
    {

        // Register the time spent in total
        double timeSpentSoFar = Preferences.Get(TimeSpentKey, TimeSpan.Zero.TotalMinutes);

        // Stop the timer
        _vm.TimeSpent.Stop();

        double timeSpentOnArticles = timeSpentSoFar + _vm.TimeSpent.Elapsed.TotalMilliseconds;

        // Save the Time spent
        Preferences.Set(TimeSpentKey, timeSpentOnArticles);

        if (TimeSpan.FromMilliseconds(timeSpentOnArticles).TotalMinutes >= TimeMaxArticles
             && !Preferences.Get(PreferencesKeys.ReviewAsked, false))
            try
            {
                await CrossStoreReview.Current.RequestReview(_isTest);
                Preferences.Set(PreferencesKeys.ReviewAsked, true);
            }
            catch (Exception ex)
            {
#if DEBUG
                    Debug.WriteLine(ex);
#else
                SentrySdk.CaptureException(ex);
#endif
            }



    }

    /// <summary>
    /// Function to open a the dropdrown
    /// </summary>
    public void OpenDropdownMenu()
    {
        double height = 150;
        double width = 200;

        // Animation
        void callbackH(double inputH) => dropdownMenu.HeightRequest = inputH;
        void callbackW(double inputW) => dropdownMenu.WidthRequest = inputW;

        uint rate = 24;
        dropdownMenu.Animate("AnimHeightDropdownMenu", callbackH, dropdownMenu.Height, height, rate, 100, Easing.SinOut);
        dropdownMenu.Animate("AnimWidthDropdownMenu", callbackW, dropdownMenu.Width, width, rate, 100, Easing.SinOut);
        dropdownMenu.Padding = 3;

        _vm.IsMenuOpen = true;
    }
    /// <summary>
    /// Function to close a modal
    /// </summary>
    public void CloseDropdownMenu()
    {

        // Animation
        void callbackH(double inputH) => dropdownMenu.HeightRequest = inputH;
        void callbackW(double inputW) => dropdownMenu.WidthRequest = inputW;
        uint rate = 24;

        dropdownMenu.Animate("AnimWidthDropdownMenu", callbackW, dropdownMenu.Width, _modalWidthStart, rate, 500, Easing.SinOut);
        dropdownMenu.Animate("AnimHeightDropdownMenu", callbackH, dropdownMenu.Height, _modalHeightStart, rate, 500, Easing.SinOut);

        dropdownMenu.Padding = 0;

        _vm.IsMenuOpen= false;
    }

    private void Menu_Clicked(object sender, EventArgs e)
    {
        // If dropdown is closed
        if (dropdownMenu.Padding == 0)
        {
            OpenDropdownMenu();
            return;
        }

        CloseDropdownMenu();
    }

    private void MenuItem_Tapped(object sender, EventArgs e)
    {
        // If dropdown is open
        if (dropdownMenu.Padding != 0)
        {
            CloseDropdownMenu();
        }
    }

    private async void SwipeBackgroundDown_Swiped(object sender, SwipedEventArgs e)
    {
        // If dropdown is open
        if (dropdownMenu.Padding != 0)
        {
            CloseDropdownMenu();
        }

        // If dropdown is open
        await scrollview.ScrollToAsync(scrollview.ScrollX, scrollview.ScrollY - 5, true);
    }

    private async void SwipeBackgroundUp_Swiped(object sender, SwipedEventArgs e)
    {
        // If dropdown is open
        if (dropdownMenu.Padding != 0)
        {
            CloseDropdownMenu();
        }
        // If dropdown is open
        await scrollview.ScrollToAsync(scrollview.ScrollX, scrollview.ScrollY + 5, true);
    }
}