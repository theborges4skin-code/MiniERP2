using MiniERP2.Models;
using OfficeOpenXml;

namespace MiniERP2.Utils;

/// <summary>
/// Excel 파일 내 _META 시트(채널 식별용 메타데이터) 읽기/쓰기.
/// _META 시트는 원본 파일에는 없고, 복사본(staging copy)에 기록한다.
/// 자동화 파이프라인이 다음에 같은 파일을 만나도 채널을 재묻지 않도록 한다.
/// </summary>
public static class MetaSheetHelper
{
    private const string SheetName = "_META";

    // ── 읽기 ──────────────────────────────────────────────────────────────────

    /// <summary>파일에서 _META 시트를 읽는다. 없으면 null 반환.</summary>
    public static FileMeta? TryRead(string filePath)
    {
        try
        {
            if (!filePath.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) &&
                !filePath.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
                return null;

            using var package = ExcelFileOpener.Open(filePath);
            var sheet = package.Workbook.Worksheets[SheetName];
            if (sheet == null) return null;

            var kvp = ReadKeyValues(sheet);
            if (!kvp.TryGetValue("channel_code", out var code) || string.IsNullOrEmpty(code))
                return null;

            return new FileMeta
            {
                SchemaVersion = int.TryParse(kvp.GetValueOrDefault("schema_version"), out var v) ? v : 1,
                ChannelCode = code,
                ChannelName = kvp.GetValueOrDefault("channel_name") ?? "",
                SourceType = kvp.GetValueOrDefault("source_type") ?? "settlement",
                FileCreatedAt = kvp.GetValueOrDefault("file_created_at") ?? "",
                Period = kvp.GetValueOrDefault("period") ?? "",
            };
        }
        catch
        {
            return null;
        }
    }

    // ── 쓰기 ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 대상 파일(원본의 복사본)에 _META 시트를 추가/갱신한다.
    /// 원본 파일은 건드리지 않는다.
    /// </summary>
    public static void Write(string targetFilePath, FileMeta meta)
    {
        using var package = ExcelFileOpener.Open(targetFilePath);

        // 기존 _META 시트가 있으면 삭제 후 재생성
        var existing = package.Workbook.Worksheets[SheetName];
        if (existing != null)
            package.Workbook.Worksheets.Delete(existing);

        var sheet = package.Workbook.Worksheets.Add(SheetName);

        // 맨 앞에 배치
        package.Workbook.Worksheets.MoveToStart(SheetName);

        WriteRow(sheet, 1, "schema_version", meta.SchemaVersion.ToString());
        WriteRow(sheet, 2, "source_type", meta.SourceType);
        WriteRow(sheet, 3, "channel_name", meta.ChannelName);
        WriteRow(sheet, 4, "channel_code", meta.ChannelCode);
        WriteRow(sheet, 5, "file_created_at",
            string.IsNullOrEmpty(meta.FileCreatedAt)
                ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                : meta.FileCreatedAt);
        WriteRow(sheet, 6, "period", meta.Period);

        sheet.Column(1).Width = 20;
        sheet.Column(2).Width = 30;

        package.Save();
    }

    /// <summary>
    /// 원본 파일을 staging 폴더에 복사한 뒤 _META를 기록하고 복사본 경로를 반환한다.
    /// </summary>
    public static string CopyAndWriteMeta(string originalPath, string stagingFolder, FileMeta meta)
    {
        Directory.CreateDirectory(stagingFolder);
        var fileName = Path.GetFileName(originalPath);
        var destPath = Path.Combine(stagingFolder, fileName);

        // 같은 파일명이 있으면 타임스탬프 붙여서 충돌 회피
        if (File.Exists(destPath))
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            destPath = Path.Combine(stagingFolder, $"{stem}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
        }

        File.Copy(originalPath, destPath);
        Write(destPath, meta);
        return destPath;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────────────────

    private static Dictionary<string, string> ReadKeyValues(ExcelWorksheet sheet)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lastRow = sheet.Dimension?.End.Row ?? 0;
        for (int row = 1; row <= lastRow; row++)
        {
            var key = sheet.Cells[row, 1].Text?.Trim();
            var val = sheet.Cells[row, 2].Text?.Trim();
            if (!string.IsNullOrEmpty(key))
                result[key] = val ?? "";
        }
        return result;
    }

    private static void WriteRow(ExcelWorksheet sheet, int row, string key, string value)
    {
        sheet.Cells[row, 1].Value = key;
        sheet.Cells[row, 2].Value = value;
    }
}
