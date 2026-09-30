using GamHubApp.Models;
using System.Collections.ObjectModel;

namespace GamHubApp.ViewModels
{
    public class AboutViewModel : BaseViewModel
    {
        public ObservableCollection<Partner> Partners { get; set; }
        public AboutViewModel()
        {
            if (App.Current is App curr && curr.Partners is not null)
                Partners = new(curr.Partners);
        }

        public Command ViewGithub
        {
            get
            {
                return new Command(async () => await Browser.OpenAsync("https://github.com/bricefriha/AresGaming", new BrowserLaunchOptions
                {
                    LaunchMode = BrowserLaunchMode.SystemPreferred,
                    TitleMode = BrowserTitleMode.Default,
                }));
            }
        }

        public Command ViewTwitter
        {
            get
            {
                return new Command(async (username) => await Browser.OpenAsync($"https://twitter.com/{username}", new BrowserLaunchOptions
                {
                    LaunchMode = BrowserLaunchMode.SystemPreferred,
                    TitleMode = BrowserTitleMode.Default,
                }));
            }
        }
        public Command ViewWeb
        {
            get
            {
                return new Command<string>(async (url) => await Browser.OpenAsync(url, new BrowserLaunchOptions
                {
                    LaunchMode = BrowserLaunchMode.External,
                    TitleMode = BrowserTitleMode.Default,
                }));
            }
        }

        public Command MailTo
        {
            get
            {
                return new Command<string>(async (address) => await Email.ComposeAsync(subject: "", body: "",to: new string[] { address }));
            }
        }
    }
}
