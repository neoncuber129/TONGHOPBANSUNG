using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tonghopbansung.Models;

namespace Tonghopbansung.Services;

public interface IBackupService
{
    void Export(AppState state, string filePath);
    AppState Import(string filePath);
}

/// <summary>Sao lưu / phục hồi bằng file SQLite (.thbs). Vẫn đọc được bản JSON cũ.</summary>
public sealed class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    public void Export(AppState state, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        DeleteIfExists(filePath);
        DeleteIfExists(filePath + "-wal");
        DeleteIfExists(filePath + "-shm");

        var store = new SqliteDataStore(filePath);
        store.Save(state, compactBackup: true);
    }

    public AppState Import(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Không tìm thấy file sao lưu.", filePath);

        if (IsSqliteDatabase(filePath))
        {
            var store = new SqliteDataStore(filePath);
            return store.LoadBackup();
        }

        // Tương thích bản sao lưu JSON cũ (.thbs / .json)
        return ImportLegacyJson(filePath);
    }

    private static AppState ImportLegacyJson(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var state = JsonSerializer.Deserialize<AppState>(json, LegacyJsonOptions)
            ?? throw new InvalidDataException("File sao lưu JSON không hợp lệ.");

        if (state.Presets.Count == 0)
            throw new InvalidDataException("File sao lưu không có preset.");

        return state;
    }

    private static bool IsSqliteDatabase(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            if (fs.Length < SqliteHeader.Length)
                return false;

            Span<byte> header = stackalloc byte[SqliteHeader.Length];
            if (fs.Read(header) < SqliteHeader.Length)
                return false;

            return header.SequenceEqual(SqliteHeader);
        }
        catch
        {
            return false;
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // bỏ qua — Save sẽ ghi đè nếu còn file chính
        }
    }
}
