using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 발송헤더(OutboundShipmentTable)에 대한 데이터베이스 작업을 처리합니다. 발송(ShipmentGroupKey)
/// 1건의 실운임을 그 발송에 속한 모든 출고 라인이 공통으로 나눠 갖습니다(§D6).
/// </summary>
public class OutboundShipmentRepository
{
    public void Upsert(OutboundShipmentModel shipment)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO OutboundShipmentTable (ShipmentGroupKey, FreightCost, ShippedAt, Note)
            VALUES ($shipmentGroupKey, $freightCost, $shippedAt, $note)
            ON CONFLICT(ShipmentGroupKey) DO UPDATE SET
                FreightCost = excluded.FreightCost,
                ShippedAt = excluded.ShippedAt,
                Note = excluded.Note
            """;
        command.Parameters.AddWithValue("$shipmentGroupKey", shipment.ShipmentGroupKey);
        command.Parameters.AddWithValue("$freightCost", shipment.FreightCost);
        command.Parameters.AddWithValue("$shippedAt", (object?)shipment.ShippedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$note", shipment.Note ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    public void Delete(string shipmentGroupKey)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM OutboundShipmentTable WHERE ShipmentGroupKey = $shipmentGroupKey";
        command.Parameters.AddWithValue("$shipmentGroupKey", shipmentGroupKey);
        command.ExecuteNonQuery();
    }

    public OutboundShipmentModel? GetByKey(string shipmentGroupKey)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ShipmentGroupKey, FreightCost, ShippedAt, Note FROM OutboundShipmentTable WHERE ShipmentGroupKey = $shipmentGroupKey";
        command.Parameters.AddWithValue("$shipmentGroupKey", shipmentGroupKey);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadShipment(reader) : null;
    }

    /// <summary>여러 발송헤더를 한 번에 조회합니다(출고이력 조회창에서 라인 목록과 JOIN할 때 N+1 방지용).</summary>
    public List<OutboundShipmentModel> GetByKeys(IEnumerable<string> shipmentGroupKeys)
    {
        var keys = shipmentGroupKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (keys.Count == 0) return [];

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();

        var paramNames = keys.Select((_, i) => $"$k{i}").ToList();
        command.CommandText = $"""
            SELECT ShipmentGroupKey, FreightCost, ShippedAt, Note
            FROM OutboundShipmentTable
            WHERE ShipmentGroupKey IN ({string.Join(",", paramNames)})
            """;
        for (var i = 0; i < keys.Count; i++)
        {
            command.Parameters.AddWithValue(paramNames[i], keys[i]);
        }

        var results = new List<OutboundShipmentModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadShipment(reader));
        }
        return results;
    }

    private static OutboundShipmentModel ReadShipment(Microsoft.Data.Sqlite.SqliteDataReader reader) => new()
    {
        ShipmentGroupKey = reader.GetString(0),
        FreightCost = reader.GetDecimal(1),
        ShippedAt = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
        Note = reader.IsDBNull(3) ? null : reader.GetString(3),
    };
}
