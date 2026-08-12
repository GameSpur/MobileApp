using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.ApplicationModel;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using GamHubApp.Core;
using GamHubApp.Models;
using GamHubApp.Services;
using GamHubApp.ViewModels;
using GamHubApp.Views;
using GamHubApp.Views.PopUps;
using Maui.RevenueCat.InAppBilling.Services;
using Microsoft.Maui.Controls.Shapes;
using Newtonsoft.Json;
using Plugin.FirebasePushNotifications;
using System.Collections.ObjectModel;

#if DEBUG
using System.Diagnostics;
#endif
#if ANDROID
using static Android.Provider.Settings;
#endif

namespace GamHubApp;

public partial class App : Application
{
    private GeneralDataBase _generalDb;
    private BackUpDataBase _backupDb;
    private Window _window;

    public bool IsLoading { get; private set; }
    private AppShell Shell { get; set; }
    public Popup LoadingIndicator { get; private set; }
    public Fetcher DataFetcher { get; set; }
    public static string ProdHost { get; } = "api.gamhub.io";
    public static string LocalHost { get; } = "gamhubdev.ddns.net";
    public User SaveInfo { get; private set; }
    public Collection<Partner> Partners { get; private set; }
    public Collection<Deal> Deals { get; private set; }
    public string InstanceID { get; set; }
    /// <summary>
    /// Date first registered to determin when is the best time to ask for user review
    /// </summary>
    public DateTime DateFirstRun { get; set; }
    public static string GeneralDBpath { get; private set; }
    public static string PathDBBackUp { get; private set; }
    private readonly IRevenueCatBilling _revenueCat;

    public enum PageType
    {
        about,
        article,
        bookmark,
        news,
        source
    }
    public App(Fetcher fetc, 
               AppShell shell, 
               GeneralDataBase generalDataBase, 
               BackUpDataBase backUpDataBase,
                IRevenueCatBilling revenueCat)
    {
        _generalDb = generalDataBase;
        _backupDb = backUpDataBase;

        DataFetcher = fetc;
        Shell = shell;
        Application.Current.UserAppTheme = AppTheme.Dark;


        InitializeComponent();
        _revenueCat = revenueCat;
    }

    /// <summary>
    /// Show the popup loading indicator
    /// </summary>
    public void ShowLoadingIndicator(Page rootPage = null)
    {

       if (IsLoading)
           return;
       IsLoading = true;

       OpenPopUp (this.LoadingIndicator = new LoadingPopUp(), rootPage);

    }

