using Tonghopbansung.Models;

namespace Tonghopbansung.Services;

public static class ScoreCalculator
{
    /// <summary>Tổng điểm — cộng bia chấm điểm; bia đổ: cộng HitBonus nếu Đổ, trừ MissPenalty nếu Không.</summary>
    public static int TotalScore(Shooter shooter, ScorePreset preset)
    {
        var flat = preset.FlatTargets;
        var total = 0;
        for (var i = 0; i < flat.Count && i < shooter.Shots.Count; i++)
        {
            var def = flat[i];
            if (def.Kind == TargetKind.KnockDown)
            {
                var v = shooter.Shots[i].FirstOrDefault();
                if (v == 1)
                    total += Math.Max(0, def.HitBonus);
                else if (v == 0)
                    total -= Math.Max(0, def.MissPenalty);
                continue;
            }

            total += shooter.Shots[i].Where(s => s.HasValue).Sum(s => s!.Value);
        }

        return total;
    }

    /// <summary>Số bia đổ (đếm các bia loại đổ có kết quả Đổ = 1).</summary>
    public static int KnockDownCount(Shooter shooter, ScorePreset preset)
    {
        var flat = preset.FlatTargets;
        var count = 0;
        for (var i = 0; i < flat.Count && i < shooter.Shots.Count; i++)
        {
            if (flat[i].Kind != TargetKind.KnockDown)
                continue;
            if (shooter.TargetTotal(i) >= 1)
                count++;
        }
        return count;
    }

    public static bool MatchesRule(Shooter shooter, ClassificationRule rule, ScorePreset preset)
    {
        rule.EnsureLegacyCondition();
        var targets = preset.FlatTargets;

        foreach (var condition in rule.Conditions)
        {
            if (condition.IsTotal)
            {
                if (TotalScore(shooter, preset) < condition.MinValue)
                    return false;
                continue;
            }

            var targetIndex = condition.TargetIndex;
            if (targetIndex < 0 || targetIndex >= targets.Count)
                return false;

            var def = targets[targetIndex];
            var isKnockDown = condition.IsKnockDown || def.Kind == TargetKind.KnockDown;
            var value = shooter.TargetTotal(targetIndex);

            if (isKnockDown)
            {
                // Bia đổ: phải khớp đúng Đổ (1) hoặc Không đổ (0)
                var required = condition.MinValue >= 1 ? 1 : 0;
                if (value != required)
                    return false;
            }
            else
            {
                // Bia chấm điểm: điểm bia ≥ ngưỡng
                if (value < condition.MinValue)
                    return false;
            }
        }

        return rule.Conditions.Count > 0;
    }

    public static string Classify(Shooter shooter, ScorePreset preset)
    {
        var ordered = preset.ClassificationRules
            .OrderByDescending(r => r.EffectivePriority)
            .ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Chưa cấu hình phân loại → để trống
        if (ordered.Count == 0)
            return string.Empty;

        foreach (var rule in ordered)
        {
            if (MatchesRule(shooter, rule, preset))
                return string.IsNullOrWhiteSpace(rule.Label) ? string.Empty : rule.Label;
        }

        // Không khớp hạng nào → trống (không gán mặc định hạng cuối)
        return string.Empty;
    }
}
