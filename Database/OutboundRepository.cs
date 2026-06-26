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
            INSERT INTO OutboundDetailTable (OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice)
            VALUES ($orderNo, $trackingNo, $mskuCode, $qty, $supplyPrice)
            """;

        foreach (var detail in details)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$orderNo", detail.OrderNo);
            command.Parameters.AddWithValue("$trackingNo", (object?)detail.TrackingNo ?? DBNull.Value);
            command.Parameters.AddWithValue("$mskuCode", detail.MskuCode);
            command.Parameters.AddWithValue("$qty", detail.Qty);
            command.Parameters.AddWithValue("$supplyPrice", detail.SupplyPrice);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}