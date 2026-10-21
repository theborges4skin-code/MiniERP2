using System.Data;
using System.Globalization;

namespace MiniERP2.MFG.DataManagement;

/// <summary>
/// 엑셀에서 읽은 행을 화면의 DataTable에 스테이징한다(MiniERP2 데이터 관리 "엑셀 불러오기"와 같은 규칙):
/// 기존 행과 매칭되면 "중복"(덮어쓰기 선택 시 값 반영), 아니면 "신규" 추가. DB 반영은 [변경내역 저장]에서.
/// 매칭: ① Id ② Id가 비었거나 없으면 보조 키(코드/SKU). 참고용 열·Id는 덮어쓰지 않는다.
/// 값 변환 오류가 하나라도 있으면 아무것도 스테이징하지 않고 오류 목록을 돌려준다.
/// </summary>
public static class ManagedTableImporter
{
    public sealed class Plan
    {
        public List<Dictionary<string, string?>> NewRows { get; } = [];
        public List<(DataRow Existing, Dictionary<string, string?> Values)> Matches { get; } = [];
        public List<string> Errors { get; } = [];
        public List<string> UnknownHeaders { get; } = [];
    }

    public static Plan Analyze(ManagedTable adapter, DataTable table, List<string> headers, List<Dictionary<string, string?>> rows)
    {
        var plan = new Plan();
        plan.UnknownHeaders.AddRange(headers.Where(h => h.Length > 0 && !table.Columns.Contains(h)));
        var fallback = adapter.FallbackKeyColumns;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var existing = FindByKey(adapter, table, row) ?? FindByFallback(table, fallback, row);
            if (existing is not null) plan.Matches.Add((existing, row));
            else plan.NewRows.Add(row);

            // 변환 가능 여부 미리 검사(엑셀 행 번호 = i + 2).
            foreach (DataColumn col in table.Columns)
            {
                if (!row.TryGetValue(col.ColumnName, out var text) || IsSkipped(adapter, col.ColumnName)) continue;
                try { ManagedTableExcelIO.ConvertValue(col, text); }
                catch (InvalidDataException ex) { plan.Errors.Add($"{i + 2}행: {ex.Message}"); }
            }
        }
        if (!adapter.AllowInsert && plan.NewRows.Count > 0) plan.Errors.Add($"이 표는 새 행을 추가할 수 없는데, 기존 행과 맞지 않는 행이 {plan.NewRows.Count}개 있습니다.");
        return plan;
    }

    /// <summary>계획을 DataTable에 반영. 반환: (추가, 덮어쓴 행).</summary>
    public static (int Added, int Overwritten) Stage(ManagedTable adapter, DataTable table, Plan plan, bool overwrite)
    {
        foreach (var values in plan.NewRows)
        {
            var newRow = table.NewRow();
            foreach (DataColumn col in table.Columns)
            {
                if (IsSkipped(adapter, col.ColumnName) || col.AutoIncrement) continue;
                if (values.TryGetValue(col.ColumnName, out var text)) newRow[col] = ManagedTableExcelIO.ConvertValue(col, text);
            }
            table.Rows.Add(newRow);
        }

        var overwritten = 0;
        if (overwrite)
        {
            foreach (var (existing, values) in plan.Matches)
            {
                var changed = false;
                foreach (DataColumn col in table.Columns)
                {
                    if (IsSkipped(adapter, col.ColumnName) || adapter.KeyColumns.Contains(col.ColumnName)) continue;
                    if (!values.TryGetValue(col.ColumnName, out var text)) continue; // 파일에 없는 열은 그대로 둔다
                    var v = ManagedTableExcelIO.ConvertValue(col, text);
                    if (!SameValue(existing[col], v)) { existing[col] = v; changed = true; }
                }
                if (changed) overwritten++;
            }
        }
        return (plan.NewRows.Count, overwritten);
    }

    private static bool IsSkipped(ManagedTable adapter, string column) =>
        adapter.InfoColumns.Contains(column) || (column == ManagedTable.IdColumn && adapter.KeyColumns.SequenceEqual([ManagedTable.IdColumn]));

    private static DataRow? FindByKey(ManagedTable adapter, DataTable table, Dictionary<string, string?> row)
    {
        var keys = adapter.KeyColumns;
        var values = new object[keys.Length];
        for (var k = 0; k < keys.Length; k++)
        {
            if (!row.TryGetValue(keys[k], out var text) || string.IsNullOrWhiteSpace(text)) return null;
            var col = table.Columns[keys[k]]!;
            try { values[k] = ManagedTableExcelIO.ConvertValue(col, text); }
            catch (InvalidDataException) { return null; }
        }
        var found = table.Rows.Find(values.Length == 1 ? values[0] : values);
        return found is { RowState: not DataRowState.Deleted } ? found : null;
    }

    private static DataRow? FindByFallback(DataTable table, string[] fallback, Dictionary<string, string?> row)
    {
        if (fallback.Length == 0 || fallback.Any(f => !row.TryGetValue(f, out var v) || string.IsNullOrWhiteSpace(v))) return null;
        return table.Rows.Cast<DataRow>().FirstOrDefault(r => r.RowState != DataRowState.Deleted &&
            fallback.All(f => string.Equals(Convert.ToString(r[f], CultureInfo.InvariantCulture)?.Trim(), row[f]!.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    private static bool SameValue(object a, object b)
    {
        if (a is DBNull && b is DBNull) return true;
        if (a is double da && b is double db) return Math.Abs(da - db) < 1e-9;
        return Equals(a, b) || string.Equals(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
