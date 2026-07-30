using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 거래처 마감보드(거래처마감보드_개발기획서.md §5.1)의 거래처 자체 마스터. 즐겨찾기(고정 노출)와
/// 수동 거래처(MiniERP2 미경유) 등록의 기준이 된다.
/// </summary>
public class PartnerMasterRepository
{
    private const string SelectCols = "PartyKey, PartyName, IsManual, IsFavorite, IsActive";

    public List<PartnerMaster> GetAll()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM PartnerMasterTable ORDER BY IsManual, PartyKey";
        using var reader = cmd.ExecuteReader();
        var list = new List<PartnerMaster>();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public PartnerMaster? GetByPartyKey(string partyKey)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM PartnerMasterTable WHERE PartyKey = $key";
        cmd.Parameters.AddWithValue("$key", partyKey);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>즐겨찾기(고정 노출) + 활성 상태인 거래처만 조회한다(§7 좌측 목록 1단계).</summary>
    public List<PartnerMaster> GetFavorites()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM PartnerMasterTable WHERE IsFavorite = 1 AND IsActive = 1";
        using var reader = cmd.ExecuteReader();
        var list = new List<PartnerMaster>();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    /// <summary>활성 상태인 수동 거래처만 조회한다(§8 — 매달 자동 노출 대상).</summary>
    public List<PartnerMaster> GetActiveManualPartners()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM PartnerMasterTable WHERE IsManual = 1 AND IsActive = 1 ORDER BY PartyKey";
        using var reader = cmd.ExecuteReader();
        var list = new List<PartnerMaster>();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public void Upsert(PartnerMaster p)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO PartnerMasterTable (PartyKey, PartyName, IsManual, IsFavorite, IsActive)
            VALUES ($key, $name, $manual, $fav, $active)
            ON CONFLICT(PartyKey) DO UPDATE SET
                PartyName = excluded.PartyName,
                IsManual = excluded.IsManual,
                IsFavorite = excluded.IsFavorite,
                IsActive = excluded.IsActive
            """;
        cmd.Parameters.AddWithValue("$key", p.PartyKey);
        cmd.Parameters.AddWithValue("$name", p.PartyName);
        cmd.Parameters.AddWithValue("$manual", p.IsManual ? 1 : 0);
        cmd.Parameters.AddWithValue("$fav", p.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$active", p.IsActive ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// CH 파티의 즐겨찾기를 토글한다. 아직 마스터 행이 없으면(즐겨찾기를 처음 켜는 경우) 새로 만든다.
    /// SalesChannelTable.IsFavorite(OFS 채널 선택용)와는 별개 축이라 여기서 건드리지 않는다.
    /// </summary>
    public void SetFavorite(string partyKey, bool isFavorite)
    {
        var existing = GetByPartyKey(partyKey);
        var partner = existing ?? new PartnerMaster { PartyKey = partyKey, IsManual = partyKey.StartsWith("MANUAL:") };
        partner.IsFavorite = isFavorite;
        Upsert(partner);
    }

    /// <summary>수동 거래처를 소프트 비활성화/재활성화한다(§8 — 이력은 보존하고 목록에서만 제외).</summary>
    public void SetActive(string partyKey, bool isActive)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE PartnerMasterTable SET IsActive = $active WHERE PartyKey = $key";
        cmd.Parameters.AddWithValue("$active", isActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$key", partyKey);
        cmd.ExecuteNonQuery();
    }

    /// <summary>새 수동 거래처를 등록하고 `MANUAL:{순번}` 키를 발급한다(§8).</summary>
    public string AddManualPartner(string partyName)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var seqCmd = conn.CreateCommand();
        seqCmd.CommandText = """
            SELECT COALESCE(MAX(CAST(substr(PartyKey, 8) AS INTEGER)), 0) + 1
            FROM PartnerMasterTable WHERE PartyKey LIKE 'MANUAL:%'
            """;
        var nextSeq = (long)seqCmd.ExecuteScalar()!;
        var partyKey = $"MANUAL:{nextSeq}";

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO PartnerMasterTable (PartyKey, PartyName, IsManual, IsFavorite, IsActive)
            VALUES ($key, $name, 1, 0, 1)
            """;
        insertCmd.Parameters.AddWithValue("$key", partyKey);
        insertCmd.Parameters.AddWithValue("$name", partyName);
        insertCmd.ExecuteNonQuery();

        return partyKey;
    }

    private static PartnerMaster Map(SqliteDataReader r) => new()
    {
        PartyKey = r.GetString(0),
        PartyName = r.IsDBNull(1) ? "" : r.GetString(1),
        IsManual = r.GetInt32(2) == 1,
        IsFavorite = r.GetInt32(3) == 1,
        IsActive = r.GetInt32(4) == 1,
    };
}
