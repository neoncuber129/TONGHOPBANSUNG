using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tonghopbansung.Models;
using Tonghopbansung.Views;

namespace Tonghopbansung.ViewModels;

public partial class PresetEditorViewModel : ObservableObject
{
    private readonly AppSession _session;
    private readonly Group _group;
    private readonly bool _isNew;

    public ScorePreset Preset { get; }
    public Group Group => _group;

    [ObservableProperty]
    private ClassificationRule? _selectedRule;

    [ObservableProperty]
    private TargetCluster? _selectedCluster;

    [ObservableProperty]
    private TargetDefinition? _selectedTarget;

    [ObservableProperty]
    private PresetWizardStep _presetStep = PresetWizardStep.GroupName;

    [ObservableProperty]
    private int _desiredClusterCount = 2;

    [ObservableProperty]
    private string _stepHint = string.Empty;

    public ObservableCollection<ClusterSetupRow> ClusterSetupRows { get; } = new();

    public string DialogTitle => _isNew
        ? $"Cấu hình bia — {_group.Name}"
        : $"Sửa cấu hình bia — {_group.Name}";

    public PresetEditorViewModel(AppSession session, Group group, ScorePreset preset, bool isNew)
    {
        _session = session;
        _group = group;
        Preset = preset;
        _isNew = isNew;
        Preset.Name = group.Name;

        if (preset.TargetCount > 0 && !_isNew)
            PresetStep = PresetWizardStep.DetailEdit;
        else
            PresetStep = PresetWizardStep.GroupName;

        SelectedCluster = preset.Clusters.FirstOrDefault();
        SelectedTarget = SelectedCluster?.Targets.FirstOrDefault();
        DesiredClusterCount = Math.Max(1, preset.Clusters.Count > 0 ? preset.Clusters.Count : 2);
        UpdateStepHint();
    }

    partial void OnPresetStepChanged(PresetWizardStep value) => UpdateStepHint();

    private void UpdateStepHint()
    {
        StepHint = PresetStep switch
        {
            PresetWizardStep.GroupName => "Bước 1 — Nhóm: nhập tên nhóm, bấm «Tiếp».",
            PresetWizardStep.ClusterCount => "Bước 2 — Phần: nhập số phần (các khối bia), bấm «Tiếp».",
            PresetWizardStep.TargetsPerCluster => "Bước 3 — Số bia từng phần: nhập số bia cho mỗi phần, bấm «Tạo bảng».",
            PresetWizardStep.DetailEdit => "Bước 4: sửa tên phần / tên bia / số đạn. Nút cuối mỗi bia: Đổ ↔ Chấm điểm.",
            _ => string.Empty
        };
    }

