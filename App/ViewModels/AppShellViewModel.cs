using CommunityToolkit.Maui.ApplicationModel;
using GamHubApp.Core;
using GamHubApp.Models;
using GamHubApp.Models.Http.Responses;
using GamHubApp.Services;
using GamHubApp.Services.UI;
using GamHubApp.Views;
using Microsoft.Extensions.Logging;
using Plugin.FirebasePushNotifications;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using GamHubApp.Services.ChangedMessages;

#if DEBUG
using System.Diagnostics;
#endif

namespace GamHubApp.ViewModels;

public class AppShellViewModel : BaseViewModel
{

    public Command MailTo
    {
        get
        {
            return new Command<string>((address) => _ = Email.ComposeAsync(subject: "", body: "", to: new string[] { address }));
        }
    }

    public Command BrowseTo
    {
        get
        {
            return new Command<string>((url) => _ = Browser.OpenAsync(url, BrowserLaunchMode.SystemPreferred));
        }
    }

    public Command TopUpGemsCommand
    {
        get;
    }

    public App CurrentApp { get; private set; }
    private Fetcher dataFetcher;
    private readonly ILogger<AppShellViewModel> _logger;
    private IFirebasePushNotification _firebasePushNotification;
    private INotificationPermissions _firebasePushPermissions;
    private GemTopUpPage _gemTopUpPage;
    public AppShell MainShell { get; }
    private bool _authenticated;

    private bool _gemEnabled;
    public bool GemEnabled { 
        get=> _gemEnabled; 
        private set 
        {
            _gemEnabled = value;
            OnPropertyChanged(nameof(GemEnabled));
        }
    }

    public bool Authenticated
    {
        get { return _authenticated; }
        set 
        {
            _authenticated = value;
            OnPropertyChanged(nameof(Authenticated));
        }
    }
    private ObservableCollection<Gem> _gems;

    public ObservableCollection<Gem> Gems
    {
        get { return _gems; }
        set 
        {
            _gems = value;
            OnPropertyChanged(nameof(Gems));
        }
    }

    private bool _dealEnabled;
    public bool DealEnabled
    {
        get
        {
            return _dealEnabled;
        }
        set
        {
            _dealEnabled = value;
            OnPropertyChanged(nameof(DealEnabled));
        }
    }

    private bool _donationEnabled;
    public bool DonationEnabled
    {
        get
        {
            return _donationEnabled;
        }
        set
        {
            _donationEnabled = value;
            OnPropertyChanged(nameof(DonationEnabled));
        }
    }

    private User _userProfile;
    public User UserProfile
    {
        get { return _userProfile; }
        set 
        {
            _userProfile = value;

            // Enable the profile view
            Authenticated = value != null;

            OnPropertyChanged(nameof(UserProfile));
        }
    }

    private int _newDealsCount;
    public int NewDealsCount
    {
       get
       {
           return _newDealsCount;
       }
       set
       {
           _newDealsCount = value;
           OnPropertyChanged(nameof(NewDealsCount));
       }    
    }

    public AppShellViewModel(Fetcher fetc,
    ILogger<AppShellViewModel> logger,
    IFirebasePushNotification firebasePushNotification,
    INotificationPermissions firebasePushPermission,
        GeneralDataBase generalDB,
        GemTopUpPage gemTopUpPage)
    {
        CurrentApp = App.Current as App;
        dataFetcher = fetc;

        _logger = logger;

        _firebasePushNotification = firebasePushNotification;
        _firebasePushPermissions = firebasePushPermission;

        Task.Run(async () =>
        {
            await Task.WhenAll(
                dataFetcher.DataInit(),
                Task.Run(async () => 
                { 
                    DonationEnabled = (await fetc.GetGlobalSettings())?.DonationEnabled ?? false; 
                }), 
                fetc.RestoreSession());
            UserProfile = fetc.UserData;
        });

        TopUpGemsCommand = new Command(() => (App.Current as App).Windows[0].Page.Navigation.PushAsync(_gemTopUpPage));
        _gemTopUpPage = gemTopUpPage;

        WeakReferenceMessenger.Default.Register<GemsUpdatedMessage>(this, async (_, _) =>
        {
            await UpdateGems();
        });
    }

