using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tonghopbansung.Models;

namespace Tonghopbansung.ViewModels;

public partial class ConditionTargetChoice : ObservableObject
{
    public int TargetIndex { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public ClassificationConditionKind Kind { get; init; }
    public TargetKind TargetKind { get; init; } = TargetKind.Scored;

    public bool IsKnockDown => Kind == ClassificationConditionKind.TargetKnockDown
                               || TargetKind == TargetKind.KnockDown;

    public override string ToString() => DisplayName;
}

public partial class KnockDownOption : ObservableObject
{
    public int Value { get; init; }
    public string Label { get; init; } = string.Empty;
}

public partial class ConditionRowViewModel : ObservableObject
{
    public ObservableCollection<ConditionTargetChoice> Choices { get; }
    public ObservableCollection<KnockDownOption> KnockDownOptions { get; } =
    [
        new KnockDownOption { Value = 1, Label = "Đổ" },
        new KnockDownOption { Value = 0, Label = "Không đổ" }
    ];

    [ObservableProperty]
    private ConditionTargetChoice? _selectedChoice;

    [ObservableProperty]
    private int _minValue;

    [ObservableProperty]
    private KnockDownOption? _selectedKnockDown;

    [ObservableProperty]
    private bool _isKnockDownCondition;

    [ObservableProperty]
    private bool _isScoreCondition = true;

    public ConditionRowViewModel(IEnumerable<ConditionTargetChoice> choices, ClassificationCondition? source = null)
    {
        Choices = new ObservableCollection<ConditionTargetChoice>(choices);
        if (source is null)
        {
            SelectedChoice = Choices.FirstOrDefault();
            MinValue = 0;
            SelectedKnockDown = KnockDownOptions[0];
            UpdateModeFromChoice();
            return;
        }

        MinValue = source.MinValue;
        SelectedKnockDown = KnockDownOptions.FirstOrDefault(o => o.Value == (source.MinValue >= 1 ? 1 : 0))
                            ?? KnockDownOptions[0];

        SelectedChoice = source.IsTotal
            ? Choices.FirstOrDefault(c => c.Kind == ClassificationConditionKind.TotalScore)
            : Choices.FirstOrDefault(c => c.TargetIndex == source.TargetIndex)
              ?? Choices.FirstOrDefault();

        UpdateModeFromChoice();
        if (IsKnockDownCondition)
            SelectedKnockDown = KnockDownOptions.FirstOrDefault(o => o.Value == (source.MinValue >= 1 ? 1 : 0))
                                ?? KnockDownOptions[0];
    }

    partial void OnSelectedChoiceChanged(ConditionTargetChoice? value) => UpdateModeFromChoice();

    private void UpdateModeFromChoice()
    {
        IsKnockDownCondition = SelectedChoice?.IsKnockDown == true;
        IsScoreCondition = !IsKnockDownCondition;
        if (IsKnockDownCondition && SelectedKnockDown is null)
            SelectedKnockDown = KnockDownOptions[0];
    }

    public ClassificationCondition ToCondition()
    {
        var choice = SelectedChoice ?? Choices.First();
        if (choice.Kind == ClassificationConditionKind.TotalScore)
        {
            return new ClassificationCondition
            {
                Kind = ClassificationConditionKind.TotalScore,
                TargetIndex = -1,
                MinValue = MinValue
            };
        }

        if (choice.IsKnockDown)
        {
            return new ClassificationCondition
            {
                Kind = ClassificationConditionKind.TargetKnockDown,
                TargetIndex = choice.TargetIndex,
                MinValue = SelectedKnockDown?.Value ?? 1
            };
        }

        return new ClassificationCondition
        {
            Kind = ClassificationConditionKind.TargetScore,
            TargetIndex = choice.TargetIndex,
            MinValue = MinValue
        };
    }
}

public partial class ClassificationRuleEditorViewModel : ObservableObject
{
    public ClassificationRule Rule { get; }

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private int _priority;

    public ObservableCollection<ConditionRowViewModel> ConditionRows { get; } = new();
    public ObservableCollection<ConditionTargetChoice> TargetChoices { get; } = new();

    public ClassificationRuleEditorViewModel(ScorePreset preset, ClassificationRule rule)
    {
        Rule = rule;
        rule.EnsureLegacyCondition();

        Label = rule.Label;
        Priority = rule.Priority != 0 ? rule.Priority : rule.EffectivePriority;

        TargetChoices.Add(new ConditionTargetChoice
        {
            TargetIndex = -1,
            DisplayName = "Tổng điểm",
            Kind = ClassificationConditionKind.TotalScore
        });

        var flat = preset.FlatTargets;
        for (var i = 0; i < flat.Count; i++)
        {
            var t = flat[i];
            var isKd = t.Kind == TargetKind.KnockDown;
            TargetChoices.Add(new ConditionTargetChoice
            {
                TargetIndex = i,
                DisplayName = isKd ? $"{t.Name} (đổ)" : $"{t.Name} (điểm)",
                Kind = isKd ? ClassificationConditionKind.TargetKnockDown : ClassificationConditionKind.TargetScore,
                TargetKind = t.Kind
            });
        }

        // Chuẩn hóa điều kiện cũ: bia đổ nhưng Kind còn TargetScore
        foreach (var c in rule.Conditions)
        {
            if (!c.IsTotal && c.TargetIndex >= 0 && c.TargetIndex < flat.Count
                && flat[c.TargetIndex].Kind == TargetKind.KnockDown)
            {
                c.Kind = ClassificationConditionKind.TargetKnockDown;
                c.MinValue = c.MinValue >= 1 ? 1 : 0;
            }
        }

        foreach (var c in rule.Conditions)
            ConditionRows.Add(new ConditionRowViewModel(TargetChoices, c));

        if (ConditionRows.Count == 0)
            AddCondition();
    }

    [RelayCommand]
    private void AddCondition()
    {
        ConditionRows.Add(new ConditionRowViewModel(TargetChoices));
    }

    [RelayCommand]
    private void RemoveCondition(ConditionRowViewModel? row)
    {
        if (row is null) return;
        if (ConditionRows.Count <= 1) return;
        ConditionRows.Remove(row);
    }

    public bool Apply(out string error)
    {
        if (string.IsNullOrWhiteSpace(Label))
        {
            error = "Hãy nhập tên hạng phân loại.";
            return false;
        }

        if (ConditionRows.Count == 0)
        {
            error = "Cần ít nhất một điều kiện.";
            return false;
        }

        Rule.Label = Label.Trim();
        Rule.Priority = Priority;
        Rule.Conditions.Clear();
        foreach (var row in ConditionRows)
            Rule.Conditions.Add(row.ToCondition());

        Rule.SyncMinScoreFromConditions();
        if (Rule.Priority == 0)
            Rule.Priority = Rule.EffectivePriority;
        Rule.NotifySummaryChanged();

        error = string.Empty;
        return true;
    }
}
