using CommunityToolkit.Mvvm.ComponentModel;
using Tonghopbansung.Services;

namespace Tonghopbansung.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public AppSession Session { get; }
    public GroupsPresetsViewModel GroupsPresets { get; }
    public ScoreEntryViewModel ScoreEntry { get; }
    public BackupViewModel Backup { get; }

    public MainViewModel()
    {
        var store = new SqliteDataStore();
        var backup = new BackupService();
        var sessionTransfer = new SessionTransferService();
        Session = new AppSession(store, backup, sessionTransfer);
        Session.Load();

        GroupsPresets = new GroupsPresetsViewModel(Session);
        ScoreEntry = new ScoreEntryViewModel(Session);
        Backup = new BackupViewModel(Session);

        Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSession.SelectedSession)
                || e.PropertyName == nameof(AppSession.SelectedGroup))
            {
                ScoreEntry.RefreshShooters();
            }

            if (e.PropertyName == nameof(AppSession.StatusMessage))
                Backup.RefreshInfo();
        };
    }
}
