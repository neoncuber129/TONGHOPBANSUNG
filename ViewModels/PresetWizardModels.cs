using CommunityToolkit.Mvvm.ComponentModel;

namespace Tonghopbansung.ViewModels;

/// <summary>Wizard cấu hình bia: Nhóm → Phần → Số bia từng phần → Chi tiết.</summary>
public enum PresetWizardStep
{
    /// <summary>Bước 1: tên nhóm.</summary>
    GroupName = 0,
    /// <summary>Bước 2: số phần.</summary>
    ClusterCount = 1,
    /// <summary>Bước 3: số bia từng phần.</summary>
    TargetsPerCluster = 2,
    /// <summary>Bước 4: sửa tên phần / bia / số đạn.</summary>
    DetailEdit = 3
}

/// <summary>Bước 3: số bia cho từng phần.</summary>
public partial class ClusterSetupRow : ObservableObject
{
    public int Index { get; set; }

    [ObservableProperty]
    private string _clusterName = string.Empty;

    [ObservableProperty]
    private int _targetCount = 1;
}
