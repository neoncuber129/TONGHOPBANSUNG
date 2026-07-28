namespace Tonghopbansung.Models;

public class AppState
{
    public List<ScorePreset> Presets { get; set; } = new();
    public List<Group> Groups { get; set; } = new();
    public List<ShootingSession> Sessions { get; set; } = new();
    public Guid? ActiveGroupId { get; set; }
    public Guid? ActiveSessionId { get; set; }

    public static AppState CreateDefault()
    {
        var preset = new ScorePreset { Name = "Nhóm 1" };
        preset.EnsureDefaultTargets(2, 5);

        var group = new Group
        {
            Name = "Nhóm 1",
            PresetId = preset.Id
        };

        return new AppState
        {
            Presets = [preset],
            Groups = [group],
            Sessions = [],
            ActiveGroupId = group.Id,
            ActiveSessionId = null
        };
    }
}
