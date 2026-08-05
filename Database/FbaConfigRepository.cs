using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 아마존 FBA 발주지(수취지) 설정(FbaConfig)에 대한 데이터베이스 작업을 처리한다. 수취지가 1곳
/// 고정이라 ConfigKey="DEFAULT" 단일 행만 다룬다.
/// </summary>
public class FbaConfigRepository
{
    public void Upsert(FbaConfigModel model)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FbaConfig
                (ConfigKey, ReceiverName, Phone, Phone2, Address, DeliveryMessage, BoxTypeLabel, TransferType, Etc1, OrderNoPrefix)
            VALUES
                ($configKey, $receiverName, $phone, $phone2, $address, $deliveryMessage, $boxTypeLabel, $transferType, $etc1, $orderNoPrefix)
            ON CONFLICT(ConfigKey) DO UPDATE SET
                ReceiverName = excluded.ReceiverName,
                Phone = excluded.Phone,
                Phone2 = excluded.Phone2,
                Address = excluded.Address,
                DeliveryMessage = excluded.DeliveryMessage,
                BoxTypeLabel = excluded.BoxTypeLabel,
                TransferType = excluded.TransferType,
                Etc1 = excluded.Etc1,
                OrderNoPrefix = excluded.OrderNoPrefix
            """;
        command.Parameters.AddWithValue("$configKey", model.ConfigKey);
        command.Parameters.AddWithValue("$receiverName", model.ReceiverName);
        command.Parameters.AddWithValue("$phone", model.Phone);
        command.Parameters.AddWithValue("$phone2", model.Phone2);
        command.Parameters.AddWithValue("$address", model.Address);
        command.Parameters.AddWithValue("$deliveryMessage", model.DeliveryMessage);
        command.Parameters.AddWithValue("$boxTypeLabel", model.BoxTypeLabel);
        command.Parameters.AddWithValue("$transferType", model.TransferType);
        command.Parameters.AddWithValue("$etc1", model.Etc1);
        command.Parameters.AddWithValue("$orderNoPrefix", model.OrderNoPrefix);
        command.ExecuteNonQuery();
    }

    public List<FbaConfigModel> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ConfigKey, ReceiverName, Phone, Phone2, Address, DeliveryMessage, BoxTypeLabel, TransferType, Etc1, OrderNoPrefix
            FROM FbaConfig
            """;

        var result = new List<FbaConfigModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadModel(reader));
        }
        return result;
    }

    /// <summary>단일 설정 행을 가져온다. 없으면 기본값(BoxTypeLabel="중", OrderNoPrefix="#FBA")을 반환한다.</summary>
    public FbaConfigModel GetDefault()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ConfigKey, ReceiverName, Phone, Phone2, Address, DeliveryMessage, BoxTypeLabel, TransferType, Etc1, OrderNoPrefix
            FROM FbaConfig
            WHERE ConfigKey = $configKey
            """;
        command.Parameters.AddWithValue("$configKey", FbaConfigModel.DefaultConfigKey);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadModel(reader) : new FbaConfigModel();
    }

    public void Delete(string configKey)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FbaConfig WHERE ConfigKey = $configKey";
        command.Parameters.AddWithValue("$configKey", configKey);
        command.ExecuteNonQuery();
    }

    private static FbaConfigModel ReadModel(SqliteDataReader reader) => new()
    {
        ConfigKey = reader.GetString(0),
        ReceiverName = reader.GetString(1),
        Phone = reader.GetString(2),
        Phone2 = reader.GetString(3),
        Address = reader.GetString(4),
        DeliveryMessage = reader.GetString(5),
        BoxTypeLabel = reader.GetString(6),
        TransferType = reader.GetString(7),
        Etc1 = reader.GetString(8),
        OrderNoPrefix = reader.GetString(9),
    };
}
