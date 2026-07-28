using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Tonghopbansung.ViewModels;

public partial class DeleteDataItem : ObservableObject
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;
}

public partial class DeleteDataViewModel : ObservableObject
{
    private readonly AppSession _session;

    public ObservableCollection<DeleteDataItem> Sessions { get; } = new();
    public ObservableCollection<DeleteDataItem> Presets { get; } = new();

    public DeleteDataViewModel(AppSession session)
    {
        _session = session;
        Reload();
    }

    public void Reload()
    {
        Sessions.Clear();
        foreach (var s in _session.Sessions.OrderBy(x => x.CreatedAt).ThenBy(x => x.Name))
        {
            var group = _session.GetGroupForSession(s);
            Sessions.Add(new DeleteDataItem
            {
                Id = s.Id,
                Title = s.Name,
                Subtitle = $"Nhóm: {group?.Name ?? "?"} · {s.PersonCount} người"
            });
        }

        Presets.Clear();
        foreach (var g in _session.Groups.OrderBy(x => x.Name))
        {
            var preset = _session.GetPresetForGroup(g);
            var sessionCount = _session.Sessions.Count(s => s.GroupId == g.Id);
            Presets.Add(new DeleteDataItem
            {
                Id = g.Id,
                Title = g.Name,
                Subtitle = sessionCount > 0
                    ? $"Preset · {preset?.TargetCount ?? 0} bia · {sessionCount} đợt"
                    : $"Preset · {preset?.TargetCount ?? 0} bia"
            });
        }
    }

    [RelayCommand]
    private void SelectAllSessions()
    {
        foreach (var i in Sessions) i.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSessions()
    {
        foreach (var i in Sessions) i.IsSelected = false;
    }

    [RelayCommand]
    private void SelectAllPresets()
    {
        foreach (var i in Presets) i.IsSelected = true;
    }

    [RelayCommand]
    private void ClearPresets()
    {
        foreach (var i in Presets) i.IsSelected = false;
    }

    public async Task<bool> DeleteSelectedAsync()
    {
        var sessionIds = Sessions.Where(x => x.IsSelected).Select(x => x.Id).ToHashSet();
        var groupIds = Presets.Where(x => x.IsSelected).Select(x => x.Id).ToHashSet();

        if (sessionIds.Count == 0 && groupIds.Count == 0)
        {
            MessageBox.Show("Hãy tick chọn đợt hoặc preset cần xóa.", "Xóa dữ liệu",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (groupIds.Count > 0 && groupIds.Count >= _session.Groups.Count)
        {
            MessageBox.Show("Phải giữ lại ít nhất một preset / nhóm.", "Không thể xóa",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // Đợt thuộc preset bị xóa (nếu chưa tick) cũng sẽ bị xóa theo
        var linkedSessions = _session.Sessions
            .Where(s => groupIds.Contains(s.GroupId) && !sessionIds.Contains(s.Id))
            .ToList();

        var parts = new List<string>();
        if (sessionIds.Count > 0)
            parts.Add($"{sessionIds.Count} đợt đã chọn");
        if (groupIds.Count > 0)
            parts.Add($"{groupIds.Count} preset/nhóm");
        if (linkedSessions.Count > 0)
            parts.Add($"{linkedSessions.Count} đợt thuộc preset bị xóa");

        var ask = MessageBox.Show(
            $"Xóa: {string.Join(", ", parts)}?\nThao tác này không hoàn tác được.",
            "Xác nhận xóa dữ liệu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (ask != MessageBoxResult.Yes) return false;

        await _session.RunBusyAsync("Đang xóa dữ liệu...", async () =>
        {
            // 1) Xóa đợt đã chọn + đợt thuộc preset bị xóa
            var removeSessionIds = sessionIds
                .Concat(linkedSessions.Select(s => s.Id))
                .ToHashSet();

            foreach (var session in _session.Sessions.Where(s => removeSessionIds.Contains(s.Id)).ToList())
                _session.Sessions.Remove(session);

            if (_session.SelectedSession is not null &&
                removeSessionIds.Contains(_session.SelectedSession.Id))
            {
                _session.SelectedSession = _session.Sessions.FirstOrDefault();
            }

            // 2) Xóa preset / nhóm
            foreach (var group in _session.Groups.Where(g => groupIds.Contains(g.Id)).ToList())
            {
                var presetId = group.PresetId;
                _session.Groups.Remove(group);
                if (!_session.Groups.Any(g => g.PresetId == presetId))
                {
                    var orphan = _session.GetPreset(presetId);
                    if (orphan is not null)
                        _session.Presets.Remove(orphan);
                }
            }

            if (_session.SelectedGroup is not null && groupIds.Contains(_session.SelectedGroup.Id))
                _session.SelectedGroup = _session.Groups.FirstOrDefault();

            _session.EnsureOnePresetPerGroup();
            await _session.PersistAsync().ConfigureAwait(true);
        });

        _session.StatusMessage = "Đã xóa dữ liệu đã chọn";
        return true;
    }
}
