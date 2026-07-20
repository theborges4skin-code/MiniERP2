using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>FBO(네이버 풀필먼트) 센터별 발주지 설정(FboChannelConfig)에 대한 데이터베이스 작업을 처리한다.</summary>
public class FboChannelConfigRepository
{
    public void Upsert(FboChannelConfigModel model)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FboChannelConfig
                (ChannelId, ChannelName, ReceiverName, Phone, Address, ReceiverSeqFormat, ChannelLabel, OrderNoPrefix, InboundType, IsDefault)
            VALUES
                ($channelId, $channelName, $receiverName, $phone, $address, $receiverSeqFormat, $channelLabel, $orderNoPrefix, $inboundType, $isDefault)
            ON CONFLICT(ChannelId) DO UPDATE SET
                ChannelName = excluded.ChannelName,
                ReceiverName = excluded.ReceiverName,
                Phone = excluded.Phone,
                Address = excluded.Address,
                ReceiverSeqFormat = excluded.ReceiverSeqFormat,
                ChannelLabel = excluded.ChannelLabel,
                OrderNoPrefix = excluded.OrderNoPrefix,
                InboundType = excluded.InboundType,
                IsDefault = excluded.IsDefault
            """;
        command.Parameters.AddWithValue("$channelId", model.ChannelId);
        command.Parameters.AddWithValue("$channelName", model.ChannelName);
        command.Parameters.AddWithValue("$receiverName", model.ReceiverName);
        command.Parameters.AddWithValue("$phone", model.Phone);
        command.Parameters.AddWithValue("$address", model.Address);
        command.Parameters.AddWithValue("$receiverSeqFormat", model.ReceiverSeqFormat);
        command.Parameters.AddWithValue("$channelLabel", model.ChannelLabel);
        command.Parameters.AddWithValue("$orderNoPrefix", model.OrderNoPrefix);
        command.Parameters.AddWithValue("$inboundType", model.InboundType);
        command.Parameters.AddWithValue("$isDefault", model.IsDefault ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public List<FboChannelConfigModel> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ChannelId, ChannelName, ReceiverName, Phone, Address, ReceiverSeqFormat, ChannelLabel, OrderNoPrefix, InboundType, IsDefault
            FROM FboChannelConfig
            """;

        var result = new List<FboChannelConfigModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadModel(reader));
        }
        return result;
    }

    public FboChannelConfigModel? GetById(string channelId)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ChannelId, ChannelName, ReceiverName, Phone, Address, ReceiverSeqFormat, ChannelLabel, OrderNoPrefix, InboundType, IsDefault
            FROM FboChannelConfig
            WHERE ChannelId = $channelId
            """;
        command.Parameters.AddWithValue("$channelId", channelId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadModel(reader) : null;
    }

    public void Delete(string channelId)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FboChannelConfig WHERE ChannelId = $channelId";
        command.Parameters.AddWithValue("$channelId", channelId);
        command.ExecuteNonQuery();
    }

    private static FboChannelConfigModel ReadModel(SqliteDataReader reader) => new()
    {
        ChannelId = reader.GetString(0),
        ChannelName = reader.GetString(1),
        ReceiverName = reader.GetString(2),
        Phone = reader.GetString(3),
        Address = reader.GetString(4),
        ReceiverSeqFormat = reader.GetString(5),
        ChannelLabel = reader.GetString(6),
        OrderNoPrefix = reader.GetString(7),
        InboundType = reader.GetString(8),
        IsDefault = reader.GetInt32(9) != 0,
    };
}
