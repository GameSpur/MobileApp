using GamHubApp.Core;
using GamHubApp.Models;
using GamHubApp.Models.Http.Payloads;
using GamHubApp.Models.Http.Responses;
using CustardApi.Objects;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using Plugin.FirebasePushNotifications;
using System.Net.Sockets;

#if ANDROID
using static Android.Provider.Settings;
#endif

#if IOS
using Maui.RevenueCat.InAppBilling.Services;
#endif
#if DEBUG
using System.Diagnostics;
#endif

namespace GamHubApp.Services;

public class Fetcher
{
    public static string ProdHost { get; } = "api.gamhub.io";
    private static string _dateFormat = "dd-MM-yyy_HH:mm:ss";
    private Session CurrentSession { get; set; }

    public string InstanceID { get; set; }
    public Service WebService { get; private set; }
    public static Collection<Source> Sources { get; set; }

    private GeneralDataBase _generalDB;
    private BackUpDataBase _backupDB;
    private Collection<Deal> _deals;
    public Collection<Deal> Deals
    {
        get => _deals;
    }
  
    private Collection<Deal> _allDeals;
    public Collection<Deal> AllDeals
    {
        get => _allDeals;
    }

    public User UserData { get; set; }
    public DeviceCultureInfo Culture { get; private set; }
    public List<Article> Bookmarks { get; private set; }
    public string NeID { get; private set; }
    public List<Gem> Gems { get; private set; }
    public Dictionary<string, string> Headers { get; private set; }

    private INotificationPermissions _firebasePushPermissions;
    private List<GamePlatform> _platforms = new();
    public string PlatformsStr { get; private set; }

#if IOS
    private readonly IRevenueCatBilling _revenueCatBilling;
#endif
    public Fetcher(GeneralDataBase generalDataBase, 
                   BackUpDataBase backUpDataBase,
                   INotificationPermissions notificationPermissions,
                   Service wService
#if IOS
                   ,IRevenueCatBilling revenueCatBilling)
#else
        )
