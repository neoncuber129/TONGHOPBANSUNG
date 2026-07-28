using ClosedXML.Excel;
using Tonghopbansung.Models;

namespace Tonghopbansung.Services;

public sealed class ExcelReportRow
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Rank { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string GroupName { get; init; } = string.Empty;
    public IReadOnlyList<string> TargetDetails { get; init; } = [];
    public int Total { get; init; }
    public int KnockDownCount { get; init; }
    public string Classification { get; init; } = string.Empty;
}

public static class ExcelExportService
{
    public static void ExportReport(
        string filePath,
        string groupName,
        string presetName,
        IReadOnlyList<ExcelReportRow> rows,
        IReadOnlyList<TargetDefinition> targets)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Kết quả");

        var hasKnockDown = targets.Any(t => t.Kind == TargetKind.KnockDown);
        // fixed(6) + targets + tổng + (bia đổ?) + xếp loại
        var lastCol = 6 + targets.Count + 2 + (hasKnockDown ? 1 : 0);

        ws.Cell(1, 1).Value = "BÁO CÁO KẾT QUẢ BẮN SÚNG";
        ws.Range(1, 1, 1, lastCol).Merge();
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(2, 1).Value = $"Đợt: {groupName}";
        ws.Cell(2, 3).Value = $"Nhóm: {presetName}";
        ws.Cell(2, 5).Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}";

        const int headerRow = 4;
        var col = 1;
        foreach (var h in new[] { "STT", "Họ tên", "Cấp bậc", "Chức vụ", "Đơn vị", "Nhóm" })
            ws.Cell(headerRow, col++).Value = h;

        foreach (var t in targets)
            ws.Cell(headerRow, col++).Value = t.Name;

        ws.Cell(headerRow, col++).Value = "Tổng";
        if (hasKnockDown)
            ws.Cell(headerRow, col++).Value = "Bia đổ";
        ws.Cell(headerRow, col).Value = "Xếp loại";

        var headerRange = ws.Range(headerRow, 1, headerRow, lastCol);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#3E6B4F");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        var r = headerRow + 1;
        foreach (var row in rows)
        {
            col = 1;
            ws.Cell(r, col++).Value = row.Index;
            ws.Cell(r, col++).Value = row.Name;
            ws.Cell(r, col++).Value = row.Rank;
            ws.Cell(r, col++).Value = row.Position;
            ws.Cell(r, col++).Value = row.Unit;
            ws.Cell(r, col++).Value = row.GroupName;

            for (var t = 0; t < targets.Count; t++)
                ws.Cell(r, col++).Value = t < row.TargetDetails.Count ? row.TargetDetails[t] : string.Empty;

            ws.Cell(r, col++).Value = row.Total;
            if (hasKnockDown)
                ws.Cell(r, col++).Value = row.KnockDownCount;
            ws.Cell(r, col).Value = row.Classification;

            var dataRange = ws.Range(r, 1, r, lastCol);
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            if (r % 2 == 0)
                dataRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F6F3");

            // Căn giữa các cột số
            ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            var totalCol = 7 + targets.Count;
            ws.Cell(r, totalCol).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (hasKnockDown)
                ws.Cell(r, totalCol + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            r++;
        }

        ws.Columns().AdjustToContents(1, 45);
        ws.SheetView.FreezeRows(headerRow);

        var ws2 = wb.Worksheets.Add("Thống kê xếp loại");
        ws2.Cell(1, 1).Value = "Xếp loại";
        ws2.Cell(1, 2).Value = "Số lượng";
        ws2.Range(1, 1, 1, 2).Style.Font.Bold = true;
        ws2.Range(1, 1, 1, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#3E6B4F");
        ws2.Range(1, 1, 1, 2).Style.Font.FontColor = XLColor.White;

        var sr = 2;
        foreach (var g in rows.GroupBy(x => x.Classification).OrderByDescending(g => g.Count()))
        {
            ws2.Cell(sr, 1).Value = g.Key;
            ws2.Cell(sr, 2).Value = g.Count();
            sr++;
        }

        ws2.Cell(sr, 1).Value = "Tổng";
        ws2.Cell(sr, 1).Style.Font.Bold = true;
        ws2.Cell(sr, 2).Value = rows.Count;
        ws2.Cell(sr, 2).Style.Font.Bold = true;
        ws2.Columns().AdjustToContents();

        wb.SaveAs(filePath);
    }
}
