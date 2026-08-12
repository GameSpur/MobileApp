using CommunityToolkit.Maui.Alerts;
using GamHubApp.Core;
using GamHubApp.Models;
using GamHubApp.Services;
using System.Collections.ObjectModel;

namespace GamHubApp.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly GeneralDataBase _generalDb;
    private readonly SourceService _sourceService;
    private bool _dealPageSett;
    public bool DealPageSett
    {
        get => _dealPageSett;
        set
        {
            if (_dealPageSett == value) return;
            UpdateSettings(PreferencesKeys.DealPageEnable, _dealPageSett = value);

            OnPropertyChanged(nameof(DealPageSett));
        }
    }

    private bool _dealViewSett;
    public bool DealViewSett
    {
        get => _dealViewSett;
        set
        {
            if (_dealViewSett == value) return;
            UpdateSettings(PreferencesKeys.DealArticleEnable, _dealViewSett = value);
            OnPropertyChanged(nameof(DealViewSett));
        }
    }

    private bool _dealReminderSett;

    public bool DealReminderSett
    {
        get => _dealReminderSett;
        set
        {
            if (_dealReminderSett == value) return;
            UpdateSettings(PreferencesKeys.DealReminderEnabled, _dealReminderSett = value);
            OnPropertyChanged(nameof(DealReminderSett));
        }
    }

    private ObservableCollection<Source> _outlets;
    public ObservableCollection<Source> Outlets 
    { 
        get => _outlets; 
        set
        {
            _outlets = value;
            OnPropertyChanged(nameof(Outlets));
        }
    }

    public bool SourcesAreInitialised { get; set; } = false;

    public Command OpenSettingsCommand
    {
        get => new Command(() => AppInfo.Current.ShowSettingsUI());
    }
    private Command<Source> _selectCommmand;
    public Command<Source> SelectCommand
    {
        get
        {
            return _selectCommmand;
        }
    }
    public Command AppearingCommand
            {
        get
        {
            return new(() =>
            {

                SourcesAreInitialised = true;

            });
        }
    }
    public SettingsViewModel (GeneralDataBase generalDataBase, SourceService sourceService)
    {
        _generalDb = generalDataBase;
        _sourceService = sourceService;
        _dealPageSett = Preferences.Get(PreferencesKeys.DealPageEnable, true);
        _dealViewSett = Preferences.Get(PreferencesKeys.DealArticleEnable, true);
        _dealReminderSett = Preferences.Get(PreferencesKeys.DealReminderEnabled, true);
        _selectCommmand = new Command<Source>(async (source) =>
        {
            if (!SourcesAreInitialised || source == null)
                return;
            await _generalDb.UpdateSourceById(source);
            DisplayUpdateToast();
            _sourceService.NotifySourcesChanged(source);
        }); ;
       
    }

    /// <summary>
    /// Update boolean settings
    /// </summary>
    /// <param name="settingsKey">Key of settings</param>
    /// <param name="value">the new bool value</param>
    private static void UpdateSettings(string settingsKey, bool value)
    {
        Preferences.Set(settingsKey, value);

        // Notify the user we are updating the preferences
        // - Making sure we do it on the main thread
        DisplayUpdateToast();
    }

    private static void DisplayUpdateToast()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
                           await (Toast.Make("Settings changes saved")).Show());
    }

    public async Task InitialiseAsync()
    {
        Outlets = new(await _generalDb.GetSources());
    }
}
