using GamHubApp.Core;
using GamHubApp.Models;
using GamHubApp.Views;
using MvvmHelpers;
using System.Collections.ObjectModel;
#if DEBUG
using System.Diagnostics;
#endif

namespace GamHubApp.ViewModels;

public class DealsViewModel : BaseViewModel
{
    public App CurrentApp { get; }
    private ObservableRangeCollection<Deal> _deals = new ();
    private ObservableRangeCollection<Deal> _presearchDeals;

    public ObservableRangeCollection<Deal> Deals
    {
        get
        {
            return _deals;
        }
        set
        {
            _deals = value;
            OnPropertyChanged(nameof(Deals));
        }
    }
    private bool _setup = false;
    private bool _isLoading = true;

    public bool IsLoading
    {
        get
        {
            return _isLoading;
        }
        set
        {
            _isLoading = value;
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    private ObservableRangeCollection<Deal> _popularDeals = new();

    public ObservableRangeCollection<Deal> PopularDeals
    {
        get
        {
            return _popularDeals;
        }
        set
        {
            _popularDeals = value;
            OnPropertyChanged(nameof(PopularDeals));
        }
    }

    private bool _filtersApplied = false;
    public bool FiltersApplied
    {
        get
        {
            return _filtersApplied;
        }
        set
        {
            _filtersApplied = value;
            OnPropertyChanged(nameof(FiltersApplied));
        }
    }
    public Command DealFilterCommand { get; }
    public Command SaveFilter { get; }
    public Command CancelFilter { get; }
    private string filterCode = null;

    public DealsViewModel()
    {
        CurrentApp = App.Current as App;
        filterCode = Preferences.Get(PreferencesKeys.DealFilterCode, filterCode);
        DealFilterCommand = new Command(() =>
        {
            if (_filterOpened) return;
            IsLoading = true;
            CurrentApp.OpenPopUp(_lastFilterPopUp = new DealFilterPopUp(this), bgTapToClose: false);
            _filterOpened = true;
        });

        SaveFilter = new Command(async () =>
        {
            filterCode = string.Empty;
            List<string> drmIDs = new ();
            foreach (var drm in _platforms)
            {
                if (drm.IsSelected)
                {
                    filterCode += drm.Id + '_';
                    drmIDs.Add(drm.Id);
                }
            }
            filterCode = string.Join('_', drmIDs);
            filterCode = filterCode?.TrimEnd('_').TrimEnd();
            if (CurrentApp.DataFetcher.PlatformsStr != filterCode)
            {
                if (CurrentApp.DataFetcher.AllDeals == null)
                {
                    await App.DisplaySoftError("Sorry, the filters cannot be applied");
                    return;
                }
                Preferences.Set(PreferencesKeys.DealFilterCode, filterCode);
               
                var allowedDrms = filterCode.Split('_');
                IOrderedEnumerable<Deal> filtersList = 
                CurrentApp.DataFetcher.AllDeals.Where(deal => 
                deal?.DRM != null && allowedDrms.Contains(deal.DRM)
                ).OrderBy(d => d.Expires);
                _presearchDeals = Deals = new ObservableRangeCollection<Deal>(
                    filtersList);

                FiltersApplied = true;
            }

            await _lastFilterPopUp?.CloseAsync();
            _filterOpened = false;
            if (_setup == true)
                IsLoading = false;
        });

        CancelFilter = new Command(async() =>
        {
            await _lastFilterPopUp?.CloseAsync();
            _filterOpened = false;
            IsLoading = false;
        });

        Task.Run(async () => {
            Platforms = new((await (App.Current as App).DataFetcher.GetDRMs()).OrderBy(plat => plat.DRM));
            for (int i = 0; i < _platforms.Count && filterCode!= null; i++)
            {
                Platforms[i].IsSelected = filterCode.Split('_').Contains(_platforms[i].Id);
            }
        }).GetAwaiter();
    }
    
    private DealFilterPopUp _lastFilterPopUp;
    private ObservableCollection<GamePlatform> _platforms;
    private bool _filterOpened;

    public ObservableCollection<GamePlatform> Platforms
    {
        get => _platforms;
        set
        {
            _platforms = value;
            OnPropertyChanged(nameof(Platforms));
        }

    }

    /// <summary>
    /// Reset the search to what it was initially
    /// </summary>
    public void ResetSearch()
    {
        Deals = _presearchDeals;
    }

    /// <summary>
    /// Process a search
    /// </summary>
    /// <param name="textSearch">input of the search</param>
    public void DealsSearch(string textSearch)
    {
        try
        {
        if (string.IsNullOrEmpty(textSearch))
        {
            ResetSearch();
            return;
        }

        var filteredDeals = _presearchDeals.Where(deal => deal?.Title?.ToLower().Contains(textSearch.ToLower()) ?? false)?.ToList();

        if (filteredDeals is null)
        {
            Deals = _presearchDeals;
            return;
        }

        Deals = new(filteredDeals);

        }
        catch (Exception ex)
        {
#if DEBUG
            Debug.WriteLine(ex);
            throw new Exception(ex.Message);
#else
            SentrySdk.CaptureException(ex);
#endif
        }
    }

    public async Task UpdateDeals()
    {
        if (!(Deals?.Count > 0))
        {
            string filterCode = Preferences.Get(PreferencesKeys.DealFilterCode, null);

            var getTendingTask = CurrentApp.DataFetcher.GetTrendingDeals();
            var getDealTask = CurrentApp.DataFetcher.GetDeals(filterCode);
            await Task.WhenAll(getTendingTask, getDealTask);
            if (filterCode != null)
            {

                Deals = new(getDealTask?.Result?.OrderBy(d => d.Expires));
                IsLoading = false;
                FiltersApplied = true;
                _setup = true;
                PopularDeals = new((getTendingTask?.Result?.Where(dt => filterCode.Contains(dt.DRM))));
                _presearchDeals = _deals;
                return;
            }


            Deals = new (getDealTask?.Result?.OrderBy(d=> d.Expires));
            PopularDeals = new (getTendingTask?.Result);

            IsLoading = false;
            _setup = true;

            _presearchDeals = _deals;
            return;
        }
        IsLoading = false;
        var newDeals = (await CurrentApp.DataFetcher.GetDeals())?.OrderBy(d => d.Expires)
                        .Select((deal, index) => new { Deal = deal, Index = index })
                        .Where((d) => _deals.FirstOrDefault(ogD => ogD.Id == d.Deal.Id) == null)
                        .ToList();
        if (newDeals == null)
            return;

        for (int i = 0; i < newDeals!.Count; i++)
        {
            var deal = newDeals[i].Deal;
            if (filterCode != null && !filterCode.Split('_').Contains(deal.DRM))
                continue;
            Deals.Insert(newDeals[i].Index, deal);
        }
        long nbExpires = _deals.LongCount(deal => deal.Expires < DateTime.UtcNow);

        for (int i = 0; i < nbExpires; i++)
        {
            Deals.RemoveAt(i);
        }

        _presearchDeals = _deals;
}
}
