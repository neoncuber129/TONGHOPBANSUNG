using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tonghopbansung.Models;

public partial class Shooter : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _rank = string.Empty;

    [ObservableProperty]
    private string _position = string.Empty;

    [ObservableProperty]
    private string _unit = string.Empty;

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private bool _isSelected = false;

    /// <summary>Shots[targetIndex][roundIndex]; null = chưa nhập.</summary>
    public List<List<int?>> Shots { get; set; } = new();

    [JsonIgnore]
    /// <summary>Tổng thô mọi bia (kể cả đổ). Hiển thị/xếp loại dùng <see cref="Services.ScoreCalculator.TotalScore"/>.</summary>
    public int TotalScore => Shots.SelectMany(t => t).Where(s => s.HasValue).Sum(s => s!.Value);

    [JsonIgnore]
    public int EnteredShotCount => Shots.SelectMany(t => t).Count(s => s.HasValue);

    public void EnsureShotMatrix(IReadOnlyList<int> roundsPerTarget)
    {
        var targetCount = roundsPerTarget.Count;

        while (Shots.Count < targetCount)
            Shots.Add([]);

        if (Shots.Count > targetCount)
            Shots.RemoveRange(targetCount, Shots.Count - targetCount);

        for (var i = 0; i < targetCount; i++)
        {
            var rounds = Math.Max(0, roundsPerTarget[i]);
            var target = Shots[i];
            while (target.Count < rounds)
                target.Add(null);
            if (target.Count > rounds)
                target.RemoveRange(rounds, target.Count - rounds);
        }

        NotifyScoresChanged();
    }

    public void ResetShots(IReadOnlyList<int> roundsPerTarget)
    {
        Shots = roundsPerTarget
            .Select(r => Enumerable.Repeat<int?>(null, Math.Max(0, r)).ToList())
            .ToList();
        NotifyScoresChanged();
    }

    public void NotifyScoresChanged()
    {
        OnPropertyChanged(nameof(TotalScore));
        OnPropertyChanged(nameof(EnteredShotCount));
        OnPropertyChanged(nameof(Shots));
    }

    public int TargetEnteredCount(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= Shots.Count)
            return 0;
        return Shots[targetIndex].Count(s => s.HasValue);
    }

    public int TargetTotal(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= Shots.Count)
            return 0;
        return Shots[targetIndex].Where(s => s.HasValue).Sum(s => s!.Value);
    }

    /// <summary>Hiển thị kết quả bia: chỉ các giá trị đã nhập; trống nếu chưa nhập.</summary>
    public string FormatTargetScores(int targetIndex, TargetKind kind = TargetKind.Scored)
    {
        if (targetIndex < 0 || targetIndex >= Shots.Count)
            return string.Empty;

        if (kind == TargetKind.KnockDown)
        {
            var v = Shots[targetIndex].FirstOrDefault();
            return v switch
            {
                1 => "Đổ",
                0 => "Không",
                _ => string.Empty
            };
        }

        var entered = Shots[targetIndex]
            .Where(s => s.HasValue)
            .Select(s => s!.Value.ToString())
            .ToList();
        return entered.Count == 0 ? string.Empty : string.Join(",", entered);
    }
}
