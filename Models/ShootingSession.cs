using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tonghopbansung.Models;

/// <summary>Một đợt bắn: gắn với một nhóm (cấu hình bia) và danh sách người tham gia.</summary>
public partial class ShootingSession : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "Đợt mới";

    /// <summary>Nhóm dùng làm cấu hình bia / xếp loại.</summary>
    [ObservableProperty]
    private Guid _groupId;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ObservableCollection<Shooter> Shooters { get; set; } = new();

    [JsonIgnore]
    public int PersonCount => Shooters.Count;
}
