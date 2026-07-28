using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tonghopbansung.Models;

public partial class Group : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "Nhóm mới";

    [ObservableProperty]
    private Guid _presetId;

    public ObservableCollection<Shooter> Shooters { get; set; } = new();
}
