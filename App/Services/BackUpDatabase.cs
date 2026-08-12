using GamHubApp.Core;
using GamHubApp.Models;
using SQLite;
using System.Collections.ObjectModel;

namespace GamHubApp.Services;

public class BackUpDataBase
{
    SQLiteAsyncConnection database;
    public Task Init()
    {
        if (database is not null)
            return null;

        database = new SQLiteAsyncConnection(AppConstant.PathDBBackUp, AppConstant.Flags);
        var creatingSource = database.CreateTableAsync<Source>();
        var creatingArticle = database.CreateTableAsync<Article>();

        return Task.WhenAll(creatingSource, creatingArticle);
    }

    /// <summary>
    /// Update all the sources in the backup database from given sources
    /// </summary>
    /// <param name="sources">outlet sources</param>
    public Task UpdateSources(List<Source> sources)
    {
        if (database is null)
            return null;

        List<Task> tasks = new();

        for (int i = 0; i < sources.Count; i++)
            tasks.Add(database.InsertOrReplaceAsync(sources[i]));

        return Task.WhenAll(tasks);
    }

    /// <summary>
    /// Update all the article in the backup database from given articles
    /// </summary>
    /// <param name="articles">articles</param>
    public Task UpdateArticles(Collection<Article> articles)
    {
        if (database is null)
            return null;

        List<Task> tasks = new();

        for (int i = 0; i < articles.Count(); i++)
            tasks.Add(database.InsertOrReplaceAsync(articles[i]));

        // update sources as well
        tasks.Add(UpdateSources(articles.Select(a => a.Source).Distinct().ToList()));

        return Task.WhenAll(tasks);
    }

    /// <summary>
    /// Get all the articles from backup
    /// </summary>
    /// <returns>List of all the articles saved as backup</returns>
    public async Task<List<Article>> GetArticles ()
    {
        if (database is null)
            return null;
        return await database.Table<Article>().ToListAsync();
    }

    
}
