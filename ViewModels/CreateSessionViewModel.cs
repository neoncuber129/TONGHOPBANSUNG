using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tonghopbansung.Models;

namespace Tonghopbansung.ViewModels;

public partial class CreateSessionViewModel : ObservableObject
{
    public ObservableCollection<Group> Groups { get; }

    [ObservableProperty]
    private string _sessionName = $"Đợt {DateTime.Now:dd/MM/yyyy HH:mm}";

    [ObservableProperty]
    private Group? _selectedGroup;

    [ObservableProperty]
    private int _personCount = 30;

    public CreateSessionViewModel(IEnumerable<Group> groups, Group? preferredGroup)
    {
        Groups = new ObservableCollection<Group>(groups);
        SelectedGroup = preferredGroup ?? Groups.FirstOrDefault();
    }

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(SessionName))
        {
            error = "Hãy nhập tên đợt bắn.";
            return false;
        }

        if (SelectedGroup is null)
        {
            error = "Hãy chọn nhóm (cấu hình bia).";
            return false;
        }

        if (PersonCount < 1)
        {
            error = "Số người phải ≥ 1.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
