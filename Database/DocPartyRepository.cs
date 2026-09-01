using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

public class DocPartyRepository
{
    private const string SelectCols = "Id, ProfileName, RegNo, CompanyName, CeoName, Address, BizType, BizItem, Tel, Email, IsDefaultSupplier, ChannelCode, IsActive, CreatedAt, StampImagePath, IsPriceMaster, ShippingFeePerShipment";

    public List<DocParty> GetAll()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM DocPartyTable ORDER BY IsDefaultSupplier DESC, Id ASC";
        using var reader = cmd.ExecuteReader();
        var list = new List<DocParty>();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public DocParty? GetDefaultSupplier()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM DocPartyTable WHERE IsDefaultSupplier = 1 LIMIT 1";
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>특정 채널에 연결된 공급받는자 프로필을 조회한다(없으면 null).</summary>
    public DocParty? GetByChannelCode(string channelCode)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM DocPartyTable WHERE ChannelCode = $code LIMIT 1";
        cmd.Parameters.AddWithValue("$code", channelCode);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>
    /// 등록번호로 거래처를 정확히 일치 검색한다(활성/비활성 불문 전체 대상) — 레거시 거래명세표
    /// 마이그레이션이 이미 존재하는 거래처(활성 포함)에 중복 레코드를 만들지 않기 위해 사용한다.
    /// </summary>
    public DocParty? FindByRegNo(string regNo)
    {
        if (string.IsNullOrWhiteSpace(regNo)) return null;
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM DocPartyTable WHERE RegNo = $regNo LIMIT 1";
        cmd.Parameters.AddWithValue("$regNo", regNo);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>채널에 연결된 모든 공급받는자 프로필을 조회한다(DocsForm "채널에서 불러오기"용).</summary>
    public List<DocParty> GetChannelLinkedAll()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM DocPartyTable WHERE ChannelCode IS NOT NULL AND ChannelCode != '' ORDER BY CompanyName";
        using var reader = cmd.ExecuteReader();
        var list = new List<DocParty>();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public void Save(DocParty party)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var transaction = conn.BeginTransaction();
        SaveCore(conn, transaction, party);
        transaction.Commit();
    }

    /// <summary>
    /// 채널 일괄등록(엑셀) 커밋 전용. 호출 측이 연 트랜잭션 안에서 여러 거래처 정보를 한 번에
    /// 저장해, SalesChannelRepository.UpsertMany와 함께 하나의 SQLite 트랜잭션으로 묶는다(§4.5).
    /// </summary>
    public void SaveMany(IEnumerable<DocParty> parties, SqliteConnection connection, SqliteTransaction transaction)
    {
        foreach (var party in parties)
        {
            SaveCore(connection, transaction, party);
        }
    }

