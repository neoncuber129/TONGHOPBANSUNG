using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tonghopbansung.Models;

namespace Tonghopbansung.ViewModels;

public enum ImportSessionMode
{
    CreateNew,
    Append
}

public partial class ImportSessionViewModel : ObservableObject
{
    public ObservableCollection<Group> MatchingGroups { get; }

    public string PackName { get; }
    public int ShooterCount { get; }
    public bool CanAppend { get; }
    public string? ActiveSessionName { get; }

    [ObservableProperty]
    private ImportSessionMode _mode = ImportSessionMode.CreateNew;

    [ObservableProperty]
    private Group? _selectedGroup;

    public ImportSessionViewModel(
        string packName,
        int shooterCount,
        IEnumerable<Group> matchingGroups,
        bool canAppend,
        string? activeSessionName)
    {
        PackName = packName;
        ShooterCount = shooterCount;
        MatchingGroups = new ObservableCollection<Group>(matchingGroups);
        CanAppend = canAppend;
        ActiveSessionName = activeSessionName;
        SelectedGroup = MatchingGroups.FirstOrDefault();

        if (MatchingGroups.Count == 0 && CanAppend)
            Mode = ImportSessionMode.Append;
    }

    public bool IsCreateMode => Mode == ImportSessionMode.CreateNew;
    public bool IsAppendMode => Mode == ImportSessionMode.Append;

    partial void OnModeChanged(ImportSessionMode value)
    {
        OnPropertyChanged(nameof(IsCreateMode));
        OnPropertyChanged(nameof(IsAppendMode));
    }

    public bool Validate(out string error)
    {
        if (Mode == ImportSessionMode.CreateNew)
        {
            if (SelectedGroup is null)
            {
                error = "Hãy chọn nhóm có cùng cấu hình bia với file.";
                return false;
            }
        }
        else if (!CanAppend)
        {
            error = "Đợt đang mở không khớp preset với file.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
