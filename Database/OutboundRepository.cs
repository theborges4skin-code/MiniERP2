using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 출고 확정된 주문 상세 내역(=발주/출고 이력)에 대한 데이터베이스 작업을 처리합니다.
/// </summary>
public class OutboundRepository
{
    /// <summary>
    /// 출고 상세 내역 목록을 데이터베이스에 저장합니다(발주확정 시점 = 발주이력의 시작점).
    /// 운송장번호가 이미 입력되어 있으면 "출고확정"으로, 없으면 "발주확정"으로 시작합니다.
    /// 이미 출고확정으로 확정된 건을 다시 저장해도(같은 OrderNo+MskuCode) 상태가 뒤로 되돌아가지
    /// 않도록, 새 운송장번호가 없으면 기존 Status/ConfirmedAt을 그대로 유지합니다.
    /// </summary>
    public void SaveOutbound(IEnumerable<OutboundDetail> details)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO OutboundDetailTable (ChannelCode, OrderNo, ShipmentGroupKey, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName)
            VALUES ($channelCode, $orderNo, $shipmentGroupKey, $trackingNo, $mskuCode, $qty, $supplyPrice, $createdAt, $status, $confirmedAt, $recipient, $address, $productName)
            ON CONFLICT(ShipmentGroupKey, MskuCode) DO UPDATE SET
                ChannelCode = excluded.ChannelCode,
                OrderNo = excluded.OrderNo,
                TrackingNo = excluded.TrackingNo,
                Qty = excluded.Qty,
                SupplyPrice = excluded.SupplyPrice,
                Recipient = excluded.Recipient,
                Address = excluded.Address,
                ProductName = excluded.ProductName,
                Status = CASE WHEN excluded.TrackingNo <> '' THEN '출고확정' ELSE OutboundDetailTable.Status END,
                ConfirmedAt = CASE WHEN excluded.TrackingNo <> '' AND OutboundDetailTable.ConfirmedAt IS NULL THEN excluded.ConfirmedAt ELSE OutboundDetailTable.ConfirmedAt END
            """;

        foreach (var detail in details)
        {
            var hasTracking = !string.IsNullOrWhiteSpace(detail.TrackingNo);
            var now = DateTime.UtcNow;

            command.Parameters.Clear();
            command.Parameters.AddWithValue("$channelCode", detail.ChannelCode);
            command.Parameters.AddWithValue("$orderNo", detail.OrderNo);
            command.Parameters.AddWithValue("$shipmentGroupKey", string.IsNullOrEmpty(detail.ShipmentGroupKey) ? detail.OrderNo : detail.ShipmentGroupKey);
            command.Parameters.AddWithValue("$trackingNo", (object?)detail.TrackingNo ?? DBNull.Value);
            command.Parameters.AddWithValue("$mskuCode", detail.MskuCode);
            command.Parameters.AddWithValue("$qty", detail.Qty);
            command.Parameters.AddWithValue("$supplyPrice", detail.SupplyPrice);
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$status", hasTracking ? "출고확정" : "발주확정");
            command.Parameters.AddWithValue("$confirmedAt", hasTracking ? now : (object)DBNull.Value);
            command.Parameters.AddWithValue("$recipient", detail.Recipient);
            command.Parameters.AddWithValue("$address", detail.Address);
            command.Parameters.AddWithValue("$productName", detail.ProductName);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// 선택된 발주이력을 "출고확정"으로 수동 확정합니다(운송장번호를 별도로 받지 않는 수기 발송확인용).
    /// </summary>
    public void MarkAsShipped(IEnumerable<long> ids)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE OutboundDetailTable SET Status = '출고확정', ConfirmedAt = $confirmedAt WHERE Id = $id";

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
    /// 운송장 결과 가져오기로 특정 건의 운송장번호를 확정합니다(수령인 기준 매칭 후 사용자가 고른
    /// 1건에 적용). 운송장번호가 채워지면 항상 "출고확정"으로 바뀝니다.
    /// </summary>
    public void ApplyTrackingNo(long id, string trackingNo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE OutboundDetailTable SET TrackingNo = $trackingNo, Status = '출고확정', ConfirmedAt = $confirmedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$trackingNo", trackingNo);
        command.Parameters.AddWithValue("$confirmedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 발주/출고 이력 관리창에서 수정한 내용(수량/납품가/운송장번호/상태)을 Id 기준으로 저장합니다.
    /// </summary>
    public void UpdateDetail(OutboundDetail detail)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OutboundDetailTable
            SET Qty = $qty, SupplyPrice = $supplyPrice, TrackingNo = $trackingNo, Status = $status,
                ConfirmedAt = $confirmedAt
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$qty", detail.Qty);
        command.Parameters.AddWithValue("$supplyPrice", detail.SupplyPrice);
        command.Parameters.AddWithValue("$trackingNo", detail.TrackingNo);
        command.Parameters.AddWithValue("$status", detail.Status);
        command.Parameters.AddWithValue("$confirmedAt", (object?)detail.ConfirmedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", detail.Id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 선택한 발주/출고 이력을 삭제합니다(되돌릴 수 없으므로 호출 측에서 사용자 확인을 받아야 합니다).
    /// </summary>
    public void DeleteByIds(IEnumerable<long> ids)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM OutboundDetailTable WHERE Id = $id";

        foreach (var id in ids)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// 지정된 채널의, 지정된 기간(포함) 내 출고 상세 내역을 조회합니다(마감 대조용).
    /// </summary>
    public List<OutboundDetail> GetByChannel(string channelCode, DateTime from, DateTime to)
    {
        return GetHistory(channelCode, from, to);
    }

    /// <summary>
    /// 발주/출고 이력을 조회합니다(발주/출고 이력 관리창용). channelCode가 null이면 전체 채널.
    /// </summary>
    public List<OutboundDetail> GetHistory(string? channelCode, DateTime from, DateTime to)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrEmpty(channelCode)
            ? """
                SELECT Id, ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName
                FROM OutboundDetailTable
                WHERE CreatedAt >= $from AND CreatedAt <= $to
                ORDER BY CreatedAt
                """
            : """
                SELECT Id, ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName
                FROM OutboundDetailTable
                WHERE ChannelCode = $channelCode AND CreatedAt >= $from AND CreatedAt <= $to
                ORDER BY CreatedAt
                """;
        if (!string.IsNullOrEmpty(channelCode))
        {
            command.Parameters.AddWithValue("$channelCode", channelCode);
        }
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

    /// <summary>
    /// 주어진 주문번호들 중 이미 발주확정/출고확정 이력이 있는 건을 찾는다(채널 무관 — 이력 저장 시의
    /// 충돌 판단 키(OrderNo, MskuCode)와 같은 기준으로 "동일 주문"을 판단). 발주서를 다시 불러왔을 때
    /// 같은 주문을 또 처리하는 건 아닌지 안내하는 데 사용한다(처리 자체를 막지는 않음).
    /// </summary>
    public List<OutboundDetail> FindByOrderNos(IEnumerable<string> orderNos)
    {
        var orderNoList = orderNos.Where(o => !string.IsNullOrWhiteSpace(o)).Distinct().ToList();
        if (orderNoList.Count == 0) return [];

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();

        var paramNames = orderNoList.Select((_, i) => $"$o{i}").ToList();
        command.CommandText = $"""
            SELECT Id, ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName
            FROM OutboundDetailTable
            WHERE OrderNo IN ({string.Join(",", paramNames)})
            """;
        for (var i = 0; i < orderNoList.Count; i++)
        {
            command.Parameters.AddWithValue(paramNames[i], orderNoList[i]);
        }

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
        Recipient = reader.GetString(10),
        Address = reader.GetString(11),
        ProductName = reader.GetString(12),
    };
}
