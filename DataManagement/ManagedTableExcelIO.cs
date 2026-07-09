using System.Data;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.DataManagement;

/// <summary>
/// 데이터 관리창의 테이블(DataTable)을 엑셀로 내보내거나, 엑셀에서 읽어 일반 행 목록(헤더→값)으로
/// 돌려줍니다. 어떤 관리 테이블이든 같은 방식으로 다룰 수 있게 공통 로직을 모았습니다.
/// </summary>
public static class ManagedTableExcelIO
{
    /// <summary>
    /// 선택된 컬럼만, 선택적으로 한 컬럼=값 필터를 적용해 엑셀로 내보냅니다.
    /// </summary>
    public static int Export(DataTable table, List<string> selectedColumns, string? filterColumn, string? filterValue, string filePath)
    {
        ExcelLicense.Ensure();
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Sheet1");

        for (int col = 0; col < selectedColumns.Count; col++)
        {
            sheet.Cells[1, col + 1].Value = selectedColumns[col];
        }

        var rowIndex = 2;
        foreach (DataRow row in table.Rows)
        {
            if (row.RowState == DataRowState.Deleted) continue;

            if (!string.IsNullOrEmpty(filterColumn))
            {
                var actual = row[filterColumn]?.ToString() ?? string.Empty;
                if (!string.Equals(actual, filterValue, StringComparison.OrdinalIgnoreCase)) continue;
            }

            for (int col = 0; col < selectedColumns.Count; col++)
            {
                var value = row[selectedColumns[col]];
                sheet.Cells[rowIndex, col + 1].Value = value is DBNull ? null : value;
            }
            rowIndex++;
        }

        sheet.Cells[sheet.Dimension.Address].AutoFitColumns();
        ExportHelper.SaveExcel(package, filePath);
        return rowIndex - 2;
    }

    /// <summary>
    /// 엑셀 1행을 헤더로 읽어, 2행부터의 데이터를 "헤더 → 셀 텍스트" 딕셔너리 목록으로 반환합니다.
    /// 알 수 없는 헤더(관리 테이블에 없는 열)는 그대로 포함시키고, 호출 측에서 무시합니다.
    /// </summary>
    public static (List<string> Headers, List<Dictionary<string, string?>> Rows) Read(string filePath)
    {
        using var package = Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? CsvWorkbookReader.LoadAsPackage(filePath)
            : new ExcelPackage(new FileInfo(filePath));
        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
        if (worksheet?.Dimension == null) return ([], []);

        var headers = new List<string>();
        for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
        {
            var header = worksheet.Cells[1, col].Value?.ToString();
            headers.Add(header ?? string.Empty);
        }

        var rows = new List<Dictionary<string, string?>>();
        for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var hasAnyValue = false;
            for (int col = 1; col <= headers.Count; col++)
            {
                if (string.IsNullOrEmpty(headers[col - 1])) continue;

                var text = worksheet.Cells[row, col].Value?.ToString();
                values[headers[col - 1]] = text;
                if (!string.IsNullOrEmpty(text)) hasAnyValue = true;
            }
            if (hasAnyValue) rows.Add(values);
        }

        return (headers, rows);
    }

    /// <summary>엑셀 셀 텍스트를 DataTable 컬럼의 실제 타입 값으로 변환합니다. 실패하면 null/기본값입니다.</summary>
    public static object? ConvertValue(Type columnType, string? text)
    {
        if (string.IsNullOrEmpty(text)) return columnType == typeof(string) ? null : DBNull.Value;

        try
        {
            if (columnType == typeof(string)) return text;
            if (columnType == typeof(decimal)) return Convert.ToDecimal(text);
            if (columnType == typeof(int)) return Convert.ToInt32(text);
            if (columnType == typeof(DateTime)) return Convert.ToDateTime(text);
            return Convert.ChangeType(text, columnType);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return columnType == typeof(string) ? text : DBNull.Value;
        }
    }
}