    /// <summary>
    /// Update the deals stored in the database
    /// </summary>
    /// <returns></returns>
    public async Task UpdateDeals()
    {
        if (Connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            // Disable the deals if we aren't connected to the internet
            DealEnabled = false;
            return;
        }

        if (!(DealEnabled = Preferences.Get(PreferencesKeys.DealPageEnable, true)))
            return;


        await dataFetcher.GetDeals();

        int newDealsCount = Preferences.Get(PreferencesKeys.NewDealCount,0) + await dataFetcher.UpdateDeals();

        // Store the new value
        Preferences.Set(PreferencesKeys.NewDealCount, newDealsCount);

        // Set the deal count
        BadgeCounterService.SetCount(newDealsCount);
    }

    /// <summary>
    /// Update the gems
    /// </summary>
    /// <returns></returns>
    public async Task UpdateGems()
    {
        try
        {
            Gems = new(await dataFetcher.GetGems());

        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#elif RELEASE
            SentrySdk.CaptureException(ex);
#endif
        }

    }

    const string _notificationKey = "Notification";

    /// <summary>
    /// Setup notification
    /// </summary>
    public async Task NotificationSetup()
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            return;

        if ((await _firebasePushPermissions.GetAuthorizationStatusAsync() is not Plugin.FirebasePushNotifications.Model.AuthorizationStatus.Granted)
            && Preferences.Get(_notificationKey, true))
#if ANDROID
            // For android we need to make sure it runs on the main thread
            MainThread.BeginInvokeOnMainThread(async () =>
#endif
            {
                bool newStatus = await _firebasePushPermissions.RequestPermissionAsync();
                Preferences.Set(_notificationKey, newStatus);
                if (!newStatus) return;
                await Task.Delay(1000);
                await _firebasePushNotification.RegisterForPushNotificationsAsync();
#if ANDROID
            });
#else
        }
        // TODO: remove once https://github.com/thomasgalliker/Plugin.FirebasePushNotifications/issues/113 is sorted 
        await Task.Delay(TimeSpan.FromSeconds(3));
#endif

        _firebasePushNotification.TokenRefreshed += this.OnTokenRefresh;
        _firebasePushNotification.NotificationOpened += OnNotificationOpened;
        _firebasePushNotification.NotificationAction += OnNotificationAction;
        _firebasePushNotification.NotificationReceived += OnNotificationReceived;
#if DEBUG
        Debug.WriteLine($"Current notification token: {_firebasePushNotification.Token}");
