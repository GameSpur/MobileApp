using GamHubApp.Core;
using Newtonsoft.Json;
using SQLite;

namespace GamHubApp.Models;

public class Deal
{
    [JsonProperty("_id")]
    public string Id { get; set; }
    [JsonProperty("title")]
    public string Title { get; set; }
    [JsonProperty("image")]
    public string Image { get; set; }
    [JsonProperty("description")]
    public string Description { get; set; }
    [JsonProperty("type")]
    public string Type { get; set; }
    [JsonProperty("discount")]
    public string Discount { get; set; }
    [JsonProperty("url")]
    public string Url { get; set; }
    [JsonProperty("createdAt")]
    public DateTime Created { get; set; }
    [JsonProperty("expiresAt")]
    public DateTime Expires { get; set; }
    [JsonProperty("partner"), Ignore]
    public Partner Partner { get; set; }
    [JsonProperty("drm"), Ignore]
    public string DRM { get; set; }
    [JsonProperty("gemRewards")]
    public string GemRewards { get; set; } = null;
    public Command Navigate
    {
        get 
        {
            return new Command(async () => 
                {
                    await Browser.OpenAsync(Url, new BrowserLaunchOptions
                    {
                        LaunchMode = BrowserLaunchMode.External
                    });
#if !DEBUG
        // Register Hook
        _ =(App.Current as App).DataFetcher.RegisterHook(this);
#endif
                    Preferences.Set(PreferencesKeys.LastDealVisit, JsonConvert.SerializeObject(this));
                    if (Preferences.Get(PreferencesKeys.DealReminderEnabled, true) && (Expires - DateTime.UtcNow).TotalHours > 5)
                        await (App.Current as App).DataFetcher.SetDealReminder(this);
                });
        }
    }
    [Ignore]
    public Command ShareDeal
    {
        get
        {
            return new Command(() =>
            {
                _ = Share.RequestAsync(new ShareTextRequest
                {
                    Uri = Url,
                    Title = "Share this deal with a friend",
                    Subject = Title,
                    Text = $"Check this out! {Title} is {(Discount.Contains('%') ? $" at {Discount} OFF" : Discount) } on {Partner.Name}"
                });
            }); ;
        }
    }
    [JsonIgnore, Ignore]
    public bool ExpirationDisplayed { get => (Expires - DateTime.Now).TotalDays < 30; }
}
