using CommunityToolkit.Maui;
using Vapolia.StrokedLabels;
using GamHubApp.Services;
using GamHubApp.ViewModels;
using GamHubApp.Views;
using Plugin.FirebasePushNotifications;
using Plugin.FirebasePushNotifications.Model.Queues;
using Maui.RevenueCat.InAppBilling;
using GamHubApp.Core;
using CustardApi.Objects;


#if IOS
using GamHubApp.Platforms.iOS.Renderers;
#endif

#if ANDROID
using GamHubApp.Platforms.Android.Notifications;
using GamHubApp.Platforms.Android.Renderers;
#endif

namespace GamHubApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
#if DEBUG
        EnvironementSetup.DebugSetup();
#endif
        builder.UseMauiApp<App>()
            .UseSentry(options => {
               
#if DEBUG
            // Sentry just pollute my logs in Debug so I disabled it
            options.Dsn = "";
#else 
            // The DSN is the only required setting.
            options.Dsn = "https://97c1a76679b7f318e7a8b041622ebb5f@o4511854137966592.ingest.de.sentry.io/4511854157824080";
#endif

#if DEBUG
                options.Debug = true;
#else
                options.Debug = false;
#endif
                options.TracesSampleRate = 1.0;
            })
               .ConfigureFonts(fonts =>
               {
                   fonts.AddFont("FontAwesome6Free-Regular-400.otf", "FaRegular");
                   fonts.AddFont("FontAwesome6Brands-Regular-400.otf", "FaBrand");
                   fonts.AddFont("FontAwesome6Free-Solid-900.otf", "FaSolid");
                   fonts.AddFont("Ubuntu-Regular.ttf", "P-Regular");
                   fonts.AddFont("Ubuntu-Bold.ttf", "P-Bold");
                   fonts.AddFont("Ubuntu-Medium.ttf", "P-Medium");
                   fonts.AddFont("Lexend-SemiBold.ttf", "P-SemiBold");
               })
               .RegisterViews()
               .RegisterViewModels()
               .UseStrokedLabelBehavior()
               .UseMauiCommunityToolkit()
               .RegisterLocalStorage()
                .UseFirebasePushNotifications(o =>
                {
                    o.AutoInitEnabled = false;
                    o.QueueFactory = new PersistentQueueFactory();
#if ANDROID
                    o.Android.NotificationChannels = NotificationChannelGamHub.GetAll().ToArray();
#endif

                })
               .ConfigureMauiHandlers(handlers =>
               {
                   handlers.AddHandler<Shell, TabbarBadgeRenderer>();
#if ANDROID
                   handlers.AddHandler(typeof(HtmlLabel), typeof(HtmlLabelHandler));
#endif
               });
#if DEBUG_LOCALHOST
builder.Services.AddCustard(host: AppConstant.Localhost,
                                port: 255,
                               sslCertificate: false);
#else
        builder.Services.AddCustard(host: "api.gamhub.io",
                               sslCertificate: true);
#endif
        builder.Services.AddSingleton<Fetcher>();

        builder.Services.AddSingleton<CommunityToolkit.Maui.Behaviors.TouchBehavior>();
        builder.Services.AddSingleton<SourceService>();
        builder.Services.AddRevenueCatBilling(true);
        return builder.Build();
    }

    public static MauiAppBuilder RegisterViewModels(this MauiAppBuilder mauiAppBuilder)
    {
        mauiAppBuilder.Services.AddSingleton<AboutViewModel>();
        mauiAppBuilder.Services.AddSingleton<AppShellViewModel>();
        mauiAppBuilder.Services.AddSingleton<BookmarkViewModel>();
        mauiAppBuilder.Services.AddSingleton<FeedsViewModel>();
        mauiAppBuilder.Services.AddSingleton<NewsViewModel>();
        mauiAppBuilder.Services.AddSingleton<SettingsViewModel>();
        mauiAppBuilder.Services.AddSingleton<DealsViewModel>();
        mauiAppBuilder.Services.AddSingleton<GemTopUpViewModel>();

        return mauiAppBuilder;
    }

    public static MauiAppBuilder RegisterLocalStorage(this MauiAppBuilder mauiAppBuilder)
    {
        mauiAppBuilder.Services.AddSingleton<GeneralDataBase>();
        mauiAppBuilder.Services.AddSingleton<BackUpDataBase>();

        return mauiAppBuilder;
    }

    public static MauiAppBuilder RegisterViews(this MauiAppBuilder mauiAppBuilder)
    {
        mauiAppBuilder.Services.AddSingleton<App>();
        mauiAppBuilder.Services.AddSingleton<AppShell>();

        // pages
        mauiAppBuilder.Services.AddSingleton<AboutPage>();
        mauiAppBuilder.Services.AddSingleton<FeedsPage>();
        mauiAppBuilder.Services.AddSingleton<BookmarkPage>();
        mauiAppBuilder.Services.AddSingleton<DealsPage>();
        mauiAppBuilder.Services.AddSingleton<DeleteFeedPopUp>();
        mauiAppBuilder.Services.AddSingleton<RenameFeedPopUp>();
        mauiAppBuilder.Services.AddSingleton<DealFilterPopUp>();
        mauiAppBuilder.Services.AddSingleton<SettingsPage>();
        mauiAppBuilder.Services.AddSingleton<GemTopUpPage>();

        // Resources
        mauiAppBuilder.Services.AddSingleton<ResourceLoader>();

        return mauiAppBuilder;
    }
}
