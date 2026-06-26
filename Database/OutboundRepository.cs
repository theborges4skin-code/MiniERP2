using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 출고 확정된 주문 상세 내역에 대한 데이터베이스 작업을 처리합니다.
/// </summary>
public class OutboundRepository
{
    /// <summary>
    /// 출고 상세 내역 목록을 데이터베이스에 저장합니다.
    /// </summary>
    public void SaveOutbound(IEnumerable<OutboundDetail> details)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO OutboundDetailTable (ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt)
            VALUES ($channelCode, $orderNo, $trackingNo, $mskuCode, $qty, $supplyPrice, $createdAt)
            """;

        foreach (var detail in details)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$channelCode", detail.ChannelCode);
            command.Parameters.AddWithValue("$orderNo", detail.OrderNo);
            command.Parameters.AddWithValue("$trackingNo", (object?)detail.TrackingNo ?? DBNull.Value);
            command.Parameters.AddWithValue("$mskuCode", detail.MskuCode);
            command.Parameters.AddWithValue("$qty", detail.Qty);
            command.Parameters.AddWithValue("$supplyPrice", detail.SupplyPrice);
            command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// 지정된 채널의, 지정된 기간(포함) 내 출고 상세 내역을 조회합니다(마감 대조용).
    /// </summary>
    public List<OutboundDetail> GetByChannel(string channelCode, DateTime from, DateTime to)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt
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
    };
}
