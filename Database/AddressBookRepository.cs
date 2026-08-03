using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 배송지 주소록(AddressBookTable + AddressChannelTagTable)에 대한 데이터베이스 작업을 처리한다.
/// 채널 태그는 주소 1건당 여러 개가 붙을 수 있어(다대다), Upsert 시 옛 태그를 전부 지우고 다시
/// 쓰는 delete-then-insert 방식으로 통째로 교체한다 — 태그 자체는 ChannelSkuTable의 가격처럼
/// 변경 이력을 남길 필요가 없는 단순 구성값이라 이 방식으로 충분하다.
/// </summary>
public class AddressBookRepository
{
    public List<AddressBookEntry> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();

        var entries = new List<AddressBookEntry>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT AddressId, Label, ReceiverName, Phone, Address, Memo, IsActive, DisplayOrder, CreatedAt
                FROM AddressBookTable
                ORDER BY DisplayOrder, Label
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(ReadEntry(reader));
            }
        }

        var tagsByAddressId = GetAllTags(connection);
        foreach (var entry in entries)
        {
            entry.ChannelTags = tagsByAddressId.GetValueOrDefault(entry.AddressId) ?? new List<string>();
        }

        return entries;
    }

    /// <summary>
    /// 주소 1건을 등록/수정하고(AddressId가 0이면 신규), 채널 태그도 함께 통째로 교체한다.
    /// 반환된 엔트리의 AddressId는 신규 등록 시 실제 발급된 값으로 채워진다.
    /// </summary>
    public AddressBookEntry Upsert(AddressBookEntry entry)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                if (entry.AddressId == 0)
                {
                    command.CommandText = """
                        INSERT INTO AddressBookTable (Label, ReceiverName, Phone, Address, Memo, IsActive, DisplayOrder, CreatedAt)
                        VALUES ($label, $receiverName, $phone, $address, $memo, $isActive, $displayOrder, $createdAt);
                        SELECT last_insert_rowid();
                        """;
                    command.Parameters.AddWithValue("$createdAt", DateTime.Now.ToString("O"));
                }
                else
                {
                    command.CommandText = """
                        UPDATE AddressBookTable SET
                            Label = $label, ReceiverName = $receiverName, Phone = $phone, Address = $address,
                            Memo = $memo, IsActive = $isActive, DisplayOrder = $displayOrder
                        WHERE AddressId = $addressId;
                        SELECT $addressId;
                        """;
                    command.Parameters.AddWithValue("$addressId", entry.AddressId);
                }

                command.Parameters.AddWithValue("$label", entry.Label);
                command.Parameters.AddWithValue("$receiverName", entry.ReceiverName);
                command.Parameters.AddWithValue("$phone", entry.Phone);
                command.Parameters.AddWithValue("$address", entry.Address);
                command.Parameters.AddWithValue("$memo", entry.Memo);
                command.Parameters.AddWithValue("$isActive", entry.IsActive ? 1 : 0);
                command.Parameters.AddWithValue("$displayOrder", entry.DisplayOrder);

                entry.AddressId = Convert.ToInt32(command.ExecuteScalar());
            }

            using (var deleteTagsCommand = connection.CreateCommand())
            {
                deleteTagsCommand.Transaction = transaction;
                deleteTagsCommand.CommandText = "DELETE FROM AddressChannelTagTable WHERE AddressId = $addressId";
                deleteTagsCommand.Parameters.AddWithValue("$addressId", entry.AddressId);
                deleteTagsCommand.ExecuteNonQuery();
            }

            foreach (var channelCode in entry.ChannelTags.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                using var insertTagCommand = connection.CreateCommand();
                insertTagCommand.Transaction = transaction;
                insertTagCommand.CommandText = "INSERT INTO AddressChannelTagTable (AddressId, ChannelCode) VALUES ($addressId, $channelCode)";
                insertTagCommand.Parameters.AddWithValue("$addressId", entry.AddressId);
                insertTagCommand.Parameters.AddWithValue("$channelCode", channelCode);
                insertTagCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return entry;
    }

    public void Delete(int addressId)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM AddressChannelTagTable WHERE AddressId = $addressId";
                command.Parameters.AddWithValue("$addressId", addressId);
                command.ExecuteNonQuery();
            }
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM AddressBookTable WHERE AddressId = $addressId";
                command.Parameters.AddWithValue("$addressId", addressId);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static Dictionary<int, List<string>> GetAllTags(SqliteConnection connection)
    {
        var result = new Dictionary<int, List<string>>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT AddressId, ChannelCode FROM AddressChannelTagTable";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var addressId = reader.GetInt32(0);
            var channelCode = reader.GetString(1);
            if (!result.TryGetValue(addressId, out var list))
            {
                list = new List<string>();
                result[addressId] = list;
            }
            list.Add(channelCode);
        }
        return result;
    }

    private static AddressBookEntry ReadEntry(SqliteDataReader reader) => new()
    {
        AddressId = reader.GetInt32(0),
        Label = reader.GetString(1),
        ReceiverName = reader.GetString(2),
        Phone = reader.GetString(3),
        Address = reader.GetString(4),
        Memo = reader.GetString(5),
        IsActive = reader.GetInt32(6) != 0,
        DisplayOrder = reader.GetInt32(7),
        CreatedAt = DateTime.TryParse(reader.GetString(8), out var createdAt) ? createdAt : DateTime.MinValue,
    };
}
