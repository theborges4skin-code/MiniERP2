using System.Text.Json;
using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 택배사 마스터 데이터에 대한 데이터베이스 작업을 처리합니다.
/// </summary>
public class CourierRepository
{
    public List<CourierMaster> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CourierName, HeaderMappingJson, TrackingImportHeaderRow, TrackingImportRecipientHeader, TrackingImportTrackingNoHeader, QuantityNotationFormat,
                   TrackingImportOrderNoHeader, TrackingImportAddressHeader, TrackingImportProductNameHeader,
                   TrackingImportReceivedDateHeader, TrackingImportFreightCostHeader
            FROM CourierMasterTable
            ORDER BY CourierName
            """;

        var couriers = new List<CourierMaster>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            couriers.Add(ReadCourier(reader));
        }
        return couriers;
    }

    public void Upsert(CourierMaster courier)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CourierMasterTable (CourierName, HeaderMappingJson, TrackingImportHeaderRow, TrackingImportRecipientHeader, TrackingImportTrackingNoHeader, QuantityNotationFormat,
                                             TrackingImportOrderNoHeader, TrackingImportAddressHeader, TrackingImportProductNameHeader,
                                             TrackingImportReceivedDateHeader, TrackingImportFreightCostHeader)
            VALUES ($courierName, $headerMappingJson, $trackingImportHeaderRow, $trackingImportRecipientHeader, $trackingImportTrackingNoHeader, $quantityNotationFormat,
                    $trackingImportOrderNoHeader, $trackingImportAddressHeader, $trackingImportProductNameHeader,
                    $trackingImportReceivedDateHeader, $trackingImportFreightCostHeader)
            ON CONFLICT(CourierName) DO UPDATE SET
                HeaderMappingJson = excluded.HeaderMappingJson,
                TrackingImportHeaderRow = excluded.TrackingImportHeaderRow,
                TrackingImportRecipientHeader = excluded.TrackingImportRecipientHeader,
                TrackingImportTrackingNoHeader = excluded.TrackingImportTrackingNoHeader,
                QuantityNotationFormat = excluded.QuantityNotationFormat,
                TrackingImportOrderNoHeader = excluded.TrackingImportOrderNoHeader,
                TrackingImportAddressHeader = excluded.TrackingImportAddressHeader,
                TrackingImportProductNameHeader = excluded.TrackingImportProductNameHeader,
                TrackingImportReceivedDateHeader = excluded.TrackingImportReceivedDateHeader,
                TrackingImportFreightCostHeader = excluded.TrackingImportFreightCostHeader
            """;
        command.Parameters.AddWithValue("$courierName", courier.CourierName);
        command.Parameters.AddWithValue("$headerMappingJson", courier.HeaderMappingJson);
        command.Parameters.AddWithValue("$trackingImportHeaderRow", courier.TrackingImportHeaderRow);
        command.Parameters.AddWithValue("$trackingImportRecipientHeader", courier.TrackingImportRecipientHeader);
        command.Parameters.AddWithValue("$trackingImportTrackingNoHeader", courier.TrackingImportTrackingNoHeader);
        command.Parameters.AddWithValue("$quantityNotationFormat", courier.QuantityNotationFormat);
        command.Parameters.AddWithValue("$trackingImportOrderNoHeader", courier.TrackingImportOrderNoHeader);
        command.Parameters.AddWithValue("$trackingImportAddressHeader", courier.TrackingImportAddressHeader);
        command.Parameters.AddWithValue("$trackingImportProductNameHeader", courier.TrackingImportProductNameHeader);
        command.Parameters.AddWithValue("$trackingImportReceivedDateHeader", courier.TrackingImportReceivedDateHeader);
        command.Parameters.AddWithValue("$trackingImportFreightCostHeader", courier.TrackingImportFreightCostHeader);
        command.ExecuteNonQuery();
    }

    public void Delete(string courierName)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CourierMasterTable WHERE CourierName = $courierName";
        command.Parameters.AddWithValue("$courierName", courierName);
        command.ExecuteNonQuery();
    }

    private static CourierMaster ReadCourier(SqliteDataReader reader) => new()
    {
        CourierName = reader.GetString(0),
        HeaderMappingJson = reader.GetString(1),
        TrackingImportHeaderRow = reader.GetInt32(2),
        TrackingImportRecipientHeader = reader.GetString(3),
        TrackingImportTrackingNoHeader = reader.GetString(4),
        QuantityNotationFormat = reader.GetString(5),
        TrackingImportOrderNoHeader = reader.GetString(6),
        TrackingImportAddressHeader = reader.GetString(7),
        TrackingImportProductNameHeader = reader.GetString(8),
        TrackingImportReceivedDateHeader = reader.GetString(9),
        TrackingImportFreightCostHeader = reader.GetString(10),
    };
}