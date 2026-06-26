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
        command.CommandText = "SELECT CourierName, HeaderMappingJson FROM CourierMasterTable ORDER BY CourierName";

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
            INSERT INTO CourierMasterTable (CourierName, HeaderMappingJson)
            VALUES ($courierName, $headerMappingJson)
            ON CONFLICT(CourierName) DO UPDATE SET
                HeaderMappingJson = excluded.HeaderMappingJson
            """;
        command.Parameters.AddWithValue("$courierName", courier.CourierName);
        command.Parameters.AddWithValue("$headerMappingJson", courier.HeaderMappingJson);
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
        HeaderMappingJson = reader.GetString(1)
    };
}