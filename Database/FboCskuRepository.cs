using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 네이버 풀필먼트(FBO) 품목 마스터(FboCskuMaster)에 대한 데이터베이스 작업을 처리한다.
/// 신규 등록 시 ChannelSkuTable(ChannelCode="NAVER_FBO")에도 CSKU를 동시 등록해, CSKU가 항상
/// 채널에 종속된다는 기존 체계(ChannelSkuModel 참고)를 그대로 따르게 한다.
/// </summary>
public class FboCskuRepository
{
    public const string FboChannelCode = "NAVER_FBO";

    private readonly ChannelSkuRepository _channelSkuRepository;

    public FboCskuRepository(ChannelSkuRepository? channelSkuRepository = null)
    {
        _channelSkuRepository = channelSkuRepository ?? new ChannelSkuRepository();
    }

    public void Upsert(FboCskuModel model)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FboCskuMaster (Csku, FboItemCode, ItemName, InvoiceDisplayName, QtyPerBox, BoxType, FreightType, IsActive, UpdatedAt)
            VALUES ($csku, $fboItemCode, $itemName, $invoiceDisplayName, $qtyPerBox, $boxType, $freightType, $isActive, $updatedAt)
            ON CONFLICT(Csku) DO UPDATE SET
                FboItemCode = excluded.FboItemCode,
                ItemName = excluded.ItemName,
                InvoiceDisplayName = excluded.InvoiceDisplayName,
                QtyPerBox = excluded.QtyPerBox,
                BoxType = excluded.BoxType,
                FreightType = excluded.FreightType,
                IsActive = excluded.IsActive,
                UpdatedAt = excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$csku", model.Csku);
        command.Parameters.AddWithValue("$fboItemCode", model.FboItemCode);
        command.Parameters.AddWithValue("$itemName", model.ItemName);
        command.Parameters.AddWithValue("$invoiceDisplayName", (object?)model.InvoiceDisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$qtyPerBox", model.QtyPerBox);
        command.Parameters.AddWithValue("$boxType", model.BoxType);
        command.Parameters.AddWithValue("$freightType", (object?)model.FreightType ?? DBNull.Value);
        command.Parameters.AddWithValue("$isActive", model.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        command.ExecuteNonQuery();

        // ChannelSkuTable 쪽은 신규 CSKU일 때만 자기참조(Msku=Csku)로 동시 등록한다. 재고/원가
        // 추적 목적이 아니라 CSKU가 채널에 종속된다는 기존 체계와의 정합성을 위한 등록이므로,
        // 이미 존재하면 손대지 않는다(CreateIfNew가 존재 시 아무것도 하지 않고 false 반환).
        _channelSkuRepository.CreateIfNew(FboChannelCode, model.Csku, model.Csku, 0m, model.ItemName);
    }

    public List<FboCskuModel> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Csku, FboItemCode, ItemName, InvoiceDisplayName, QtyPerBox, BoxType, FreightType, IsActive, UpdatedAt FROM FboCskuMaster";

        var result = new List<FboCskuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadModel(reader));
        }
        return result;
    }

    public FboCskuModel? GetByCsku(string csku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Csku, FboItemCode, ItemName, InvoiceDisplayName, QtyPerBox, BoxType, FreightType, IsActive, UpdatedAt FROM FboCskuMaster WHERE Csku = $csku";
        command.Parameters.AddWithValue("$csku", csku);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadModel(reader) : null;
    }

    public void Delete(string csku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FboCskuMaster WHERE Csku = $csku";
        command.Parameters.AddWithValue("$csku", csku);
        command.ExecuteNonQuery();
    }

    private static FboCskuModel ReadModel(SqliteDataReader reader) => new()
    {
        Csku = reader.GetString(0),
        FboItemCode = reader.GetString(1),
        ItemName = reader.GetString(2),
        InvoiceDisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
        QtyPerBox = reader.GetInt32(4),
        BoxType = reader.GetString(5),
        FreightType = reader.IsDBNull(6) ? null : reader.GetString(6),
        IsActive = reader.GetInt32(7) != 0,
        UpdatedAt = reader.IsDBNull(8) || string.IsNullOrEmpty(reader.GetString(8)) ? null : DateTime.Parse(reader.GetString(8)),
    };
}
