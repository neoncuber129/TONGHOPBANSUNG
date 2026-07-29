using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tonghopbansung.Models;

namespace Tonghopbansung.Services;

public interface ISessionTransferService
{
    SessionTransferFile Build(ShootingSession session, ScorePreset preset, Group? group);
    void Export(SessionTransferFile pack, string filePath);
    SessionTransferFile Import(string filePath);
    SessionTransferFile Parse(string json);
}

public sealed class SessionTransferService : ISessionTransferService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SessionTransferFile Build(ShootingSession session, ScorePreset preset, Group? group)
    {
        var pack = new SessionTransferFile
        {
            Format = SessionTransferFile.FormatId,
            Version = SessionTransferFile.CurrentVersion,
            ExportedAt = DateTime.UtcNow.ToString("o"),
            Preset = ClonePreset(preset),
            Session = new SessionTransferSession
            {
                Id = session.Id,
                Name = session.Name,
                CreatedAt = session.CreatedAt.ToUniversalTime().ToString("o"),
                Shooters = session.Shooters.Select(CloneShooter).ToList()
            },
            SourceGroup = group is null
                ? null
                : new SessionTransferSourceGroup
                {
                    Id = group.Id,
                    Name = group.Name
                }
        };
        Validate(pack);
        return pack;
    }

    public void Export(SessionTransferFile pack, string filePath)
    {
        Validate(pack);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var json = JsonSerializer.Serialize(pack, Options);
        var temp = filePath + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public SessionTransferFile Import(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Không tìm thấy file đợt bắn.", filePath);
        return Parse(File.ReadAllText(filePath));
    }

    public SessionTransferFile Parse(string json)
    {
        SessionTransferFile? pack;
        try
        {
            pack = JsonSerializer.Deserialize<SessionTransferFile>(json, Options);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Không đọc được JSON file đợt bắn.", ex);
        }

        if (pack is null)
            throw new InvalidDataException("File đợt bắn không hợp lệ.");

        Validate(pack);
        return pack;
    }

    public static void Validate(SessionTransferFile pack)
    {
        if (!string.Equals(pack.Format, SessionTransferFile.FormatId, StringComparison.Ordinal))
            throw new InvalidDataException(
                "File không phải định dạng đợt bắn (.thbss). Dùng tab Sao lưu nếu đây là file .thbs.");

        if (pack.Version != SessionTransferFile.CurrentVersion)
            throw new InvalidDataException(
                $"Phiên bản file đợt bắn không hỗ trợ (version={pack.Version}).");

        if (pack.Preset is null)
            throw new InvalidDataException("File thiếu cấu hình bia (preset).");

        pack.Preset.InvalidateLayoutCache();
        if (pack.Preset.TargetCount == 0)
            throw new InvalidDataException("Preset trong file không có bia.");

        if (pack.Session is null)
            throw new InvalidDataException("File thiếu dữ liệu đợt bắn.");

        if (string.IsNullOrWhiteSpace(pack.Session.Name))
            throw new InvalidDataException("Tên đợt trong file không hợp lệ.");

        pack.Session.Shooters ??= new List<Shooter>();
        var rounds = pack.Preset.GetRoundCounts();
        for (var i = 0; i < pack.Session.Shooters.Count; i++)
        {
            var shooter = pack.Session.Shooters[i]
                ?? throw new InvalidDataException($"Xạ thủ #{i + 1} không hợp lệ.");
            if (shooter.Shots is null)
                throw new InvalidDataException($"Xạ thủ #{i + 1} thiếu ma trận điểm.");
            if (shooter.Shots.Count != rounds.Count)
                throw new InvalidDataException(
                    $"Xạ thủ #{i + 1}: số bia điểm ({shooter.Shots.Count}) không khớp preset ({rounds.Count}).");

            for (var ti = 0; ti < rounds.Count; ti++)
            {
                var row = shooter.Shots[ti];
                if (row is null)
                    throw new InvalidDataException(
                        $"Xạ thủ #{i + 1}: bia {ti + 1} không có dữ liệu điểm.");
                if (row.Count != rounds[ti])
                    throw new InvalidDataException(
                        $"Xạ thủ #{i + 1}: bia {ti + 1} có {row.Count} phát, cần {rounds[ti]}.");

                var target = pack.Preset.FlatTargets[ti];
                foreach (var score in row)
                {
                    if (!score.HasValue) continue;
                    var valid = target.Kind == TargetKind.KnockDown
                        ? score.Value is 0 or 1
                        : score.Value is >= 0 and <= 10;
                    if (!valid)
                        throw new InvalidDataException(
                            $"Xạ thủ #{i + 1}: điểm không hợp lệ tại bia {ti + 1}.");
                }
            }
        }
    }

    private static ScorePreset ClonePreset(ScorePreset preset)
    {
        var clone = new ScorePreset
        {
            Id = preset.Id,
            Name = preset.Name
        };
        foreach (var cluster in preset.Clusters)
        {
            var c = new TargetCluster
            {
                Id = cluster.Id,
                Name = cluster.Name
            };
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
        foreach (var rule in preset.ClassificationRules)
            clone.ClassificationRules.Add(rule.Clone());
        return clone;
    }

    private static Shooter CloneShooter(Shooter s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Rank = s.Rank,
        Position = s.Position,
        Unit = s.Unit,
        Order = s.Order,
        IsSelected = s.IsSelected,
        Shots = s.Shots.Select(row => row.ToList()).ToList()
    };
}
