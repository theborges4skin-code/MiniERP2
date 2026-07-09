using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 레거시 거래명세표 마이그레이션으로 적재되는 과거 발행건(DocStatementTable/DocStatementLineTable) 저장소.
/// DocsForm이 새로 작성하는 문서(TradeStatementDoc)와는 무관한, 읽기 전용에 가까운 이력 데이터다.
/// </summary>
public class DocStatementRepository
{
    private const string HeaderCols =
        "Id, PartyId, IssueDate, IssueYearMonth, TotalSupply, TotalTax, TotalAmount, TotalQty, " +
        "CarryoverBalance, ReconcileNote, TemplateSignature, StatusFlags, SourceFileName, SourceSheetName, CreatedAt";

    private const string LineCols =
        "Id, StatementId, RowNo, LineDate, ItemName, Spec, Qty, UnitPrice, UnitPriceVatIncluded, SupplyAmount, Tax, Total, Note";

    /// <summary>거래처(선택)·발행일 범위(선택)로 발행건 헤더를 조회한다(조회/내보내기 화면용).</summary>
    public List<DocStatement> GetFiltered(int? partyId, DateTime? from, DateTime? to)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();

        var where = new List<string>();
        if (partyId.HasValue) where.Add("PartyId = $partyId");
        if (from.HasValue) where.Add("IssueDate >= $from");
        if (to.HasValue) where.Add("IssueDate <= $to");
        string whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        cmd.CommandText = $"SELECT {HeaderCols} FROM DocStatementTable {whereClause} ORDER BY IssueDate, Id";
        if (partyId.HasValue) cmd.Parameters.AddWithValue("$partyId", partyId.Value);
        if (from.HasValue) cmd.Parameters.AddWithValue("$from", from.Value.ToString("yyyy-MM-dd"));
        if (to.HasValue) cmd.Parameters.AddWithValue("$to", to.Value.ToString("yyyy-MM-dd"));

