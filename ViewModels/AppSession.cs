using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tonghopbansung.Models;
using Tonghopbansung.Services;

namespace Tonghopbansung.ViewModels;

/// <summary>Trạng thái dùng chung giữa các tab; tự lưu khi Persist().</summary>
public partial class AppSession : ObservableObject
{
    private readonly IDataStore _store;
    private readonly IBackupService _backup;
    private readonly ISessionTransferService _sessionTransfer;

    public ObservableCollection<ScorePreset> Presets { get; } = new();
    public ObservableCollection<Group> Groups { get; } = new();
    public ObservableCollection<ShootingSession> Sessions { get; } = new();

    [ObservableProperty]
    private Group? _selectedGroup;

    [ObservableProperty]
    private ShootingSession? _selectedSession;

    [ObservableProperty]
    private string _statusMessage = "Sẵn sàng";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    public AppSession(IDataStore store, IBackupService backup, ISessionTransferService? sessionTransfer = null)
    {
        _store = store;
        _backup = backup;
        _sessionTransfer = sessionTransfer ?? new SessionTransferService();
    }

    public string DataDirectory => _store.DataDirectory;

    /// <summary>Chạy công việc nặng trên nền, hiện overlay progress trên UI.</summary>
    public async Task RunBusyAsync(string message, Action backgroundWork)
    {
        IsBusy = true;
        BusyMessage = message;
        try
        {
            await Task.Yield();
            await Task.Run(backgroundWork).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    public async Task RunBusyAsync(string message, Func<Task> work)
    {
        IsBusy = true;
        BusyMessage = message;
        try
        {
            await Task.Yield();
            await work().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    public void Load()
    {
        var state = _store.Load();
        ApplyState(state, persist: false);
        StatusMessage = "Đã tải dữ liệu";
    }

    public void Persist()
    {
        FlushDeferredPersist();
        SaveNow();
    }

    public async Task PersistAsync(string? busyMessage = null)
    {
        FlushDeferredPersist(save: false);
        var state = ToState();
        if (string.IsNullOrWhiteSpace(busyMessage))
        {
            await Task.Run(() => _store.Save(state)).ConfigureAwait(true);
            StatusMessage = $"Đã lưu {DateTime.Now:HH:mm:ss}";
            return;
        }

        await RunBusyAsync(busyMessage, () => _store.Save(state));
        StatusMessage = $"Đã lưu {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>Lưu trễ (gộp nhiều lần sửa ô) — giảm ghi disk.</summary>
    public void PersistDeferred(int delayMs = 900)
    {
        _persistPending = true;
        if (_persistTimer is null)
        {
            _persistTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(delayMs)
            };
            _persistTimer.Tick += (_, _) =>
            {
                _persistTimer.Stop();
                if (!_persistPending) return;
                _persistPending = false;
                SaveNow();
            };
        }

        _persistTimer.Stop();
        _persistTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _persistTimer.Start();
    }

    public void FlushDeferredPersist() => FlushDeferredPersist(save: true);

    private void FlushDeferredPersist(bool save)
    {
        if (_persistTimer is not null)
            _persistTimer.Stop();
        if (!_persistPending) return;
        _persistPending = false;
        if (save)
            SaveNow();
    }

    private void SaveNow()
    {
        _store.Save(ToState());
        StatusMessage = $"Đã lưu {DateTime.Now:HH:mm:ss}";
    }

    private System.Windows.Threading.DispatcherTimer? _persistTimer;
    private bool _persistPending;

    public AppState ToState() => new()
    {
        Presets = Presets.ToList(),
        Groups = Groups.ToList(),
        Sessions = Sessions.ToList(),
        ActiveGroupId = SelectedGroup?.Id,
        ActiveSessionId = SelectedSession?.Id
    };

    public void ApplyState(AppState state, bool persist)
    {
        Presets.Clear();
        foreach (var p in state.Presets)
        {
            if (p.TargetCount == 0)
                p.EnsureDefaultClusters();
            Presets.Add(p);
        }

        Groups.Clear();
        foreach (var g in state.Groups)
            Groups.Add(g);

        Sessions.Clear();
        foreach (var s in state.Sessions)
            Sessions.Add(s);

        MigrateGroupShootersToSessions();

        SelectedGroup = Groups.FirstOrDefault(g => g.Id == state.ActiveGroupId)
                        ?? Groups.FirstOrDefault();

        SelectedSession = Sessions.FirstOrDefault(s => s.Id == state.ActiveSessionId)
                          ?? Sessions.FirstOrDefault();

        EnsureOnePresetPerGroup();
        EnsureAllSessionMatrices();
        if (persist)
            Persist();
    }

    /// <summary>Dữ liệu cũ: danh sách nằm trên Group → chuyển sang Đợt bắn.</summary>
    private void MigrateGroupShootersToSessions()
    {
        foreach (var group in Groups)
        {
            if (group.Shooters.Count == 0) continue;

            var keep = group.Shooters
                .Where(s => !string.IsNullOrWhiteSpace(s.Name) || s.EnteredShotCount > 0)
                .OrderBy(s => s.Order)
                .ToList();

            if (keep.Count == 0)
            {
                group.Shooters.Clear();
                continue;
            }

            // Tránh tạo trùng nếu đã có đợt chứa cùng shooter Id
            var already = Sessions.Any(sess =>
                sess.GroupId == group.Id &&
                sess.Shooters.Any(s => keep.Any(k => k.Id == s.Id)));
            if (!already)
            {
                var session = new ShootingSession
                {
                    Name = $"Đợt — {group.Name}",
                    GroupId = group.Id,
                    CreatedAt = DateTime.Now
                };
                var order = 1;
                foreach (var s in keep)
                {
                    s.Order = order++;
                    session.Shooters.Add(s);
                }
                Sessions.Add(session);
            }

            group.Shooters.Clear();
        }
    }

    public void EnsureOnePresetPerGroup()
    {
        var byId = Presets.ToDictionary(p => p.Id);

        foreach (var group in Groups.ToList())
        {
            if (!byId.TryGetValue(group.PresetId, out var shared))
            {
                var created = CreateDefaultPreset(group.Name);
                Presets.Add(created);
                byId[created.Id] = created;
                group.PresetId = created.Id;
                continue;
            }

            var sharers = Groups.Where(g => g.PresetId == shared.Id).ToList();
            if (sharers.Count <= 1)
            {
                shared.Name = group.Name;
                continue;
            }

            foreach (var extra in sharers.Skip(1))
            {
                var clone = shared.CloneDeep(extra.Name);
                Presets.Add(clone);
                byId[clone.Id] = clone;
                extra.PresetId = clone.Id;
            }

            shared.Name = sharers[0].Name;
        }

        var used = Groups.Select(g => g.PresetId).ToHashSet();
        for (var i = Presets.Count - 1; i >= 0; i--)
        {
            if (!used.Contains(Presets[i].Id))
                Presets.RemoveAt(i);
        }
    }

    public ScorePreset CreateEmptyPreset(string name)
    {
        var preset = CreateDefaultPreset(name);
        preset.Clusters.Clear();
        return preset;
    }

    public void SyncPresetNameWithGroup(Group group)
    {
        var preset = GetPreset(group.PresetId);
        if (preset is not null)
            preset.Name = group.Name;
    }

    public ScorePreset? GetPreset(Guid presetId) =>
        Presets.FirstOrDefault(p => p.Id == presetId);

    public ScorePreset? GetPresetForGroup(Group? group) =>
        group is null ? null : GetPreset(group.PresetId);

    public Group? GetGroup(Guid groupId) =>
        Groups.FirstOrDefault(g => g.Id == groupId);

    public Group? GetGroupForSession(ShootingSession? session) =>
        session is null ? null : GetGroup(session.GroupId);

    public ScorePreset? GetPresetForSession(ShootingSession? session) =>
        GetPresetForGroup(GetGroupForSession(session));

    public void EnsureAllSessionMatrices()
    {
        foreach (var session in Sessions)
            EnsureSessionMatrices(session);
    }

    public void EnsureSessionMatrices(ShootingSession session)
    {
        var preset = GetPresetForSession(session);
        var rounds = preset?.GetRoundCounts() ?? Array.Empty<int>();
        var i = 1;
        foreach (var s in session.Shooters.OrderBy(x => x.Order).ToList())
        {
            // Chỉ resize khi lệch kích thước — tránh đụng NotifyScoresChanged hàng loạt
            if (NeedsMatrixResize(s, rounds))
                s.EnsureShotMatrix(rounds);
            s.Order = i++;
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

    public void EnsureSessionRoster(ShootingSession session, int personCount)
    {
        personCount = Math.Max(0, personCount);
        var preset = GetPresetForSession(session);
        var rounds = preset?.GetRoundCounts() ?? Array.Empty<int>();

        while (session.Shooters.Count < personCount)
        {
            session.Shooters.Add(new Shooter
            {
                Name = string.Empty,
                Rank = string.Empty,
                Position = string.Empty,
                Unit = string.Empty,
                Order = session.Shooters.Count + 1,
                IsSelected = false
            });
        }

        while (session.Shooters.Count > personCount)
            session.Shooters.RemoveAt(session.Shooters.Count - 1);

        EnsureSessionMatrices(session);
    }

    public Shooter CreateEmptyShooterForSession(ShootingSession session, int order)
    {
        var preset = GetPresetForSession(session);
        var shooter = new Shooter
        {
            Name = string.Empty,
            Rank = string.Empty,
            Position = string.Empty,
            Unit = string.Empty,
            Order = order,
            IsSelected = false
        };
        shooter.EnsureShotMatrix(preset?.GetRoundCounts() ?? Array.Empty<int>());
        return shooter;
    }

    public ShootingSession CreateSession(string name, Group group, int personCount)
    {
        var session = new ShootingSession
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Đợt {DateTime.Now:dd/MM HH:mm}" : name.Trim(),
            GroupId = group.Id,
            CreatedAt = DateTime.Now
        };
        Sessions.Add(session);
        EnsureSessionRoster(session, Math.Max(1, personCount));
        SelectedSession = session;
        Persist();
        return session;
    }

    public async Task<ShootingSession> CreateSessionAsync(string name, Group group, int personCount)
    {
        var session = new ShootingSession
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Đợt {DateTime.Now:dd/MM HH:mm}" : name.Trim(),
            GroupId = group.Id,
            CreatedAt = DateTime.Now
        };
        Sessions.Add(session);
        SelectedSession = session;

        await RunBusyAsync("Đang tạo đợt và danh sách...", async () =>
        {
            EnsureSessionRoster(session, Math.Max(1, personCount));
            BusyMessage = "Đang lưu vào CSDL...";
            var state = ToState();
            await Task.Run(() => _store.Save(state)).ConfigureAwait(true);
        });

        StatusMessage = $"Đã lưu {DateTime.Now:HH:mm:ss}";
        return session;
    }

    public bool RemoveSession(ShootingSession session)
    {
        if (!Sessions.Contains(session)) return false;
        Sessions.Remove(session);
        if (SelectedSession?.Id == session.Id)
            SelectedSession = Sessions.FirstOrDefault();
        Persist();
        return true;
    }

    /// <summary>Có điểm đã nhập trong các đợt dùng preset của nhóm này không.</summary>
    public bool AnyEnteredScoresForPreset(Guid presetId)
    {
        var groupIds = Groups.Where(g => g.PresetId == presetId).Select(g => g.Id).ToHashSet();
        return Sessions
            .Where(s => groupIds.Contains(s.GroupId))
            .SelectMany(s => s.Shooters)
            .Any(s => s.EnteredShotCount > 0);
    }

    public void ResizeMatricesForPreset(Guid presetId)
    {
        var groupIds = Groups.Where(g => g.PresetId == presetId).Select(g => g.Id).ToHashSet();
        foreach (var session in Sessions.Where(s => groupIds.Contains(s.GroupId)))
            EnsureSessionMatrices(session);
    }

    public void ExportBackup(string path)
    {
        _backup.Export(ToState(), path);
        StatusMessage = $"Đã sao lưu: {path}";
    }

    public async Task ExportBackupAsync(string path)
    {
        FlushDeferredPersist(save: false);
        var state = ToState();
        await RunBusyAsync("Đang sao lưu dữ liệu...", () => _backup.Export(state, path));
        StatusMessage = $"Đã sao lưu: {path}";
    }

    public void ImportBackup(string path)
    {
        var state = _backup.Import(path);
        ApplyState(state, persist: true);
        StatusMessage = $"Đã phục hồi từ: {path}";
    }

    public async Task ImportBackupAsync(string path)
    {
        AppState? state = null;
        await RunBusyAsync("Đang phục hồi dữ liệu...", async () =>
        {
            state = await Task.Run(() => _backup.Import(path)).ConfigureAwait(true);
            BusyMessage = "Đang áp dụng dữ liệu...";
            ApplyState(state, persist: false);
            BusyMessage = "Đang ghi vào CSDL...";
            var snap = ToState();
            await Task.Run(() => _store.Save(snap)).ConfigureAwait(true);
        });
        StatusMessage = $"Đã phục hồi từ: {path}";
    }

    public SessionTransferFile LoadSessionTransfer(string path) =>
        _sessionTransfer.Import(path);

    public IReadOnlyList<Group> FindGroupsMatchingPreset(ScorePreset importedPreset) =>
        Groups.Where(g =>
        {
            var p = GetPreset(g.PresetId);
            return p is not null && p.SameConfigurationAs(importedPreset);
        }).ToList();

    public bool CanAppendSessionTransfer(SessionTransferFile pack, ShootingSession? target)
    {
        var preset = GetPresetForSession(target);
        return preset is not null && preset.SameConfigurationAs(pack.Preset);
    }

    public async Task ExportSessionAsync(ShootingSession session, string path)
    {
        var preset = GetPresetForSession(session)
            ?? throw new InvalidOperationException("Đợt chưa gắn cấu hình bia hợp lệ.");
        if (preset.TargetCount == 0)
            throw new InvalidOperationException("Đợt chưa gắn cấu hình bia hợp lệ.");

        var group = GetGroupForSession(session);
        FlushDeferredPersist(save: false);
        var pack = _sessionTransfer.Build(session, preset, group);
        await RunBusyAsync("Đang xuất đợt bắn...", () => _sessionTransfer.Export(pack, path));
        StatusMessage = $"Đã xuất đợt \"{session.Name}\": {path}";
    }

    public async Task<ShootingSession> ImportSessionCreateAsync(SessionTransferFile pack, Group targetGroup)
    {
        var localPreset = GetPresetForGroup(targetGroup)
            ?? throw new InvalidOperationException("Nhóm đích chưa có cấu hình bia.");
        if (!localPreset.SameConfigurationAs(pack.Preset))
            throw new InvalidOperationException(
                "Cấu hình bia của nhóm đích không khớp với file. Chỉ nhập khi preset cùng cấu hình.");

        var session = new ShootingSession
        {
            Id = Guid.NewGuid(),
            Name = pack.Session.Name.Trim(),
            GroupId = targetGroup.Id,
            CreatedAt = DateTime.Now
        };

        await RunBusyAsync("Đang nhập đợt bắn...", async () =>
        {
            foreach (var shooter in RemintShooters(pack.Session.Shooters, localPreset, startOrder: 1))
                session.Shooters.Add(shooter);

            Sessions.Add(session);
            SelectedSession = session;
            SelectedGroup = targetGroup;
            BusyMessage = "Đang lưu vào CSDL...";
            var state = ToState();
            await Task.Run(() => _store.Save(state)).ConfigureAwait(true);
        });

        StatusMessage =
            $"Đã nhập đợt \"{session.Name}\" ({session.Shooters.Count} người) vào nhóm \"{targetGroup.Name}\"";
        return session;
    }

    public async Task ImportSessionAppendAsync(SessionTransferFile pack, ShootingSession target)
    {
        var localPreset = GetPresetForSession(target)
            ?? throw new InvalidOperationException("Đợt đích chưa gắn cấu hình bia.");
        if (!localPreset.SameConfigurationAs(pack.Preset))
            throw new InvalidOperationException(
                "Cấu hình bia của đợt hiện tại không khớp với file. Chỉ nối khi preset cùng cấu hình.");

        var added = 0;
        await RunBusyAsync("Đang nối đợt bắn...", async () =>
        {
            // Giữ nguyên thứ tự hiện tại rồi nối người từ file vào cuối.
            var existingOrder = 1;
            foreach (var existing in target.Shooters)
                existing.Order = existingOrder++;

            var startOrder = target.Shooters.Count + 1;
            foreach (var shooter in RemintShooters(pack.Session.Shooters, localPreset, startOrder))
            {
                target.Shooters.Add(shooter);
                added++;
            }

            SelectedSession = target;
            BusyMessage = "Đang lưu vào CSDL...";
            var state = ToState();
            await Task.Run(() => _store.Save(state)).ConfigureAwait(true);
        });

        StatusMessage =
            $"Đã nối {added} người vào đợt \"{target.Name}\" (tổng {target.Shooters.Count})";
    }

    private static IEnumerable<Shooter> RemintShooters(
        IEnumerable<Shooter> source,
        ScorePreset preset,
        int startOrder)
    {
        var rounds = preset.GetRoundCounts();
        var order = startOrder;
        foreach (var s in source)
        {
            var clone = new Shooter
            {
                Id = Guid.NewGuid(),
                Name = s.Name,
                Rank = s.Rank,
                Position = s.Position,
                Unit = s.Unit,
                Order = order++,
                IsSelected = false,
                Shots = s.Shots.Select(row => row.ToList()).ToList()
            };
            clone.EnsureShotMatrix(rounds);
            yield return clone;
        }
    }

    public ScorePreset CreateDefaultPreset(string name)
    {
        var preset = new ScorePreset { Name = name };
        preset.EnsureDefaultTargets(2, 5);
        // Phân loại để trống — người dùng tự thêm từng hạng
        return preset;
    }
}
