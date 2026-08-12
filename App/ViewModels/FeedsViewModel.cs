using GamHubApp.Models;
using GamHubApp.Views;
using System.Collections.ObjectModel;
using Command = Microsoft.Maui.Controls.Command;
using MvvmHelpers;
using CommunityToolkit.Mvvm.Messaging;
using GamHubApp.Services;



#if DEBUG
using System.Diagnostics;
using Newtonsoft.Json;
#endif

namespace GamHubApp.ViewModels;

public class FeedsViewModel : BaseViewModel
{
    private bool _dataLoaded = false;
    public bool DataLoaded 
    { 
        get { return _dataLoaded; }
        set { _dataLoaded = value; }
    }
    public List<UpdateOrder> UpdateOrders { get; private set; }
    private ObservableCollection<Feed> _feeds = new();

		public ObservableCollection<Feed> Feeds
		{
			get { return _feeds; }
			set 
			{
				_feeds = value; 
				OnPropertyChanged(nameof(Feeds));
			}
		}
    private ObservableCollection<TabButton> _feedTabs;

    public ObservableCollection<TabButton> FeedTabs
	{
		get { return _feedTabs; }
		set 
		{
        _feedTabs = value; 
			OnPropertyChanged(nameof(FeedTabs));
		}
	}

    private int _currentFeedIndex;

    public int CurrentFeedIndex
    {
        get { return _currentFeedIndex; }
        set 
        {
            _currentFeedIndex = value;


            OnPropertyChanged(nameof(CurrentFeedIndex));

            _oldIndex = _currentFeedIndex;
        }
    }

    private bool _isRefreshing; 

    public bool IsRefreshing 
	{
		get { return _isRefreshing; }
		set 
		{
        _isRefreshing = value; 
			OnPropertyChanged(nameof(IsRefreshing));
		}
	}

	private ObservableRangeCollection<Article> _articles;

	public ObservableRangeCollection<Article> Articles
	{
		get { return _articles; }
		set 
		{
            _articles = value; 
			OnPropertyChanged(nameof(Articles));
		}
	}

    private Feed _selectedFeed;

    public Feed SelectedFeed
    {
        get { return _selectedFeed; }
        set 
        {
            _selectedFeed = value;
            CurrentFeedIndex = _feeds.IndexOf(value);
            Refresh(value,true);
            OnPropertyChanged(nameof(SelectedFeed));
        }
    }

    private TabButton _selectedFeedTab;
    public TabButton SelectedFeedTab
    {
        get { return _selectedFeedTab; }
        set
        {
            if (_selectedFeedTab != value)
            {
                // Set the IsSelected property for the newly selected tab
                _selectedFeedTab = value;
                int index = _feedTabs.IndexOf(value);
                FeedTabs[index].IsSelected = true;
                SelectedFeed = _feeds[index];
                // Reset the IsSelected property for other tabs
                for (int i = 0; i < _feedTabs.Count; i++)
                {
                    if (i != index)
                    {
                        FeedTabs[i].IsSelected = false;
                    }
                }

                OnPropertyChanged(nameof(SelectedFeedTab));
            }
        }
    }

    public Command<Feed> RefreshAll => new Command<Feed>((feed) =>
    {
        IsRefreshing = true;
        Refresh(feed);

    });

    public Command<Feed> SwitchFeed => new Command<Feed>((feed) =>
    {
        if (IsBusy)
            return;

        _ = SwitchFeedAsync(feed);

        RefreshArticles.Execute(null);

    });

    private async Task SwitchFeedAsync(Feed feed)
    {
        await Task.Run(() =>
        {
            UnnoticedArticles.Clear();
            // Set new feed
            SwitchTabs(_feeds.IndexOf(feed));
            SelectedFeed.IsLoaded = false;
        });
    }

    public App CurrentApp { get; }

    private GeneralDataBase _generalDB;

    private CancellationTokenSource _articlesCancelationToken =new ();

    public Command UncoverNewArticles { get; private set; }

    public Command<Feed> Delete => new Command<Feed>( (feed) =>
    {
        CurrentApp.OpenPopUp (new DeleteFeedPopUp(_selectedFeed, this, _generalDB));

    });

