using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// ⚠ 임시(실험용) 저장소 — <c>DocLineHistoryTable</c>은 기존 PriceQuoteTable/DocHistoryTable/
/// ChannelSkuPriceHistory와 완전히 독립적인 표다(문서이력_조회축_갭재검토_A.md rev.2 참고).
/// 채널×CSKU×기간 통합 조회 기능을 단독으로 검증하기 위한 것으로, 검증이 끝나면 실제 문서관리
/// 기능에 편입하거나 이 저장소 자체를 폐기한다.
/// </summary>
public class DocLineHistoryRepository
{
    private const string Cols =
        "Id, DocGroupKey, DocNo, DocType, ChannelCode, ChannelName, CskuCode, ItemNameSnap, Qty, " +
        "UnitPrice, SupplyAmount, Tax, Total, IssueDate, SourceRef, Note, CreatedAt";

    public void Add(DocLineHistory line)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DocLineHistoryTable
                (DocGroupKey, DocNo, DocType, ChannelCode, ChannelName, CskuCode, ItemNameSnap, Qty,
                 UnitPrice, SupplyAmount, Tax, Total, IssueDate, YearMonth, Quarter, SourceRef, Note, CreatedAt)
            VALUES
                ($docGroupKey, $docNo, $docType, $channelCode, $channelName, $cskuCode, $itemName, $qty,
                 $unitPrice, $supplyAmount, $tax, $total, $issueDate, $yearMonth, $quarter, $sourceRef, $note, $createdAt)
            """;
        Bind(command, line);
        command.ExecuteNonQuery();
    }

    /// <summary>여러 줄(보통 문서 1건의 라인들)을 한 번에 저장한다.</summary>
    public void AddRange(IEnumerable<DocLineHistory> lines)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var line in lines)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO DocLineHistoryTable
                        (DocGroupKey, DocNo, DocType, ChannelCode, ChannelName, CskuCode, ItemNameSnap, Qty,
                         UnitPrice, SupplyAmount, Tax, Total, IssueDate, YearMonth, Quarter, SourceRef, Note, CreatedAt)
                    VALUES
                        ($docGroupKey, $docNo, $docType, $channelCode, $channelName, $cskuCode, $itemName, $qty,
                         $unitPrice, $supplyAmount, $tax, $total, $issueDate, $yearMonth, $quarter, $sourceRef, $note, $createdAt)
                    """;
                Bind(command, line);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 조회축 핵심 쿼리 — 채널/CSKU/문서유형/기간(년월 or 분기)으로 필터링한다. 모든 인자는
    /// 선택이다(null/빈 문자열이면 그 조건은 무시). CskuCode에 빈 문자열을 명시적으로 넘기면
    /// "미매핑" 버킷만 조회한다(cskuCodeIsUnmappedOnly).
    /// </summary>
    public List<DocLineHistory> Query(
        string? channelCode = null,
        string? cskuCode = null,
        bool cskuCodeIsUnmappedOnly = false,
        DocLineHistoryType? docType = null,
        string? yearMonth = null,
        string? quarter = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (!string.IsNullOrEmpty(channelCode)) where.Add("ChannelCode = $channelCode");
        if (cskuCodeIsUnmappedOnly) where.Add("CskuCode = ''");
        else if (!string.IsNullOrEmpty(cskuCode)) where.Add("CskuCode = $cskuCode");
        if (docType.HasValue) where.Add("DocType = $docType");
        if (!string.IsNullOrEmpty(yearMonth)) where.Add("YearMonth = $yearMonth");
        if (!string.IsNullOrEmpty(quarter)) where.Add("Quarter = $quarter");
        if (from.HasValue) where.Add("date(IssueDate) >= date($from)");
        if (to.HasValue) where.Add("date(IssueDate) <= date($to)");

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        command.CommandText = $"SELECT {Cols} FROM DocLineHistoryTable {whereClause} ORDER BY IssueDate DESC, Id DESC";

        if (!string.IsNullOrEmpty(channelCode)) command.Parameters.AddWithValue("$channelCode", channelCode);
        if (!cskuCodeIsUnmappedOnly && !string.IsNullOrEmpty(cskuCode)) command.Parameters.AddWithValue("$cskuCode", cskuCode);
        if (docType.HasValue) command.Parameters.AddWithValue("$docType", docType.Value.ToString());
        if (!string.IsNullOrEmpty(yearMonth)) command.Parameters.AddWithValue("$yearMonth", yearMonth);
        if (!string.IsNullOrEmpty(quarter)) command.Parameters.AddWithValue("$quarter", quarter);
        if (from.HasValue) command.Parameters.AddWithValue("$from", from.Value.ToString("yyyy-MM-dd"));
        if (to.HasValue) command.Parameters.AddWithValue("$to", to.Value.ToString("yyyy-MM-dd"));

        var list = new List<DocLineHistory>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(Read(reader));
        return list;
    }

