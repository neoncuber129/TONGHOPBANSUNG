using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Tonghopbansung.Models;

namespace Tonghopbansung.Services;

/// <summary>Lưu AppState vào SQLite (data.db). Tự chuyển từ data.json nếu còn.</summary>
public sealed class SqliteDataStore : IDataStore
{
    private static readonly JsonSerializerOptions ShotsJsonOptions = new();

    public string DataDirectory { get; }
    public string DataFilePath { get; }
    public string LegacyJsonPath { get; }

    public SqliteDataStore()
        : this(Path.Combine(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            "data.db"))
    {
    }

    public SqliteDataStore(string databasePath)
    {
        DataFilePath = Path.GetFullPath(databasePath);
        DataDirectory = Path.GetDirectoryName(DataFilePath)
                        ?? AppContext.BaseDirectory.TrimEnd(
                            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        LegacyJsonPath = Path.Combine(DataDirectory, "data.json");
    }

    public AppState Load()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            EnsureDatabase(useWal: true);

            if (!HasAnyData())
            {
                if (File.Exists(LegacyJsonPath))
                {
                    var fromJson = new JsonDataStore().Load();
                    if (fromJson.Presets.Count > 0)
                    {
                        Save(fromJson);
                        TryRenameLegacyJson();
                        return fromJson;
                    }
                }

                var def = AppState.CreateDefault();
                Save(def);
                return def;
            }

            var state = ReadAll();
            if (state.Presets.Count == 0)
            {
                state = AppState.CreateDefault();
                Save(state);
                return state;
            }

            EnsureOnePresetPerGroup(state);
            EnsureShotMatrices(state);
            return state;
        }
        catch
        {
            return AppState.CreateDefault();
        }
    }

    /// <summary>Đọc file SQLite sao lưu (không tạo dữ liệu mặc định).</summary>
    public AppState LoadBackup()
    {
        if (!File.Exists(DataFilePath))
            throw new FileNotFoundException("Không tìm thấy file sao lưu.", DataFilePath);

        EnsureDatabase(useWal: false);
        var state = ReadAll(useWal: false);
        if (state.Presets.Count == 0)
            throw new InvalidDataException("File sao lưu không có dữ liệu nhóm / preset.");

        EnsureOnePresetPerGroup(state);
        EnsureShotMatrices(state);
        return state;
    }

    public void Save(AppState state) => Save(state, compactBackup: false);

    public void Save(AppState state, bool compactBackup)
    {
        Directory.CreateDirectory(DataDirectory);
        EnsureDatabase(useWal: !compactBackup);

        using var conn = Open(useWal: !compactBackup);
        using var tx = conn.BeginTransaction();

        Execute(conn, tx, "DELETE FROM classification_conditions");
        Execute(conn, tx, "DELETE FROM classification_rules");
        Execute(conn, tx, "DELETE FROM targets");
        Execute(conn, tx, "DELETE FROM clusters");
        Execute(conn, tx, "DELETE FROM shooters");
        Execute(conn, tx, "DELETE FROM sessions");
        Execute(conn, tx, "DELETE FROM groups");
        Execute(conn, tx, "DELETE FROM presets");
        Execute(conn, tx, "DELETE FROM meta");

        SetMeta(conn, tx, "active_group_id", state.ActiveGroupId?.ToString() ?? "");
        SetMeta(conn, tx, "active_session_id", state.ActiveSessionId?.ToString() ?? "");
        SetMeta(conn, tx, "schema_version", "2");

        for (var pi = 0; pi < state.Presets.Count; pi++)
        {
            var preset = state.Presets[pi];
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO presets(id, name, sort_order) VALUES ($id, $name, $ord)";
                cmd.Parameters.AddWithValue("$id", preset.Id.ToString());
                cmd.Parameters.AddWithValue("$name", preset.Name ?? "");
                cmd.Parameters.AddWithValue("$ord", pi);
                cmd.ExecuteNonQuery();
            }

            for (var ci = 0; ci < preset.Clusters.Count; ci++)
            {
                var cluster = preset.Clusters[ci];
                if (cluster.Id == Guid.Empty)
                    cluster.Id = Guid.NewGuid();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "INSERT INTO clusters(id, preset_id, name, sort_order) VALUES ($id, $pid, $name, $ord)";
                    cmd.Parameters.AddWithValue("$id", cluster.Id.ToString());
                    cmd.Parameters.AddWithValue("$pid", preset.Id.ToString());
                    cmd.Parameters.AddWithValue("$name", cluster.Name ?? "");
                    cmd.Parameters.AddWithValue("$ord", ci);
                    cmd.ExecuteNonQuery();
                }

                for (var ti = 0; ti < cluster.Targets.Count; ti++)
                {
                    var t = cluster.Targets[ti];
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "INSERT INTO targets(cluster_id, name, round_count, kind, miss_penalty, hit_bonus, sort_order) VALUES ($cid, $name, $rc, $kind, $pen, $bonus, $ord)";
                    cmd.Parameters.AddWithValue("$cid", cluster.Id.ToString());
                    cmd.Parameters.AddWithValue("$name", t.Name ?? "");
                    cmd.Parameters.AddWithValue("$rc", t.RoundCount);
                    cmd.Parameters.AddWithValue("$kind", (int)t.Kind);
                    cmd.Parameters.AddWithValue("$pen", t.Kind == TargetKind.KnockDown ? Math.Max(0, t.MissPenalty) : 0);
                    cmd.Parameters.AddWithValue("$bonus", t.Kind == TargetKind.KnockDown ? Math.Max(0, t.HitBonus) : 0);
                    cmd.Parameters.AddWithValue("$ord", ti);
                    cmd.ExecuteNonQuery();
                }
            }

            for (var ri = 0; ri < preset.ClassificationRules.Count; ri++)
            {
                var rule = preset.ClassificationRules[ri];
                rule.EnsureLegacyCondition();
                long ruleId;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "INSERT INTO classification_rules(preset_id, label, min_score, priority, sort_order) VALUES ($pid, $label, $min, $pri, $ord)";
                    cmd.Parameters.AddWithValue("$pid", preset.Id.ToString());
                    cmd.Parameters.AddWithValue("$label", rule.Label ?? "");
                    cmd.Parameters.AddWithValue("$min", rule.MinScore);
                    cmd.Parameters.AddWithValue("$pri", rule.Priority);
                    cmd.Parameters.AddWithValue("$ord", ri);
                    cmd.ExecuteNonQuery();
                }

                using (var idCmd = conn.CreateCommand())
                {
                    idCmd.Transaction = tx;
                    idCmd.CommandText = "SELECT last_insert_rowid()";
                    ruleId = (long)(idCmd.ExecuteScalar() ?? 0L);
                }

                for (var cdi = 0; cdi < rule.Conditions.Count; cdi++)
                {
                    var c = rule.Conditions[cdi];
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText =
                        "INSERT INTO classification_conditions(rule_id, kind, target_index, min_value, sort_order) VALUES ($rid, $kind, $ti, $mv, $ord)";
                    cmd.Parameters.AddWithValue("$rid", ruleId);
                    cmd.Parameters.AddWithValue("$kind", (int)c.Kind);
                    cmd.Parameters.AddWithValue("$ti", c.TargetIndex);
                    cmd.Parameters.AddWithValue("$mv", c.MinValue);
                    cmd.Parameters.AddWithValue("$ord", cdi);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        for (var gi = 0; gi < state.Groups.Count; gi++)
        {
            var g = state.Groups[gi];
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO groups(id, name, preset_id, sort_order) VALUES ($id, $name, $pid, $ord)";
            cmd.Parameters.AddWithValue("$id", g.Id.ToString());
            cmd.Parameters.AddWithValue("$name", g.Name ?? "");
            cmd.Parameters.AddWithValue("$pid", g.PresetId.ToString());
            cmd.Parameters.AddWithValue("$ord", gi);
            cmd.ExecuteNonQuery();

            // Legacy: shooter còn trên group (trước khi AppSession migrate sang session)
            for (var si = 0; si < g.Shooters.Count; si++)
                InsertShooter(conn, tx, g.Shooters[si], sessionId: null, groupId: g.Id, sortOrder: si);
        }

        for (var sei = 0; sei < state.Sessions.Count; sei++)
        {
            var s = state.Sessions[sei];
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO sessions(id, name, group_id, created_at, sort_order) VALUES ($id, $name, $gid, $at, $ord)";
                cmd.Parameters.AddWithValue("$id", s.Id.ToString());
                cmd.Parameters.AddWithValue("$name", s.Name ?? "");
                cmd.Parameters.AddWithValue("$gid", s.GroupId.ToString());
                cmd.Parameters.AddWithValue("$at", s.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("$ord", sei);
                cmd.ExecuteNonQuery();
            }

            for (var si = 0; si < s.Shooters.Count; si++)
                InsertShooter(conn, tx, s.Shooters[si], sessionId: s.Id, groupId: null, sortOrder: si);
        }

        tx.Commit();

        if (compactBackup)
            Execute(conn, null, "VACUUM;");
    }

    private AppState ReadAll(bool useWal = true)
    {
        using var conn = Open(useWal);
        var state = new AppState
        {
            ActiveGroupId = ParseGuidOrNull(GetMeta(conn, "active_group_id")),
            ActiveSessionId = ParseGuidOrNull(GetMeta(conn, "active_session_id"))
        };

        var presets = new Dictionary<Guid, ScorePreset>();
        var legacyPresetPenalties = new Dictionary<Guid, int>();
        using (var cmd = conn.CreateCommand())
        {
            // Cột knock_down_miss_penalty cũ (preset) → migrate sang từng bia
            cmd.CommandText = "SELECT id, name, knock_down_miss_penalty FROM presets ORDER BY sort_order, name";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = Guid.Parse(r.GetString(0));
                var legacyPenalty = r.FieldCount > 2 && !r.IsDBNull(2) ? Math.Max(0, r.GetInt32(2)) : 0;
                var p = new ScorePreset { Id = id, Name = r.GetString(1) };
                if (legacyPenalty > 0)
                    legacyPresetPenalties[id] = legacyPenalty;
                presets[id] = p;
                state.Presets.Add(p);
            }
        }

        var clustersByPreset = new Dictionary<Guid, List<(Guid Id, string Name, int Ord)>>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, preset_id, name, sort_order FROM clusters ORDER BY sort_order";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var cid = Guid.Parse(r.GetString(0));
                var pid = Guid.Parse(r.GetString(1));
                if (!clustersByPreset.TryGetValue(pid, out var list))
                {
                    list = [];
                    clustersByPreset[pid] = list;
                }
                list.Add((cid, r.GetString(2), r.GetInt32(3)));
            }
        }

        var targetsByCluster = new Dictionary<Guid, List<(string Name, int RoundCount, TargetKind Kind, int MissPenalty, int HitBonus, int Ord)>>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT cluster_id, name, round_count, kind, miss_penalty, hit_bonus, sort_order FROM targets ORDER BY sort_order";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var cid = Guid.Parse(r.GetString(0));
                if (!targetsByCluster.TryGetValue(cid, out var list))
                {
                    list = [];
                    targetsByCluster[cid] = list;
                }
                var miss = r.IsDBNull(4) ? 0 : Math.Max(0, r.GetInt32(4));
                var bonus = r.IsDBNull(5) ? 0 : Math.Max(0, r.GetInt32(5));
                var ord = r.GetInt32(6);
                list.Add((r.GetString(1), r.GetInt32(2), (TargetKind)r.GetInt32(3), miss, bonus, ord));
            }
        }

        foreach (var (presetId, clusterRows) in clustersByPreset)
        {
            if (!presets.TryGetValue(presetId, out var preset)) continue;
            legacyPresetPenalties.TryGetValue(presetId, out var legacyPenalty);
            foreach (var (cid, name, _) in clusterRows.OrderBy(x => x.Ord))
            {
                var cluster = new TargetCluster { Id = cid, Name = name };
                if (targetsByCluster.TryGetValue(cid, out var targets))
                {
                    foreach (var t in targets.OrderBy(x => x.Ord))
                    {
                        var miss = t.MissPenalty;
                        if (t.Kind == TargetKind.KnockDown && miss <= 0 && legacyPenalty > 0)
                            miss = legacyPenalty;
                        cluster.Targets.Add(new TargetDefinition
                        {
                            Name = t.Name,
                            RoundCount = t.RoundCount,
                            Kind = t.Kind,
                            MissPenalty = t.Kind == TargetKind.KnockDown ? miss : 0,
                            HitBonus = t.Kind == TargetKind.KnockDown ? t.HitBonus : 0
                        });
                    }
                }
                preset.Clusters.Add(cluster);
            }
            preset.InvalidateLayoutCache();
        }

        var rulesByPreset = new Dictionary<Guid, List<(long RuleId, string Label, int MinScore, int Priority, int Ord)>>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT id, preset_id, label, min_score, priority, sort_order FROM classification_rules ORDER BY sort_order";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ruleId = r.GetInt64(0);
                var pid = Guid.Parse(r.GetString(1));
                if (!rulesByPreset.TryGetValue(pid, out var list))
                {
                    list = [];
                    rulesByPreset[pid] = list;
                }
                list.Add((ruleId, r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5)));
            }
        }

        var conditionsByRule = new Dictionary<long, List<(ClassificationConditionKind Kind, int TargetIndex, int MinValue, int Ord)>>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT rule_id, kind, target_index, min_value, sort_order FROM classification_conditions ORDER BY sort_order";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var ruleId = r.GetInt64(0);
                if (!conditionsByRule.TryGetValue(ruleId, out var list))
                {
                    list = [];
                    conditionsByRule[ruleId] = list;
                }
                list.Add(((ClassificationConditionKind)r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4)));
            }
        }

        foreach (var (presetId, ruleRows) in rulesByPreset)
        {
            if (!presets.TryGetValue(presetId, out var preset)) continue;
            foreach (var row in ruleRows.OrderBy(x => x.Ord))
            {
                var rule = new ClassificationRule
                {
                    Label = row.Label,
                    MinScore = row.MinScore,
                    Priority = row.Priority
                };
                if (conditionsByRule.TryGetValue(row.RuleId, out var conds))
                {
                    foreach (var c in conds.OrderBy(x => x.Ord))
                    {
                        rule.Conditions.Add(new ClassificationCondition
                        {
                            Kind = c.Kind,
                            TargetIndex = c.TargetIndex,
                            MinValue = c.MinValue
                        });
                    }
                }
                rule.EnsureLegacyCondition();
                preset.ClassificationRules.Add(rule);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name, preset_id FROM groups ORDER BY sort_order, name";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                state.Groups.Add(new Group
                {
                    Id = Guid.Parse(r.GetString(0)),
                    Name = r.GetString(1),
                    PresetId = Guid.Parse(r.GetString(2))
                });
            }
        }

        var groupsById = state.Groups.ToDictionary(g => g.Id);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name, group_id, created_at FROM sessions ORDER BY sort_order, created_at";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var created = DateTime.TryParse(r.GetString(3), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                    ? dt
                    : DateTime.Now;
                state.Sessions.Add(new ShootingSession
                {
                    Id = Guid.Parse(r.GetString(0)),
                    Name = r.GetString(1),
                    GroupId = Guid.Parse(r.GetString(2)),
                    CreatedAt = created
                });
            }
        }

        var sessionsById = state.Sessions.ToDictionary(s => s.Id);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT id, session_id, group_id, name, rank, position, unit, sort_order, is_selected, shots_json FROM shooters ORDER BY sort_order";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var shooter = new Shooter
                {
                    Id = Guid.Parse(r.GetString(0)),
                    Name = r.IsDBNull(3) ? "" : r.GetString(3),
                    Rank = r.IsDBNull(4) ? "" : r.GetString(4),
                    Position = r.IsDBNull(5) ? "" : r.GetString(5),
                    Unit = r.IsDBNull(6) ? "" : r.GetString(6),
                    Order = r.GetInt32(7),
                    IsSelected = r.GetInt32(8) != 0,
                    Shots = DeserializeShots(r.IsDBNull(9) ? "[]" : r.GetString(9))
                };

                if (!r.IsDBNull(1) && Guid.TryParse(r.GetString(1), out var sid) &&
                    sessionsById.TryGetValue(sid, out var session))
                {
                    session.Shooters.Add(shooter);
                }
                else if (!r.IsDBNull(2) && Guid.TryParse(r.GetString(2), out var gid) &&
                         groupsById.TryGetValue(gid, out var group))
                {
                    group.Shooters.Add(shooter);
                }
            }
        }

        return state;
    }

    private static void InsertShooter(
        SqliteConnection conn,
        SqliteTransaction tx,
        Shooter shooter,
        Guid? sessionId,
        Guid? groupId,
        int sortOrder)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO shooters(id, session_id, group_id, name, rank, position, unit, sort_order, is_selected, shots_json)
            VALUES ($id, $sid, $gid, $name, $rank, $pos, $unit, $ord, $sel, $shots)
            """;
        cmd.Parameters.AddWithValue("$id", shooter.Id.ToString());
        cmd.Parameters.AddWithValue("$sid", sessionId?.ToString() is { } s ? s : DBNull.Value);
        cmd.Parameters.AddWithValue("$gid", groupId?.ToString() is { } g ? g : DBNull.Value);
        cmd.Parameters.AddWithValue("$name", shooter.Name ?? "");
        cmd.Parameters.AddWithValue("$rank", shooter.Rank ?? "");
        cmd.Parameters.AddWithValue("$pos", shooter.Position ?? "");
        cmd.Parameters.AddWithValue("$unit", shooter.Unit ?? "");
        cmd.Parameters.AddWithValue("$ord", sortOrder);
        cmd.Parameters.AddWithValue("$sel", shooter.IsSelected ? 1 : 0);
        cmd.Parameters.AddWithValue("$shots", SerializeShots(shooter.Shots));
        cmd.ExecuteNonQuery();
    }

    private static string SerializeShots(List<List<int?>> shots) =>
        JsonSerializer.Serialize(shots, ShotsJsonOptions);

    private static List<List<int?>> DeserializeShots(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<List<int?>>>(json, ShotsJsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void EnsureDatabase(bool useWal = true)
    {
        using var conn = Open(useWal);
        Execute(conn, null, """
            CREATE TABLE IF NOT EXISTS meta (
              key TEXT PRIMARY KEY NOT NULL,
              value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS presets (
              id TEXT PRIMARY KEY NOT NULL,
              name TEXT NOT NULL,
              sort_order INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS clusters (
              id TEXT PRIMARY KEY NOT NULL,
              preset_id TEXT NOT NULL,
              name TEXT NOT NULL,
              sort_order INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS targets (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              cluster_id TEXT NOT NULL,
              name TEXT NOT NULL,
              round_count INTEGER NOT NULL,
              kind INTEGER NOT NULL,
              miss_penalty INTEGER NOT NULL DEFAULT 0,
              hit_bonus INTEGER NOT NULL DEFAULT 0,
              sort_order INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS classification_rules (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              preset_id TEXT NOT NULL,
              label TEXT NOT NULL,
              min_score INTEGER NOT NULL,
              priority INTEGER NOT NULL,
              sort_order INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS classification_conditions (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              rule_id INTEGER NOT NULL,
              kind INTEGER NOT NULL,
              target_index INTEGER NOT NULL,
              min_value INTEGER NOT NULL,
              sort_order INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS groups (
              id TEXT PRIMARY KEY NOT NULL,
              name TEXT NOT NULL,
              preset_id TEXT NOT NULL,
              sort_order INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sessions (
              id TEXT PRIMARY KEY NOT NULL,
              name TEXT NOT NULL,
              group_id TEXT NOT NULL,
              created_at TEXT NOT NULL,
              sort_order INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS shooters (
              id TEXT PRIMARY KEY NOT NULL,
              session_id TEXT NULL,
              group_id TEXT NULL,
              name TEXT NOT NULL,
              rank TEXT NOT NULL,
              position TEXT NOT NULL,
              unit TEXT NOT NULL,
              sort_order INTEGER NOT NULL,
              is_selected INTEGER NOT NULL,
              shots_json TEXT NOT NULL
            );
            """);

        EnsureColumn(conn, "presets", "knock_down_miss_penalty", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "targets", "miss_penalty", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "targets", "hit_bonus", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string definition)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        r.Close();
        Execute(conn, null, $"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }

    private bool HasAnyData()
    {
        if (!File.Exists(DataFilePath)) return false;
        using var conn = Open(useWal: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM presets";
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private SqliteConnection Open(bool useWal = true)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DataFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = useWal
                ? "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;"
                : "PRAGMA foreign_keys = ON; PRAGMA journal_mode = DELETE;";
            cmd.ExecuteNonQuery();
        }
        return conn;
    }

    private static void Execute(SqliteConnection conn, SqliteTransaction? tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void SetMeta(SqliteConnection conn, SqliteTransaction tx, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO meta(key, value) VALUES ($k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    private static string GetMeta(SqliteConnection conn, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string ?? "";
    }

    private static Guid? ParseGuidOrNull(string? s) =>
        Guid.TryParse(s, out var g) ? g : null;

    private void TryRenameLegacyJson()
    {
        try
        {
            var bak = LegacyJsonPath + ".bak";
            if (File.Exists(bak))
                File.Delete(bak);
            File.Move(LegacyJsonPath, bak);
        }
        catch
        {
            // giữ nguyên data.json nếu rename thất bại
        }
    }

    private static void EnsureOnePresetPerGroup(AppState state)
    {
        var byId = state.Presets.ToDictionary(p => p.Id);

        foreach (var group in state.Groups)
        {
            if (!byId.TryGetValue(group.PresetId, out var shared))
            {
                var created = new ScorePreset { Name = group.Name };
                created.EnsureDefaultClusters(2, 5);
                state.Presets.Add(created);
                byId[created.Id] = created;
                group.PresetId = created.Id;
                continue;
            }

            var sharers = state.Groups.Where(g => g.PresetId == shared.Id).ToList();
            if (sharers.Count <= 1)
            {
                if (string.IsNullOrWhiteSpace(shared.Name) || shared.Name.StartsWith("Preset ", StringComparison.OrdinalIgnoreCase))
                    shared.Name = group.Name;
                continue;
            }

            foreach (var extra in sharers.Skip(1))
            {
                var clone = shared.CloneDeep(extra.Name);
                state.Presets.Add(clone);
                byId[clone.Id] = clone;
                extra.PresetId = clone.Id;
            }

            if (string.IsNullOrWhiteSpace(shared.Name) || shared.Name.StartsWith("Preset ", StringComparison.OrdinalIgnoreCase))
                shared.Name = sharers[0].Name;
        }

        var used = state.Groups.Select(g => g.PresetId).ToHashSet();
        state.Presets.RemoveAll(p => !used.Contains(p.Id));
    }

    private static void EnsureShotMatrices(AppState state)
    {
        var presets = state.Presets.ToDictionary(p => p.Id);
        var groups = state.Groups.ToDictionary(g => g.Id);

        foreach (var group in state.Groups)
        {
            if (!presets.TryGetValue(group.PresetId, out var preset))
                continue;
            var rounds = preset.GetRoundCounts();
            foreach (var shooter in group.Shooters)
                shooter.EnsureShotMatrix(rounds);
        }

        foreach (var session in state.Sessions)
        {
            if (!groups.TryGetValue(session.GroupId, out var group))
                continue;
            if (!presets.TryGetValue(group.PresetId, out var preset))
                continue;
            var rounds = preset.GetRoundCounts();
            foreach (var shooter in session.Shooters)
                shooter.EnsureShotMatrix(rounds);
        }
    }
}
