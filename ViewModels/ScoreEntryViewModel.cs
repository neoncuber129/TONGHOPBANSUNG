using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Tonghopbansung.Models;
using Tonghopbansung.Services;
using Tonghopbansung.Views;

namespace Tonghopbansung.ViewModels;

public partial class TargetScoreCellViewModel : ObservableObject
{
    public int TargetIndex { get; }
    public string Header { get; }
    public TargetKind Kind { get; }

    [ObservableProperty]
    private string _scoresText = string.Empty;

    [ObservableProperty]
    private int _targetTotal;

    public TargetScoreCellViewModel(int targetIndex, string header, TargetKind kind = TargetKind.Scored)
    {
        TargetIndex = targetIndex;
        Header = header;
        Kind = kind;
    }

    public void Refresh(Shooter shooter)
    {
        ScoresText = shooter.FormatTargetScores(TargetIndex, Kind);
        TargetTotal = shooter.TargetTotal(TargetIndex);
    }
}

public partial class ShooterRowViewModel : ObservableObject
{
    public Shooter Shooter { get; }
    public int Index { get; private set; }
    public ObservableCollection<TargetScoreCellViewModel> TargetCells { get; } = new();

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    private int _knockDownCount;

    [ObservableProperty]
    private string _classification = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    public ShooterRowViewModel(Shooter shooter, int index, ScorePreset preset)
    {
        Shooter = shooter;
        Index = index;
        var flat = preset.FlatTargets;
        for (var i = 0; i < flat.Count; i++)
        {
            var def = flat[i];
            TargetCells.Add(new TargetScoreCellViewModel(i, def.Name, def.Kind));
        }
        Refresh(preset);
    }

    public void SetIndex(int index)
    {
        if (Index == index) return;
        Index = index;
        OnPropertyChanged(nameof(Index));
    }

    public void Refresh(ScorePreset preset)
    {
        Shooter.EnsureShotMatrix(preset.GetRoundCounts());
        foreach (var cell in TargetCells)
            cell.Refresh(Shooter);

        Total = ScoreCalculator.TotalScore(Shooter, preset);
        KnockDownCount = ScoreCalculator.KnockDownCount(Shooter, preset);
        Classification = ScoreCalculator.Classify(Shooter, preset);
        ProgressText = $"{Shooter.EnteredShotCount}/{preset.TotalRounds}";
    }
}

public partial class ScoreButtonViewModel : ObservableObject
{
    public int Score { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    public ScoreButtonViewModel(int score, string? label = null)
    {
        Score = score;
        Label = label ?? score.ToString();
    }
}

public partial class RoundColumnViewModel : ObservableObject
{
    public int TargetIndex { get; }
    public int RoundIndex { get; }
    public bool IsKnockDown { get; }
    public string Title => IsKnockDown ? "Kết quả" : $"Đ{RoundIndex + 1}";
    public ObservableCollection<ScoreButtonViewModel> ScoreButtons { get; } = new();

    [ObservableProperty]
    private int? _currentValue;

    public RoundColumnViewModel(int targetIndex, int roundIndex, int? current, bool isKnockDown = false)
    {
        TargetIndex = targetIndex;
        RoundIndex = roundIndex;
        IsKnockDown = isKnockDown;
        CurrentValue = current;

        if (isKnockDown)
        {
            // 1 = Đổ, 0 = Không đổ
            ScoreButtons.Add(new ScoreButtonViewModel(1, "Đổ") { IsSelected = current == 1 });
            ScoreButtons.Add(new ScoreButtonViewModel(0, "Không") { IsSelected = current == 0 });
        }
        else
        {
            for (var s = 10; s >= 0; s--)
                ScoreButtons.Add(new ScoreButtonViewModel(s) { IsSelected = current == s });
        }
    }

    public static RoundColumnViewModel CreateKnockDown(int targetIndex, int roundIndex, int? current) =>
        new(targetIndex, roundIndex, current, isKnockDown: true);

    public void SelectScore(int score)
    {
        CurrentValue = score;
        foreach (var btn in ScoreButtons)
            btn.IsSelected = btn.Score == score;
    }

    public void Clear()
    {
        CurrentValue = null;
        foreach (var btn in ScoreButtons)
            btn.IsSelected = false;
    }