    public Command<Feed> Rename => new Command<Feed>( (feed) =>
    {
        CurrentApp.OpenPopUp (new  RenameFeedPopUp(_selectedFeed, this, _generalDB));

    });

    public Command<Feed> Edit => new Command<Feed>(async (feed) =>
    {
        IsFromDetail = true;
        CurrentFocusIndex = _feeds.IndexOf(_selectedFeed);
        await CurrentApp.Windows[0].Page.Navigation.PushAsync(new EditFeedPage(_selectedFeed, this, _generalDB));

    });

    private bool _onTopScroll;

    public bool OnTopScroll
    {
        get { return _onTopScroll; }
        set
        {
            _onTopScroll = value;
            OnPropertyChanged(nameof(OnTopScroll));
        }
    }

    public Command RefreshArticles =>new (() =>
    {


        if (IsBusy)
            return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_feeds.Count <= 0)
                return;
            await Task.Run(() =>
            {
                try
                {

                    if (SelectedFeed == null)
                        SelectedFeed = _feeds[0];
                    Refresh(SelectedFeed);
                }
                catch
                {
                    // in case of error, refresh the first feed
                    RefreshFirstFeed();
                }
            });
        });
        _dataLoaded = true;
    });

	public FeedsViewModel(GeneralDataBase generalDataBase)
    {
        // CurrentApp and CurrentPage will allow use to access to global properties
        CurrentApp = App.Current as App;

        _generalDB = generalDataBase;

        // Instantiate definitions 
        FeedTabs = new ObservableRangeCollection<TabButton>();
        _ = Task.Run(async () =>
        {
            Feeds = new ObservableCollection<Feed>(await generalDataBase.GetFeeds());
            List<Task> tasks = new ();
            for (int i = 0; i < _feeds.Count; i++)
                if (string.IsNullOrEmpty(_feeds[i].MongoID))
                    tasks.Add(CurrentApp.DataFetcher.CreateFeed(_feeds[i]));
            await Task.WhenAll(tasks);
        });
        _articles = new ObservableRangeCollection<Article>();

        // Organise feeds into tabs
        CopyFeedsToTabs();

        // Select first item
        CurrentFeedIndex = 0;
        UncoverNewArticles = new Command(() =>
        {
            if (UnnoticedArticles == null)
                return;
            if (UnnoticedArticles.Count <= 0)
                return;

            //CurrentApp.ShowLoadingIndicator();
            int indexFeed = _feeds.IndexOf(_feeds.FirstOrDefault(f => f.Id == SelectedFeed.Id));
            _ = Task.Run(() =>
            {
                // Scroll up
                WeakReferenceMessenger.Default.Send(new ScrollFeedPageChangedMessage(this));

                // Add the unnoticed articles
                UpdateArticles([.. UnnoticedArticles], SelectedFeed, indexFeed);

                UnnoticedArticles.Clear();

            });

        });


        UpdateOrders = new List<UpdateOrder>();


        if (_feeds.Count <= 0)
            return;

        IsRefreshing = true;

        RefreshArticles.Execute(null);

    }

    private ObservableCollection<Article> _unnoticedArticles = new ();

    public ObservableCollection<Article> UnnoticedArticles
    {
        get { return _unnoticedArticles; }
        set
        {
            _unnoticedArticles = value;
            WeakReferenceMessenger.Default.Send(new UnnoticedFeedArticlesChangedMessage(_unnoticedArticles)
            {
                Id = SelectedFeed.Id,
            });

            OnPropertyChanged(nameof(UnnoticedArticles));
        }
    }

    public Command<string> FeedSelect
    {
        get { return new Command<string> ((feedId) =>
        {
            if (string.IsNullOrEmpty(feedId))
                return;

            int nextIndex = _feeds.IndexOf(_feeds.FirstOrDefault(feed => feed.Id == feedId));

            if (nextIndex == -1)
                return;
            Articles.Clear();
            SelectedFeedTab = _feedTabs[nextIndex]; 


            // Flag all the other feeds as unloaded
            for (int i = 0; i < _feeds.Count; i++)
            {
                if (i != nextIndex)
                {
                    _feeds[i].IsLoaded = false;

                }
            }

        }); }
    }

    /// <summary>
    /// Refresh selected feed
    /// </summary>
    /// <param name="feed">feed of choice</param>
    /// <param name="force">Whether we refresh the feed from scratch or not</param>
    public void Refresh(Feed feed, bool force = false)
	{

        _= Task.Run(async () =>
        {

            // Determine whether or not it's the first time loading the article of this feed
            bool isFirstLoad = _articles == null || _articles.Count <= 0;

            try
            {
                await AggregateFeed(feed, isFirstLoad || force);
            }
            finally
            {
                // End the loading indicator
                IsRefreshing = false;
            } }
            
        ).ContinueWith ((tr) => IsBusy = false);
        
    }

    /// <summary>
    /// Load articles via search
    /// </summary>
    /// <param name="feed">feed we are searching</param>
    private async Task AggregateFeed(Feed feed, bool force = true)
    {
            int indexFeed = _feeds.IndexOf(_feeds.FirstOrDefault(f => f.Id == feed.Id));

        List<Article> articles = new ();

        // Figure out if the feed deserve an update
        string timeUpdate = string.Empty;
        if (force)
        {
            Articles.Clear();
            UnnoticedArticles.Clear();
        }

        if (Articles?.Count != 0)
            timeUpdate = Articles?.First().FullPublishDate.ToUniversalTime().ToString("dd-MM-yyy_HH:mm:ss");

        bool needUpdate = feed.IsLoaded && !string.IsNullOrEmpty(timeUpdate);


        _articlesCancelationToken.Cancel();
        _articlesCancelationToken = new CancellationTokenSource();
        // Make sure we have internet connection
        if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                articles = (await CurrentApp.DataFetcher.GetFeedArticles(feed.Keywords,
                                                                         timeUpdate,
                                                                         needUpdate,
                                                                         _articlesCancelationToken)).Where(article => (article.Blocked == null || article.Blocked == false) && article.Source.IsActive).ToList();

            // Offline search
            else
            {
                var words = feed.Keywords.Split(' ');
                for (int i = 0; i < words.Count(); i++)
                {
                    var word = words[i];
                    articles.AddRange(feed.Articles?.Where((e) => e.Title.Contains(word)));
                }

            }
            if (force)
            {
                // Update list of articles
                InsertArticles(articles, force);
                SelectedFeed.IsLoaded = true;

                IsRefreshing = false;
                return;
            }

            if (needUpdate)
            {
                if (articles.Count > 0)
                    if (OnTopScroll || _articles?.Count < 1)
                        UpdateArticles(articles, feed, indexFeed);
                    else
                    UnnoticedArticles = new ObservableCollection<Article>( articles);
            }
            else
                // Update list of articles
                InsertArticles(articles);
            SelectedFeed.IsLoaded = true;

            IsRefreshing = false;

    }

    /// <summary>
    /// Insert a set of articles in the current list
    /// </summary>
    /// <param name="articles">articles to add</param>
    /// <param name="force">if true we overwrite the current article list</param>
    private void InsertArticles(IEnumerable<Article> articles, bool force = false)
    {
        ObservableRangeCollection<Article> articlesOld = new (_articles);
        if (force)
        {
            Articles = new ObservableRangeCollection<Article>(articles);
            return;
        }

        Articles = new ObservableRangeCollection<Article>();

        Articles.AddRange(articles);
        if (articlesOld.Any())
           Articles.AddRange(articlesOld);
            
    }

    // See detail of the article
    public Command GoToDetail
    {
        get
        {
            return new Command( (id) =>
            {
                var articlePage = new ArticlePage(Articles.FirstOrDefault(art => art.Id == id.ToString()));


                _ = App.Current.Windows[0].Page.Navigation.PushAsync(articlePage);
            }); ;
        }
    }

    private bool _isMenuOpen;
    private int _oldIndex;

    public bool IsMenuOpen
    {
        get { return _isMenuOpen; }
        set
        {
            _isMenuOpen = value;
            OnPropertyChanged(nameof(IsMenuOpen));
        }
    }

    public bool IsFromDetail { get; set; }
    public int CurrentFocusIndex { get; set; }
    public bool ListHasBeenUpdated { get; set; }

    /// <summary>
    /// Update the current article feed by adding new elements
    /// </summary>
    /// <param name="articles">new articles</param>
    private void UpdateArticles(List<Article> articles, Feed feed, int indexFeed)
    {
        for (int i = 0; i < articles.Count(); ++i)
        {
            var current = articles[i];
            Article existingArticle = feed.Articles?.FirstOrDefault(a => a.Id == current.Id);
            if (existingArticle == null)
            {

                Article item = articles.FirstOrDefault(a => a.Id == current.Id);

                var index = articles.IndexOf(item);

                // Add article one by one for a better visual effect
                Articles.Insert(index == -1 ? 0 + i : index, current);
            }
            else
            {
                int index = Feeds[indexFeed].Articles.IndexOf(existingArticle);
                // replace the existing one with the new one
                Articles.Remove(existingArticle);
                Articles.Insert(index, current);
            }

        }
    }

    /// <summary>
    /// Remove a feed from the list
    /// </summary>
    /// <param name="feed">feed we want to remove</param>
    public async Task RemoveFeed(Feed feed)
    {
        CurrentApp.ShowLoadingIndicator();
        IsBusy = true;
        int feedIndex = Feeds.IndexOf(_feeds.FirstOrDefault(f => f.Id == feed.Id));
        int indexNext = feedIndex + 1;
        
        int indexPrev = 0;
        if (feedIndex > 0)
            indexPrev = feedIndex - 1;

        Feeds.RemoveAt(feedIndex);
        FeedTabs.RemoveAt(feedIndex);

        Feed feedInView = new Feed();

        await Task.Factory.StartNew(() =>
        {
            if (_selectedFeed == null)
                CurrentFeedIndex = 0;
            if (_selectedFeed.Id == feed.Id)
            {
                if (_feeds.Count <= 0)
                {
                    Articles?.Clear();
                    return;

                }

                // We try to establish the next feed
                if (indexPrev >= 0)
                {
                    CurrentFeedIndex = indexPrev;
                    feedInView = _feeds[indexPrev];
                }
                else
                {
                    CurrentFeedIndex = indexNext;
                    feedInView = _feeds[indexNext];
                }
            }
            else
                CurrentFeedIndex = 0;

        })
        .ContinueWith((Action<Task>)((e) =>
        {
            // Load selected feed
            _ = SwitchFeedAsync(feedInView);
        }));
    }

    /// <summary>
    /// Update a feed
    /// </summary>
    /// <param name="feed">Updated data of feed</param>
    public void UpdateCurrentFeed (Feed feed)
    {
        SelectedFeed = feed;
        Feed feedToUpdate = Feeds.FirstOrDefault((e) => e.Id == feed.Id);

        // Update the item visually
        if (feedToUpdate != null)
        {
            int index = _feeds.IndexOf(feedToUpdate);
            var f =_feeds[index] = feed;
        }
    }

    /// <summary>
    /// Processed launched when the page reappear
    /// </summary>
    public async Task Resume()
    {
        if (!_dataLoaded)
        {
            Feeds = new ObservableCollection<Feed>(await _generalDB.GetFeeds());

            // Refresh the first feed
            RefreshFirstFeed();

            // Organise feeds into tabs
            CopyFeedsToTabs();

            return;
        };

        try
        {
            await UpdateFeeds();
        }
        catch (Exception ex)
#if DEBUG
        {
            Debug.WriteLine($"Selected Feed {JsonConvert.SerializeObject(_selectedFeed)}");
            Debug.WriteLine(ex.Message);
        }
#else
        {
            SentrySdk.CaptureException(ex);
        }
#endif
    }

    /// <summary>
    /// Method to refresh the first feed
    /// </summary>
    private void RefreshFirstFeed()
    {
        if (_feeds?.Count <= 0)
            return;
        SelectedFeed = _feeds[0];

        _dataLoaded = true;
    }

    /// <summary>
    /// Update the feeds list
    /// </summary>
    private async Task UpdateFeeds()
    {
        if (!_dataLoaded) return;
        if (_feeds == null) return;
        IsBusy = true;

        Collection<Feed> updatedFeeds;

        // Get the updated list of feed
        updatedFeeds = new ObservableCollection<Feed>(await _generalDB.GetFeeds());

        if (updatedFeeds is null)
        {
            IsBusy = false;
            return;
        }

        List<Feed> newFeeds = updatedFeeds.Where(feed => !_feeds.Any(item => item.Id == feed.Id)).ToList();
        List<Feed> removedFeeds = _feeds.Where(feed => !updatedFeeds.Any(item => item.Id == feed.Id)).ToList();

        // Add the new feeds
        foreach (var feed in newFeeds)
        {
            AddFeed(feed);
        }
        // Remove the outdated feeds
        foreach (var feed in removedFeeds)
        {
            _ =RemoveFeed(feed);
        }
        IsBusy = false;
    }

    /// <summary>
    /// Add a feed to the list: this method not only add it to the list of feed but also create a tab for this feed
    /// </summary>
    /// <param name="feed">feed that will be added</param>
    private void AddFeed(Feed feed)
    {
        Feeds.Add(feed);

        // Add a tab for this feed
        FeedTabs.Add(new()
        {
            Id = feed.Id,
            Title = feed.Title,

        });
    }

    /// <summary>
    /// Copies the items from the <see cref="_feeds"/> collection to the <see cref="FeedTabs"/> collection, transforming them into <see cref="TabButton"/> items.
    /// </summary>
    private void CopyFeedsToTabs()
    {
        FeedTabs.Clear();

        var tabButtons = _feeds.Select(feed => new TabButton
        {
            Id = feed.Id,
            Title = feed.Title
            // Set other properties of TabButton if needed
        });

        foreach (var tabButton in tabButtons)
        {
            FeedTabs.Add(tabButton);
        }

        if (FeedTabs.Count >0)
            FeedTabs[_currentFeedIndex].IsSelected = true;

    }

    /// <summary>
    /// Select a tab from its index
    /// </summary>
    /// <param name="index">the index of the tab</param>
    private void SelectTab(int index)
    {
        _oldIndex = index;


        FeedTabs[index].IsSelected = true;
    }

    /// <summary>
    /// Select a tab after the select tab was deleted
    /// </summary>
    /// <param name="fromIndex">index of the former tab</param>
    public void SelectDefaultTab(int fromIndex)
    {
        int indexNext = fromIndex + 1;
        int indexPrev = fromIndex - 1;

        int tabIndex = 0;
        if (_feeds.Count <= 0)
            return;

        // We try to establish the next feed
        if (indexPrev >= 0 )
            tabIndex = indexPrev;
        else if (indexNext <= _feeds.Count -1)
            tabIndex = indexPrev;

        if (tabIndex == -1)
            tabIndex ++;
        // Select the tab that we determined
        SelectTab(tabIndex);

        // Refresh the tab
        SelectedFeed = _feeds[tabIndex];
    }

    /// <summary>
    /// Remove a feed using it's index
    /// </summary>
    /// <param name="feedIndex">index of the feed</param>
    public void RemoveFeedByIndex(int feedIndex)
    {
        Articles?.Clear();
        Feeds.RemoveAt(feedIndex);
        SelectDefaultTab(feedIndex);
    }

    /// <summary>
    /// Delect a tab from its index
    /// </summary>
    /// <param name="index">the index of the tab</param>
    private void DeselectTab(int index)
    {
        try
        {
            if (index == -1)
                index = 0;

            if (FeedTabs.Count <= 0)
                return;
            FeedTabs[index].IsSelected = false;
        }
        catch {}
    }

    /// <summary>
    /// Switch from a tab to another
    /// </summary>
    /// <param name="index"> index of the tab you want to switch to</param>
    private void SwitchTabs(int index)
    {
        if (index == -1)
            return;

        // Deselect the previous tab
        if (_oldIndex <= _feeds.Count -1)
            DeselectTab(_oldIndex);

        // Select the new one
        SelectTab(index);
    }

}
