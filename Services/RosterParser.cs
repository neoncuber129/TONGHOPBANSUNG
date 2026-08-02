namespace Tonghopbansung.Services;

public sealed class RosterEntry
{
    public string Name { get; init; } = string.Empty;
    public string Rank { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
}

public static class RosterParser
{
    /// <summary>
    /// Parse paste từ Excel: mỗi dòng một người.
    /// Cột (tab hoặc ;): Họ tên | Cấp bậc | Chức vụ | Đơn vị (các cột sau tùy chọn).
    /// Nếu một dòng chỉ có tên (hoặc nhiều tên cách nhau dấu phẩy trên một dòng khi không có tab) — xử lý tương thích cũ.
    /// </summary>
    public static List<RosterEntry> ParseEntries(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var entries = new List<RosterEntry>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            // Excel thường dùng tab giữa các cột
            if (line.Contains('\t') || line.Contains(';'))
            {
                var parts = line.Split(['\t', ';'], StringSplitOptions.None)
                    .Select(p => p.Trim())
                    .ToArray();

                var name = parts.Length > 0 ? NormalizeName(parts[0]) : string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                entries.Add(new RosterEntry
                {
                    Name = name,
                    Rank = parts.Length > 1 ? parts[1] : string.Empty,
                    Position = parts.Length > 2 ? parts[2] : string.Empty,
                    Unit = parts.Length > 3 ? parts[3] : string.Empty
                });
            }
            else if (line.Contains(','))
            {
                // Có thể là "Họ tên, Cấp bậc, Chức vụ, Đơn vị" hoặc nhiều tên
                var parts = line.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length >= 2 && parts.Length <= 4 && !parts.All(p => p.Split(' ').Length <= 4 && LooksLikePersonNameList(parts)))
                {
                    // Ưu tiên coi là một người nhiều cột nếu 2–4 phần
                    var name = NormalizeName(parts[0]);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    entries.Add(new RosterEntry
                    {
                        Name = name,
                        Rank = parts.Length > 1 ? parts[1] : string.Empty,
                        Position = parts.Length > 2 ? parts[2] : string.Empty,
                        Unit = parts.Length > 3 ? parts[3] : string.Empty
                    });
                }
                else
                {
                    foreach (var part in parts)
                    {
                        var name = NormalizeName(part);
                        if (!string.IsNullOrWhiteSpace(name))
                            entries.Add(new RosterEntry { Name = name });
                    }
                }
            }
            else
            {
                var name = NormalizeName(line);
                if (!string.IsNullOrWhiteSpace(name))
                    entries.Add(new RosterEntry { Name = name });
            }
        }

        return entries;
    }

    /// <summary>Tương thích cũ: chỉ lấy danh sách tên.</summary>
    public static List<string> Parse(string? text) =>
        ParseEntries(text).Select(e => e.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

    /// <summary>
    /// Tách văn bản dán thành lưới ô như Excel (tab hoặc ;).
    /// Giữ dòng trống giữa vùng dán để không lệch hàng; bỏ dòng thừa ở cuối.
    /// </summary>
    public static List<string[]> ParseGrid(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        return lines
            .Select(line => line.Split(['\t', ';'], StringSplitOptions.None)
                .Select(cell => cell.Trim())
                .ToArray())
            .ToList();
    }

    public static bool LooksLikeSpreadsheetPaste(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains('\t')
               || text.Contains(';')
               || text.Contains('\n')
               || text.Contains('\r');
    }

    private static bool LooksLikePersonNameList(string[] parts) =>
        parts.Length > 4;

    private static string NormalizeName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length > 2 && char.IsDigit(trimmed[0]))
        {
            var i = 0;
            while (i < trimmed.Length && (char.IsDigit(trimmed[i]) || trimmed[i] is '.' or ')' or ' '))
                i++;
            if (i < trimmed.Length)
                trimmed = trimmed[i..].Trim();
        }
        return trimmed;
    }
}
