namespace MiniERP2.Database;

/// <summary>거래처 마감자료 대조 — 거래처 품명 → 우리 CSKU 연결(PartnerItemNameMapTable). 채널별로 한 번 지정하면 계속 쓴다.</summary>
public class PartnerItemNameMapRepository
{
    public Dictionary<string, string> GetByChannel(string channelCode)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PartnerItemName, CskuCode FROM PartnerItemNameMapTable WHERE ChannelCode = $ch";
        cmd.Parameters.AddWithValue("$ch", channelCode);
        using var reader = cmd.ExecuteReader();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read()) map[reader.GetString(0)] = reader.GetString(1);
        return map;
    }

    public void Upsert(string channelCode, string partnerItemName, string cskuCode)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO PartnerItemNameMapTable (ChannelCode, PartnerItemName, CskuCode, UpdatedAt)
            VALUES ($ch, $name, $csku, $now)
            ON CONFLICT(ChannelCode, PartnerItemName) DO UPDATE SET CskuCode = excluded.CskuCode, UpdatedAt = excluded.UpdatedAt
            """;
        cmd.Parameters.AddWithValue("$ch", channelCode);
        cmd.Parameters.AddWithValue("$name", partnerItemName.Trim());
        cmd.Parameters.AddWithValue("$csku", cskuCode);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }
}
