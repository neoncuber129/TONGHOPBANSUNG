using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tonghopbansung.Models;

public partial class TargetDefinition : ObservableObject
{
    [ObservableProperty]
    private string _name = "Bia";

    /// <summary>Số đạn tối đa — chỉ dùng khi Kind = Scored. Bia đổ luôn = 1 trạng thái.</summary>
    [ObservableProperty]
    private int _roundCount = 5;

    [ObservableProperty]
    private TargetKind _kind = TargetKind.Scored;

    /// <summary>
    /// Điểm trừ khi bia đổ có kết quả «Không». Chỉ dùng với Kind = KnockDown. 0 = không trừ.
    /// </summary>
    [ObservableProperty]
    private int _missPenalty;

    /// <summary>
    /// Điểm cộng khi bia đổ có kết quả «Đổ». Chỉ dùng với Kind = KnockDown. 0 = không cộng.
    /// </summary>
    [ObservableProperty]
    private int _hitBonus;

    [JsonIgnore]
    public bool IsKnockDown => Kind == TargetKind.KnockDown;

    [JsonIgnore]
    public bool ShowRoundCount => Kind == TargetKind.Scored;

    [JsonIgnore]
    public bool ShowKnockDownScores => Kind == TargetKind.KnockDown;

    [JsonIgnore]
    public string KindLabel => Kind == TargetKind.KnockDown ? "Đổ / Không đổ" : "Chấm điểm";

    [JsonIgnore]
    public string KindToggleLabel => Kind == TargetKind.KnockDown ? "→ Chấm điểm" : "→ Bia đổ";

    /// <summary>Số slot kết quả thực tế dùng cho ma trận điểm.</summary>
    [JsonIgnore]
    public int EffectiveRoundCount => Kind == TargetKind.KnockDown ? 1 : Math.Max(1, RoundCount);

    partial void OnKindChanged(TargetKind value)
    {
        if (value == TargetKind.KnockDown)
            RoundCount = 1;
        else
        {
            MissPenalty = 0;
            HitBonus = 0;
        }

        OnPropertyChanged(nameof(IsKnockDown));
        OnPropertyChanged(nameof(ShowRoundCount));
        OnPropertyChanged(nameof(ShowKnockDownScores));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(KindToggleLabel));
        OnPropertyChanged(nameof(EffectiveRoundCount));
    }

    public void ToggleKind()
    {
        if (Kind == TargetKind.KnockDown)
        {
            Kind = TargetKind.Scored;
            if (RoundCount <= 1)
                RoundCount = 5;
        }
        else
        {
            Kind = TargetKind.KnockDown;
            RoundCount = 1;
        }
    }
}
