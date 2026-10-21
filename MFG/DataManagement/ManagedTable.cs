using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;
using MiniERP2.MFG.Data;

namespace MiniERP2.MFG.DataManagement;

/// <summary>
/// [데이터 관리] 탭이 다루는 mfgdb 테이블 하나(MiniERP2 DataManagement/IManagedDataTable과 같은 구조).
/// 화면은 DataTable로 조회·편집·스테이징하고, 실제 반영은 <see cref="ManagedTableChangeApplier"/>가 RowState에 따라
/// Insert/Update/Delete를 한 트랜잭션으로 호출한다.
///
/// MiniERP2와 다른 점: 행 식별 키를 "Id"로 둔다. 코드(원료 코드 등)를 파일에서 고쳐도 같은 행으로 매칭되게 하기 위함이다
/// (MiniERP2처럼 코드를 키로 쓰면 코드를 바꾼 행이 신규로 들어가 버린다). 파일에서 Id를 비워 두면 <see cref="FallbackKeyColumns"/>
/// (예: 코드)로 기존 행을 찾고, 그래도 없으면 신규로 본다.
/// </summary>
public abstract class ManagedTable
{
    public const string IdColumn = "Id";

    public abstract string DisplayName { get; }

    /// <summary>화면 상단 안내 문구(무엇을 고칠 수 있는지, 주의점).</summary>
    public virtual string Description => string.Empty;

    /// <summary>행 식별 키(기본 "Id").</summary>
    public virtual string[] KeyColumns => [IdColumn];

    /// <summary>파일에 Id가 없을 때 기존 행을 찾는 보조 키(예: ["코드"]).</summary>
    public virtual string[] FallbackKeyColumns => [];

    /// <summary>참고용 표시 열(파일에서 고쳐도 무시됨).</summary>
    public virtual string[] InfoColumns => [];

    public virtual bool AllowInsert => true;
    public virtual bool AllowDelete => true;

    protected abstract void DefineColumns(DataTable table);

    protected abstract IEnumerable<object?[]> LoadRows(SqliteConnection c);

    public abstract void Insert(DataRow row, SqliteConnection c, SqliteTransaction tx);

    public abstract void Update(DataRow row, SqliteConnection c, SqliteTransaction tx);

    public abstract void Delete(DataRow row, SqliteConnection c, SqliteTransaction tx);

    public DataTable LoadCurrent()
    {
        var table = new DataTable(DisplayName);
        DefineColumns(table);
        if (table.Columns.Contains(IdColumn) && KeyColumns.SequenceEqual([IdColumn]))
        {
            // 화면/파일에서 새로 추가한 행은 음수 임시 Id를 받는다(저장 시 DB가 실제 Id를 부여).
            var id = table.Columns[IdColumn]!;
            id.AutoIncrement = true;
            id.AutoIncrementSeed = -1;
            id.AutoIncrementStep = -1;
        }
        table.PrimaryKey = KeyColumns.Select(k => table.Columns[k]!).ToArray();
        using var c = MfgDb.Open();
        foreach (var values in LoadRows(c)) table.Rows.Add(values);
        table.AcceptChanges();
        return table;
    }

    // ---- 행 값 읽기 헬퍼 ----

    protected static string S(DataRow r, string col) => r[col] is DBNull or null ? string.Empty : Convert.ToString(r[col], CultureInfo.InvariantCulture)!.Trim();

    protected static string? NS(DataRow r, string col) => S(r, col) is { Length: > 0 } s ? s : null;

    protected static double? ND(DataRow r, string col) => r[col] is DBNull or null ? null : Convert.ToDouble(r[col], CultureInfo.InvariantCulture);

    protected static double D(DataRow r, string col, double fallback = 0) => ND(r, col) ?? fallback;

    protected static long? NL(DataRow r, string col) => r[col] is DBNull or null ? null : Convert.ToInt64(r[col], CultureInfo.InvariantCulture);

    protected static bool B(DataRow r, string col, bool fallback = false) => r[col] is DBNull or null ? fallback : Convert.ToBoolean(r[col], CultureInfo.InvariantCulture);

    protected static long OriginalId(DataRow r) => Convert.ToInt64(r[IdColumn, DataRowVersion.Original], CultureInfo.InvariantCulture);

