using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tonghopbansung.Models;

public partial class ScorePreset : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = "Preset mới";

    /// <summary>Cấu trúc cha–con: phần → từng bia (số đạn).</summary>
    public ObservableCollection<TargetCluster> Clusters { get; set; } = new();

    public ObservableCollection<ClassificationRule> ClassificationRules { get; set; } = new();

    private List<TargetDefinition>? _flatCache;
    private int[]? _roundCache;

    /// <summary>Danh sách bia phẳng (thứ tự bắn / chỉ số điểm).</summary>
    [JsonIgnore]
    public IReadOnlyList<TargetDefinition> FlatTargets
    {
        get
        {
            _flatCache ??= Clusters.SelectMany(c => c.Targets).ToList();
            return _flatCache;
        }
    }

    [JsonIgnore]
    public int TargetCount => FlatTargets.Count;

    [JsonIgnore]
    public int TotalRounds => FlatTargets.Sum(t => t.EffectiveRoundCount);

    public IReadOnlyList<int> GetRoundCounts()
    {
        if (_roundCache is not null) return _roundCache;
        _roundCache = FlatTargets.Select(t => t.EffectiveRoundCount).ToArray();
        return _roundCache;
    }

    public void InvalidateLayoutCache()
    {
        _flatCache = null;
        _roundCache = null;
    }

    public bool SameLayoutAs(ScorePreset? other)
    {
        if (other is null) return false;
        var a = FlatTargets.ToList();
        var b = other.FlatTargets.ToList();
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].EffectiveRoundCount != b[i].EffectiveRoundCount) return false;
            if (a[i].Kind != b[i].Kind) return false;
        }
        return true;
    }

    public void EnsureDefaultClusters(int targetCount = 2, int rounds = 5)
    {
        if (Clusters.Count > 0 && TargetCount > 0) return;
        Clusters.Clear();
        var cluster = new TargetCluster { Name = "Phần 1" };
        for (var i = 0; i < Math.Max(1, targetCount); i++)
        {
            cluster.Targets.Add(new TargetDefinition
            {
                Name = $"Bia {i + 1}",
                RoundCount = rounds,
                Kind = TargetKind.Scored
            });
        }
        Clusters.Add(cluster);
        InvalidateLayoutCache();
    }

    /// <summary>Đặt tổng số bia: giữ điểm/cấu hình cũ khi có thể, bổ sung hoặc cắt thừa.</summary>
    public void SetTargetCount(int count, int defaultRounds = 5)
    {
        count = Math.Max(1, count);
        var flat = FlatTargets.ToList();

        if (flat.Count == count) return;

        if (flat.Count < count)
        {
            // Thêm vào cluster cuối (hoặc tạo mới)
            var cluster = Clusters.LastOrDefault();
            if (cluster is null)
            {
                cluster = new TargetCluster { Name = "Phần 1" };
                Clusters.Add(cluster);
            }

            while (FlatTargets.Count < count)
            {
                cluster.Targets.Add(new TargetDefinition
                {
                    Name = $"Bia {FlatTargets.Count + 1}",
                    RoundCount = defaultRounds,
                    Kind = TargetKind.Scored
                });
            }
        }
        else
        {
            var toRemove = flat.Count - count;
            for (var i = 0; i < toRemove; i++)
            {
                // Xóa từ cuối
                for (var c = Clusters.Count - 1; c >= 0; c--)
                {
                    if (Clusters[c].Targets.Count == 0) continue;
                    Clusters[c].Targets.RemoveAt(Clusters[c].Targets.Count - 1);
                    break;
                }
            }

            // Dọn cluster rỗng (giữ ít nhất 1)
            while (Clusters.Count > 1 && Clusters.Any(c => c.Targets.Count == 0))
            {
                var empty = Clusters.LastOrDefault(c => c.Targets.Count == 0);
                if (empty is null) break;
                Clusters.Remove(empty);
            }
        }

        RenumberDefaultTargetNames();
        InvalidateLayoutCache();
    }

    public void RenumberDefaultTargetNames()
    {
        var index = 1;
        foreach (var target in FlatTargets)
        {
            if (string.IsNullOrWhiteSpace(target.Name) ||
                System.Text.RegularExpressions.Regex.IsMatch(target.Name, @"^Bia\s+\d+$"))
            {
                target.Name = $"Bia {index}";
            }
            index++;
        }
    }

    // Tương thích API cũ
    public void EnsureDefaultTargets(int count = 2, int rounds = 5) =>
        EnsureDefaultClusters(count, rounds);

    public void SyncTargetNames() => RenumberDefaultTargetNames();

    /// <summary>Sao chép sâu cấu hình bia (Id mới) — dùng khi tách preset dùng chung thành 1:1 theo nhóm.</summary>
    public ScorePreset CloneDeep(string? name = null)
    {
        var clone = new ScorePreset
        {
            Id = Guid.NewGuid(),
            Name = name ?? Name
        };

        foreach (var cluster in Clusters)
        {
            var c = new TargetCluster { Name = cluster.Name };
            foreach (var t in cluster.Targets)
            {
                c.Targets.Add(new TargetDefinition
                {
                    Name = t.Name,
                    RoundCount = t.RoundCount,
                    Kind = t.Kind,
                    MissPenalty = t.MissPenalty,
                    HitBonus = t.HitBonus
                });
            }
            clone.Clusters.Add(c);
        }

        foreach (var rule in ClassificationRules)
            clone.ClassificationRules.Add(rule.Clone());

        return clone;
    }
}
