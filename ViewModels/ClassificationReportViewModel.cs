using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Tonghopbansung.Models;
using Tonghopbansung.Services;

namespace Tonghopbansung.ViewModels;

public sealed class ClassificationReportRow
{
    public string Label { get; init; } = string.Empty;
    public int Count { get; init; }
    public string PercentText { get; init; } = "0%";
}

public partial class ClassificationReportViewModel : ObservableObject
{
    public ObservableCollection<ClassificationReportRow> Rows { get; } = new();

    [ObservableProperty]
    private string _title = "Báo cáo phân loại";

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _totalPercentText = "100%";

    public static ClassificationReportViewModel? Build(AppSession session, ShootingSession? shootingSession)
    {
        if (shootingSession is null) return null;
        var group = session.GetGroupForSession(shootingSession);
        var preset = session.GetPresetForSession(shootingSession);
        if (preset is null) return null;

        var named = shootingSession.Shooters
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .ToList();
        var selected = named.Where(s => s.IsSelected).ToList();
        var source = selected.Count > 0 ? selected : named;

        var rounds = preset.GetRoundCounts();
        var counts = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);

        foreach (var shooter in source)
        {
            shooter.EnsureShotMatrix(rounds);
            var grade = ScoreCalculator.Classify(shooter, preset);
            counts[grade] = counts.TryGetValue(grade, out var c) ? c + 1 : 1;
        }

        var total = source.Count;
        var vm = new ClassificationReportViewModel
        {
            Title = "Báo cáo phân loại",
            Subtitle = selected.Count > 0
                ? $"Đợt: {shootingSession.Name} · Nhóm: {group?.Name ?? preset.Name} · {total} người đã chọn"
                : $"Đợt: {shootingSession.Name} · Nhóm: {group?.Name ?? preset.Name} · {total} người có tên",
            TotalCount = total,
            TotalPercentText = total > 0 ? "100%" : "0%"
        };

        foreach (var rule in preset.ClassificationRules.OrderByDescending(r => r.EffectivePriority))
        {
            rule.EnsureLegacyCondition();
            var label = string.IsNullOrWhiteSpace(rule.Label) ? "—" : rule.Label;
            var count = counts.TryGetValue(label, out var n) ? n : 0;
            vm.Rows.Add(new ClassificationReportRow
            {
                Label = label,
                Count = count,
                PercentText = total > 0 ? $"{count * 100.0 / total:0.#}%" : "0%"
            });
            counts.Remove(label);
        }

        foreach (var kv in counts.OrderByDescending(x => x.Value))
        {
            vm.Rows.Add(new ClassificationReportRow
            {
                Label = kv.Key,
                Count = kv.Value,
                PercentText = total > 0 ? $"{kv.Value * 100.0 / total:0.#}%" : "0%"
            });
        }

        if (vm.Rows.Count == 0)
        {
            vm.Rows.Add(new ClassificationReportRow
            {
                Label = "—",
                Count = total,
                PercentText = total > 0 ? "100%" : "0%"
            });
        }

        return vm;
    }
}