#endif
        await dataFetcher.SetupNotificationEntity((await SecureStorage.Default.GetAsync(AppConstant.NotificationToken))?? _firebasePushNotification.Token);

        // NOTE: this is mostly here for the devices that already have a token but don't have a notification entity
        // TODO: we may need to remove this at some point
        if (await SecureStorage.Default.GetAsync(AppConstant.NotificationToken) is null)
            await RegisterNotificationEntity(_firebasePushNotification.Token);

        _firebasePushNotification.SubscribeTopic("daily_catchup");
        _firebasePushNotification.SubscribeTopic("feed_subscription");
        _firebasePushNotification.SubscribeTopic("deal_reminder");
    }

    /// <summary>
    /// Register a new notification Entry
    /// </summary>
    /// <remarks>
    /// This should only be used if no other token is saved on this device
    /// </remarks>
    /// <param name="token">token for the NE</param>
    private async Task RegisterNotificationEntity(string token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                await SecureStorage.SetAsync(AppConstant.NotificationToken, token);
                await dataFetcher.RegisterNotificationEntity(token);
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
    }

    /// <summary>
    /// update a new notification Entry
    /// </summary>
    /// <param name="newToken">new notification token</param>
    /// <param name="oldToken">former notification token</param>
    private async Task UpdateNotificationEntity(string newToken, string oldToken)
    {
        if (!string.IsNullOrEmpty(newToken) &&
            !string.IsNullOrEmpty(oldToken))
        {
            try
            {
                await SecureStorage.SetAsync(AppConstant.NotificationToken, newToken);
                await dataFetcher.UpdateNotificationEntity(newToken, oldToken);

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
    }

    private void OnNotificationReceived(object sender, FirebasePushNotificationDataEventArgs e)
    {
        if (e.Data == null)
            return;
        
        // Update notification count
        int count = (Preferences.Get(PreferencesKeys.NotificationCount, 0))+1;
        Preferences.Set(PreferencesKeys.NotificationCount, count);
        Badge.Default.SetCount((uint)count);
    }

    private void OnNotificationAction(object sender, FirebasePushNotificationActionEventArgs e)
    {

        if (e.Action?.Id == null)
            return;

        switch (e.Action.Id)
        {
            case "open_in_app":
                OpenArticleInApp(e.Data["articleId"].ToString());
                break;
            case "open_in_browser":
                MainThread.BeginInvokeOnMainThread(async () =>
                    await Browser.OpenAsync(e.Data["url"].ToString(), new BrowserLaunchOptions
                    {
                        LaunchMode = BrowserLaunchMode.SystemPreferred,
                        TitleMode = BrowserTitleMode.Default,
                    }));
                break;
            default:
                OpenArticleInApp(e.Data["articleId"].ToString());
                break;


        }
        int count = (Preferences.Get(PreferencesKeys.NotificationCount, 1))-1;
        if (count < 0)
            count = 0;
        Preferences.Set(PreferencesKeys.NotificationCount, count);
        Badge.Default.SetCount((uint)count);
    }


    /// <summary>
    /// Event that gets triggered when a notification is opened
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnNotificationOpened(object sender, FirebasePushNotificationResponseEventArgs e)
    {
        try
        {
            if (e.Data?.Count <= 0)
                return;
            if (e.Data.ContainsKey ("articleId"))
                OpenArticleInApp(e.Data["articleId"].ToString());
            else if (e.Data.ContainsKey("dealId") && e.Data.ContainsKey("url"))
            {
                MainThread.BeginInvokeOnMainThread(async () => 
                {
                    try
                    {
                        await Shell.Current.GoToAsync("///MyDealsPage");
                    } 
                    catch 
                    {}

                    await Browser.OpenAsync(e.Data["url"].ToString(), new BrowserLaunchOptions
                    {
                        LaunchMode = BrowserLaunchMode.External
                    });
                }); 

            }
            Task.Run(async () =>
            {
                await UpdateDeals();
            });

            // Update notification count
            int count = (Preferences.Get(PreferencesKeys.NotificationCount, 1)) - 1;
            if (count < 0)
                count = 0;
            Preferences.Set(PreferencesKeys.NotificationCount, count);
            Badge.Default.SetCount((uint)count);

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
    /// Open an article in the app 
    /// </summary>
    /// <param name="articleId">id of the article to open</param>
    private void OpenArticleInApp(string articleId)
    {
        (App.Current as App).ShowLoadingIndicator();
        if (articleId is null) 
        {
            (App.Current as App).RemoveLoadingIndicator();
            return;
        }

        // Handle the nafication to the page on the main thread
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            Article article = await dataFetcher.GetArticle(articleId);

            if (article is null)
            {
                (App.Current as App).RemoveLoadingIndicator();
                return;
            }

            // To make sure the navigation stack isn't empty
            await Shell.Current.GoToAsync("///NewsPage");


            await App.Current.Windows[0].Page.Navigation.PushAsync(new ArticlePage(article));
            (App.Current as App).RemoveLoadingIndicator();

        });
    }

    private async void OnTokenRefresh(object sender, FirebasePushNotificationTokenEventArgs e)
    {
#if DEBUG
        Debug.WriteLine($"New notification token: {e.Token}");
#endif
        string newToken = e.Token;
        string oldToken = await SecureStorage.Default.GetAsync(AppConstant.NotificationToken);
        if (string.IsNullOrEmpty(oldToken))
        {
            await RegisterNotificationEntity(newToken);
            return;
        }

        // Update Notification Entity otherwise
        await UpdateNotificationEntity(newToken, oldToken);

    }

    public void PostAuthProcess(AuthResponse res) 
    {
        // Save user info
        dataFetcher.SaveUserInfo(res.UserData);

        // Save the session
        _ = dataFetcher.SaveSession(res.Session);

        // Set user data
        UserProfile = res.UserData;

        // Sync gems if any
        _ = dataFetcher.UserGemsSync(); 
    }

    public void Appearing()
    {
        try
        {
            if (dataFetcher.Culture is null)
            {
                dataFetcher.SetCultureInfo().Wait();
                if (dataFetcher.Culture is not null)
                GemEnabled = dataFetcher.Culture?.RegionCode == "EU" ||
                                dataFetcher.Culture?.RegionCode == "NA";
                return;
            }
            GemEnabled = dataFetcher.Culture?.RegionCode == "EU" ||
                            dataFetcher.Culture?.RegionCode == "NA";

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

}
