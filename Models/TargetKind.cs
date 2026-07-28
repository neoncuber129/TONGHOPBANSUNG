namespace Tonghopbansung.Models;

/// <summary>Loại bia: chấm điểm 0–10 hoặc bia đổ / không đổ.</summary>
public enum TargetKind
{
    /// <summary>Bia chấm điểm (10 → 0).</summary>
    Scored = 0,
    /// <summary>Bia đổ / không đổ (2 trạng thái).</summary>
    KnockDown = 1
}
