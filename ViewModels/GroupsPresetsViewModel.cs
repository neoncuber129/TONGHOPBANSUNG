using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tonghopbansung.Models;
using Tonghopbansung.Views;

namespace Tonghopbansung.ViewModels;

public partial class GroupsPresetsViewModel : ObservableObject
{
    private readonly AppSession _session;

    public ObservableCollection<Group> Groups => _session.Groups;

    public GroupsPresetsViewModel(AppSession session)
    {
        _session = session;
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSession.SelectedGroup))
            {
                OnPropertyChanged(nameof(SelectedGroup));
                OnPropertyChanged(nameof(SelectedGroupSummary));
                OnPropertyChanged(nameof(SelectedPreset));
            }
        };
    }

    public Group? SelectedGroup
    {
        get => _session.SelectedGroup;
        set
        {
            if (_session.SelectedGroup == value) return;
            _session.SelectedGroup = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedGroupSummary));
            OnPropertyChanged(nameof(SelectedPreset));
            _session.Persist();
        }
    }

    public ScorePreset? SelectedPreset => _session.GetPresetForGroup(SelectedGroup);

    public string SelectedGroupSummary => GetGroupConfigSummary(SelectedGroup);

    public void NotifyAfterDataChange()
    {
        OnPropertyChanged(nameof(SelectedGroup));
        OnPropertyChanged(nameof(SelectedGroupSummary));
        OnPropertyChanged(nameof(SelectedPreset));
        OnPropertyChanged(nameof(Groups));
    }

    public string GetGroupConfigSummary(Group? group)
    {
        var preset = _session.GetPresetForGroup(group);
        if (preset is null) return "Chưa có cấu hình";
        if (preset.TargetCount == 0) return "Chưa cấu hình bia";
        var scoreHints = preset.FlatTargets
            .Where(t => t.Kind == TargetKind.KnockDown && (t.HitBonus > 0 || t.MissPenalty > 0))
            .Select(t =>
            {
                var parts = new List<string>();
                if (t.HitBonus > 0) parts.Add($"+{t.HitBonus}");
                if (t.MissPenalty > 0) parts.Add($"−{t.MissPenalty}");
                return $"{t.Name}:{string.Join("/", parts)}";
            })
            .ToList();
        var scoreText = scoreHints.Count > 0 ? $" · Bia đổ {string.Join(", ", scoreHints)}" : string.Empty;
        return $"{preset.Clusters.Count} phần · {preset.TargetCount} bia · {preset.TotalRounds} phát{scoreText}";
    }

    [RelayCommand]
    private void AddGroup()
    {
        var preset = _session.CreateEmptyPreset($"Nhóm {Groups.Count + 1}");
        _session.Presets.Add(preset);

        var group = new Group
        {
            Name = preset.Name,
            PresetId = preset.Id
        };
        Groups.Add(group);
        SelectedGroup = group;
        _session.Persist();

        if (OpenEditor(group, isNew: true) != true)
        {
            // Hủy tạo mới → xóa nhóm + preset trống
            if (preset.TargetCount == 0)
            {
                Groups.Remove(group);
                _session.Presets.Remove(preset);
                SelectedGroup = Groups.FirstOrDefault();
                _session.Persist();
            }
        }
        else
        {
            _session.SyncPresetNameWithGroup(group);
            _session.Persist();
        }
    }

    [RelayCommand]
    private void EditGroupPreset()
    {
        if (SelectedGroup is null)
        {
            MessageBox.Show("Hãy chọn một nhóm.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var preset = _session.GetPresetForGroup(SelectedGroup);
        if (preset is null)
        {
            preset = _session.CreateEmptyPreset(SelectedGroup.Name);
            _session.Presets.Add(preset);
            SelectedGroup.PresetId = preset.Id;
            _session.Persist();
        }

        OpenEditor(SelectedGroup, isNew: preset.TargetCount == 0);
        _session.SyncPresetNameWithGroup(SelectedGroup);
        _session.Persist();
        OnPropertyChanged(nameof(SelectedGroupSummary));
        OnPropertyChanged(nameof(SelectedPreset));
    }

    [RelayCommand]
    private void RemoveGroup()
    {
        if (SelectedGroup is null) return;
        if (Groups.Count <= 1)
        {
            MessageBox.Show("Phải còn ít nhất một nhóm.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var usedBySessions = _session.Sessions.Count(s => s.GroupId == SelectedGroup.Id);
        var warn = usedBySessions > 0
            ? $"Nhóm \"{SelectedGroup.Name}\" đang được {usedBySessions} đợt bắn dùng. Xóa nhóm sẽ chuyển các đợt đó sang nhóm còn lại. Tiếp tục?"
            : $"Xóa nhóm \"{SelectedGroup.Name}\" và cấu hình bia của nhóm?";

        if (MessageBox.Show(warn, "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var presetId = SelectedGroup.PresetId;
        var fallback = Groups.First(g => g.Id != SelectedGroup.Id);
        foreach (var session in _session.Sessions.Where(s => s.GroupId == SelectedGroup.Id).ToList())
        {
            session.GroupId = fallback.Id;
            _session.EnsureSessionMatrices(session);
        }

        Groups.Remove(SelectedGroup);
        SelectedGroup = Groups.FirstOrDefault();

        if (!Groups.Any(g => g.PresetId == presetId))
        {
            var orphan = _session.GetPreset(presetId);
            if (orphan is not null)
                _session.Presets.Remove(orphan);
        }

        _session.Persist();
    }

    private bool? OpenEditor(Group group, bool isNew)
    {
        var preset = _session.GetPresetForGroup(group);
        if (preset is null) return false;

        // Đồng bộ tên trước khi sửa
        preset.Name = group.Name;

        var vm = new PresetEditorViewModel(_session, group, preset, isNew);
        var dialog = new PresetEditorDialog
        {
            Owner = Application.Current.MainWindow,
            DataContext = vm
        };
        var result = dialog.ShowDialog();

        // Sau dialog: tên nhóm = tên đã sửa trong editor
        group.Name = preset.Name;
        return result;
    }
}
