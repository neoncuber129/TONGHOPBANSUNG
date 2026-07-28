using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tonghopbansung.Models;

/// <summary>Phần (bậc cha) chứa một hoặc nhiều bia con.</summary>
public partial class TargetCluster : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "Phần";

    public ObservableCollection<TargetDefinition> Targets { get; set; } = new();
}