    public string DisplayLabel(int score) =>
        IsKnockDown ? (score == 1 ? "Đổ" : "Không") : score.ToString();
}

public partial class TargetEntryColumnViewModel : ObservableObject
{
    public int TargetIndex { get; }
    public string Title { get; }
    public TargetKind Kind { get; }
    public bool IsKnockDown => Kind == TargetKind.KnockDown;
    public ObservableCollection<RoundColumnViewModel> Rounds { get; } = new();

    public TargetEntryColumnViewModel(int targetIndex, TargetDefinition definition, IReadOnlyList<int?> shots)
    {
        TargetIndex = targetIndex;
        Title = definition.Name;
        Kind = definition.Kind;
        for (var r = 0; r < shots.Count; r++)
        {
            if (Kind == TargetKind.KnockDown)
                Rounds.Add(RoundColumnViewModel.CreateKnockDown(targetIndex, r, shots[r]));
            else
                Rounds.Add(new RoundColumnViewModel(targetIndex, r, shots[r]));
        }
    }
}

public partial class ClusterEntryGroupViewModel : ObservableObject
{
    public string Title { get; }
    public ObservableCollection<TargetEntryColumnViewModel> Targets { get; } = new();

    public ClusterEntryGroupViewModel(string title) => Title = title;
}

public partial class ScoreEntryDialogViewModel : ObservableObject
{
    private readonly Shooter _shooter;
    private readonly ScorePreset _preset;
    private readonly Action _onSaved;

    public string ShooterName => _shooter.Name;
    public ObservableCollection<ClusterEntryGroupViewModel> ClusterGroups { get; } = new();

    [ObservableProperty]
    private string _summaryText = string.Empty;

    public ScoreEntryDialogViewModel(Shooter shooter, ScorePreset preset, Action onSaved)
    {
        _shooter = shooter;
        _preset = preset;
        _onSaved = onSaved;
        _shooter.EnsureShotMatrix(preset.GetRoundCounts());

        var flatIndex = 0;
        foreach (var cluster in preset.Clusters)
        {
            var group = new ClusterEntryGroupViewModel(cluster.Name);
            foreach (var target in cluster.Targets)
            {
                var shots = _shooter.Shots[flatIndex];
                group.Targets.Add(new TargetEntryColumnViewModel(flatIndex, target, shots));
                flatIndex++;
            }
            ClusterGroups.Add(group);
        }

        UpdateSummary();
    }

    private IEnumerable<TargetEntryColumnViewModel> AllTargets =>
        ClusterGroups.SelectMany(g => g.Targets);

    [RelayCommand]
    private void SetScore(object? parameter)
    {
        if (parameter is not ScorePickParameter pick) return;
        pick.Round.SelectScore(pick.Score);
        UpdateSummary();
    }

    [RelayCommand]
    private void ClearRound(RoundColumnViewModel? round)
    {
        round?.Clear();
        UpdateSummary();
    }

