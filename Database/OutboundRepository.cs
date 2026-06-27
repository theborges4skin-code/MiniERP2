using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 출고 확정된 주문 상세 내역에 대한 데이터베이스 작업을 처리합니다.
/// </summary>
public class OutboundRepository
{
    /// <summary>
    /// 출고 상세 내역 목록을 데이터베이스에 저장합니다(발주확정 시점 = 발주이력의 시작점).
    /// 운송장번호가 이미 입력되어 있으면 "발송완료"로, 없으면 "발송대기"로 시작합니다.
    /// 이미 발송완료로 확정된 건을 다시 저장해도(같은 OrderNo+MskuCode) 상태가 뒤로 되돌아가지
    /// 않도록, 새 운송장번호가 없으면 기존 Status/ConfirmedAt을 그대로 유지합니다.
    /// </summary>
    public void SaveOutbound(IEnumerable<OutboundDetail> details)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO OutboundDetailTable (ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt)
            VALUES ($channelCode, $orderNo, $trackingNo, $mskuCode, $qty, $supplyPrice, $createdAt, $status, $confirmedAt)
            ON CONFLICT(OrderNo, MskuCode) DO UPDATE SET
                ChannelCode = excluded.ChannelCode,
                TrackingNo = excluded.TrackingNo,
                Qty = excluded.Qty,
                SupplyPrice = excluded.SupplyPrice,
                Status = CASE WHEN excluded.TrackingNo <> '' THEN '발송완료' ELSE OutboundDetailTable.Status END,
                ConfirmedAt = CASE WHEN excluded.TrackingNo <> '' AND OutboundDetailTable.ConfirmedAt IS NULL THEN excluded.ConfirmedAt ELSE OutboundDetailTable.ConfirmedAt END
            """;

        foreach (var detail in details)
        {
            var hasTracking = !string.IsNullOrWhiteSpace(detail.TrackingNo);
            var now = DateTime.UtcNow;

            command.Parameters.Clear();
            command.Parameters.AddWithValue("$channelCode", detail.ChannelCode);
            command.Parameters.AddWithValue("$orderNo", detail.OrderNo);
            command.Parameters.AddWithValue("$trackingNo", (object?)detail.TrackingNo ?? DBNull.Value);
            command.Parameters.AddWithValue("$mskuCode", detail.MskuCode);
            command.Parameters.AddWithValue("$qty", detail.Qty);
            command.Parameters.AddWithValue("$supplyPrice", detail.SupplyPrice);
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$status", hasTracking ? "발송완료" : "발송대기");
            command.Parameters.AddWithValue("$confirmedAt", hasTracking ? now : (object)DBNull.Value);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// 선택된 발주이력을 "발송완료"로 수동 확정합니다(운송장번호를 별도로 받지 않는 수기 발송확인용).
    /// </summary>
    public void MarkAsShipped(IEnumerable<long> ids)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE OutboundDetailTable SET Status = '발송완료', ConfirmedAt = $confirmedAt WHERE Id = $id";

        foreach (var id in ids)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$confirmedAt", DateTime.UtcNow);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// 주문번호 기준으로 운송장번호를 일괄 업로드/갱신하고 "발송완료"로 확정합니다(같은 주문번호의
    /// 모든 SKU 줄에 적용됨). 일치하는 주문번호가 없으면 그 항목은 조용히 건너뜁니다.
    /// </summary>
    public int BulkUpdateTrackingNoByOrderNo(IReadOnlyDictionary<string, string> trackingNoByOrderNo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE OutboundDetailTable
            SET TrackingNo = $trackingNo, Status = '발송완료', ConfirmedAt = $confirmedAt
            WHERE OrderNo = $orderNo
            """;

        var updatedCount = 0;
        foreach (var (orderNo, trackingNo) in trackingNoByOrderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo) || string.IsNullOrWhiteSpace(trackingNo)) continue;

            command.Parameters.Clear();
            command.Parameters.AddWithValue("$trackingNo", trackingNo);
            command.Parameters.AddWithValue("$confirmedAt", DateTime.UtcNow);
            command.Parameters.AddWithValue("$orderNo", orderNo);
            updatedCount += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return updatedCount;
    }

    /// <summary>
    /// 지정된 채널의, 지정된 기간(포함) 내 출고 상세 내역을 조회합니다(마감 대조/발주이력 추적용).
    /// </summary>
    public List<OutboundDetail> GetByChannel(string channelCode, DateTime from, DateTime to)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt
            FROM OutboundDetailTable
            WHERE ChannelCode = $channelCode AND CreatedAt >= $from AND CreatedAt <= $to
            ORDER BY CreatedAt
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$to", to);

        var results = new List<OutboundDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadOutboundDetail(reader));
        }
        return results;
    }

    private static OutboundDetail ReadOutboundDetail(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ChannelCode = reader.GetString(1),
        OrderNo = reader.GetString(2),
        TrackingNo = reader.GetString(3),
        MskuCode = reader.GetString(4),
        Qty = reader.GetInt32(5),
        SupplyPrice = reader.GetDecimal(6),
        CreatedAt = reader.GetDateTime(7),
        Status = reader.GetString(8),
        ConfirmedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
    };
}