    private static void SaveCore(SqliteConnection conn, SqliteTransaction transaction, DocParty party)
    {
        // 활성 상태는 채널 연결 여부로만 결정한다(수동 토글 없음) — 거래명세표_DB이식_개발스펙.md §2.1 정책.
        party.IsActive = !string.IsNullOrWhiteSpace(party.ChannelCode);

        if (party.Id == 0)
        {
            party.CreatedAt ??= DateTime.Now;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO DocPartyTable (ProfileName, RegNo, CompanyName, CeoName, Address, BizType, BizItem, Tel, Email, IsDefaultSupplier, ChannelCode, IsActive, CreatedAt, StampImagePath, IsPriceMaster, ShippingFeePerShipment)
                VALUES ($pn, $rn, $cn, $ceo, $addr, $bt, $bi, $tel, $email, $sup, $chan, $active, $created, $stamp, $pricemaster, $shipfee)
                """;
            Bind(cmd, party);
            cmd.Parameters.AddWithValue("$created", party.CreatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
            using var idCmd = conn.CreateCommand();
            idCmd.Transaction = transaction;
            idCmd.CommandText = "SELECT last_insert_rowid()";
            party.Id = (int)(long)idCmd.ExecuteScalar()!;
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                UPDATE DocPartyTable SET ProfileName=$pn, RegNo=$rn, CompanyName=$cn, CeoName=$ceo,
                    Address=$addr, BizType=$bt, BizItem=$bi, Tel=$tel, Email=$email, IsDefaultSupplier=$sup,
                    ChannelCode=$chan, IsActive=$active, StampImagePath=$stamp,
                    IsPriceMaster=$pricemaster, ShippingFeePerShipment=$shipfee
                WHERE Id=$id
                """;
            Bind(cmd, party);
            cmd.Parameters.AddWithValue("$id", party.Id);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>같은 CompanyName을 가진 행 중 현재 대표단가 채널(IsPriceMaster=1)을 찾는다(자기 자신 제외).</summary>
    public DocParty? FindPriceMasterInGroup(string companyName, int excludeId)
    {
        if (string.IsNullOrWhiteSpace(companyName)) return null;
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM DocPartyTable WHERE CompanyName = $cn AND IsPriceMaster = 1 AND Id != $id LIMIT 1";
        cmd.Parameters.AddWithValue("$cn", companyName);
        cmd.Parameters.AddWithValue("$id", excludeId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    /// <summary>
    /// 지정한 행을 대표단가 채널로 지정하고, 같은 CompanyName을 가진 다른 행의 대표단가 표시는
    /// 해제한다(§4.3 — CompanyName 그룹당 1개 유지).
    /// </summary>
    public void SetPriceMaster(int id, string companyName)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var transaction = conn.BeginTransaction();
        using (var clearCmd = conn.CreateCommand())
        {
            clearCmd.Transaction = transaction;
            clearCmd.CommandText = "UPDATE DocPartyTable SET IsPriceMaster = 0 WHERE CompanyName = $cn";
            clearCmd.Parameters.AddWithValue("$cn", companyName);
            clearCmd.ExecuteNonQuery();
        }
        using (var setCmd = conn.CreateCommand())
        {
            setCmd.Transaction = transaction;
            setCmd.CommandText = "UPDATE DocPartyTable SET IsPriceMaster = 1 WHERE Id = $id";
            setCmd.Parameters.AddWithValue("$id", id);
            setCmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <summary>CompanyName 그룹의 대표단가 채널 행을 조회한다(온라인 거래처 취합 §5 ResolveSupplyPrice용).</summary>
    public DocParty? GetPriceMasterByCompanyName(string companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName)) return null;
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectCols} FROM DocPartyTable WHERE CompanyName = $cn AND IsPriceMaster = 1 LIMIT 1";
        cmd.Parameters.AddWithValue("$cn", companyName);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public void SetDefaultSupplier(int id)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var clearCmd = conn.CreateCommand();
        clearCmd.CommandText = "UPDATE DocPartyTable SET IsDefaultSupplier = 0";
        clearCmd.ExecuteNonQuery();
        using var setCmd = conn.CreateCommand();
        setCmd.CommandText = "UPDATE DocPartyTable SET IsDefaultSupplier = 1 WHERE Id = $id";
        setCmd.Parameters.AddWithValue("$id", id);
        setCmd.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DocPartyTable WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static DocParty Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        ProfileName = r.IsDBNull(1) ? "" : r.GetString(1),
        RegNo = r.IsDBNull(2) ? "" : r.GetString(2),
        CompanyName = r.IsDBNull(3) ? "" : r.GetString(3),
        CeoName = r.IsDBNull(4) ? "" : r.GetString(4),
        Address = r.IsDBNull(5) ? "" : r.GetString(5),
        BizType = r.IsDBNull(6) ? "" : r.GetString(6),
        BizItem = r.IsDBNull(7) ? "" : r.GetString(7),
        Tel = r.IsDBNull(8) ? "" : r.GetString(8),
        Email = r.IsDBNull(9) ? "" : r.GetString(9),
        IsDefaultSupplier = r.GetInt32(10) == 1,
        ChannelCode = r.IsDBNull(11) ? "" : r.GetString(11),
        IsActive = !r.IsDBNull(12) && r.GetInt32(12) == 1,
        CreatedAt = r.IsDBNull(13) || string.IsNullOrWhiteSpace(r.GetString(13)) ? null : DateTime.Parse(r.GetString(13)),
        StampImagePath = r.IsDBNull(14) ? null : r.GetString(14),
        IsPriceMaster = !r.IsDBNull(15) && r.GetInt32(15) == 1,
        ShippingFeePerShipment = r.IsDBNull(16) ? 3000m : (decimal)r.GetDouble(16),
    };

    private static void Bind(SqliteCommand cmd, DocParty p)
    {
        cmd.Parameters.AddWithValue("$pn", p.ProfileName);
        cmd.Parameters.AddWithValue("$rn", p.RegNo);
        cmd.Parameters.AddWithValue("$cn", p.CompanyName);
        cmd.Parameters.AddWithValue("$ceo", p.CeoName);
        cmd.Parameters.AddWithValue("$addr", p.Address);
        cmd.Parameters.AddWithValue("$bt", p.BizType);
        cmd.Parameters.AddWithValue("$bi", p.BizItem);
        cmd.Parameters.AddWithValue("$tel", p.Tel);
        cmd.Parameters.AddWithValue("$email", p.Email);
        cmd.Parameters.AddWithValue("$sup", p.IsDefaultSupplier ? 1 : 0);
        cmd.Parameters.AddWithValue("$chan", p.ChannelCode);
        cmd.Parameters.AddWithValue("$active", p.IsActive ? 1 : 0);
        cmd.Parameters.AddWithValue("$stamp", (object?)p.StampImagePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pricemaster", p.IsPriceMaster ? 1 : 0);
        cmd.Parameters.AddWithValue("$shipfee", p.ShippingFeePerShipment);
    }
}