#endif
    {
        // Set webservice
        WebService = wService;

        _generalDB = generalDataBase;
        _backupDB = backUpDataBase;
        _firebasePushPermissions = notificationPermissions;

        Task.WhenAll([
            GetSources(),
            SetCultureInfo(),
            GetDRMs()
            ]).GetAwaiter();
    }

    /// <summary>
    /// Executes the specified asynchronous action with retry logic.
    /// Retries the action up to the specified number of times if it fails.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the action.</typeparam>
    /// <param name="action">The asynchronous action to execute.</param>
    /// <param name="retries">The number of retry attempts (default is 3).</param>
    private static async Task<T> WithRetryAsync<T>(Func<Task<T>> action, int retries = 3)
    {
        for (int attempt = 1; attempt <= retries; attempt++)
        {
            if (!Fetcher.CheckFeasability())
                return default;
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) 
            when (
                ex.InnerException is SocketException ||
                ex.Message.Contains("hostname nor servname provided", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("NameResolutionFailure", StringComparison.OrdinalIgnoreCase))
            {
                // DNS failure — retry
                if (attempt == retries)
                    return default; 

                await Task.Delay(300 * attempt); 
            }

            catch (Exception ex) 
            when (ex.Message.Contains("The operation was canceled."))
            {
                // Snail retry 🐌
                if (attempt == retries)
                    return default;

                await Task.Delay(300 * attempt);
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

        return default;
    }

    /// <summary>
    /// Get the last 2 months worth of feed
    /// </summary>
    /// <returns>last 2 months worth of feed</returns>
    public async Task<Collection<Article>> GetMainFeedUpdate()
    {
        if (!Fetcher.CheckFeasability())
            return [];
        ResetHandler();

        try
        {
            var header = (Headers?.Count ?? 0) > 0 ? Headers : await GetHeaders();
            return await WithRetryAsync(() =>
                this.WebService.Get<Collection<Article>>(controller: "feeds",
                                                         action: "update",
                                                         singleUseHeaders: header,
                                                         parameters: [DateTime.Now.AddMonths(-2).ToString(_dateFormat)],
                                                         jsonBody: null,
                                                         unSuccessCallback: e => _ = HandleHttpException(e)));
        }
#if DEBUG
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return [];
        }
#else
        catch
        {
            return []; 
        }
#endif
    }

    /// <summary>
    /// Get all the sources
    /// </summary>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    public async Task<Collection<Source>> GetSources()
    {
        if (!Fetcher.CheckFeasability())
            return Fetcher.Sources = [];

        ResetHandler();

        var savedSources = await _generalDB.GetSources();

        Fetcher.Sources = await WithRetryAsync(() =>
                this.WebService.Get<Collection<Source>>(controller: "sources", 
                                                        action: "getAll",
                                                        unSuccessCallback: e => _ = HandleHttpException(e)))
                                  ?? [];

        var newSources = Fetcher.Sources
                .ExceptBy(savedSources.Select(s => s.MongoId), f => f.MongoId)
                .ToList();
        List<Task> sourcesTasks = [];
        for (int i = 0; i < newSources.Count; i++)
        {
            var newSource = newSources[i];
            newSource.IsSelected = true;
            sourcesTasks.Add( _generalDB.InsertSources(newSource));
            
        }
        await Task.WhenAll(sourcesTasks);
        return Fetcher.Sources;
    }

    /// <summary>
    /// Get all the DRMs for deals
    /// </summary>
    /// <returns>DRMs</returns>
    public async Task<List<GamePlatform>> GetDRMs()
    {
        if (!CheckFeasability())
            return [];
        ResetHandler();

        if (_platforms.Count > 0)
            return _platforms;

        _platforms = (await WithRetryAsync(() =>
                (WebService.Get<DrmResponse>(controller: "deals", 
                                                 action: "platforms",
                                                 unSuccessCallback: e => _ = HandleHttpException(e)))))?.Data;
        
        PlatformsStr = string.Join('_', _platforms.Select(p => p.Id));
        return _platforms;
    }

    /// <summary>
    /// Get the feed of an article
    /// </summary>
    /// <param name="keywords">keywords of the feed</param>
    /// <param name="timeUpdate">time of the last update (if applicable)</param>
    /// <param name="needUpdate">does the feed need an update</param>
    /// <returns></returns>
    public async Task<Collection<Article>> GetFeedArticles(string keywords, string timeUpdate = null, bool needUpdate = false, CancellationTokenSource cancellationTokenSource = null)
    {
        if (!Fetcher.CheckFeasability())
            return new Collection<Article>();
        ResetHandler();

        try
        {
            if (string.IsNullOrEmpty(keywords))
                return new Collection<Article>();
            //using (cancellationTokenSource) ;
            // Convert the spaces to make it url friendly
            keywords = keywords.Trim().Replace(' ', '+');

            var haeders = (Headers?.Count ?? 0) > 0 ? Headers : await GetHeaders();
            return await WithRetryAsync(() =>
                WebService.Get<Collection<Article>>(controller: "feeds",
                                                    action: needUpdate ? "update" : null,
                                                    singleUseHeaders: haeders,
                                                    cancellationToken: cancellationTokenSource?.Token ?? default,
                                                    parameters: needUpdate ? [timeUpdate, keywords] : [keywords],
                                                    unSuccessCallback: (err) => _ = HandleHttpException(err)));
        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return new Collection<Article>();
        }
    }

    /// <summary>
    /// Get a brand new discord session from a resfresh token
    /// </summary>
    /// <param name="refreshToken">refreshToken given by discord auth</param>
    /// <returns></returns>
    public async Task<Session> RefreshDiscordSession(string refreshToken)
    {
        if (!Fetcher.CheckFeasability())
            return null;

        ResetHandler();

        try
        {
            RefreshDiscordPayload payload = new()
            {
                RefreshToken = refreshToken
            };
            RefreshSessionResponse res = await WithRetryAsync(() =>
                WebService.Post<RefreshSessionResponse>(controller: "auth",
                                                        action: "discord/refresh_token",
                                                        jsonBody: JsonConvert.SerializeObject(payload),
                                                        unSuccessCallback: e => _ = HandleHttpException(e)));

            return res?.Session;
        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return null;
        }
    }
    /// <summary>
    /// Get the latest articles since given date
    /// </summary>
    /// <param name="dateUpdate">given date as "dd-MM-yyy_HH:mm:ss"</param>
    /// <returns>latest articles the date provided</returns>
    public async Task<Collection<Article>> GetMainFeedUpdate(string dateUpdate)
    {
        if (!Fetcher.CheckFeasability())
            return [];
        ResetHandler();
        try
        {
            if (string.IsNullOrEmpty(dateUpdate))
            {
                return await GetMainFeedUpdate();
            }

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            Dictionary<string, string> headers = (Headers?.Count ?? 0) > 0 ? Headers : await GetHeaders();

            return await WithRetryAsync(() =>
                 this.WebService.Get<Collection<Article>>(controller: "feeds",
                                                               action: "update",
                                                               singleUseHeaders: headers,
                                                               parameters: new string[] { dateUpdate },
                                                               jsonBody: null,
                                                               cancellationToken: cts.Token,
                                                               unSuccessCallback: e => _ = HandleHttpException(e)))?? [];
        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#endif
            return [];
        }
    }

    /// <summary>
    /// Get the chunk articles from given date
    /// </summary>
    /// <param name="dateFrom">date from which the last article was published as "dd-MM-yyy_HH:mm:ss"</param>
    /// <param name="length">length of the chunk in hour</param>
    /// <returns>chunk articles the date provided</returns>
    public async Task<Collection<Article>> GetFeedChunk(DateTime dateFrom, int length)
    {
        if (!Fetcher.CheckFeasability())
            return [];
        ResetHandler();
        try
        {
            string[] parameters =
            [
               dateFrom.AddHours(-length).ToString("dd-MM-yyy_HH:mm:ss"),
               dateFrom.AddMinutes(-1).ToString("dd-MM-yyy_HH:mm:ss"),
            ];

            Dictionary<string, string> headers = (Headers?.Count ?? 0) > 0 ? Headers : await GetHeaders();
            return await WithRetryAsync(() =>
                this.WebService.Get<Collection<Article>>(controller: "feeds",
                                                               parameters: parameters,
                                                               singleUseHeaders: headers,
                                                               jsonBody: null,
                                                               unSuccessCallback: e => _ = HandleHttpException(e)));
        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return [];
        }
    }

    /// <summary>
    /// Get the settings
    /// </summary>
    /// <returns>Global synamic settings</returns>
    public async Task<DynamicSetting> GetGlobalSettings()
    {
        if (!Fetcher.CheckFeasability())
            return new DynamicSetting() ;
        ResetHandler();
        try
        {
            using var cts = new CancellationTokenSource();
            
            Dictionary<string,string> parameters = new ()
            {
#if ANDROID
                { "os", "android"},
#endif
#if IOS
                { "os", "iOS"},
#endif
            };
            
            return (await WithRetryAsync(() =>
                this.WebService.Get<DynamicSetting>(controller: "monitor/settings",
                                                               parameters: parameters,jsonBody: null,
                                                               unSuccessCallback: e => _ = HandleHttpException(e))));
        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#endif
            return new DynamicSetting();
        }
    }
    /// <summary>
    /// Get the trending articles from the past 5 hours
    /// </summary>
    /// <returns>Articles trending</returns>
    public async Task<Collection<Article>> GetTrendingArticles()
    {
        if (!Fetcher.CheckFeasability())
            return [];
        ResetHandler();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            
            Dictionary<string,string> parameters = new ()
            {
                { "period", "18h"},
                { "limit", "5"},
            };

            Dictionary<string, string> headers = (Headers?.Count ?? 0) > 0 ? Headers : await GetHeaders();

            var articleTrends = (await WithRetryAsync(async () =>
                 await this.WebService.Get<ArticleTrendResponse>(controller: "monitor/trends",
                                                               parameters: parameters,
                                                               singleUseHeaders: headers,
                                                               jsonBody: null,
                                                               cancellationToken: cts.Token,
                                                               unSuccessCallback: e => _ = HandleHttpException(e))))?.Data.
                                                               Select(at => at.Article).ToList();

            if (articleTrends == null)
                return [];
            return new ([.. articleTrends]);
        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#endif
            return [];
        }
    }

    /// <summary>
    /// Get the trending articles from the past 5 hours
    /// </summary>
    /// <returns>Articles trending</returns>
    public async Task<Collection<Deal>> GetTrendingDeals()
    {
        if (!Fetcher.CheckFeasability())
            return [];
        ResetHandler();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            
            Dictionary<string,string> parameters = new ()
            {
                { "period", "15d"},
                { "limit", "10"},
                { "timecons", "0"},
            };

            var dealTrends = (await WithRetryAsync(() =>
                this.WebService.Get<DealTrendResponse>(controller: "monitor/deal/trends",
                                                               parameters: parameters,
                                                               jsonBody: null,
                                                               cancellationToken: cts.Token,
                                                               unSuccessCallback: e => _ = HandleHttpException(e))))?.Data.
                                                               Select(dt =>
                                                               {
                                                                   if (dt.Deal.Expires < DateTime.Now)
                                                                       return null;
                                                                   return dt.Deal;
                                                               });

            if (dealTrends == null)
                return [];
            return new ([.. dealTrends]);

        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#endif
            return [];
        }
    }

    /// <summary>
    /// Get all the partners
    /// </summary>
    /// <returns>partners</returns>
    public async Task<Collection<Partner>> GetPartners()
    {
        if (!Fetcher.CheckFeasability())
            return [];
        ResetHandler();
        try
        {
            return await WithRetryAsync(() =>
                this.WebService.Get<Collection<Partner>>(controller: "partners",
                                                         unSuccessCallback: e => _ = HandleHttpException(e)));
        }

        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return [];
        }
    }

    /// <summary>
    /// Get all the deals
    /// </summary>
    /// <returns>all the deals</returns>
    public async Task<Collection<Deal>> GetDeals(string filterCode = null)
    {
        if (!Fetcher.CheckFeasability())
            return [];
        ResetHandler();
        try
        {
            filterCode ??= Preferences.Get(PreferencesKeys.DealFilterCode, null);

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var dealResults = await WithRetryAsync(() =>
                this.WebService.Get<Collection<Deal>>(controller: "deals",
                                                      cancellationToken: cts.Token,
                                                      unSuccessCallback: e => _ = HandleHttpException(e)));

            if (dealResults == null)
                return null;

            _allDeals = dealResults;
            //TODO: update this entire thing once we can just pass filtercode to the API
            if (filterCode == null || filterCode == PlatformsStr)
                return _deals = _allDeals;

            return _deals = [.. (_allDeals ?? []).Where((
                deal => 
                filterCode.Split('_').Contains(deal.DRM))).ToList()];
        }

        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return null;
        }
    }

    /// <summary>
    /// Get an article
    /// </summary>
    /// <param name="articleId">id of the article we want</param>
    /// <returns>retrun the article</returns>
    public async Task<Article> GetArticle(string articleId)
    {
        if (!Fetcher.CheckFeasability())
            return null;
        ResetHandler();
        try
        {

            Dictionary<string, string> headers = (Headers?.Count ?? 0) > 0 ? Headers : await GetHeaders(); 

            return await WithRetryAsync(() =>
                this.WebService.Get<Article>(controller: "article",
                                                      parameters: [articleId],
                                                      singleUseHeaders: headers,
                                                      unSuccessCallback: e => _ = HandleHttpException(e)));
        }

        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return null;
        }
    }

    /// <summary>
    /// Register a notification entity
    /// </summary>
    /// <param name="token">notification token</param>
    /// <returns>retrun the article</returns>
    public async Task RegisterNotificationEntity(string token, Dictionary<string, string> rqHeaders = null )
    {
        if (!Fetcher.CheckFeasability())
            return;
        ResetHandler();
        try
        {
            if (rqHeaders is null)
            {
                rqHeaders = new();
                if (UserData != null)
                    rqHeaders.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");

            }
#if DEBUG
            string res =
#endif
                await this.WebService.Post(controller: "monitor",
                                           action: "NE/create",
                                           parameters: new Dictionary<string, string>
                                           {
                                               { nameof(token), token }
                                           },
                                           singleUseHeaders: rqHeaders.Count > 0? rqHeaders: null,
                                           unSuccessCallback: e => _ = HandleHttpException(e));
#if DEBUG
            Debug.WriteLine($"NE/create: {res}");
#endif
            await SetupNotificationEntity(token);
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
    /// Get the status of a feed notification subscription
    /// </summary>
    /// <param name="feed">ID of the feed concerned by the subscription</param>
    /// <param name="token">notification token link to the NE of the user</param>
    public async Task<bool> CheckSubStatus(string feed, string token)
    {
        try
        {
            if (!Fetcher.CheckFeasability())
                return false;
            ResetHandler();

            Dictionary<string, string> rqHeaders = await GetHeaders();
            if (UserData != null)
                rqHeaders.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");

            var res = await WithRetryAsync(() =>
                 this.WebService.Get<SubStatusRes>(controller: "monitor",
                                                               action: "NE/feedstatus",
                                                               parameters: new Dictionary<string, string>
                                                               {
                                                                   { nameof(token), token },
                                                                   { nameof(feed), feed }
                                                               },
                                                               singleUseHeaders: rqHeaders.Count > 0? rqHeaders: null,
                                                               unSuccessCallback: e => _ = HandleHttpException(e)));
            if (res is not null)
                return res.Enabled;
            return false;
        }

        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return false;
        }
    }

    /// <summary>
    /// Subcribe to a feed notification subscription
    /// </summary>
    /// <param name="feedID">ID of the feed concerned by the subscription</param>
    /// <param name="token">notification token link to the NE of the user</param>
    public async Task SubscribeToFeed(string feedID, string token)
    {
        try
        {
            if (string.IsNullOrEmpty(feedID) || string.IsNullOrEmpty(token))
                return;
            ResetHandler();

            Dictionary<string, string> rqHeaders = await GetHeaders();
            if (UserData != null)
                rqHeaders.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");

            FeedSubPayload rqBody = new() 
            {
                Feed= feedID,
                Token= token,
            };

#if DEBUG
            string res =
#endif
                await this.WebService.Post(controller: "monitor",
                                           action: "NE/subscribe",
                                           payload: rqBody,
                                           singleUseHeaders: rqHeaders.Count > 0? rqHeaders: null,
                                           unSuccessCallback: e => _ = HandleHttpException(e));
#if DEBUG
            Debug.WriteLine($"NE/subscribe: {res}");
#endif
            return;
        }

        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return;
        }
    }

    /// <summary>
    /// Create a feed
    /// </summary>
    /// <param name="feed">ID of the feed concerned by the subscription</param>
    public async Task<Feed> CreateFeed(Feed feed)
    {
        try
        {
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return null;

            string name = feed.Title;
            string keyword = feed.Keywords;

            Dictionary<string, string> rqHeaders = await GetHeaders();
            if (UserData != null)
                rqHeaders.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");


            var res = await WithRetryAsync(() =>
                             this.WebService.Post<FeedResponse>(controller: "feeds",
                                                       action: "custom/create",
                                                       parameters: new Dictionary<string, string>
                                                       {
                                                            { nameof(name), name },
                                                            { nameof(keyword), keyword },
                                                        },
                                                       singleUseHeaders: rqHeaders.Count > 0? rqHeaders: null,
                                                       unSuccessCallback: e => _ = HandleHttpException(e)));

            // Subscribe by default
            feed.MongoID = res.Data.MongoID;
            await _generalDB.UpdateFeed(feed);
            string token = await SecureStorage.Default.GetAsync(AppConstant.NotificationToken);

            if (string.IsNullOrEmpty(token))
                return feed;
            
            await SubscribeToFeed(feed.MongoID, token);

            return res.Data;
        }

        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return null;
        }
    }

    /// <summary>
    /// Create a feed
    /// </summary>
    /// <param name="feed">ID of the feed concerned by the subscription</param>
    public async Task<Feed> UpdateFeed(Feed feed)
    {
        if (!Fetcher.CheckFeasability())
            return null;
        ResetHandler();
        try
        {
            string name = feed.Title;
            string keyword = feed.Keywords;
            string id = feed.MongoID;
            await _generalDB.UpdateFeed(feed);

            if (string.IsNullOrEmpty(id) || Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                return feed;

            Dictionary<string, string> rqHeaders = await GetHeaders();
            if (UserData != null)
                rqHeaders.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");


            return (await WithRetryAsync(() =>
                            this.WebService.Put<FeedResponse>(controller: "feeds",
                                           action: "custom/update",
                                           parameters: new Dictionary<string, string>
                                           {
                                                { nameof(name), name },
                                                { nameof(keyword), keyword },
                                                { nameof(id), id },
                                            },
                                           singleUseHeaders: rqHeaders.Count > 0 ? rqHeaders : null,
                                           unSuccessCallback: e => _ = HandleHttpException(e)))).Data;
        }

        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return null;
        }
    }

    /// <summary>
    /// Subcribe to a feed notification subscription
    /// </summary>
    /// <param name="feedID">ID of the feed concerned by the subscription</param>
    /// <param name="token">notification token link to the NE of the user</param>
    /// <returns>retrun the article</returns>
    public async Task UnsubscribeToFeed(string feedID, string token)
    {
        if (!Fetcher.CheckFeasability())
            return ;
        ResetHandler();
        try
        {
            Dictionary<string, string> rqHeaders = await GetHeaders();
            if (UserData != null)
                rqHeaders.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");

            FeedSubPayload rqBody = new() 
            {
                Feed= feedID,
                Token= token,
            };
#if DEBUG
            string res =
#endif
            await WithRetryAsync(() =>
                 this.WebService.Delete(controller: "monitor",
                                         action: "NE/unsubscribe",
                                         payload: rqBody,
                                         singleUseHeaders: rqHeaders.Count > 0? rqHeaders: null,
                                         unSuccessCallback: e => _ = HandleHttpException(e)));
#if DEBUG
            Debug.WriteLine($"NE/unsubscribe: {res}");
#endif
            return;
        }

        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return;
        }
    }

    /// <summary>
    /// Update a notification entity
    /// </summary>
    /// <param name="newToken">new notification token</param>
    /// <param name="oldToken">former notification token</param>
    /// <returns>retrun the article</returns>
    public async Task UpdateNotificationEntity(string newToken, string oldToken)
    {
        if (!Fetcher.CheckFeasability())
            return;
        ResetHandler();
        try
        {
            Dictionary<string, string> rqHeaders = new();
            if (UserData != null)
                rqHeaders.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");

            NEupdateResponse res = await WithRetryAsync(() =>
                                        this.WebService.Put<NEupdateResponse>(controller: "monitor",
                                           action: "NE/update",
                                           parameters: new Dictionary<string, string>
                                           {
                                               { nameof(oldToken), oldToken },
                                               { nameof(newToken), newToken }
                                           },
                                           singleUseHeaders: rqHeaders,
                                           unSuccessCallback: e => _ = HandleHttpException(e)));

#if DEBUG
            Debug.WriteLine($"NE/update: {res.Msg}");
#endif
            if (!res.Success)
                await RegisterNotificationEntity(newToken, rqHeaders);
            else

                await SetupNotificationEntity(newToken);
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

    private void ResetHandler()
    {
#if ANDROID
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.VanillaIceCream)
            return;
#endif
        WebService.CustomHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => true
        };
    }

    /// <summary>
    /// Save all the tokens of a session and expiration
    /// </summary>
    /// <param name="newSession"></param>
    public async Task SaveSession (Session newSession)
    {
        if (newSession != null)
            return;

        // Keep the current session
        CurrentSession = newSession;

        List<Task> tasks =
        [
            // Save sensitive data
            SecureStorage.SetAsync(nameof(Session.AccessToken), newSession.AccessToken),
            SecureStorage.SetAsync(nameof(Session.RefreshToken), newSession.RefreshToken),
            SecureStorage.SetAsync(nameof(Session.TokenType), newSession.TokenType),
        ];
        
        string token = await SecureStorage.Default.GetAsync(AppConstant.NotificationToken);
        if (!string.IsNullOrEmpty(token))
            tasks.Add(UpdateNotificationEntity(token, token));

        await Task.WhenAll(tasks);

        // Save regular data about the session
        Preferences.Set(nameof(Session.ExpiresIn), newSession.ExpiresIn);
        Preferences.Set(nameof(Session.Created), newSession.Created);
    }

    /// <summary>
    /// Restore the last session if any
    /// </summary>
    public async Task RestoreSession ()
    {
        if (!Fetcher.CheckFeasability())
            return;
        // Save regular data about the session
        int exp = Preferences.Get(nameof(Session.ExpiresIn), Int16.MinValue);
        string refreshToken = await SecureStorage.Default.GetAsync(nameof(Session.RefreshToken)).ConfigureAwait(false);

        // If there was no session just leave
        if (string.IsNullOrEmpty(refreshToken)) return;

        DateTime dt = Preferences.Get(nameof(Session.Created), DateTime.MinValue);

        // Check if the token expired
        if ((DateTime.UtcNow - dt).TotalSeconds >= exp)
        {
            // Refresh the token
            await SaveSession(await RefreshDiscordSession(refreshToken));

            return;
        }

        if (!EvaluateCurrentSession()) return;

        // Save sensitive data
        var accessTask = SecureStorage.Default.GetAsync(nameof(Session.AccessToken));
        var tokenTypeTask = SecureStorage.Default.GetAsync(nameof(Session.TokenType));

        await Task.WhenAll(accessTask, tokenTypeTask);

        // Fill up the current session with the data gathered from the previous one
        CurrentSession = new()
        {
            AccessToken = accessTask.Result,
            RefreshToken = refreshToken,
            TokenType = tokenTypeTask.Result,
            ExpiresIn = exp
        };


    }

    /// <summary>
    /// Evaluate the validity and state of the current session
    /// </summary>
    /// <returns>true -> session alive; false -> dead session</returns>
    private bool EvaluateCurrentSession()
    {
        if (!RecoverUserInfo())
        {
            // Kill the previous session
            KillSession();
            return false;

        }
        return true;
    }

    /// <summary>
    /// Get common header
    /// </summary>
    /// <returns></returns>
    private async Task<Dictionary<string,string>> GetHeaders()
    {
        try
        {
            var apiKey = Csign.GenerateApiKey();

            string instanceID = await SecureStorage.Default.GetAsync(AppConstant.InstanceIdKey);
            if (string.IsNullOrEmpty(instanceID))
            {
                // Reissue an instanceID
                instanceID = await SetupInstance();
            }
    #if DEBUG
            Debug.WriteLine($"ApiKey: {apiKey}");
    #endif
            return Headers = new Dictionary<string, string>
            {
                { "x-api-key", apiKey},
                { "instance", instanceID},
            };

        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif
            return [];
        }

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
    /// Adding a hook to an article
    /// </summary>
    /// <param name="article">article hooked</param>
    public async Task RegisterHook(Article article)
    {
        if (!Fetcher.CheckFeasability())
            return ;
        ResetHandler();
        var headers = await GetHeaders();
#if DEBUG
        Debug.WriteLine($"Instance: {headers["instance"]}");
#endif
        var paramss = new Dictionary<string, string>
        {
            { nameof(article), article.MongooseId},
        };

      await WebService.Post(controller: "monitor",
                              action: "register",
                              singleUseHeaders: headers,
                              parameters: paramss,
                              unSuccessCallback: e => _ = HandleHttpException(e)
                               );
    }

    /// <summary>
    /// Adding a hook to a deal
    /// </summary>
    /// <param name="deal">deal to be hooked</param>
    public async Task RegisterHook(Deal deal)
    {
        if (deal is null)
            return;
        if (!Fetcher.CheckFeasability())
            return ;
        var headers = await GetHeaders();
#if DEBUG
        Debug.WriteLine($"Instance: {headers["instance"]}");
        Debug.WriteLine($"API Key: {headers["x-api-key"]}");
#endif
        var paramss = new Dictionary<string, string>
        {
            { nameof(deal), deal.Id},
        };

        await WebService.Post(controller: "monitor",
                              action: "register",
                              singleUseHeaders: headers,
                              parameters: paramss,
                              unSuccessCallback: e => _ = HandleHttpException(e)
                               );
    }

    /// <summary>
    /// Request rewards from deal
    /// </summary>
    /// <remarks>
    /// If the user made a purchase on this deal. they will be rewarded the gem amount if no
    /// </remarks>
    /// <param name="deal">deal where reward is requested</param>
    public async Task<bool> RequestReward(Deal deal)
    {
        if (!Fetcher.CheckFeasability())
            return false;
        var headers = await GetHeaders();

#if DEBUG
        Debug.WriteLine($"Instance: {headers["instance"]}");
        Debug.WriteLine($"API Key: {headers["x-api-key"]}");
#endif

        if (UserData != null)
            headers.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");

        var paramss = new Dictionary<string, string>
        {
            { nameof(deal), deal.Id},
        };

#if DEBUG
        Debug.WriteLine($"{nameof(deal)}: {paramss[nameof(deal)]}");
#endif

        return (await WebService.Get<GemsRewardResponse>(controller: "gems",
                              action: "request/deal",
                              singleUseHeaders: headers,
                              parameters: paramss,
                              unSuccessCallback: e => _ = HandleHttpException(e)
                               )).Rewarded;
    }

    /// <summary>
    /// Adding a hook to an article
    /// </summary>
    /// <param name="article">article from which the reward is requested</param>
    public async Task<bool> RequestReward(Article article)
    {
        if (!Fetcher.CheckFeasability())
            return false;
        var headers = await GetHeaders();
        if (UserData != null)
            headers.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");

        var paramss = new Dictionary<string, string>
        {
            { nameof(article), article.MongooseId},
        };

       return (await WithRetryAsync(() =>
                 WebService.Get<GemsRewardResponse>(controller: "gems",
                              action: "request/article",
                              singleUseHeaders: headers,
                              parameters: paramss,
                              unSuccessCallback: e => _ = HandleHttpException(e))
                               )).Rewarded;
    }

    /// <summary>
    /// SYnc users and their gems
    /// </summary>
    /// <returns> true ➡️ user gems have been synced | false ➡️ either user not logged in or gems can't be synced</returns>
    public async Task<bool> UserGemsSync()
    {
        if (!Fetcher.CheckFeasability() || UserData is null)
            return false;

        var headers = await GetHeaders();
        if (UserData != null)
            headers.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");

        await WithRetryAsync(() =>
                 WebService.Put<GemsRewardResponse>(controller: "gems",
                              action: "sync",
                              singleUseHeaders: headers,
                              unSuccessCallback: e => _ = HandleHttpException(e)
                               ));
        return true;
    }

    /// <summary>
    /// Get all the gems from the user
    /// </summary>
    public async Task<List<Gem>> GetGems()
    {
        try
        {
        if (!Fetcher.CheckFeasability())
                return [] ;
            ResetHandler();

        var headers = await GetHeaders();
        if (UserData != null)
            headers.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");


        return Gems =  (await WithRetryAsync(() =>
                                 WebService.Get<UserGemsResponse>(controller: "gems",
                                              singleUseHeaders: headers,
                                              unSuccessCallback: e => _ = HandleHttpException(e)
                                               )))?.Data ?? [];

        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Set a reminder for a deal
    /// </summary>
    /// <param name="deal"></param>
    /// <returns></returns>
    public async Task SetDealReminder(Deal deal)
    {
        if (!Fetcher.CheckFeasability() || 
            await _firebasePushPermissions.GetAuthorizationStatusAsync() is not Plugin.FirebasePushNotifications.Model.AuthorizationStatus.Granted ||
            !Preferences.Get(PreferencesKeys.DealReminderEnabled, true))
            return ;
        ResetHandler();

        Dictionary<string, string> rqHeaders = new();
        if (UserData != null)
            rqHeaders.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");

        if (string.IsNullOrEmpty(NeID))
            await SetupNotificationEntity(await SecureStorage.Default.GetAsync(AppConstant.NotificationToken));

#if DEBUG
        var res =
#endif
        await WithRetryAsync(() =>
                 WebService.Post<ReminderResponse>(controller: "deals",
                                               action: "reminder/set",
                                               singleUseHeaders: rqHeaders,
                                               parameters: new Dictionary<string, string>
                                               {
                                                   { nameof(deal), deal.Id },
                                                   { "ne", NeID }
                                               },
                                               unSuccessCallback: e => _ = HandleHttpException(e)
                                                ));
#if DEBUG
        Debug.WriteLine($"Reminder set: {res.Response}");
#endif
    }

    /// <summary>
    /// Get the notification entity of a token
    /// </summary>
    /// <param name="token">token linked to the notification entity</param>
    /// <returns></returns>
    public async Task<NotificationEntity> GetNotificationEntity(string token)
    {
        if (!Fetcher.CheckFeasability())
            return null;
        ResetHandler();


        Dictionary<string, string> headers = (Headers?.Count ?? 0) > 0 ? Headers : await GetHeaders();

        return (await WithRetryAsync(() =>
                 WebService.Get<NeResponse>(controller: "monitor",
                                               action: "NE",
                                               singleUseHeaders: headers,
                                               parameters: new Dictionary<string, string>
                                               {
                                                   { nameof(token), token },
                                               },
                                               unSuccessCallback: e => _ = HandleHttpException(e)
                                                )))?.Data;
    }

    /// <summary>
    /// Get the culture infor of the device
    /// </summary>
    public async Task<DeviceCultureInfo> GetCultureInfo()
    {
        if (!Fetcher.CheckFeasability())
            return null;
        ResetHandler();

        Dictionary<string, string> headers = (Headers?.Count ?? 0) > 0 ? Headers : await GetHeaders();

        return await WithRetryAsync(() =>
                     WebService.Get<DeviceCultureInfo>(controller: "monitor",
                                                   action: "culture",
                                                   singleUseHeaders: headers,
                                                   unSuccessCallback: e => _ = HandleHttpException(e)
                                                    ));
    }

    /// <summary>
    /// Kill a session
    /// </summary>
    public void KillSession()
    {
        // Empty the property
        CurrentSession = null;

        // Clear all data stored
        SecureStorage.Remove(nameof(Session.AccessToken));
        SecureStorage.Remove(nameof(Session.TokenType));
        SecureStorage.Remove(nameof(NeID));


    }

    /// <summary>
    /// Recover the info relevant to the user
    /// </summary>
    /// <returns>true: data found | false: data not found</returns>
    public bool RecoverUserInfo()
    {

        // Get preferences
        string userDataStr = Preferences.Get(nameof(UserData), string.Empty);

        if (string.IsNullOrEmpty(userDataStr))
            return false;

        // Set Userdata object
        return (UserData = JsonConvert.DeserializeObject<User>(userDataStr)) != null;
    }

    /// <summary>
    /// Save the info relevant to the user
    /// </summary>
    public void SaveUserInfo(User user)
    {
        UserData = user;

        // Save preferences
        Preferences.Set(nameof(UserData), JsonConvert.SerializeObject(UserData));
    }

    /// <summary>
    /// Method to handle exceptions
    /// </summary>
    /// <param name="err"></param>
    /// <exception cref="Exception"></exception>
    private async Task HandleHttpException(HttpResponseMessage err)
    {
        string errMsg = await err.Content.ReadAsStringAsync();
#if DEBUG
        Debug.WriteLine(errMsg);
#endif
        if (errMsg.Contains("internet connection") || 
            errMsg.Contains("Connection failure") || 
            errMsg.Contains("An SSL error has occurred and a secure connection") ||
            errMsg.Contains("OperationCanceled")
            )
            // If the error is being thrown because there is no internet: there is no point reporting it 
            return;
#if DEBUG
        throw new Exception(errMsg);
#else
        SentrySdk.CaptureException(new Exception(errMsg));
#endif
    }

    /// <summary>
    /// check feasability of the request
    /// </summary>
    /// <remarks> This method is static for now be we can change that</remarks>
    /// <returns>true == Feasable</returns>
    private static bool CheckFeasability()
    {
        return Connectivity.NetworkAccess == NetworkAccess.Internet;
    }

    /// <summary>
    /// Set the culture info of the device. either fetching it from the server of from the storage
    /// </summary>
    public async Task SetCultureInfo()
    {
        try
        {
            string ciRaw = Preferences.Get(PreferencesKeys.CultureInfo, string.Empty);

            if (string.IsNullOrEmpty(ciRaw))
            {
                if ((Culture = await GetCultureInfo()) is null)
                    return;

                Preferences.Set(PreferencesKeys.CultureInfo, JsonConvert.SerializeObject(Culture));
                return;
            }

            Culture = JsonConvert.DeserializeObject<DeviceCultureInfo>(ciRaw);

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

    #region Local Actions

    /// <summary>
    /// Delete a feed
    /// </summary>
    /// <param name="feed">Feed we want to delete</param>
    /// <returns>Update status</returns>
    public async Task<int> DeleteFeed(Feed feed)
    {
        return await _generalDB.DeleteFeed(feed);
    }

    /// <summary>
    /// Delete a article
    /// </summary>
    /// <param name="article">article we want to delete</param>
    /// <returns>Update status</returns>
    public async Task<int> DeleteArticle(Article article)
    {
        int res = -1;
        Article item = Bookmarks.SingleOrDefault(b => b.MongooseId == article.MongooseId);
        
        if (item is null)
            return res;

        res = await _generalDB.DeleteArticleBookmark(article);
        if (res == 1)
        {
            Bookmarks.RemoveAt(Bookmarks.IndexOf(item));
    }

        return res;
    }

    /// <summary>
    /// Check if an article exist
    /// </summary>
    /// <param name="articleId">Id of the article we want to check</param>
    /// <returns>true -> exist; false -> doesn't</returns>
    public bool ArticleExist (string articleId)
    {
        if (Bookmarks is null)
            return false;

        return Bookmarks.SingleOrDefault(a => a.MongooseId == articleId) is not null;
    }

    /// <summary>
    /// Load/Reload all the Bookmarks
    /// </summary>
    public async Task LoadBookmarks ()
    {
        
        Bookmarks = [.. await _generalDB.GetArticles()];
    }

    /// <summary>
    /// Load/Reload all the Bookmarks
    /// </summary>
    public async Task UpdateBackupSources ()
    {
        try
        {
            if (Fetcher.Sources is not null)
                await _backupDB.UpdateSources([.. Fetcher.Sources]);

        } catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
#else
            SentrySdk.CaptureException(ex);
#endif

        }
    }

    /// <summary>
    /// Set the notification id of the current session
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async Task SetupNotificationEntity (string token)
    {
        try 
        { 

            NeID = await SecureStorage.Default.GetAsync(nameof(NeID));
            if (!string.IsNullOrEmpty(NeID) || string.IsNullOrEmpty(token))
                return;

            var ne = await GetNotificationEntity(token);
            if (ne is null)
            {
                Dictionary<string, string> rqHeaders = new();
                if (UserData != null)
                    rqHeaders.Add("Authorization", $"{await SecureStorage.Default.GetAsync(nameof(Session.TokenType))} {await SecureStorage.Default.GetAsync(nameof(Session.AccessToken))}");


                await RegisterNotificationEntity(token);
                return;
            }
            await SecureStorage.SetAsync(nameof(NeID), NeID = ne?.Id);
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
    /// Add a bookmark to an article
    /// </summary>
    /// <param name="article">article to be bookmarked</param>
    /// <returns>true -> exist; false -> doesn't</returns>
    public async Task<int> AddBookmark (Article article)
    {
        int res = -1;
        res =  await _generalDB.InsertArticleBookmark(article);
        if (res == 1)
            Bookmarks.Insert(0, article);
        return res;
    }

    /// <summary>
    /// Update deals
    /// </summary>
    /// <returns>count of new deals</returns>
    public async Task<int> UpdateDeals()
    {
        return await _generalDB.UpdateDeals(_deals);
    }

#endregion

}
