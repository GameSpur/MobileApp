using CommunityToolkit.Mvvm.Messaging;
using GamHubApp.Helpers.Tools;
using GamHubApp.Models;
using GamHubApp.Services;
using GamHubApp.Services.ChangedMessages;
using GamHubApp.Views;
using MvvmHelpers;
using System.Collections.ObjectModel;
#if DEBUG
using System.Diagnostics;
#endif

namespace GamHubApp.ViewModels;

public class NewsViewModel : BaseViewModel
{
    // Hour interval
    private const int _refreshInterval = 24;
    private string _prevSearch;
    private string _lastCallDateTime;
    private bool _isSearching;
    public bool IsSearching
    {
        get
        {
            return _isSearching;
        }
        set
        {
            _isSearching = value;
            if (!_isSearching)
                IsSearchLoading = false;
            OnPropertyChanged(nameof(IsSearching));
        }
    }

    private bool _isSearchOpen;
    public bool IsSearchOpen
    {
        get
        {
            return _isSearchOpen;
        }
        set
        {
            _isSearchOpen = value;
            OnPropertyChanged(nameof(IsSearchOpen));
        }
    }
    private Collection<Feed> Feeds {  get; set; }
    private string _searchText;

    public string SearchText
    {
        get
        {
            return _searchText;
        }
        set
        {
            _searchText = value;

            IsCurrentSearchSaved = Feeds.Any<Feed>(feed => feed.Keywords.ToLower() == value.ToLower());
            OnPropertyChanged(nameof(SearchText));
        }
    }
    private Feed _currentFeed;

    public Feed CurrentFeed
    {
        get { return _currentFeed;; }
        set 
        {
            _currentFeed = value;
            OnPropertyChanged(nameof(CurrentFeed));
        }
    }

    public Command OpenSearch
    {
        get
        {
            return new Command(() =>
            {
                IsSearching = true;

            });
        }
    }

    private bool _isCurrentSearchSaved;

    public bool IsCurrentSearchSaved
    {
        get { return _isCurrentSearchSaved; }
        set 
        { 
            _isCurrentSearchSaved = value;
            OnPropertyChanged(nameof(IsCurrentSearchSaved));
        }
    }

    private bool _isSearchProcessed;

    public bool IsSearchProcessed
    {
        get { return _isSearchProcessed; }
        set 
        {
            _isSearchProcessed = value;
            OnPropertyChanged(nameof(IsSearchProcessed));
        }
    }

    public Command SaveSearch
    {
        get
        {
            return new Command(async () =>
            {
                if (string.IsNullOrEmpty(SearchText))
                    return;
                
                IsCurrentSearchSaved = !_isCurrentSearchSaved;

                Feed feedTarget = _currentFeed ?? Feeds.FirstOrDefault(fd =>  fd.Title.ToLower() == SearchText.ToLower());

                if (_isCurrentSearchSaved)
                {
                    CurrentFeed.Id = Guid.NewGuid().ToString();
                    CurrentFeed.Title = SearchText;
                    CurrentFeed.Keywords = SearchText; 
                    CurrentFeed.IsSaved = true;

                    await _generalDB.InsertFeed(_currentFeed);
                    Feeds.Add(_currentFeed);

                    // Update the feeds on the servers
                    await Task.Run(async () => await CurrentApp.DataFetcher.CreateFeed(_currentFeed));
                    return;

                }

                if (feedTarget?.Keywords == null)
                    feedTarget = Feeds.FirstOrDefault(f => f.Keywords.ToLower() == SearchText.ToLower());

                await _generalDB.DeleteFeed(feedTarget);

                Feeds.Remove(feedTarget);
            });
        }
    }
    public Command CloseSearch
    {
        get
        {
            return new Command(async () =>
            {
                IsSearching = false;

                if (string.IsNullOrEmpty(_searchText) || !IsSearchProcessed) return;


                // Scroll up before fetching the items
                WeakReferenceMessenger.Default.Send(this);

                await Task.Run(() =>
                {

                    _prevSearch = null;

                    // Empty the search bar
                    SearchText = string.Empty;

                    // Get former feed from memory
                    Articles = new(_feedMemory);

                    IsSearchProcessed = false;
                }).ConfigureAwait(false) ;

            });
        }
    }