    [RelayCommand]
    private void ClearAll()
    {
        if (MessageBox.Show("Xóa toàn bộ điểm của người này?", "Xác nhận",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        foreach (var target in AllTargets)
        {
            foreach (var round in target.Rounds)
                round.Clear();
        }
        UpdateSummary();
    }

    public void ApplyToShooter()
    {
        WriteCurrentToShooter();
        _shooter.NotifyScoresChanged();
        _onSaved();
    }

    private void UpdateSummary()
    {
        var entered = AllTargets.SelectMany(t => t.Rounds).Count(r => r.CurrentValue.HasValue);

        // Chỉ tính xếp loại tạm — không giữ thay đổi vào shooter (Hủy sẽ không lưu)
        var backup = CaptureShots();
        try
        {
            WriteCurrentToShooter();
            var total = ScoreCalculator.TotalScore(_shooter, _preset);
            var grade = ScoreCalculator.Classify(_shooter, _preset);
            SummaryText = string.IsNullOrWhiteSpace(grade)
                ? $"{total} điểm ({entered}/{_preset.TotalRounds} phát)"
                : $"{total} điểm ({entered}/{_preset.TotalRounds} phát) — {grade}";
        }
        finally
        {
            RestoreShots(backup);
        }
    }

    private void WriteCurrentToShooter()
    {
        foreach (var target in AllTargets)
        {
            foreach (var round in target.Rounds)
                _shooter.Shots[target.TargetIndex][round.RoundIndex] = round.CurrentValue;
        }
    }

    private static List<List<int?>> CaptureShots(Shooter shooter) =>
        shooter.Shots.Select(t => t.ToList()).ToList();

    private List<List<int?>> CaptureShots() => CaptureShots(_shooter);

    private void RestoreShots(List<List<int?>> shots)
    {
        for (var t = 0; t < shots.Count && t < _shooter.Shots.Count; t++)
        {
            var src = shots[t];
            var dst = _shooter.Shots[t];
            for (var r = 0; r < src.Count && r < dst.Count; r++)
                dst[r] = src[r];
        }
    }
}

public sealed class ScorePickParameter
{
    public required RoundColumnViewModel Round { get; init; }
    public required int Score { get; init; }
}

public partial class ScoreEntryViewModel : ObservableObject
{
    private readonly AppSession _session;
    private readonly System.Windows.Threading.DispatcherTimer _searchTimer;
    private Guid? _loadedSessionId;
    private string _headerSignature = string.Empty;

    public ObservableCollection<ShootingSession> Sessions => _session.Sessions;
    public ObservableCollection<ShooterRowViewModel> AllRows { get; } = new();
    public ObservableCollection<string> TargetHeaders { get; } = new();
    /// <summary>Số viên đạn từng cột bia — dùng để tính độ rộng cột hiển thị đủ kết quả.</summary>
    public ObservableCollection<int> TargetRoundCounts { get; } = new();
    public ObservableCollection<TargetKind> TargetKinds { get; } = new();

    /// <summary>View đã lọc — DataGrid bind vào đây để tìm kiếm không tạo lại toàn bộ dòng.</summary>
    public System.ComponentModel.ICollectionView Rows { get; }

    [ObservableProperty]
    private bool _showOnlySelected = false;

    [ObservableProperty]
    private string _nameSearch = string.Empty;

    [ObservableProperty]
    private ShooterRowViewModel? _selectedRow;

    [ObservableProperty]
    private int _addRowCount = 10;

    [ObservableProperty]
    private string _sessionInfo = string.Empty;

    [ObservableProperty]
    private int _visibleRowCount;

    public ScoreEntryViewModel(AppSession session)
    {
        _session = session;
        Rows = System.Windows.Data.CollectionViewSource.GetDefaultView(AllRows);
        Rows.Filter = FilterRow;

        _searchTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            ApplyFilter();
        };

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSession.SelectedSession))
            {
                OnPropertyChanged(nameof(SelectedSession));
                ReloadSessionRows(force: true);
            }
        };
        ReloadSessionRows(force: true);
    }

    public ShootingSession? SelectedSession
    {
        get => _session.SelectedSession;
        set
        {
            if (_session.SelectedSession == value) return;
            _session.SelectedSession = value;
            OnPropertyChanged();
            ReloadSessionRows(force: true);
            _session.Persist();
        }
    }

    partial void OnShowOnlySelectedChanged(bool value) => ApplyFilter();

    partial void OnNameSearchChanged(string value)
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    public void RefreshShooters() => ReloadSessionRows(force: true);

    private bool FilterRow(object obj)
    {
        if (obj is not ShooterRowViewModel row) return false;
        if (ShowOnlySelected && !row.Shooter.IsSelected) return false;

        var query = NameSearch.Trim();
        if (query.Length == 0) return true;

        var s = row.Shooter;
        return (!string.IsNullOrEmpty(s.Name) && s.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
               || (!string.IsNullOrEmpty(s.Rank) && s.Rank.Contains(query, StringComparison.CurrentCultureIgnoreCase))
               || (!string.IsNullOrEmpty(s.Position) && s.Position.Contains(query, StringComparison.CurrentCultureIgnoreCase))
               || (!string.IsNullOrEmpty(s.Unit) && s.Unit.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void ApplyFilter()
    {
        Rows.Refresh();
        VisibleRowCount = AllRows.Count(r => FilterRow(r));
    }

    [RelayCommand]
    private void ClearNameSearch()
    {
        NameSearch = string.Empty;
    }

    [RelayCommand]
    private async Task CreateSession()
    {
        if (_session.Groups.Count == 0)
        {
            MessageBox.Show("Chưa có nhóm. Hãy tạo nhóm (cấu hình bia) ở tab Nhóm trước.",
                "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var vm = new CreateSessionViewModel(_session.Groups, _session.SelectedGroup);
        var dialog = new CreateSessionDialog
        {
            Owner = Application.Current.MainWindow,
            DataContext = vm
        };

        if (dialog.ShowDialog() != true || vm.SelectedGroup is null)
            return;

        var preset = _session.GetPresetForGroup(vm.SelectedGroup);
        if (preset is null || preset.TargetCount == 0)
        {
            MessageBox.Show($"Nhóm \"{vm.SelectedGroup.Name}\" chưa cấu hình bia. Hãy sửa cấu hình bia trước.",
                "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await _session.CreateSessionAsync(vm.SessionName, vm.SelectedGroup, vm.PersonCount);
        OnPropertyChanged(nameof(SelectedSession));
        ReloadSessionRows(force: true);
        _session.StatusMessage = $"Đã tạo đợt \"{vm.SessionName}\" ({vm.PersonCount} người)";
    }

    [RelayCommand]
    private void RemoveSession()
    {
        if (SelectedSession is null) return;
        if (MessageBox.Show($"Xóa đợt \"{SelectedSession.Name}\" và toàn bộ danh sách / điểm của đợt?",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _session.RemoveSession(SelectedSession);
        OnPropertyChanged(nameof(SelectedSession));
        ReloadSessionRows(force: true);
    }

    [RelayCommand]
    private void RefreshRows() => ReloadSessionRows(force: true);

    private void ReloadSessionRows(bool force)
    {
        SessionInfo = string.Empty;

        if (SelectedSession is null)
        {
            AllRows.Clear();
            if (TargetHeaders.Count > 0)
            {
                TargetHeaders.Clear();
                TargetRoundCounts.Clear();
                TargetKinds.Clear();
                _headerSignature = string.Empty;
                OnPropertyChanged(nameof(TargetHeaders));
            }
            _loadedSessionId = null;
            VisibleRowCount = 0;
            SessionInfo = "Chưa có đợt bắn — bấm «Tạo đợt» để bắt đầu.";
            return;
        }

        var group = _session.GetGroupForSession(SelectedSession);
        var preset = _session.GetPresetForSession(SelectedSession);
        if (preset is null)
        {
            AllRows.Clear();
            _loadedSessionId = null;
            VisibleRowCount = 0;
            SessionInfo = "Đợt không gắn được nhóm / cấu hình bia.";
            return;
        }

        _session.EnsureSessionMatrices(SelectedSession);
        SessionInfo = $"Nhóm: {group?.Name ?? "?"} · {SelectedSession.PersonCount} người · {preset.Clusters.Count} phần · {preset.TargetCount} bia";

        var flat = preset.FlatTargets;
        var signature = string.Join("|", flat.Select(t => $"{t.Name}:{t.Kind}:{t.EffectiveRoundCount}:{t.MissPenalty}:{t.HitBonus}"));
        if (signature != _headerSignature)
        {
            TargetHeaders.Clear();
            TargetRoundCounts.Clear();
            TargetKinds.Clear();
            foreach (var t in flat)
            {
                TargetHeaders.Add(t.Name);
                TargetRoundCounts.Add(t.EffectiveRoundCount);
                TargetKinds.Add(t.Kind);
            }
            _headerSignature = signature;
            OnPropertyChanged(nameof(TargetHeaders));
            force = true;
        }

        if (!force && _loadedSessionId == SelectedSession.Id && AllRows.Count == SelectedSession.Shooters.Count)
        {
            ApplyFilter();
            return;
        }

        AllRows.Clear();
        var index = 1;
        foreach (var shooter in SelectedSession.Shooters.OrderBy(s => s.Order))
            AllRows.Add(new ShooterRowViewModel(shooter, index++, preset));

        _loadedSessionId = SelectedSession.Id;
        ApplyFilter();
    }

    public void RefreshRowsPublic() => ReloadSessionRows(force: true);

    public void PasteNamesFromClipboard(string clipboardText, int startRowIndex = 0)
    {
        if (SelectedSession is null) return;

        var entries = RosterParser.ParseEntries(clipboardText);
        if (entries.Count == 0) return;

        var preset = _session.GetPresetForSession(SelectedSession);
        if (preset is null) return;

        ApplyEntriesFromRow(entries, preset, Math.Max(0, startRowIndex));

        RenumberOrders();
        // Cập nhật dòng hiện có + thêm dòng mới, không rebuild toàn bộ
        SyncRowsAfterStructureChange(preset);
        _session.PersistDeferred();
        _session.StatusMessage = $"Đã dán {entries.Count} dòng";
    }

    private void SyncRowsAfterStructureChange(ScorePreset preset)
    {
        if (SelectedSession is null) return;

        var byId = AllRows.ToDictionary(r => r.Shooter.Id);
        var ordered = SelectedSession.Shooters.OrderBy(s => s.Order).ToList();
        AllRows.Clear();
        var index = 1;
        foreach (var shooter in ordered)
        {
            if (byId.TryGetValue(shooter.Id, out var existing))
            {
                existing.SetIndex(index++);
                AllRows.Add(existing);
            }
            else
            {
                AllRows.Add(new ShooterRowViewModel(shooter, index++, preset));
            }
        }

        _loadedSessionId = SelectedSession.Id;
        SessionInfo = $"Nhóm: {_session.GetGroupForSession(SelectedSession)?.Name ?? "?"} · {SelectedSession.PersonCount} người · {preset.Clusters.Count} phần · {preset.TargetCount} bia";
        ApplyFilter();
    }

    private void ApplyEntriesFromRow(List<RosterEntry> entries, ScorePreset preset, int startRowIndex)
    {
        var ordered = SelectedSession!.Shooters.OrderBy(s => s.Order).ToList();

        while (ordered.Count < startRowIndex + entries.Count)
        {
            var shooter = _session.CreateEmptyShooterForSession(SelectedSession, ordered.Count + 1);
            SelectedSession.Shooters.Add(shooter);
            ordered.Add(shooter);
        }

        var rounds = preset.GetRoundCounts();
        for (var i = 0; i < entries.Count; i++)
        {
            var row = ordered[startRowIndex + i];
            var entry = entries[i];
            row.Name = entry.Name;
            if (!string.IsNullOrWhiteSpace(entry.Rank))
                row.Rank = entry.Rank;
            if (!string.IsNullOrWhiteSpace(entry.Position))
                row.Position = entry.Position;
            if (!string.IsNullOrWhiteSpace(entry.Unit))
                row.Unit = entry.Unit;
            if (NeedsMatrixResize(row, rounds))
                row.EnsureShotMatrix(rounds);
        }
    }

    private static bool NeedsMatrixResize(Shooter shooter, IReadOnlyList<int> rounds)
    {
        if (shooter.Shots.Count != rounds.Count) return true;
        for (var i = 0; i < rounds.Count; i++)
        {
            if (shooter.Shots[i].Count != rounds[i])
                return true;
        }
        return false;
    }

    private void RenumberOrders()
    {
        if (SelectedSession is null) return;
        var order = 1;
        foreach (var row in AllRows)
            row.Shooter.Order = order++;
        RenumberAllIndices();
    }

    private void RenumberAllIndices()
    {
        var idx = 1;
        foreach (var row in AllRows)
            row.SetIndex(idx++);
    }

    /// <summary>Sắp xếp lưới theo cột (chuột phải trên header). STT tự đánh lại 1..n.</summary>
    public void SortRows(string columnHeader, bool ascending)
    {
        if (AllRows.Count == 0) return;

        var sorted = GetSortKey(columnHeader) is { } key
            ? (ascending ? AllRows.OrderBy(key) : AllRows.OrderByDescending(key))
            : AllRows.OrderBy(r => r.Shooter.Order);

        var list = sorted.ToList();
        AllRows.Clear();
        foreach (var row in list)
            AllRows.Add(row);

        RenumberOrders();
        ApplyFilter();
        _session.PersistDeferred();
    }

    private Func<ShooterRowViewModel, IComparable>? GetSortKey(string columnHeader)
    {
        var targetIdx = TargetHeaders.IndexOf(columnHeader);
        if (targetIdx >= 0)
            return r => targetIdx < r.TargetCells.Count ? r.TargetCells[targetIdx].TargetTotal : 0;

        return columnHeader switch
        {
            "STT" => r => r.Shooter.Order,
            "Họ tên" => r => r.Shooter.Name ?? string.Empty,
            "Cấp bậc" => r => r.Shooter.Rank ?? string.Empty,
            "Chức vụ" => r => r.Shooter.Position ?? string.Empty,
            "Đơn vị" => r => r.Shooter.Unit ?? string.Empty,
            "Tổng" => r => r.Total,
            "Bia đổ" => r => r.KnockDownCount,
            "Xếp loại" => r => r.Classification ?? string.Empty,
            "Tiến độ" => r => r.Shooter.EnteredShotCount,
            _ => null
        };
    }

    public bool CanSortColumn(string columnHeader) =>
        columnHeader is not ("Chọn" or "Nhập");

    [RelayCommand]
    private void SelectAll()
    {
        if (SelectedSession is null) return;
        foreach (var s in SelectedSession.Shooters.Where(s => !string.IsNullOrWhiteSpace(s.Name)))
            s.IsSelected = true;
        _session.PersistDeferred();
        ApplyFilter();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        if (SelectedSession is null) return;
        foreach (var s in SelectedSession.Shooters)
            s.IsSelected = false;
        _session.PersistDeferred();
        ApplyFilter();
    }

    [RelayCommand]
    private void AddRow()
    {
        AddRows(1);
    }

    [RelayCommand]
    private void AddMoreRows()
    {
        AddRows(Math.Max(1, AddRowCount));
    }

    private void AddRows(int count)
    {
        if (SelectedSession is null) return;
        var preset = _session.GetPresetForSession(SelectedSession);
        if (preset is null) return;

        var startIndex = AllRows.Count + 1;
        for (var i = 0; i < count; i++)
        {
            var shooter = _session.CreateEmptyShooterForSession(SelectedSession, SelectedSession.Shooters.Count + 1);
            SelectedSession.Shooters.Add(shooter);
            AllRows.Add(new ShooterRowViewModel(shooter, startIndex + i, preset));
        }

        RenumberOrders();
        SessionInfo = $"Nhóm: {_session.GetGroupForSession(SelectedSession)?.Name ?? "?"} · {SelectedSession.PersonCount} người · {preset.Clusters.Count} phần · {preset.TargetCount} bia";
        ApplyFilter();
        _session.PersistDeferred();
        _session.StatusMessage = $"Đã thêm {count} dòng (tổng {SelectedSession.Shooters.Count})";
    }

    [RelayCommand]
    private async Task RemoveSelectedRows()
    {
        if (SelectedSession is null) return;

        var toRemove = AllRows.Where(r => r.Shooter.IsSelected).Select(r => r.Shooter).ToList();
        if (toRemove.Count == 0 && SelectedRow is not null)
            toRemove.Add(SelectedRow.Shooter);

        if (toRemove.Count == 0)
        {
            MessageBox.Show("Hãy tick «Chọn» các dòng cần xóa.", "Xóa dòng",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var ask = MessageBox.Show(
            $"Xóa {toRemove.Count} dòng đã chọn?\nThao tác này không hoàn tác được.",
            "Xác nhận xóa dòng",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (ask != MessageBoxResult.Yes) return;

        await _session.RunBusyAsync("Đang xóa dòng...", async () =>
        {
            var removeSet = toRemove.ToHashSet();
            foreach (var s in toRemove)
                SelectedSession.Shooters.Remove(s);

            for (var i = AllRows.Count - 1; i >= 0; i--)
            {
                if (removeSet.Contains(AllRows[i].Shooter))
                    AllRows.RemoveAt(i);
            }

            RenumberOrders();
            var preset = _session.GetPresetForSession(SelectedSession);
            if (preset is not null)
                SessionInfo = $"Nhóm: {_session.GetGroupForSession(SelectedSession)?.Name ?? "?"} · {SelectedSession.PersonCount} người · {preset.Clusters.Count} phần · {preset.TargetCount} bia";

            ApplyFilter();
            await _session.PersistAsync().ConfigureAwait(true);
        });

        _session.StatusMessage = $"Đã xóa {toRemove.Count} dòng";
    }

    public void PersistEdits() => _session.PersistDeferred();

    [RelayCommand]
    private void OpenEntry(ShooterRowViewModel? row)
    {
        if (row is null || SelectedSession is null) return;
        var preset = _session.GetPresetForSession(SelectedSession);
        if (preset is null) return;

        var dialogVm = new ScoreEntryDialogViewModel(row.Shooter, preset, () =>
        {
            _session.PersistDeferred(400);
            row.Refresh(preset);
            _session.StatusMessage = $"Đã nhập điểm: {row.Shooter.Name}";
        });

        var dialog = new ScoreEntryDialog
        {
            Owner = Application.Current.MainWindow,
            DataContext = dialogVm
        };

        dialog.ShowDialog();
        // Chỉ refresh dòng hiện tại — không rebuild cả lưới
        row.Refresh(preset);
    }

    [RelayCommand]
    private async Task ShowReport()
    {
        if (SelectedSession is null)
        {
            MessageBox.Show("Hãy chọn đợt bắn trước.", "Báo cáo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ClassificationReportViewModel? vm = null;
        await _session.RunBusyAsync("Đang lập báo cáo...", async () =>
        {
            vm = ClassificationReportViewModel.Build(_session, SelectedSession);
            await Task.CompletedTask;
        });

        if (vm is null)
        {
            MessageBox.Show("Đợt chưa gắn nhóm / cấu hình bia.", "Báo cáo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (vm.TotalCount == 0)
        {
            MessageBox.Show("Chưa có người có họ tên để thống kê.", "Báo cáo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new ClassificationReportDialog
        {
            Owner = Application.Current.MainWindow,
            DataContext = vm
        };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private async Task ExportExcel()
    {
        if (SelectedSession is null)
        {
            MessageBox.Show("Hãy chọn đợt bắn trước.", "Xuất Excel",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var group = _session.GetGroupForSession(SelectedSession);
        var preset = _session.GetPresetForSession(SelectedSession);
        if (preset is null)
        {
            MessageBox.Show("Đợt chưa gắn nhóm / cấu hình bia.", "Xuất Excel",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var named = SelectedSession.Shooters
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .OrderBy(s => s.Order)
            .ToList();
        var selected = named.Where(s => s.IsSelected).ToList();
        var source = selected.Count > 0 ? selected : named;

        if (source.Count == 0)
        {
            MessageBox.Show("Chưa có người có họ tên để xuất.", "Xuất Excel",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.Count == 0)
        {
            var ask = MessageBox.Show(
                $"Chưa tick «Chọn» ai.\nXuất tất cả {named.Count} người có tên?",
                "Xuất Excel", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;
        }

        var safeName = string.Join("_", SelectedSession.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"KetQua_{safeName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            DefaultExt = ".xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var rounds = preset.GetRoundCounts();
            var targets = preset.FlatTargets.ToList();
            var groupName = group?.Name ?? preset.Name;
            var sessionName = SelectedSession.Name;
            var filePath = dlg.FileName;

            // Chuẩn bị dữ liệu trên UI (object model), ghi file trên nền
            var rows = new List<ExcelReportRow>();
            var index = 1;
            foreach (var s in source)
            {
                s.EnsureShotMatrix(rounds);
                var details = new List<string>(targets.Count);
                for (var t = 0; t < targets.Count; t++)
                    details.Add(s.FormatTargetScores(t, targets[t].Kind));

                rows.Add(new ExcelReportRow
                {
                    Index = index++,
                    Name = s.Name,
                    Rank = s.Rank,
                    Position = s.Position,
                    Unit = s.Unit,
                    GroupName = groupName,
                    TargetDetails = details,
                    Total = ScoreCalculator.TotalScore(s, preset),
                    KnockDownCount = ScoreCalculator.KnockDownCount(s, preset),
                    Classification = ScoreCalculator.Classify(s, preset)
                });
            }

            await _session.RunBusyAsync("Đang xuất Excel...", () =>
            {
                ExcelExportService.ExportReport(filePath, sessionName, groupName, rows, targets);
            });

            _session.StatusMessage = $"Đã xuất Excel: {rows.Count} người → {filePath}";
            MessageBox.Show($"Đã xuất {rows.Count} người.\n{filePath}", "Xuất Excel",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi xuất Excel:\n{ex.Message}", "Lỗi",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
