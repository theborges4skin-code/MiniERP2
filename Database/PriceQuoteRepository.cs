using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 견적/가격 기록 관리(견적기록관리_개발기획서_확정본.md §3.1~3.2, Step 3)의 PriceQuoteTable/
/// PriceQuoteLineTable 저장소. 헤더는 대리키(Id AUTOINCREMENT)라 FboOrderRepository(자연키 Upsert)와
/// 달리 신규/기존을 Id 유무로 구분해서 처리한다. 라인은 매번 전체 delete-then-reinsert로 교체한다
/// (그리드에서 자유롭게 라인을 추가·삭제한 뒤 한 번에 저장하는 화면 흐름과 맞음 — FboOrderRepository/
/// DocStatementRepository와 동일 패턴).
/// </summary>
public class PriceQuoteRepository
{
    private const string HeaderCols =
        "Id, QuoteNo, ChannelCode, PriceKind, QuoteFormType, Origin, Title, QuoteDate, EffectiveFrom, " +
        "EffectiveTo, AutoApply, Status, DeliveryMethod, DeliveredAt, DeliveredTo, Note, PriceBasis, " +
        "RootQuoteId, RevisionNo, SupersededBy, RevisionReason, CreatedAt, UpdatedAt";

    private const string LineCols =
        "Id, QuoteId, RowNo, CskuCode, Msku, ItemNameSnap, Spec, Unit, Qty, OldPrice, NewPrice, " +
        "SupplyAmount, Tax, Total, ChangeReason, Note, IsApplied, PromotedFrom";

    /// <summary>해당 날짜의 다음 견적번호(Q{yyMMdd}{2자리seq})를 계산한다(D2).</summary>
    public string GenerateNextQuoteNo(DateTime date)
    {
        var datePart = date.ToString("yyMMdd");
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PriceQuoteTable WHERE QuoteNo LIKE $prefix";
        command.Parameters.AddWithValue("$prefix", $"Q{datePart}%");
        var count = Convert.ToInt32(command.ExecuteScalar());
        return $"Q{datePart}{count + 1:00}";
    }

    /// <summary>
    /// 견적 헤더+라인을 저장한다. quote.Id가 0이면 신규 삽입(채번된 Id를 quote.Id에 채워 반환),
    /// 아니면 해당 Id를 그대로 갱신한다. 라인은 항상 전체 교체(delete-then-reinsert).
    /// </summary>
    public int SaveQuote(PriceQuote quote, List<PriceQuoteLine> lines)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var now = DateTime.Now;
            quote.UpdatedAt = now;
            quote.CreatedAt ??= now;

            if (quote.Id == 0)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = """
                    INSERT INTO PriceQuoteTable
                        (QuoteNo, ChannelCode, PriceKind, QuoteFormType, Origin, Title, QuoteDate, EffectiveFrom,
                         EffectiveTo, AutoApply, Status, DeliveryMethod, DeliveredAt, DeliveredTo, Note, PriceBasis,
                         RootQuoteId, RevisionNo, SupersededBy, RevisionReason, CreatedAt, UpdatedAt)
                    VALUES
                        ($quoteNo, $channelCode, $priceKind, $formType, $origin, $title, $quoteDate, $effFrom,
                         $effTo, $autoApply, $status, $deliveryMethod, $deliveredAt, $deliveredTo, $note, $priceBasis,
                         $rootId, $revNo, $supersededBy, $revReason, $createdAt, $updatedAt)
                    """;
                BindHeader(insertCommand, quote);
                insertCommand.ExecuteNonQuery();