    [RelayCommand]
    private void ApplyGroupName()
    {
        if (string.IsNullOrWhiteSpace(Preset.Name))
        {
            MessageBox.Show("Hãy nhập tên nhóm.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Preset.Name = Preset.Name.Trim();
        _group.Name = Preset.Name;
        DesiredClusterCount = Math.Max(1, DesiredClusterCount);
        PresetStep = PresetWizardStep.ClusterCount;
    }

    [RelayCommand]
    private void ApplyClusterCount()
    {
        var count = Math.Max(1, DesiredClusterCount);
        DesiredClusterCount = count;

        ClusterSetupRows.Clear();
        for (var i = 1; i <= count; i++)
        {
            ClusterSetupRows.Add(new ClusterSetupRow
            {
                Index = i,
                ClusterName = $"Phần {i}",
                TargetCount = 1
            });
        }

        PresetStep = PresetWizardStep.TargetsPerCluster;
    }

    [RelayCommand]
    private void BuildPresetFromSetup()
    {
        if (ClusterSetupRows.Count == 0)
        {
            MessageBox.Show("Chưa có dữ liệu phần.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var groupsUsing = _session.Groups.Where(g => g.PresetId == Preset.Id).ToList();
        if (_session.AnyEnteredScoresForPreset(Preset.Id) &&
            MessageBox.Show("Tạo lại cấu hình bia có thể ảnh hưởng điểm đã nhập. Tiếp tục?",
                "Cảnh báo", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        Preset.Clusters.Clear();
        var globalBia = 1;

        foreach (var row in ClusterSetupRows)
        {
            var targetCount = Math.Max(1, row.TargetCount);
            var cluster = new TargetCluster
            {
                Name = string.IsNullOrWhiteSpace(row.ClusterName) ? $"Phần {row.Index}" : row.ClusterName.Trim()
            };

            for (var t = 0; t < targetCount; t++)
            {
                cluster.Targets.Add(new TargetDefinition
                {
                    Name = $"Bia {globalBia++}",
                    RoundCount = 5,
                    Kind = TargetKind.Scored
                });
            }

            Preset.Clusters.Add(cluster);
        }

        Preset.InvalidateLayoutCache();
        SelectedCluster = Preset.Clusters.FirstOrDefault();
        SelectedTarget = SelectedCluster?.Targets.FirstOrDefault();
        PresetStep = PresetWizardStep.DetailEdit;
        ApplyLayoutToGroupsUsingPreset();
        _session.Persist();
    }

    [RelayCommand]
    private void BackStep()
    {
        PresetStep = PresetStep switch
        {
            PresetWizardStep.ClusterCount => PresetWizardStep.GroupName,
            PresetWizardStep.TargetsPerCluster => PresetWizardStep.ClusterCount,
            PresetWizardStep.DetailEdit when ClusterSetupRows.Count > 0 => PresetWizardStep.TargetsPerCluster,
            PresetWizardStep.DetailEdit => PresetWizardStep.ClusterCount,
            _ => PresetWizardStep.GroupName
        };
    }

    [RelayCommand]
    private void RestartSetup()
    {
        if (Preset.TargetCount > 0 &&
            MessageBox.Show("Thiết lập lại sẽ xóa cấu hình bia hiện tại. Tiếp tục?",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        Preset.Clusters.Clear();
        DesiredClusterCount = 2;
        ClusterSetupRows.Clear();
        Preset.InvalidateLayoutCache();
        PresetStep = PresetWizardStep.GroupName;
        ApplyLayoutToGroupsUsingPreset();
        _session.Persist();
    }

    [RelayCommand]
    private void SavePresetDetail()
    {
        if (string.IsNullOrWhiteSpace(Preset.Name))
        {
            MessageBox.Show("Hãy nhập tên nhóm.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var t in Preset.FlatTargets)
        {
            if (t.Kind == TargetKind.KnockDown)
            {
                t.RoundCount = 1;
                t.MissPenalty = Math.Max(0, t.MissPenalty);
                t.HitBonus = Math.Max(0, t.HitBonus);
            }
            else
            {
                t.RoundCount = Math.Max(1, t.RoundCount);
                t.MissPenalty = 0;
                t.HitBonus = 0;
            }
        }

        ApplyLayoutToGroupsUsingPreset();
        _group.Name = Preset.Name.Trim();
        Preset.Name = _group.Name;
        _session.Persist();
        MessageBox.Show("Đã lưu cấu hình bia của nhóm.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ToggleTargetKind(TargetDefinition? target)
    {
        if (target is null) return;
        target.ToggleKind();
        Preset.InvalidateLayoutCache();
        ApplyLayoutToGroupsUsingPreset();
        _session.Persist();
    }

    [RelayCommand]
    private void AddRule()
    {
        var rule = new ClassificationRule { Label = "Hạng mới", MinScore = 0, Priority = 0 };
        rule.EnsureLegacyCondition();
        if (!OpenRuleEditor(rule))
            return;

        Preset.ClassificationRules.Add(rule);
        SelectedRule = rule;
        _session.Persist();
    }

    [RelayCommand]
    private void EditRule()
    {
        if (SelectedRule is null)
        {
            MessageBox.Show("Hãy chọn một hạng phân loại.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!OpenRuleEditor(SelectedRule))
            return;

        SelectedRule.NotifySummaryChanged();
        _session.Persist();
    }

    [RelayCommand]
    private void RemoveRule()
    {
        if (SelectedRule is null) return;
        if (MessageBox.Show($"Xóa hạng \"{SelectedRule.Label}\"?", "Xác nhận",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        Preset.ClassificationRules.Remove(SelectedRule);
        SelectedRule = Preset.ClassificationRules.FirstOrDefault();
        _session.Persist();
    }

    private bool OpenRuleEditor(ClassificationRule rule)
    {
        var vm = new ClassificationRuleEditorViewModel(Preset, rule);
        var dialog = new ClassificationRuleEditorDialog
        {
            Owner = Application.Current.MainWindow,
            DataContext = vm
        };
        return dialog.ShowDialog() == true;
    }

    private void ApplyLayoutToGroupsUsingPreset()
    {
        _session.ResizeMatricesForPreset(Preset.Id);
    }
}