    protected static long CurrentId(DataRow r) => Convert.ToInt64(r[IdColumn], CultureInfo.InvariantCulture);

    protected static string Required(DataRow r, string col)
    {
        var v = S(r, col);
        if (v.Length == 0) throw new InvalidDataException($"'{col}' 값이 비어 있습니다.");
        return v;
    }

    /// <summary>코드 → Id. 없으면 어느 열의 어떤 값인지 알려 주는 오류.</summary>
    protected static long IdByCode(SqliteConnection c, SqliteTransaction tx, string table, string code, string what)
    {
        using var cmd = MfgDb.Cmd(c, $"SELECT Id FROM {table} WHERE Code=$c", tx, ("$c", code));
        return cmd.ExecuteScalar() is { } v and not DBNull
            ? Convert.ToInt64(v, CultureInfo.InvariantCulture)
            : throw new InvalidDataException($"{what} '{code}'을(를) 찾을 수 없습니다.");
    }

    protected static long? OptionalIdByCode(SqliteConnection c, SqliteTransaction tx, string table, string? code, string what) =>
        string.IsNullOrWhiteSpace(code) ? null : IdByCode(c, tx, table, code.Trim(), what);

    protected static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>변경내역 저장 결과 건수.</summary>
public record ApplyResult(int Inserted, int Updated, int Deleted)
{
    public int Total => Inserted + Updated + Deleted;
}

/// <summary>
/// 스테이징된 DataTable 변경분을 한 트랜잭션으로 mfgdb에 반영한다(중간에 실패하면 전부 취소 — MiniERP2보다 한 단계 안전).
/// 실패하면 몇 번째 행인지 알려 주는 예외를 던진다.
/// </summary>
public static class ManagedTableChangeApplier
{
    public static ApplyResult Apply(ManagedTable adapter, DataTable table)
    {
        var changes = table.GetChanges();
        if (changes is null) return new ApplyResult(0, 0, 0);

        int inserted = 0, updated = 0, deleted = 0;
        using var c = MfgDb.Open();
        using var tx = c.BeginTransaction();
        // 삭제를 먼저 처리해야 같은 코드로 삭제 후 새로 넣는 경우 UNIQUE 충돌이 나지 않는다.
        foreach (var row in changes.Rows.Cast<DataRow>().OrderBy(r => r.RowState == DataRowState.Deleted ? 0 : 1))
        {
            try
            {
                switch (row.RowState)
                {
                    case DataRowState.Added:
                        if (!adapter.AllowInsert) throw new InvalidOperationException("이 표는 새 행을 추가할 수 없습니다.");
                        adapter.Insert(row, c, tx);
                        inserted++;
                        break;
                    case DataRowState.Deleted:
                        if (!adapter.AllowDelete) throw new InvalidOperationException("이 표는 행을 삭제할 수 없습니다.");
                        adapter.Delete(row, c, tx);
                        deleted++;
                        break;
                    case DataRowState.Modified:
                        adapter.Update(row, c, tx);
                        updated++;
                        break;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{Describe(row)} 반영 실패: {Friendly(ex)}\n(이번 저장은 전부 취소되었습니다 — DB는 저장 전 그대로입니다.)", ex);
            }
        }
        tx.Commit();
        table.AcceptChanges();
        return new ApplyResult(inserted, updated, deleted);
    }

    private static string Describe(DataRow row)
    {
        var version = row.RowState == DataRowState.Deleted ? DataRowVersion.Original : DataRowVersion.Current;
        var parts = row.Table.Columns.Cast<DataColumn>().Take(3).Select(col => $"{col.ColumnName}={row[col, version]}");
        var kind = row.RowState switch { DataRowState.Added => "신규", DataRowState.Deleted => "삭제", _ => "수정" };
        return $"[{kind}] {string.Join(", ", parts)}";
    }

    private static string Friendly(Exception ex) => ex switch
    {
        SqliteException { SqliteErrorCode: 19 } s when s.Message.Contains("UNIQUE") => "같은 코드/SKU가 이미 있습니다(중복).",
        SqliteException { SqliteErrorCode: 19 } s when s.Message.Contains("FOREIGN KEY") => "다른 표에서 쓰고 있어 지우거나 바꿀 수 없습니다(예: 배합에 쓰인 원료).",
        _ => ex.Message,
    };
}