    /// <summary>
    /// Remove the popup loading indicator
    /// </summary>
    public void RemoveLoadingIndicator()
    {
       if (!IsLoading)
           return;


       IsLoading = false;

        // Close the popup
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await this.LoadingIndicator.CloseAsync();

            }
            catch (Exception
#if DEBUG
            ex
#endif
            )
            {
#if DEBUG
                Debug.WriteLine(ex);
#endif
            }
        });
    }

    /// <summary>
    /// Process ran if the app was previously used in offline mode
    /// </summary>
    private async Task RecoverFromOffline()
    {
        bool connectivity = Connectivity.NetworkAccess == NetworkAccess.Internet;
        if (Preferences.Get(PreferencesKeys.OfflineLastRun, false) &&
            connectivity)
        {
            var feeds = new ObservableCollection<Feed>(await _generalDb.GetFeeds());
            List<Task> tasks = new List<Task>();
            for (int i = 0; i < feeds.Count; i++)
                tasks.Add(DataFetcher.UpdateFeed(feeds[i]));

            await Task.WhenAll(tasks);
        }
        Preferences.Set(PreferencesKeys.OfflineLastRun, !connectivity);
    }

    protected override void OnStart()
    {
        SetupInstance().GetAwaiter();

        Task.Run(RecoverFromOffline);

        // Register the date of the first run
        DateFirstRun = Preferences.Get(nameof(DateFirstRun), DateTime.MinValue);
        if (DateFirstRun == DateTime.MinValue)
        {
            // Set the property
            DateFirstRun = DateTime.Now;

            // Register this date as the first date
            Preferences.Set("date", DateFirstRun);

        }

        // Reset notificaiton badges
        Badge.Default.SetCount(0);
#if __IOS__
        _revenueCat.Initialize(AppConstant.RevenueCatApiKey_iOS);
#else
        _revenueCat.Initialize(AppConstant.RevenueCatApiKey_Android);
#endif
        base.OnStart();
    }
    protected override Window CreateWindow(IActivationState activationState)
    {
        LoadingIndicator = new LoadingPopUp();

        _generalDb.Init()?.GetAwaiter();
        _backupDb.Init()?.GetAwaiter();
        DataFetcher.LoadBookmarks().GetAwaiter();
        DataFetcher.UpdateBackupSources().GetAwaiter();
        try
        {

            return _window = new Window(Shell);
        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return _window;

        }
    }
    protected override void OnSleep()
     {
         base.OnSleep();
         Page currentPage = Shell?.CurrentPage;

         if (currentPage?.ToString() == "GamHubApp.Views.ArticlePage")
         {

             ((ArticleViewModel)((ArticlePage)currentPage).BindingContext).TimeSpent.Stop();
         }
     }
     

     protected override void OnResume()
     {
         Page currentPage = Shell.CurrentPage;
            Shell.Resume();
#if IOS
        // Check if user was on a deal before that
        string lastDealViewed = Preferences.Get(PreferencesKeys.LastDealVisit, null);

        if (!string.IsNullOrEmpty(lastDealViewed))
        {
            try
            {
                Task.Run(async () => 
                {
                    Deal lastDeal = JsonConvert.DeserializeObject<Deal>(lastDealViewed);

                    if (await DataFetcher.RequestReward(lastDeal) && 
                        Convert.ToInt16(lastDeal.GemRewards) > 0)
                    {
                        OpenPopUp(new RewardPopUp(lastDeal.GemRewards), Shell.CurrentPage);
                    }

                });
                Preferences.Clear(PreferencesKeys.LastDealVisit);
            }
            catch (Exception ex)
            {
#if DEBUG
                throw new Exception($"Deal - RequestReward: {ex.Message}",ex);
#else
                SentrySdk.CaptureException(ex);
#endif
            }
        }
        _ = Shell.RefreshGems().ConfigureAwait(false);
        
#endif

        if (currentPage.ToString() == "GamHubApp.Views.ArticlePage")
         {

             ((ArticleViewModel)((ArticlePage)currentPage).BindingContext).TimeSpent.Start();
         }

         if (currentPage.ToString() == "GamHubApp.Views.NewsPage")
         {

             ((NewsPage)currentPage).OnResume();
         }

        if (currentPage.ToString() == "GamHubApp.Views.DealsPage")
        {

            ((DealsPage)currentPage).Resume();
        }

        // Reset notificaiton badges
        Badge.Default.SetCount(0);
    }
     /// <summary>
     /// Open any popup
     /// </summary>
     /// <param name="popUp">pop up to open</param>
     /// <param name="page">parent page</param>
     public void OpenPopUp(Popup popUp, Page page = null, bool bgTapToClose = true)
     {
         try
         {

             if (popUp == null)
                 return;

             if (page == null)
                 page = Shell;
             if (page.Navigation.NavigationStack.Any(p => p?.Id == popUp!.Id))
                 return;
             MainThread.BeginInvokeOnMainThread(async () => await page.ShowPopupAsync(popUp, options: new PopupOptions()
             {
                 CanBeDismissedByTappingOutsideOfPopup = bgTapToClose,
                 Shape = new RoundRectangle
                 {
                     StrokeThickness = 0
                 }
             }));
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
#else
        catch (Exception ex) 
        {
            SentrySdk.CaptureException(ex);
        }
#endif
    }

    /// <summary>
    /// Display an error message as snack bar with a link to support email
    /// </summary>
    /// <param name="message">error message to be displayed</param>
    public static async Task DisplaySoftError(string message)
    {

        await Snackbar.Make(message,
            () =>
            {
                Email.ComposeAsync(subject: "", body: "", to: new string[] { "support@gamespur.games" });
            },
            actionButtonText: "contact support").Show();
    }

    /// <summary>
    /// Setup the instance for a device
    /// </summary>
    /// <returns>a task returning the id of the instance</returns>
    public async Task<string> SetupInstance()
    {

#if IOS
        string instanceID = await SecureStorage.Default.GetAsync(AppConstant.InstanceIdKey);
        if (string.IsNullOrEmpty(instanceID))
        {
            await SecureStorage.Default.SetAsync(AppConstant.InstanceIdKey, instanceID = UIKit.UIDevice.CurrentDevice.IdentifierForVendor.ToString().ToLower().Replace("-", string.Empty).Substring(0,30));

        }

#if DEBUG
        Debug.WriteLine($"Instance: {instanceID}");
#endif
        return
#endif
        InstanceID =
#if ANDROID
            Secure.GetString(Android.App.Application.Context.ContentResolver, Secure.AndroidId);
#if DEBUG
        Debug.WriteLine($"Instance: {InstanceID}");
#endif
        await SecureStorage.Default.SetAsync(AppConstant.InstanceIdKey, InstanceID);
        return InstanceID;
#elif IOS
            instanceID;
#endif

    }
    /// <summary>
    /// Load all the partners
    /// </summary>
    /// <returns></returns>
    public async Task LoadPartners()
    {
        Partners = await DataFetcher.GetPartners();

    }

    /// <summary>
    /// Save the info relevant to the user
    /// </summary>
    public void SaveUserInfo(User user)
    {
        SaveInfo = user;

        // Save preferences
        Preferences.Set(nameof(SaveInfo), JsonConvert.SerializeObject(SaveInfo));
    }
    /// <summary>
    /// Save the info relevant to the user
    /// </summary>
    public void DeleteUserInfo()
    {
        // Save preferences
        Preferences.Remove(nameof(SaveInfo));
    }
    /// <summary>
    /// Recover the info relevant to the user
    /// </summary>
    /// <returns>true: data found | false: data not found</returns>
    public bool RecoverUserInfo()
    {

        // Get preferences
        string userDataStr = Preferences.Get(nameof(SaveInfo), string.Empty);

        if (string.IsNullOrEmpty(userDataStr))
            return false;

        // Set Userdata object
        return (SaveInfo = DataFetcher.UserData = JsonConvert.DeserializeObject<User>(userDataStr)) != null;
    }

    /// <summary>
    /// Log out the current active user
    /// </summary>
    public void LogoutCurrentAccount()
    {
        // Delete user data
        DeleteUserInfo();

        // Close the current session
        DataFetcher.KillSession();
    }

    /// <summary>
    /// Show a pop up to confirm wether or not the user wants to logout
    /// </summary>
    /// <param name="user">user</param>
    /// <returns>true: confirm | false: cancel</returns>
    public async Task<bool> ShowLogoutConfirmation(User user = null)
    {
        if (user == null)
            user = DataFetcher.UserData;

        LogoutConfirmationPopUp popUp = new(user);
        OpenPopUp(popUp);

        // Wait for a response
        while (popUp.ResponseResult == null)
            await Task.Delay(10);

        // in any case close the pop up after receiving a response
        await popUp.CloseAsync();

        return popUp.ResponseResult ?? false;
    }

    internal void OpenPopUp(object value)
    {
        throw new NotImplementedException();
    }
}
