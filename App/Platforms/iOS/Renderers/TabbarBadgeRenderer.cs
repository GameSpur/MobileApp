using GamHubApp.Services.UI;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Microsoft.Maui.Platform;
using Sentry;
using System.Diagnostics;
using UIKit;

namespace GamHubApp.Platforms.iOS.Renderers;

public class TabbarBadgeRenderer : ShellRenderer
{
    protected override IShellTabBarAppearanceTracker CreateTabBarAppearanceTracker()
    {
        //return base.CreateTabBarAppearanceTracker();
        return new BadgeShellTabbarAppearanceTracker();
    }
}
class BadgeShellTabbarAppearanceTracker : ShellTabBarAppearanceTracker
{
    private UITabBarItem? _cartTabbarItem;
    public override void UpdateLayout(UITabBarController controller)
    {
        base.UpdateLayout(controller);

        if (_cartTabbarItem is null
            && controller.TabBar.Items?.Length == 3)
        {
            // TODO: the index is hardcoded here, it would be nice to find a way to set this programatically or otherwise
            const int dealsTabbarItemIndex = 2;
            _cartTabbarItem = controller.TabBar.Items[dealsTabbarItemIndex];
            if (_cartTabbarItem is not null)
            {
                UpdateBadge(0);
                BadgeCounterService.CountChanged += OnCountChanged;
            }
        }
    }
    private void OnCountChanged(object? sender, int newCount)
    {
        UpdateBadge(newCount);
    }
    private void UpdateBadge(int count)
    {
        try
        {
            MainThread.BeginInvokeOnMainThread(() => 
            {
                if (_cartTabbarItem is not null)
                {
                    if (count <= 0)
                    {
                        _cartTabbarItem.BadgeValue = null;
                    }
                    else
                    {
                        _cartTabbarItem.BadgeValue = count.ToString();
                        _cartTabbarItem.BadgeColor = (App.Current.Resources["DiscountColor"] as Color).ToPlatform();
                    }
                }
            });
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
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        BadgeCounterService.CountChanged -= OnCountChanged;
    }
}

//// Code inspired by https://github.com/Abhayprince/TabbarBadgeShellMAUI