    // Property list of articles
    private ObservableRangeCollection<Article> _articles;

    public ObservableRangeCollection<Article> Articles
    {
        get
        {
            ObservableRangeCollection<Article> upArticles = [];
            
            try
            {
            for (int i = 0; i < _articles?.Count; i++)
            {
                var article = _articles[i];
                article.Source = _sources.SingleOrDefault(src => src.MongoId == article.SourceId);

                if (article.Source?.IsSelected ?? true)
                    upArticles.Add(article);
            }

            }

            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine(ex);

#else
                SentrySdk.CaptureException(ex);
#endif
            }
            return upArticles;
        }
        set
        {
            _articles = value;

            if (IsSearching != true)
                _feedMemory = new (_articles);

            OnPropertyChanged(nameof(Articles));
            SetProperty(ref _articles, value);
        }
    }
    private ObservableCollection<Article> _trendingArticles = [];

    public ObservableCollection<Article> TrendingArticles
    {
        get
        {
            ObservableRangeCollection<Article> upArticles = [];
            try
            {
            for (int i = 0; i < _trendingArticles?.Count; i++)
            {
                var article = _trendingArticles[i];
                var source = _sources.SingleOrDefault(src => src.MongoId == article.Source.MongoId);

                if (source != null)
                    article.Source = source;

                if (article.Source?.IsSelected ?? true)
                    upArticles.Add(article);
            }
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine(ex);

#else
                SentrySdk.CaptureException(ex);
#endif
            }
            return upArticles;
        }
        set
        {
            _trendingArticles = value;

            OnPropertyChanged(nameof(TrendingArticles));
        }
    }

    // Property list of articles
    private ObservableCollection<Article> _unnoticedArticles;

    public ObservableCollection<Article> UnnoticedArticles
    {
        get { return _unnoticedArticles; }
        set
        {
            _unnoticedArticles = value;
            WeakReferenceMessenger.Default.Send(new UnnoticedArticlesChangedMessage(_unnoticedArticles));

            OnPropertyChanged(nameof(UnnoticedArticles));
        }
    }
    private bool _onTopScroll = true;

    public bool OnTopScroll
    {
        get { return _onTopScroll; }
        set 
        {
            _onTopScroll = value;
            OnPropertyChanged(nameof(OnTopScroll));
        }
    }


    public App CurrentApp { get; }

    private GeneralDataBase _generalDB;
    private BackUpDataBase _backUpDataBase;
    private SourceService _sourceService;

    // Command to refresh the news feed
    private readonly Command _refreshFeed;

    public Command RefreshFeed
    {
        get { return _refreshFeed; }
    }

    public Command LoadSearch
    {
        get; private set;
    }
    // Command to add a Bookmark
    private readonly Command _shareArticle;

    public Command ShareArticle
    {
        get { return _shareArticle; }
    }
    // See detail of the article
    public Command GoToDetail
    {
        get
        {
            return new Command( (id) =>
            {
                var articlePage = new ArticlePage(_articles.FirstOrDefault(art => art.Id == id.ToString()));


                _= App.Current.Windows[0].Page.Navigation.PushAsync(articlePage);
            }); ;
        }
    }
    public Command RefreshBottomCommand { get; private set; }
    public Command UncoverNewArticlesCommand { get; private set; }

    private bool _isRefreshing;
    private ObservableRangeCollection<Article> _feedMemory;

    public bool IsRefreshing
    {
        get { return _isRefreshing; }
        set
        {
            _isRefreshing = value;
            OnPropertyChanged(nameof(IsRefreshing));
        }
    }

    public bool IsFirstLoad { get; private set; } = true;
    private bool _isSearchLoading;
    public bool IsSearchLoading
    {
        get { return _isSearchLoading; }
        set 
        {
            _isSearchLoading = value;
            OnPropertyChanged(nameof(IsSearchLoading));
        }
    }
    public bool IsLoadingChunks { get; private set; }

    private List<Source> _sources = [];

    public List<Source> Sources
    {
        get { return _sources; }
        set 
        {
            _sources = value; 
        }
    }


    public NewsViewModel(GeneralDataBase generalDataBase, BackUpDataBase backUpDataBase, SourceService sourceService)
    {
        CurrentApp = App.Current as App;
        _generalDB = generalDataBase;
        _backUpDataBase = backUpDataBase;
        _sourceService = sourceService;

        // Listen for updates from Settings
        _sourceService.SourcesChanged += OnSourcesChanged;

        UnnoticedArticles = new();
        Task.Run(async () =>
        {
            Articles = new([.. (await GetBackupFromDb()).OrderBy(a => a.Time)]);
            Sources = await generalDataBase.GetSources();
        });
        
        RefreshBottomCommand = new(() =>
        {
            if (IsFirstLoad || IsLoadingChunks)
                return;
            _ = LoadChunks().ConfigureAwait(false);
        });

        UncoverNewArticlesCommand = new Command(() =>
        {
            if (UnnoticedArticles == null)
                return;
            if (UnnoticedArticles.Count <= 0)
                return;

            _ = Task.Run( () =>
            {
                // Add the unnoticed articles
                UpdateArticles(UnnoticedArticles);

                // Scroll up
                WeakReferenceMessenger.Default.Send(new ScrollMainPageChangedMessage(this));

                UnnoticedArticles.Clear();

            });

        });

        CurrentFeed = new Feed();

        WeakReferenceMessenger.Default.Register<FeedUpdatedMessage>(this, (r, m) =>
        {
            Feed updatedFeed = m.Feed;
            int index = Feeds.IndexOf(Feeds.FirstOrDefault(feed => feed.Id == updatedFeed.Id));

            if (index == -1)
                return;

            Feeds[index] = updatedFeed;
        });

        // Handle if a article change sees a change of bookmark state
        WeakReferenceMessenger.Default.Register(this, (MessageHandler<object, BookmarkChangedMessage>)((r, m) =>
        {
            // Escape is the current page is the news page
            if (((IShellSectionController)Shell.Current?.CurrentItem?.CurrentItem).PresentedPage is NewsPage)
                return;
            if (m.ArticleSent is null)
                return;
            Article article = _articles.FirstOrDefault((Func<Article, bool>)(a => a.Id == m.ArticleSent.Id));
            // Get article index
            int index = _articles.IndexOf(article);

            try
            {
                if (_articles.Count > 0)
                    // Reload the Article that has been bookmarked
                    Articles[index] = m.ArticleSent;
                
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine(ex);

#else
                SentrySdk.CaptureException(ex);
#endif
            }
        }));

        _refreshFeed = new Command<bool>( async (isAll) =>
        {

            if (IsSearchOpen)
            {
                if (string.IsNullOrEmpty(SearchText))
                    return;
                // Fetch the article
                await SearchArticles();
                return;
            }

            // Refresh the time displayed
            RefreshArticlesTime();

            await Task.WhenAll(
                FetchTrendingArticles(),
                FetchNewerArticles().ContinueWith(res => IsRefreshing = false)
                );

        });

        LoadSearch = new Command(async () =>
        {
            if (IsSearchLoading || string.IsNullOrEmpty(SearchText))
                return;
            IsSearchLoading = true;

            await SearchArticles().ContinueWith((_) =>
            {
                IsSearchLoading = false;
                IsSearchProcessed = true;
            });
        });

        // Set command to share an article
        _shareArticle = new Command((id) =>
        {
            // Get selected article
            var article = _articles.FirstOrDefault(art => art.Id == id.ToString());

            _= Share.RequestAsync(new ShareTextRequest
            {
                Uri = article.Url,
                Title = "Share this article",
                Subject = article.Title,
                Text = article.Title
            });
        });


    }

    private void OnSourcesChanged(object sender, Source source)
    {
        _ = Task.Run(async () =>
        {
            int index = _sources.IndexOf(_sources.SingleOrDefault(s => s.MongoId == source.MongoId));
            if (index < 0)
                return;
            _sources[index] = source;

            OnPropertyChanged(nameof(Articles));
            OnPropertyChanged(nameof(TrendingArticles));


        }).ConfigureAwait(false);
    }

    /// <summary>
    ///  Fetch the trending articles
    /// </summary>
    public async Task FetchTrendingArticles()
    {
        var newTrandingArticles = await CurrentApp.DataFetcher.GetTrendingArticles();
        if (_trendingArticles?.Count > 0 && newTrandingArticles.Count <= 0)
            return;
        TrendingArticles = new (newTrandingArticles);
    }

    /// <summary>
    /// Fetch the newest articles
    /// </summary>
    /// <returns></returns>
    public async Task FetchNewerArticles()
    {
        if (_articles?.Count < 0)
            return;

        // Get time of the last article in date
        _lastCallDateTime = _articles?.First().FullPublishDate.ToUniversalTime().ToString("dd-MM-yyy_HH:mm:ss");

        // Get all the aricles from this date
        var articles = new ObservableRangeCollection<Article>([.. (await CurrentApp.DataFetcher
                            .GetMainFeedUpdate(_lastCallDateTime)
                            .ConfigureAwait(false))
                            .Where(article =>
                                (article.Blocked == null || article.Blocked == false) && article.Source.IsActive)]);

        if (articles.Count == 0)
            return;

        if (OnTopScroll)
        {
            // Update list of articles
            UpdateArticles(articles);
            try
            {
                // Manage backup
                _ = RefreshDB();

            }
            catch (Exception ex)
            {
#if DEBUG
                throw new (ex.Message);
#else
                SentrySdk.CaptureException(ex);
#endif
            }

        }

        else
            UnnoticedArticles = new ObservableCollection<Article>(articles);

    }
    /// <summary>
    /// Fetch all the articles
    /// </summary>
    public async Task FetchExistingArticles()
    {
        try
        {
        // Check internet
        if (Connectivity.NetworkAccess != NetworkAccess.Internet)
        {
                _ =Task.Run(static async () =>
                {
                    await Task.Delay(2000);
                    if (Shell.Current?.CurrentPage is NewsPage page)
                        await page.DisplayMessage($"You're offline, please make sure you're connected to the internet")
                             .ConfigureAwait(false);

                });

            return;
        }

        // Load the artcles of the last 24hrs
        IEnumerable<Article> fetchedArticles = (await CurrentApp.DataFetcher.GetMainFeedUpdate(DateTime.UtcNow.AddHours(-_refreshInterval).ToString("dd-MM-yyy_HH:mm:ss")).ConfigureAwait(false)).Where(article => (article.Blocked == null || article.Blocked == false) && article.Source.IsActive);

        if (fetchedArticles.Any())
            Articles = new ObservableRangeCollection<Article>(fetchedArticles);

        // Refresh the db
        await RefreshDB().ConfigureAwait(false);

        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex.Message);
#else
            SentrySdk.CaptureException(ex);
#endif
        }
    }
    /// <summary>
    /// Load the next chunk of articles
    /// </summary>
    /// <returns></returns>
    private async Task LoadChunks()
    {
        
        if (_articles.Count <= 0 || IsLoadingChunks)
            return;
                
        IsLoadingChunks = true;
        await Task.Run(async () =>
        {
            // get articles of the next 24hours after that
            var collection = (await CurrentApp.DataFetcher.GetFeedChunk(_articles.LastOrDefault().FullPublishDate, 12)).Where(article => (article.Blocked == null || article.Blocked == false) && article.Source.IsActive).ToList();
            
            Articles.AddRange(collection);

        }).ContinueWith(res => IsLoadingChunks = false);
        return;
    }

    /// <summary>
    /// Update the current article feed by adding new elements
    /// </summary>
    /// <param name="articles">new articles</param>
    private void UpdateArticles(IEnumerable<Article> articles)
    {
        // Create a copy of the input ObservableCollection
        Collection<Article> listArticle = new (articles.Reverse().ToList());

        // Lists to store articles to be added and updated
        Collection<Article> articlesToUpdate = new ();

        // Iterate through the copied list of articles
        foreach (var current in listArticle)
        {
            // Check if the current article already exists in the _articles collection
            Article existingArticle = _articles.FirstOrDefault(a => a.MongooseId == current.MongooseId);

            if (existingArticle == null)
                // Article doesn't exist in _articles, add it to the articlesToAdd list
                Articles.Insert(0,current);
            else if (!existingArticle.IsEqualTo(current))
                // Article exists in _articles, add it to the articlesToUpdate list
                articlesToUpdate.Add(current);
        }
        foreach (var articleToUpdate in articlesToUpdate)
        {
            // Find the existing article to be updated in the '_articles' collection
            Article existingArticle = _articles.FirstOrDefault(a => a.Id == articleToUpdate.Id);
            if (existingArticle != null)
            {
                // Get the index of the existing article in the 'Articles' collection
                int index = _articles.IndexOf(existingArticle);

                // Remove the existing article and insert the updated one at the same index
                Articles.Remove(existingArticle);
                Articles.Insert(index, articleToUpdate);
            }
            // If existingArticle is null, handle the case where the article to update was not found
        }
    }
    /// <summary>
    /// Sync the local db
    /// </summary>
    /// <returns></returns>
    private Task RefreshDB()
    {
            return Task.Run(async () =>
            {
                try
                {
                     await _backUpDataBase.UpdateArticles(_articles);
                }
                catch (Exception ex)
                {

#if DEBUG
                    Debug.WriteLine(ex);

#else
                SentrySdk.CaptureException(ex);
#endif
                }
            });
        
    }

    /// <summary>
    /// Load articles via search
    /// </summary>
    private async Task SearchArticles()
    {
        ObservableRangeCollection<Article> articles = new ();
        bool isUpdate = _prevSearch == SearchText;
        string timeParam = string.Empty;

        if (Connectivity.NetworkAccess == NetworkAccess.Internet)
        {
            if (isUpdate)
                timeParam = _articles?.FirstOrDefault().FullPublishDate.ToUniversalTime().ToString("dd-MM-yyy_HH:mm:ss");

            List<Article> collection = [];
            try
            {
                collection = [.. (await CurrentApp.DataFetcher.GetFeedArticles(SearchText, timeParam, isUpdate)).Where(article => /*(article.Blocked == null || article.Blocked == false)*/ 
                article.Source?.IsActive ?? false)];
            

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
            
            articles = new(collection);
            
        }
        // Offline search
        else
        {
            var words = SearchText.Split(' ');
            for (int i = 0; i < words.Count(); i++)
            {
                var word = words[i];
                articles.AddRange(_articles.Where((e) => e.Title.Contains(word)));
            }
            
        }

        if (isUpdate)
        {
            if (articles.Count > 0)
                UpdateArticles(articles.Where(article => article.Blocked == null || article.Blocked == false));
        }
        else
        {
            if (articles is null)
                return;
            // Update list of articles
            Articles = new ObservableRangeCollection<Article>(articles.Where(article => article.Blocked == null || article.Blocked == false));

        }

        IsRefreshing = false;
        IsSearchOpen = true;
        _prevSearch = SearchText;

    }

    /// <summary>
    /// Get all the articles from the db
    /// </summary>
    /// <returns></returns>
    private async Task<IEnumerable<Article>> GetBackupFromDb()
    {
        return (await _backUpDataBase.GetArticles()).Where(article => article.Blocked == null || article.Blocked == false).Reverse<Article>();
    }

    /// <summary>
     /// Processed launched when the page reappear
     /// </summary>
    public async Task Resume()
    {
        if (IsFirstLoad)
        {

            _ = FetchExistingArticles().ContinueWith(_ =>
            {
                IsFirstLoad = false;
            });
        }
        else
        {
            // Refresh the time displayed when be comeback
            RefreshArticlesTime();
#if ANDROID
            IsRefreshing = true;
#elif IOS
            // NOTE: we can't always set `IsRefreshing` on iOS otherwise it would trigger a scroll upwards
            // see more: https://github.com/Gamhub-io/MobileApp/issues/330
            if (_onTopScroll)
                IsRefreshing = true;
            else
                RefreshFeed.Execute(_isSearching);
#endif
        }
        Sources = await _generalDB.GetSources();
        
        await FetchTrendingArticles();

        ObservableCollection <Feed> curFeeds = [.. await _generalDB.GetFeeds()];

        // We try to figure out if the two feed lists contains the same items
        if (!FeedToolkit.CampareItems(Feeds, curFeeds))
        {
            // Reload the feeds
            Feeds = curFeeds;
        }

    }

    /// <summary>
    /// Refresh the timspan of all the articles currently in display
    /// </summary>
    private void RefreshArticlesTime()
    {
        if (Articles is null)
            return;
        for (int i = 0; i < Articles.Count; i++)
            _articles[i].RefreshTimeCommand.Execute(null);
    }
}