using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tonghopbansung.Models;

namespace Tonghopbansung.Services;

public sealed class JsonDataStore : IDataStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string DataDirectory { get; }
    public string DataFilePath { get; }

    public JsonDataStore()
    {
        // Lưu cạnh file .exe (thư mục publish / chạy app)
        DataDirectory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        DataFilePath = Path.Combine(DataDirectory, "data.json");
    }

    public AppState Load()
    {
        try
        {
            if (!File.Exists(DataFilePath))
                return AppState.CreateDefault();

            var json = File.ReadAllText(DataFilePath);
            var state = JsonSerializer.Deserialize<AppState>(json, Options);
            if (state is null || state.Presets.Count == 0)
                return AppState.CreateDefault();

            MigrateLegacyPresets(json, state);
            EnsureOnePresetPerGroup(state);
            EnsureShotMatrices(state);
            return state;
        }
        catch
        {
            return AppState.CreateDefault();
        }
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(state, Options);
        var temp = DataFilePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Copy(temp, DataFilePath, overwrite: true);
        File.Delete(temp);
    }

    private static void MigrateLegacyPresets(string json, AppState state)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("presets", out var presetsEl))
            return;

        var index = 0;
        foreach (var presetEl in presetsEl.EnumerateArray())
        {
            if (index >= state.Presets.Count) break;
            var preset = state.Presets[index++];

            if (preset.Clusters.Count > 0 && preset.TargetCount > 0)
                continue;

            // Legacy flat "targets" array
            if (presetEl.TryGetProperty("targets", out var targetsEl) &&
                targetsEl.ValueKind == JsonValueKind.Array &&
                targetsEl.GetArrayLength() > 0)
            {
                var cluster = new TargetCluster { Name = "Phần 1" };
                foreach (var tEl in targetsEl.EnumerateArray())
                {
                    var name = tEl.TryGetProperty("name", out var n) ? n.GetString() ?? "Bia" : "Bia";
                    var rounds = 5;
                    if (tEl.TryGetProperty("roundCount", out var rc) && rc.TryGetInt32(out var rcVal))
                        rounds = Math.Max(1, rcVal);
                    cluster.Targets.Add(new TargetDefinition { Name = name, RoundCount = rounds });
                }
                preset.Clusters.Clear();
                preset.Clusters.Add(cluster);
                continue;
            }

            var count = 2;
            var defaultRounds = 5;
            if (presetEl.TryGetProperty("targetCount", out var tc) && tc.TryGetInt32(out var tcVal))
                count = Math.Max(1, tcVal);
            if (presetEl.TryGetProperty("roundsPerTarget", out var rp) && rp.TryGetInt32(out var rpVal))
                defaultRounds = Math.Max(1, rpVal);

            preset.EnsureDefaultClusters(count, defaultRounds);
        }

        foreach (var preset in state.Presets.Where(p => p.TargetCount == 0))
            preset.EnsureDefaultClusters();
    }

    /// <summary>Mỗi nhóm một preset riêng; clone nếu nhiều nhóm đang dùng chung.</summary>
    private static void EnsureOnePresetPerGroup(AppState state)
    {
        var byId = state.Presets.ToDictionary(p => p.Id);

        foreach (var group in state.Groups)
        {
            if (!byId.TryGetValue(group.PresetId, out var shared))
            {
                var created = new ScorePreset { Name = group.Name };
                created.EnsureDefaultClusters(2, 5);
                state.Presets.Add(created);
                byId[created.Id] = created;
                group.PresetId = created.Id;
                continue;
            }

            var sharers = state.Groups.Where(g => g.PresetId == shared.Id).ToList();
            if (sharers.Count <= 1)
            {
                if (string.IsNullOrWhiteSpace(shared.Name) || shared.Name.StartsWith("Preset ", StringComparison.OrdinalIgnoreCase))
                    shared.Name = group.Name;
                continue;
            }

            // Nhóm đầu giữ preset gốc; các nhóm còn lại nhận bản sao
            foreach (var extra in sharers.Skip(1))
            {
                var clone = shared.CloneDeep(extra.Name);
                state.Presets.Add(clone);
                byId[clone.Id] = clone;
                extra.PresetId = clone.Id;
            }

            if (string.IsNullOrWhiteSpace(shared.Name) || shared.Name.StartsWith("Preset ", StringComparison.OrdinalIgnoreCase))
                shared.Name = sharers[0].Name;
        }

        var used = state.Groups.Select(g => g.PresetId).ToHashSet();
        state.Presets.RemoveAll(p => !used.Contains(p.Id));
    }

    private static void EnsureShotMatrices(AppState state)
    {
        var presets = state.Presets.ToDictionary(p => p.Id);
        var groups = state.Groups.ToDictionary(g => g.Id);

        foreach (var group in state.Groups)
        {
            if (!presets.TryGetValue(group.PresetId, out var preset))
                continue;
            var rounds = preset.GetRoundCounts();
            foreach (var shooter in group.Shooters)
                shooter.EnsureShotMatrix(rounds);
        }

        foreach (var session in state.Sessions)
        {
            if (!groups.TryGetValue(session.GroupId, out var group))
                continue;
            if (!presets.TryGetValue(group.PresetId, out var preset))
                continue;
            var rounds = preset.GetRoundCounts();
            foreach (var shooter in session.Shooters)
                shooter.EnsureShotMatrix(rounds);
        }
    }
}
