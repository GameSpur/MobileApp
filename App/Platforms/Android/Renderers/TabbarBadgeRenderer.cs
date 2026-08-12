
using GamHubApp.Services.UI;
using Google.Android.Material.Badge;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Microsoft.Maui.Platform;

namespace GamHubApp.Platforms.Android.Renderers;

public class TabbarBadgeRenderer : ShellRenderer
{
    protected override IShellBottomNavViewAppearanceTracker CreateBottomNavViewAppearanceTracker(ShellItem shellItem)
    {
        if (shellItem.Items.Count == 1)
            return base.CreateBottomNavViewAppearanceTracker(shellItem);
        return new BadgeShellBottomNavViewAppearanceTracker(this, shellItem);
    }
    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem shellItem)
    {
        return new ActionableTabItemRenderer(this);
    }
}

class BadgeShellBottomNavViewAppearanceTracker : ShellBottomNavViewAppearanceTracker
{
    private BadgeDrawable? badgeDrawable;
    public BadgeShellBottomNavViewAppearanceTracker(IShellContext shellContext, ShellItem shellItem) : base(shellContext, shellItem)
    {
    }
    public override void SetAppearance(BottomNavigationView bottomView, IShellAppearanceElement appearance)
    {
        base.SetAppearance(bottomView, appearance);

        if (badgeDrawable is null)
        {
            // TODO: the index is hardcoded here, it would be nice to find a way to set this programatically or otherwise
            const int dealTabbarItemIndex = 2;

            badgeDrawable = bottomView.GetOrCreateBadge(dealTabbarItemIndex);
            UpdateBadge(0);
            BadgeCounterService.CountChanged += OnCountChanged;
        }
    }
    private void OnCountChanged(object? sender, int newCount)
    {
        UpdateBadge(newCount);
    }
    private void UpdateBadge(int count)
    {
        if (badgeDrawable is not null)
        {
            if (count <= 0)
            {
                badgeDrawable.SetVisible(false);
            }
            else
            {
                badgeDrawable.Number = count;
                badgeDrawable.BackgroundColor = (App.Current.Resources["DiscountColor"] as Color).ToPlatform();
                badgeDrawable.BadgeTextColor = Colors.White.ToPlatform();
                badgeDrawable.SetVisible(true);
            }
        }
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        BadgeCounterService.CountChanged -= OnCountChanged;
    }
}

//// Code inspired by https://github.com/Abhayprince/TabbarBadgeShellMAUI