        using var reader = cmd.ExecuteReader();
        var list = new List<DocStatement>();
        while (reader.Read()) list.Add(MapHeader(reader));
        return list;
    }

    /// <summary>주어진 발행건의 라인을 행순번(RowNo) 순으로 조회한다(엑셀 내보내기용).</summary>
    public List<DocStatementLine> GetLines(int statementId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {LineCols} FROM DocStatementLineTable WHERE StatementId = $id ORDER BY RowNo";
        cmd.Parameters.AddWithValue("$id", statementId);
        using var reader = cmd.ExecuteReader();
        var list = new List<DocStatementLine>();
        while (reader.Read()) list.Add(MapLine(reader));
        return list;
    }

    /// <summary>
    /// 이 (파일명, 시트명)으로 이미 커밋된 발행건이 있으면 그 거래처ID를 반환한다(없으면 null).
    /// 등록번호 없는 익명 거래처를 재커밋할 때, 매번 새 고아 거래처를 만들지 않고 같은 발행건이
    /// 예전에 연결했던 거래처를 그대로 재사용하기 위해 쓴다(LegacyStatementCommitService 참고).
    /// </summary>
    public int? FindPartyIdBySource(string sourceFileName, string sourceSheetName)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PartyId FROM DocStatementTable WHERE SourceFileName = $fn AND SourceSheetName = $sn LIMIT 1";
        cmd.Parameters.AddWithValue("$fn", sourceFileName);
        cmd.Parameters.AddWithValue("$sn", sourceSheetName);
        var result = cmd.ExecuteScalar();
        return result == null ? null : (int)(long)result;
    }

    public List<DocStatement> GetAll()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {HeaderCols} FROM DocStatementTable ORDER BY Id";
        using var reader = cmd.ExecuteReader();
        var list = new List<DocStatement>();
        while (reader.Read()) list.Add(MapHeader(reader));
        return list;
    }

    /// <summary>
    /// UNIQUE(SourceFileName, SourceSheetName) 기준 재실행 시 기본 replace 정책(스펙 §3.8) — 기존 헤더·라인이
    /// 있으면 지우고 다시 넣는다. 헤더+라인을 한 트랜잭션으로 묶어 부분 반영을 방지한다.
    /// </summary>
    public void Upsert(DocStatement statement)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            int? existingId = FindIdBySource(conn, tx, statement.SourceFileName, statement.SourceSheetName);
            if (existingId.HasValue)
            {
                DeleteLines(conn, tx, existingId.Value);
                DeleteHeader(conn, tx, existingId.Value);
            }

            statement.CreatedAt ??= DateTime.Now;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO DocStatementTable
                        (PartyId, IssueDate, IssueYearMonth, TotalSupply, TotalTax, TotalAmount, TotalQty,
                         CarryoverBalance, ReconcileNote, TemplateSignature, StatusFlags, SourceFileName, SourceSheetName, CreatedAt)
                    VALUES
                        ($partyId, $issueDate, $ym, $supply, $tax, $amount, $qty,
                         $carry, $note, $sig, $flags, $fn, $sn, $created)
                    """;
                BindHeader(cmd, statement);
                cmd.ExecuteNonQuery();
            }

            using (var idCmd = conn.CreateCommand())
            {
                idCmd.Transaction = tx;
                idCmd.CommandText = "SELECT last_insert_rowid()";
                statement.Id = (int)(long)idCmd.ExecuteScalar()!;
            }

            int rowNo = 1;
            foreach (var line in statement.Lines)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO DocStatementLineTable
                        (StatementId, RowNo, LineDate, ItemName, Spec, Qty, UnitPrice, UnitPriceVatIncluded, SupplyAmount, Tax, Total, Note)
                    VALUES
                        ($stId, $rowNo, $lineDate, $item, $spec, $qty, $unitPrice, $vatInc, $supply, $tax, $total, $note)
                    """;
                cmd.Parameters.AddWithValue("$stId", statement.Id);
                cmd.Parameters.AddWithValue("$rowNo", rowNo++);
                cmd.Parameters.AddWithValue("$lineDate", (object?)line.LineDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$item", line.ItemName);
                cmd.Parameters.AddWithValue("$spec", line.Spec);
                cmd.Parameters.AddWithValue("$qty", line.Qty);
                cmd.Parameters.AddWithValue("$unitPrice", line.UnitPrice);
                cmd.Parameters.AddWithValue("$vatInc", line.UnitPriceVatIncluded ? 1 : 0);
                cmd.Parameters.AddWithValue("$supply", line.SupplyAmount);
                cmd.Parameters.AddWithValue("$tax", line.Tax);
                cmd.Parameters.AddWithValue("$total", line.Total);
                cmd.Parameters.AddWithValue("$note", line.Note);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static int? FindIdBySource(SqliteConnection conn, SqliteTransaction tx, string fileName, string sheetName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Id FROM DocStatementTable WHERE SourceFileName = $fn AND SourceSheetName = $sn LIMIT 1";
        cmd.Parameters.AddWithValue("$fn", fileName);
        cmd.Parameters.AddWithValue("$sn", sheetName);
        var result = cmd.ExecuteScalar();
        return result == null ? null : (int)(long)result;
    }

    private static void DeleteLines(SqliteConnection conn, SqliteTransaction tx, int statementId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM DocStatementLineTable WHERE StatementId = $id";
        cmd.Parameters.AddWithValue("$id", statementId);
        cmd.ExecuteNonQuery();
    }

    private static void DeleteHeader(SqliteConnection conn, SqliteTransaction tx, int statementId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM DocStatementTable WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", statementId);
        cmd.ExecuteNonQuery();
    }

    private static void BindHeader(SqliteCommand cmd, DocStatement s)
    {
        cmd.Parameters.AddWithValue("$partyId", s.PartyId);
        cmd.Parameters.AddWithValue("$issueDate", (object?)s.IssueDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ym", s.IssueYearMonth);
        cmd.Parameters.AddWithValue("$supply", s.TotalSupply);
        cmd.Parameters.AddWithValue("$tax", s.TotalTax);
        cmd.Parameters.AddWithValue("$amount", s.TotalAmount);
        cmd.Parameters.AddWithValue("$qty", s.TotalQty);
        cmd.Parameters.AddWithValue("$carry", s.CarryoverBalance);
        cmd.Parameters.AddWithValue("$note", s.ReconcileNote);
        cmd.Parameters.AddWithValue("$sig", s.TemplateSignature);
        cmd.Parameters.AddWithValue("$flags", s.StatusFlags);
        cmd.Parameters.AddWithValue("$fn", s.SourceFileName);
        cmd.Parameters.AddWithValue("$sn", s.SourceSheetName);
        cmd.Parameters.AddWithValue("$created", s.CreatedAt!.Value.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private static DocStatement MapHeader(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        PartyId = r.GetInt32(1),
        IssueDate = r.IsDBNull(2) ? null : DateTime.Parse(r.GetString(2)),
        IssueYearMonth = r.IsDBNull(3) ? "" : r.GetString(3),
        TotalSupply = r.GetDecimal(4),
        TotalTax = r.GetDecimal(5),
        TotalAmount = r.GetDecimal(6),
        TotalQty = r.GetDecimal(7),
        CarryoverBalance = r.GetDecimal(8),
        ReconcileNote = r.IsDBNull(9) ? "" : r.GetString(9),
        TemplateSignature = r.IsDBNull(10) ? "" : r.GetString(10),
        StatusFlags = r.IsDBNull(11) ? "" : r.GetString(11),
        SourceFileName = r.IsDBNull(12) ? "" : r.GetString(12),
        SourceSheetName = r.IsDBNull(13) ? "" : r.GetString(13),
        CreatedAt = r.IsDBNull(14) || string.IsNullOrWhiteSpace(r.GetString(14)) ? null : DateTime.Parse(r.GetString(14)),
    };

    private static DocStatementLine MapLine(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        StatementId = r.GetInt32(1),
        RowNo = r.GetInt32(2),
        LineDate = r.IsDBNull(3) ? null : DateTime.Parse(r.GetString(3)),
        ItemName = r.IsDBNull(4) ? "" : r.GetString(4),
        Spec = r.IsDBNull(5) ? "" : r.GetString(5),
        Qty = r.GetDecimal(6),
        UnitPrice = r.GetDecimal(7),
        UnitPriceVatIncluded = r.GetInt32(8) == 1,
        SupplyAmount = r.GetDecimal(9),
        Tax = r.GetDecimal(10),
        Total = r.GetDecimal(11),
        Note = r.IsDBNull(12) ? "" : r.GetString(12),
    };
}
