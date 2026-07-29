namespace Tonghopbansung.Models;

/// <summary>File xuất/nhập một đợt bắn (.thbss) — dùng chung web ↔ PC.</summary>
public sealed class SessionTransferFile
{
    public const string FormatId = "tonghopbansung.session";
    public const int CurrentVersion = 1;
    public const string FileExtension = ".thbss";

    public string Format { get; set; } = FormatId;
    public int Version { get; set; } = CurrentVersion;
    public string ExportedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public ScorePreset Preset { get; set; } = new();
    public SessionTransferSession Session { get; set; } = new();
    public SessionTransferSourceGroup? SourceGroup { get; set; }
}

public sealed class SessionTransferSession
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public List<Shooter> Shooters { get; set; } = new();
}

public sealed class SessionTransferSourceGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
