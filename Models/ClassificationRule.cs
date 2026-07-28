using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tonghopbansung.Models;

public enum ClassificationConditionKind
{
    /// <summary>Tổng điểm cả đợt (≥ MinValue).</summary>
    TotalScore = 0,
    /// <summary>Bia chấm điểm (≥ MinValue).</summary>
    TargetScore = 1,
    /// <summary>Bia đổ: MinValue 1 = phải Đổ, 0 = phải Không đổ.</summary>
    TargetKnockDown = 2
}

/// <summary>Một điều kiện trong hạng phân loại (AND với các điều kiện khác).</summary>
public partial class ClassificationCondition : ObservableObject
{
    [ObservableProperty]
    private ClassificationConditionKind _kind = ClassificationConditionKind.TotalScore;

    /// <summary>Chỉ số bia phẳng khi Kind là Target*; -1 nếu tổng điểm.</summary>
    [ObservableProperty]
    private int _targetIndex = -1;

    /// <summary>
    /// Tổng / bia điểm: ngưỡng tối thiểu (≥).
    /// Bia đổ: 1 = Đổ, 0 = Không đổ.
    /// </summary>
    [ObservableProperty]
    private int _minValue;

    [JsonIgnore]
    public bool IsTotal => Kind == ClassificationConditionKind.TotalScore || TargetIndex < 0;

    [JsonIgnore]
    public bool IsKnockDown => Kind == ClassificationConditionKind.TargetKnockDown;

    public ClassificationCondition Clone() => new()
    {
        Kind = Kind,
        TargetIndex = TargetIndex,
        MinValue = MinValue
    };
}

public partial class ClassificationRule : ObservableObject
{
    [ObservableProperty]
    private string _label = string.Empty;

    /// <summary>Điểm tổng tối thiểu (tương thích dữ liệu cũ; đồng bộ với điều kiện Tổng nếu có).</summary>
    [ObservableProperty]
    private int _minScore;

    /// <summary>Ưu tiên xét hạng (cao hơn xét trước).</summary>
    [ObservableProperty]
    private int _priority;

    public ObservableCollection<ClassificationCondition> Conditions { get; set; } = new();

    [JsonIgnore]
    public int EffectivePriority =>
        Priority != 0
            ? Priority
            : Conditions.Count > 0
                ? Conditions.Where(c => !c.IsKnockDown).Select(c => c.MinValue).DefaultIfEmpty(MinScore).Max()
                : MinScore;

    [JsonIgnore]
    public string ConditionSummary
    {
        get
        {
            EnsureLegacyCondition();
            if (Conditions.Count == 0)
                return $"Tổng ≥ {MinScore}";

            return string.Join("  và  ", Conditions.Select(c => DescribeCondition(c)));
        }
    }

    public static string DescribeCondition(ClassificationCondition c, IReadOnlyList<TargetDefinition>? targets = null)
    {
        if (c.IsTotal)
            return $"Tổng ≥ {c.MinValue}";

        var name = targets is not null && c.TargetIndex >= 0 && c.TargetIndex < targets.Count
            ? targets[c.TargetIndex].Name
            : $"Bia {c.TargetIndex + 1}";

        var isKnockDown = c.IsKnockDown
            || (targets is not null
                && c.TargetIndex >= 0
                && c.TargetIndex < targets.Count
                && targets[c.TargetIndex].Kind == TargetKind.KnockDown);

        if (isKnockDown)
            return c.MinValue >= 1 ? $"{name} = Đổ" : $"{name} = Không đổ";

        return $"{name} ≥ {c.MinValue}";
    }

    public string GetConditionSummary(IReadOnlyList<TargetDefinition>? targets)
    {
        EnsureLegacyCondition();
        if (Conditions.Count == 0)
            return $"Tổng ≥ {MinScore}";
        return string.Join("  và  ", Conditions.Select(c => DescribeCondition(c, targets)));
    }

    /// <summary>Dữ liệu cũ chỉ có MinScore → tạo 1 điều kiện Tổng.</summary>
    public void EnsureLegacyCondition()
    {
        if (Conditions.Count > 0) return;
        Conditions.Add(new ClassificationCondition
        {
            Kind = ClassificationConditionKind.TotalScore,
            TargetIndex = -1,
            MinValue = MinScore
        });
        if (Priority == 0)
            Priority = MinScore;
    }

    public void SyncMinScoreFromConditions()
    {
        var total = Conditions.FirstOrDefault(c => c.IsTotal);
        if (total is not null)
            MinScore = total.MinValue;
        else if (Conditions.Any(c => !c.IsKnockDown))
            MinScore = Conditions.Where(c => !c.IsKnockDown).Min(c => c.MinValue);
    }

    public ClassificationRule Clone()
    {
        var clone = new ClassificationRule
        {
            Label = Label,
            MinScore = MinScore,
            Priority = Priority
        };
        foreach (var c in Conditions)
            clone.Conditions.Add(c.Clone());
        return clone;
    }

    public void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(ConditionSummary));
        OnPropertyChanged(nameof(EffectivePriority));
    }
}
