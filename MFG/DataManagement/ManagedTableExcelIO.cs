using System.Data;
using System.Globalization;
using System.Text;
using OfficeOpenXml;

namespace MiniERP2.MFG.DataManagement;

/// <summary>
/// [데이터 관리] 엑셀 내보내기/불러오기(MiniERP2 ManagedTableExcelIO와 같은 동작). 1행 = 헤더, 2행부터 데이터.
/// 불러오기는 .xlsx와 .csv(UTF-8, 엑셀 CSV 저장) 모두 받는다.
/// </summary>
public static class ManagedTableExcelIO
{
    public static int Export(DataTable table, List<string> selectedColumns, string? filterColumn, string? filterValue, string filePath, IReadOnlyCollection<string> infoColumns)
    {
        ExcelPackage.License.SetNonCommercialPersonal("MiniERP2");
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Sheet1");
        for (var col = 0; col < selectedColumns.Count; col++)
        {
            var cell = sheet.Cells[1, col + 1];
            cell.Value = selectedColumns[col];
            cell.Style.Font.Bold = true;
            if (infoColumns.Contains(selectedColumns[col]) || selectedColumns[col] == ManagedTable.IdColumn)
            {
                // 참고용 열(고쳐도 무시됨)과 Id(행 식별용 — 고치지 말 것)는 회색으로 표시한다.
                cell.Style.Font.Color.SetColor(System.Drawing.Color.Gray);
                cell.AddComment(selectedColumns[col] == ManagedTable.IdColumn
                    ? "행 식별용 — 고치지 마세요. 새 행은 비워 두면 됩니다."
                    : "참고용 — 고쳐도 반영되지 않습니다.");
            }
        }

        var rowIndex = 2;
        foreach (DataRow row in table.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;
            if (!string.IsNullOrEmpty(filterColumn) &&
                !string.Equals(Convert.ToString(row[filterColumn], CultureInfo.InvariantCulture), filterValue, StringComparison.OrdinalIgnoreCase)) continue;
            for (var col = 0; col < selectedColumns.Count; col++)
            {
                var value = row[selectedColumns[col]];
                sheet.Cells[rowIndex, col + 1].Value = value switch
                {
                    DBNull => null,
                    bool b => b ? "Y" : "N",
                    long l when selectedColumns[col] == ManagedTable.IdColumn && l < 0 => null, // 저장 전 임시 Id는 내보내지 않음
                    _ => value,
                };
            }
            rowIndex++;
        }
        if (sheet.Dimension is not null) sheet.Cells[sheet.Dimension.Address].AutoFitColumns(8, 60);
        sheet.View.FreezePanes(2, 1);
        package.SaveAs(new FileInfo(filePath));
        return rowIndex - 2;
    }

    public static (List<string> Headers, List<Dictionary<string, string?>> Rows) Read(string filePath)
    {
        if (Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase)) return ReadCsv(filePath);

        ExcelPackage.License.SetNonCommercialPersonal("MiniERP2");
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var package = new ExcelPackage(stream);
        var ws = package.Workbook.Worksheets.FirstOrDefault();
        if (ws?.Dimension is null) return ([], []);
        var headers = new List<string>();
        for (var col = 1; col <= ws.Dimension.End.Column; col++) headers.Add(ws.Cells[1, col].Value?.ToString()?.Trim() ?? string.Empty);

        var rows = new List<Dictionary<string, string?>>();
        for (var r = 2; r <= ws.Dimension.End.Row; r++)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var any = false;
            for (var col = 1; col <= headers.Count; col++)
            {
                if (headers[col - 1].Length == 0) continue;
                var v = ws.Cells[r, col].Value;
                var text = v switch
                {
                    null => null,
                    double d => d.ToString("R", CultureInfo.InvariantCulture),
                    DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    _ => Convert.ToString(v, CultureInfo.InvariantCulture),
                };
                values[headers[col - 1]] = text;
                if (!string.IsNullOrEmpty(text)) any = true;
            }
            if (any) rows.Add(values);
        }
        return (headers, rows);
    }

    private static (List<string>, List<Dictionary<string, string?>>) ReadCsv(string path)
    {
        var lines = ParseCsv(File.ReadAllText(path, Encoding.UTF8));
        if (lines.Count == 0) return ([], []);
        var headers = lines[0].Select(h => h.Trim().TrimStart('﻿')).ToList();
        var rows = new List<Dictionary<string, string?>>();
        foreach (var line in lines.Skip(1))
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count && i < line.Count; i++) if (headers[i].Length > 0) values[headers[i]] = line[i];
            if (values.Values.Any(v => !string.IsNullOrEmpty(v))) rows.Add(values);
        }
        return (headers, rows);
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var result = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') inQuotes = false;
                else field.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                result.Add(row); row = [];
            }
            else field.Append(ch);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); result.Add(row); }
        return result;
    }

    /// <summary>셀 텍스트 → 열 타입 값. 변환 실패 시 문자열 열은 원문, 그 밖은 오류(어느 값인지 알려 줌).</summary>
    public static object ConvertValue(DataColumn column, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DBNull.Value;
        var t = text.Trim();
        var type = column.DataType;
        if (type == typeof(string)) return t;
        if (type == typeof(bool))
        {
            return t.ToUpperInvariant() switch
            {
                "Y" or "YES" or "TRUE" or "1" or "O" or "예" => true,
                "N" or "NO" or "FALSE" or "0" or "X" or "아니오" => false,
                _ => throw new InvalidDataException($"'{column.ColumnName}'에 Y/N이 아닌 값 '{t}'"),
            };
        }
        var s = t.Replace(",", "").Replace("%", "");
        if (type == typeof(double) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        if (type == typeof(long) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)) return (long)l;
        throw new InvalidDataException($"'{column.ColumnName}'에 숫자가 아닌 값 '{t}'");
    }
}