                using var idCommand = connection.CreateCommand();
                idCommand.Transaction = transaction;
                idCommand.CommandText = "SELECT last_insert_rowid()";
                quote.Id = (int)(long)idCommand.ExecuteScalar()!;
            }
            else
            {
                using var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = """
                    UPDATE PriceQuoteTable SET
                        QuoteNo = $quoteNo, ChannelCode = $channelCode, PriceKind = $priceKind,
                        QuoteFormType = $formType, Origin = $origin, Title = $title, QuoteDate = $quoteDate,
                        EffectiveFrom = $effFrom, EffectiveTo = $effTo, AutoApply = $autoApply, Status = $status,
                        DeliveryMethod = $deliveryMethod, DeliveredAt = $deliveredAt, DeliveredTo = $deliveredTo,
                        Note = $note, PriceBasis = $priceBasis, RootQuoteId = $rootId, RevisionNo = $revNo,
                        SupersededBy = $supersededBy, RevisionReason = $revReason, UpdatedAt = $updatedAt
                    WHERE Id = $id
                    """;
                BindHeader(updateCommand, quote);
                updateCommand.Parameters.AddWithValue("$id", quote.Id);
                updateCommand.ExecuteNonQuery();
            }

            using (var deleteLinesCommand = connection.CreateCommand())
            {
                deleteLinesCommand.Transaction = transaction;
                deleteLinesCommand.CommandText = "DELETE FROM PriceQuoteLineTable WHERE QuoteId = $quoteId";
                deleteLinesCommand.Parameters.AddWithValue("$quoteId", quote.Id);
                deleteLinesCommand.ExecuteNonQuery();
            }

            var rowNo = 1;
            foreach (var line in lines)
            {
                line.QuoteId = quote.Id;
                line.RowNo = rowNo++;

                using var lineCommand = connection.CreateCommand();
                lineCommand.Transaction = transaction;
                lineCommand.CommandText = """
                    INSERT INTO PriceQuoteLineTable
                        (QuoteId, RowNo, CskuCode, Msku, ItemNameSnap, Spec, Unit, Qty, OldPrice, NewPrice,
                         SupplyAmount, Tax, Total, ChangeReason, Note, IsApplied, PromotedFrom)
                    VALUES
                        ($quoteId, $rowNo, $cskuCode, $msku, $itemName, $spec, $unit, $qty, $oldPrice, $newPrice,
                         $supplyAmount, $tax, $total, $changeReason, $note, $isApplied, $promotedFrom)
                    """;
                BindLine(lineCommand, line);
                lineCommand.ExecuteNonQuery();
            }

            transaction.Commit();
            return quote.Id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public (PriceQuote? Quote, List<PriceQuoteLine> Lines) GetQuote(int id)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();

        PriceQuote? quote = null;
        using (var headerCommand = connection.CreateCommand())
        {
            headerCommand.CommandText = $"SELECT {HeaderCols} FROM PriceQuoteTable WHERE Id = $id";
            headerCommand.Parameters.AddWithValue("$id", id);
            using var reader = headerCommand.ExecuteReader();
            if (reader.Read()) quote = ReadHeader(reader);
        }

        var lines = new List<PriceQuoteLine>();
        using (var lineCommand = connection.CreateCommand())
        {
            lineCommand.CommandText = $"SELECT {LineCols} FROM PriceQuoteLineTable WHERE QuoteId = $id ORDER BY RowNo";
            lineCommand.Parameters.AddWithValue("$id", id);
            using var reader = lineCommand.ExecuteReader();
            while (reader.Read()) lines.Add(ReadLine(reader));
        }

        return (quote, lines);
    }

    /// <summary>견적 목록 조회(화면용). 채널/구분(납품·매입)/최신본만 필터는 전부 선택 인자다.</summary>
    public List<PriceQuote> GetAll(string? channelCode = null, string? priceKind = null, bool latestOnly = false)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();

        var where = new List<string>();
        if (!string.IsNullOrEmpty(channelCode)) where.Add("ChannelCode = $channelCode");
        if (!string.IsNullOrEmpty(priceKind)) where.Add("PriceKind = $priceKind");
        // 최신본 판정은 컬럼이 아니라 SupersededBy IS NULL로 한다(§3.1).
        if (latestOnly) where.Add("SupersededBy IS NULL");
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        command.CommandText = $"SELECT {HeaderCols} FROM PriceQuoteTable {whereClause} ORDER BY EffectiveFrom DESC, Id DESC";
        if (!string.IsNullOrEmpty(channelCode)) command.Parameters.AddWithValue("$channelCode", channelCode);
        if (!string.IsNullOrEmpty(priceKind)) command.Parameters.AddWithValue("$priceKind", priceKind);

        var list = new List<PriceQuote>();
        using var readerAll = command.ExecuteReader();
        while (readerAll.Read()) list.Add(ReadHeader(readerAll));
        return list;
    }

    /// <summary>견적 1건(헤더+라인)을 통째로 삭제한다. 출고 이력이 있으면 삭제 금지해야 한다는
    /// 규칙(D4)은 호출 측이 <see cref="HasOutboundHistory"/>로 먼저 확인할 책임이다 — 여기서는
    /// 무조건 삭제한다(FboOrderRepository.DeleteOrder와 동일한 책임 분리).</summary>
    public void Delete(int id)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var lineCommand = connection.CreateCommand())
            {
                lineCommand.Transaction = transaction;
                lineCommand.CommandText = "DELETE FROM PriceQuoteLineTable WHERE QuoteId = $id";
                lineCommand.Parameters.AddWithValue("$id", id);
                lineCommand.ExecuteNonQuery();
            }
            using (var headerCommand = connection.CreateCommand())
            {
                headerCommand.Transaction = transaction;
                headerCommand.CommandText = "DELETE FROM PriceQuoteTable WHERE Id = $id";
                headerCommand.Parameters.AddWithValue("$id", id);
                headerCommand.ExecuteNonQuery();
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
    /// 이 견적 라인(채널+CSKU, 적용기간)의 가격으로 실제 출고된 이력이 있는지 판정한다(D4/§4.3).
    /// 1건이라도 있으면 그 라인은 자유 수정·삭제가 금지되고 개정 견적으로만 바꿀 수 있다.
    /// CskuCode가 비어있는 과거분 출고 이력은(G9, 채널+Msku로 fallback) 모호하면 "있음"으로
    /// 보수적으로 판단한다(§4.3 — 잘못 지워지는 것보다 개정 경로로 유도하는 게 안전).
    /// </summary>
    public bool HasOutboundHistory(string channelCode, string cskuCode, string msku, DateTime effectiveFrom, DateTime? effectiveTo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM OutboundDetailTable
                WHERE ChannelCode = $channelCode
                  AND Status = '출고확정'
                  AND (CskuCode = $cskuCode OR (CskuCode = '' AND MskuCode = $msku))
                  AND date(ConfirmedAt) >= date($effectiveFrom)
                  AND ($effectiveTo IS NULL OR date(ConfirmedAt) <= date($effectiveTo))
            )
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$cskuCode", cskuCode);
        command.Parameters.AddWithValue("$msku", msku);
        command.Parameters.AddWithValue("$effectiveFrom", effectiveFrom.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$effectiveTo", (object?)effectiveTo?.ToString("yyyy-MM-dd") ?? DBNull.Value);

        var result = command.ExecuteScalar();
        return result is long l && l == 1;
    }

    private static void BindHeader(SqliteCommand command, PriceQuote quote)
    {
        command.Parameters.AddWithValue("$quoteNo", quote.QuoteNo);
        command.Parameters.AddWithValue("$channelCode", quote.ChannelCode);
        command.Parameters.AddWithValue("$priceKind", quote.PriceKind);
        command.Parameters.AddWithValue("$formType", quote.QuoteFormType);
        command.Parameters.AddWithValue("$origin", quote.Origin);
        command.Parameters.AddWithValue("$title", quote.Title);
        command.Parameters.AddWithValue("$quoteDate", (object?)quote.QuoteDate?.ToString("yyyy-MM-dd") ?? "");
        command.Parameters.AddWithValue("$effFrom", (object?)quote.EffectiveFrom?.ToString("yyyy-MM-dd") ?? "");
        command.Parameters.AddWithValue("$effTo", (object?)quote.EffectiveTo?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        command.Parameters.AddWithValue("$autoApply", quote.AutoApply ? 1 : 0);
        command.Parameters.AddWithValue("$status", quote.Status);
        command.Parameters.AddWithValue("$deliveryMethod", quote.DeliveryMethod);
        command.Parameters.AddWithValue("$deliveredAt", (object?)quote.DeliveredAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value);
        command.Parameters.AddWithValue("$deliveredTo", quote.DeliveredTo);
        command.Parameters.AddWithValue("$note", quote.Note);
        command.Parameters.AddWithValue("$priceBasis", quote.PriceBasis);
        command.Parameters.AddWithValue("$rootId", (object?)quote.RootQuoteId ?? DBNull.Value);
        command.Parameters.AddWithValue("$revNo", quote.RevisionNo);
        command.Parameters.AddWithValue("$supersededBy", (object?)quote.SupersededBy ?? DBNull.Value);
        command.Parameters.AddWithValue("$revReason", quote.RevisionReason);
        command.Parameters.AddWithValue("$createdAt", quote.CreatedAt!.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$updatedAt", quote.UpdatedAt!.Value.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private static void BindLine(SqliteCommand command, PriceQuoteLine line)
    {
        command.Parameters.AddWithValue("$quoteId", line.QuoteId);
        command.Parameters.AddWithValue("$rowNo", line.RowNo);
        command.Parameters.AddWithValue("$cskuCode", line.CskuCode);
        command.Parameters.AddWithValue("$msku", line.Msku);
        command.Parameters.AddWithValue("$itemName", line.ItemNameSnap);
        command.Parameters.AddWithValue("$spec", line.Spec);
        command.Parameters.AddWithValue("$unit", line.Unit);
        command.Parameters.AddWithValue("$qty", line.Qty);
        command.Parameters.AddWithValue("$oldPrice", (object?)line.OldPrice ?? DBNull.Value);
        command.Parameters.AddWithValue("$newPrice", line.NewPrice);
        command.Parameters.AddWithValue("$supplyAmount", line.SupplyAmount);
        command.Parameters.AddWithValue("$tax", line.Tax);
        command.Parameters.AddWithValue("$total", line.Total);
        command.Parameters.AddWithValue("$changeReason", line.ChangeReason);
        command.Parameters.AddWithValue("$note", line.Note);
        command.Parameters.AddWithValue("$isApplied", line.IsApplied ? 1 : 0);
        command.Parameters.AddWithValue("$promotedFrom", (object?)line.PromotedFrom ?? DBNull.Value);
    }

    private static PriceQuote ReadHeader(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        QuoteNo = r.GetString(1),
        ChannelCode = r.GetString(2),
        PriceKind = r.GetString(3),
        QuoteFormType = r.GetString(4),
        Origin = r.GetString(5),
        Title = r.GetString(6),
        QuoteDate = ParseNullableDate(r, 7),
        EffectiveFrom = ParseNullableDate(r, 8),
        EffectiveTo = ParseNullableDate(r, 9),
        AutoApply = r.GetInt32(10) == 1,
        Status = r.GetString(11),
        DeliveryMethod = r.GetString(12),
        DeliveredAt = ParseNullableDate(r, 13),
        DeliveredTo = r.GetString(14),
        Note = r.GetString(15),
        PriceBasis = r.GetString(16),
        RootQuoteId = r.IsDBNull(17) ? null : r.GetInt32(17),
        RevisionNo = r.GetInt32(18),
        SupersededBy = r.IsDBNull(19) ? null : r.GetInt32(19),
        RevisionReason = r.GetString(20),
        CreatedAt = ParseNullableDate(r, 21),
        UpdatedAt = ParseNullableDate(r, 22),
    };

    private static PriceQuoteLine ReadLine(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        QuoteId = r.GetInt32(1),
        RowNo = r.GetInt32(2),
        CskuCode = r.GetString(3),
        Msku = r.GetString(4),
        ItemNameSnap = r.GetString(5),
        Spec = r.GetString(6),
        Unit = r.GetString(7),
        Qty = r.GetDecimal(8),
        OldPrice = r.IsDBNull(9) ? null : r.GetDecimal(9),
        NewPrice = r.GetDecimal(10),
        SupplyAmount = r.GetDecimal(11),
        Tax = r.GetDecimal(12),
        Total = r.GetDecimal(13),
        ChangeReason = r.GetString(14),
        Note = r.GetString(15),
        IsApplied = r.GetInt32(16) == 1,
        PromotedFrom = r.IsDBNull(17) ? null : r.GetInt32(17),
    };

    private static DateTime? ParseNullableDate(SqliteDataReader r, int ordinal) =>
        r.IsDBNull(ordinal) || string.IsNullOrWhiteSpace(r.GetString(ordinal)) ? null : DateTime.Parse(r.GetString(ordinal));
}
