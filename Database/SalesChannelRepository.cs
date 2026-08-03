using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 판매 채널에 대한 데이터베이스 작업을 처리합니다.
/// </summary>
public class SalesChannelRepository
{
    public List<SalesChannel> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ChannelCode, ChannelName, GroupName, IsFavorite, DisplayOrder, LastUsedDate, IsPurchase, IsSales
            FROM SalesChannelTable
            ORDER BY GroupName, DisplayOrder, ChannelName
            """;

        var channels = new List<SalesChannel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            channels.Add(new SalesChannel
            {
                ChannelCode = reader.GetString(0),
                ChannelName = reader.GetString(1),
                GroupName = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsFavorite = reader.GetBoolean(3),
                DisplayOrder = reader.GetInt32(4),
                LastUsedDate = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                IsPurchase = reader.GetBoolean(6),
                IsSales = reader.GetBoolean(7),
            });
        }
        return channels;
    }

    public void UpdateLastUsedDate(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SalesChannelTable SET LastUsedDate = $date WHERE ChannelCode = $channelCode
            """;
        command.Parameters.AddWithValue("$date", DateTime.Now.ToString("O"));
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.ExecuteNonQuery();
    }

    public void Upsert(SalesChannel channel)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        UpsertCore(command, channel);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 채널 일괄등록(엑셀) 커밋 전용. 호출 측이 연 트랜잭션 안에서 여러 채널을 한 번에 Upsert해,
    /// 이어지는 DocParty 저장과 함께 하나의 SQLite 트랜잭션으로 묶을 수 있게 한다(기획서 §4.5).
    /// </summary>
    public void UpsertMany(IEnumerable<SalesChannel> channels, SqliteConnection connection, SqliteTransaction transaction)
    {
        foreach (var channel in channels)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            UpsertCore(command, channel);
            command.ExecuteNonQuery();
        }
    }

    private static void UpsertCore(SqliteCommand command, SalesChannel channel)
    {
        command.CommandText = """
            INSERT INTO SalesChannelTable (ChannelCode, ChannelName, GroupName, IsFavorite, DisplayOrder, IsPurchase, IsSales)
            VALUES ($channelCode, $channelName, $groupName, $isFavorite, $displayOrder, $isPurchase, $isSales)
            ON CONFLICT(ChannelCode) DO UPDATE SET
                ChannelName = excluded.ChannelName,
                GroupName = excluded.GroupName,
                IsFavorite = excluded.IsFavorite,
                DisplayOrder = excluded.DisplayOrder,
                IsPurchase = excluded.IsPurchase,
                IsSales = excluded.IsSales
            """;
        command.Parameters.AddWithValue("$channelCode", channel.ChannelCode);
        command.Parameters.AddWithValue("$channelName", channel.ChannelName);
        command.Parameters.AddWithValue("$groupName", (object?)channel.GroupName ?? DBNull.Value);
        command.Parameters.AddWithValue("$isFavorite", channel.IsFavorite);
        command.Parameters.AddWithValue("$displayOrder", channel.DisplayOrder);
        command.Parameters.AddWithValue("$isPurchase", channel.IsPurchase);
        command.Parameters.AddWithValue("$isSales", channel.IsSales);
    }

    public void Delete(string channelCode)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SalesChannelTable WHERE ChannelCode = $channelCode";
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.ExecuteNonQuery();
    }
}