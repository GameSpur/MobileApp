using GamHubApp.Core;
using GamHubApp.Helpers.Comparers;
using GamHubApp.Models;
using SQLite;
using System.Collections.ObjectModel;

namespace GamHubApp.Services;

public sealed class GeneralDataBase
{
    SQLiteAsyncConnection database;
    private Task<CreateTableResult> _creatingSource;
    private Task<CreateTableResult> _creatingArticle;
    private Task<CreateTableResult> _creatingFeed;
    private Task<CreateTableResult> _creatingDeal;

    public List<Source> Sources { get; private set; }
    public async Task Init()
    {
        if (database is not null)
            return;

        database = new SQLiteAsyncConnection(AppConstant.GeneralDBpath, AppConstant.Flags);
        _creatingSource = database.CreateTableAsync<Source>();
        _creatingArticle = database.CreateTableAsync<Article>();
        _creatingFeed = database.CreateTableAsync<Feed>();
        _creatingDeal = database.CreateTableAsync<Deal>();


        await Task.WhenAll( _creatingSource, 
                            _creatingArticle, 
                            _creatingFeed,
                            _creatingDeal);
    }

    /// <summary>
    /// Get a specific article
    /// </summary>
    /// <param name="id">id of the article</param>
    /// <returns>the article</returns>
    public async Task<Article> GetArticleById(string id)
    {
        return await database.Table<Article>().FirstOrDefaultAsync(a => a.MongooseId == id);
    }

    /// <summary>
    /// Get all articles
    /// </summary>
    /// <returns>the article</returns>
    public async Task<List<Article>> GetArticles()
    {
        return await database.Table<Article>().ToListAsync();
    }

    /// <summary>
    /// Get a specific source
    /// </summary>
    /// <param name="id">id of the source</param>
    /// <returns>the source</returns>
    public async Task<Source> GetSourceById(string id)
    {
        return await database.Table<Source>().FirstOrDefaultAsync(a => a.MongoId == id);
    }

    /// <summary>
    /// Get a specific source
    /// </summary>
    /// <param name="id">id of the source</param>
    /// <returns>the source</returns>
    public async Task<int> UpdateSourceById(Source source)
    {

        return await database.UpdateAsync(source);
    }

    /// <summary>
    /// Get sources
    /// </summary>
    /// <returns>the source</returns>
    public async Task<List<Source>> GetSources()
    {
        if (database is null)
            await Init();
        return await database.Table<Source>().ToListAsync();
    }

    /// <summary>
    /// Add a specific article to bookmarks
    /// </summary>
    /// <param name="article">the article we want to insert</param>
    /// <returns>update status</returns>
    public async Task<int> InsertArticleBookmark(Article article)
    {
        return await database.InsertOrReplaceAsync(article);
    }

    public async Task<int> InsertSources(Source newSource)
    {
        return await database.InsertAsync(newSource);
    }

    /// <summary>
    /// Delete a specific article that have been bookmarked
    /// </summary>
    /// <param name="article">the article we want to remove</param>
    /// <returns>update status</returns>
    public async Task<int> DeleteArticleBookmark(Article article)
    {
        return await database.DeleteAsync(article);
    }

    /// <summary>
    /// Get Articles that have been bookmarked
    /// </summary>
    /// <returns>Bookmarked articles</returns>
    public async Task<List<Article>> GetBookmarkedArticles()
    {

        return (await database.Table<Article>().ToListAsync())
                              .Reverse<Article>()
                              .Select(a => { a.Source = Fetcher.Sources.SingleOrDefault(s => s.MongoId == a.SourceId); ; return a; })
                              .ToList() ?? new List<Article>();
    }

    /// <summary>
    /// Update a feed
    /// </summary>
    /// <param name="feed">Feed we want to update</param>
    /// <returns>Update status</returns>
    public async Task<int> UpdateFeed(Feed feed)
    {
        return await database.UpdateAsync(feed);
    }

    /// <summary>
    /// Get all feeds
    /// </summary>
    /// <returns>all feeds</returns>
    public async Task<List<Feed>> GetFeeds()
    {
        return await database.Table<Feed>().ToListAsync() ?? new List<Feed>();
    }

    /// <summary>
    /// Get all deals
    /// </summary>
    /// <returns>all deals stored so far</returns>
    public async Task<List<Deal>> GetDeals()
    {
        return (await database.Table<Deal>().ToListAsync())?.Where(
            d => d.Expires > DateTime.UtcNow
            ).ToList() ?? new List<Deal>();
    }

    /// <summary>
    /// Update deals
    /// </summary>
    /// <returns>count of new deals</returns>
    public async Task<int> UpdateDeals(Collection<Deal> newDeals)
    {
        if (newDeals == null)
            return 0;
        int newCount = 0;

        await _creatingDeal;
        var currD = await GetDeals();
        DealComparer dealComp = new ();
        List<Task> insertJobs = new List<Task> ();
        for (int i = 0; newDeals.Count > i; i++)
        {
            var newDeal = newDeals[i];
            if (!currD.Contains(newDeal, dealComp))
            {
                ++newCount;
                insertJobs.Add(database.InsertOrReplaceAsync(newDeal));
            }
        }

        await Task.WhenAll(insertJobs);
        return newCount;
    }

    /// <summary>
    /// insert a feed
    /// </summary>
    /// <param name="feed">Feed we want to insert</param>
    /// <returns>all feeds</returns>
    public async Task<int> InsertFeed(Feed feed)
    {
        return await database.InsertAsync(feed);
    }

    /// <summary>
    /// Delete a feed
    /// </summary>
    /// <param name="feed">Feed we want to delete</param>
    /// <returns>Update status</returns>
    public async Task<int> DeleteFeed(Feed feed)
    {
        return await database.DeleteAsync(feed);
    }


}