    /// <summary>
    /// CSKU 1건 = 1행 요약(문서관리 메인창 레벨1 그리드). 데이터량이 ERP 내부용 규모라 SQL
    /// 윈도우함수 대신 <see cref="Query"/>로 받은 뒤 메모리에서 채널+CSKU로 묶는다 — "최초/최근
    /// 단가"처럼 그룹의 특정 행(가장 이르거나 늦은 IssueDate) 값을 뽑는 로직이 SQL보다 훨씬
    /// 단순해진다.
    /// </summary>
    public List<DocLineHistoryCskuSummary> GetCskuSummary(string? channelCode = null, DocLineHistoryType? docType = null)
    {
        var lines = Query(channelCode: channelCode, docType: docType);

        return lines
            .GroupBy(l => (l.ChannelCode, l.CskuCode))
            .Select(g =>
            {
                var ordered = g.OrderBy(l => l.IssueDate).ThenBy(l => l.Id).ToList();
                var first = ordered[0];
                var last = ordered[^1];
                return new DocLineHistoryCskuSummary
                {
                    ChannelCode = g.Key.ChannelCode,
                    ChannelName = last.ChannelName,
                    CskuCode = g.Key.CskuCode,
                    LatestItemNameSnap = last.ItemNameSnap,
                    DocCount = ordered.Count,
                    FirstUnitPrice = first.UnitPrice,
                    LastUnitPrice = last.UnitPrice,
                    FirstIssueDate = first.IssueDate,
                    LastIssueDate = last.IssueDate,
                };
            })
            .OrderByDescending(s => s.LastIssueDate)
            .ToList();
    }

    /// <summary>
    /// 채널별 실제 발행 문서건수(줄 수가 아니라 문서 단위)를 센다. 전화주문 등 1회성 거래처를
    /// 실제 채널로 등록하면서 채널 목록이 계속 늘어나는 문제(사용자 상담)에 대응해, 화면에서
    /// "문서 1건뿐인 채널"을 걸러 보여줄 때 쓴다. <see cref="DocLineHistory.DocGroupKey"/>가
    /// 비어있는 줄은 그 줄 혼자 문서 1건으로 취급한다(모델 문서화 참고) — 그래서 단순히
    /// COUNT(DISTINCT DocGroupKey)로 세면 빈 문자열끼리 전부 한 건으로 뭉쳐 잘못 집계된다.
    /// </summary>
    public Dictionary<string, int> GetDocCountByChannel()
    {
        return Query()
            .GroupBy(l => l.ChannelCode)
            .ToDictionary(
                g => g.Key,
                g => g.Where(l => !string.IsNullOrEmpty(l.DocGroupKey)).Select(l => l.DocGroupKey).Distinct().Count()
                    + g.Count(l => string.IsNullOrEmpty(l.DocGroupKey)));
    }

    /// <summary>
    /// 이 임시 시스템 전용 견적 문서번호를 채번한다("TQ{yyMMdd}{2자리seq}") — 실제 서비스 테이블인
    /// PriceQuoteTable.QuoteNo("Q{yyMMdd}{seq}")와 접두사를 다르게 둬서 두 체계가 섞이지 않게 한다.
    /// 한 문서에 속한 줄 여러 건이 같은 DocNo를 공유하므로, 줄 수가 아니라 DISTINCT DocNo로 센다.
    /// </summary>
    public string GenerateNextTempQuoteNo(DateTime date)
    {
        var datePart = date.ToString("yyMMdd");
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(DISTINCT DocNo) FROM DocLineHistoryTable WHERE DocNo LIKE $prefix";
        command.Parameters.AddWithValue("$prefix", $"TQ{datePart}%");
        var count = Convert.ToInt32(command.ExecuteScalar());
        return $"TQ{datePart}{count + 1:00}";
    }

    public void DeleteAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DocLineHistoryTable";
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, DocLineHistory line)
    {
        command.Parameters.AddWithValue("$docGroupKey", line.DocGroupKey);
        command.Parameters.AddWithValue("$docNo", line.DocNo);
        command.Parameters.AddWithValue("$docType", line.DocType.ToString());
        command.Parameters.AddWithValue("$channelCode", line.ChannelCode);
        command.Parameters.AddWithValue("$channelName", line.ChannelName);
        command.Parameters.AddWithValue("$cskuCode", line.CskuCode);
        command.Parameters.AddWithValue("$itemName", line.ItemNameSnap);
        command.Parameters.AddWithValue("$qty", (double)line.Qty);
        command.Parameters.AddWithValue("$unitPrice", (double)line.UnitPrice);
        command.Parameters.AddWithValue("$supplyAmount", (double)line.SupplyAmount);
        command.Parameters.AddWithValue("$tax", (double)line.Tax);
        command.Parameters.AddWithValue("$total", (double)line.Total);
        command.Parameters.AddWithValue("$issueDate", line.IssueDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$yearMonth", line.YearMonth);
        command.Parameters.AddWithValue("$quarter", line.Quarter);
        command.Parameters.AddWithValue("$sourceRef", line.SourceRef);
        command.Parameters.AddWithValue("$note", line.Note);
        command.Parameters.AddWithValue("$createdAt", line.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private static DocLineHistory Read(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        DocGroupKey = r.GetString(1),
        DocNo = r.GetString(2),
        DocType = Enum.Parse<DocLineHistoryType>(r.GetString(3)),
        ChannelCode = r.GetString(4),
        ChannelName = r.GetString(5),
        CskuCode = r.GetString(6),
        ItemNameSnap = r.GetString(7),
        Qty = (decimal)r.GetDouble(8),
        UnitPrice = (decimal)r.GetDouble(9),
        SupplyAmount = (decimal)r.GetDouble(10),
        Tax = (decimal)r.GetDouble(11),
        Total = (decimal)r.GetDouble(12),
        IssueDate = DateTime.Parse(r.GetString(13)),
        SourceRef = r.GetString(14),
        Note = r.GetString(15),
        CreatedAt = DateTime.Parse(r.GetString(16)),
    };
}
