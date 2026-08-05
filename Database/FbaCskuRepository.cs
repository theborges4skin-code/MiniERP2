using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 아마존 FBA 품목 마스터(FbaCskuMaster)에 대한 데이터베이스 작업을 처리한다. 신규 등록 시
/// ChannelSkuTable(ChannelCode="AMAZON_FBA")에도 CSKU를 자기참조(Msku=Csku)로 동시 등록해, CSKU가
/// 항상 채널에 종속된다는 기존 체계를 그대로 따르게 한다(FboCskuRepository와 동일 패턴).
/// </summary>
public class FbaCskuRepository
{
    public const string FbaChannelCode = "AMAZON_FBA";

    private readonly ChannelSkuRepository _channelSkuRepository;

    public FbaCskuRepository(ChannelSkuRepository? channelSkuRepository = null)
    {
        _channelSkuRepository = channelSkuRepository ?? new ChannelSkuRepository();
    }

    public void Upsert(FbaCskuModel model)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO FbaCskuMaster
                (Csku, ItemName, InvoiceDisplayName, Asin, CommodityDescription, HsCode, ItemPrice, Volume, UnitWeightG, QtyPerLayer, MocraListingNo, IsActive, UpdatedAt)
            VALUES
                ($csku, $itemName, $invoiceDisplayName, $asin, $commodityDescription, $hsCode, $itemPrice, $volume, $unitWeightG, $qtyPerLayer, $mocraListingNo, $isActive, $updatedAt)
            ON CONFLICT(Csku) DO UPDATE SET
                ItemName = excluded.ItemName,
                InvoiceDisplayName = excluded.InvoiceDisplayName,
                Asin = excluded.Asin,
                CommodityDescription = excluded.CommodityDescription,
                HsCode = excluded.HsCode,
                ItemPrice = excluded.ItemPrice,
                Volume = excluded.Volume,
                UnitWeightG = excluded.UnitWeightG,
                QtyPerLayer = excluded.QtyPerLayer,
                MocraListingNo = excluded.MocraListingNo,
                IsActive = excluded.IsActive,
                UpdatedAt = excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$csku", model.Csku);
        command.Parameters.AddWithValue("$itemName", model.ItemName);
        command.Parameters.AddWithValue("$invoiceDisplayName", (object?)model.InvoiceDisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$asin", model.Asin);
        command.Parameters.AddWithValue("$commodityDescription", model.CommodityDescription);
        command.Parameters.AddWithValue("$hsCode", model.HsCode);
        command.Parameters.AddWithValue("$itemPrice", model.ItemPrice);
        command.Parameters.AddWithValue("$volume", model.Volume);
        command.Parameters.AddWithValue("$unitWeightG", model.UnitWeightG);
        command.Parameters.AddWithValue("$qtyPerLayer", model.QtyPerLayer);
        command.Parameters.AddWithValue("$mocraListingNo", model.MocraListingNo);
        command.Parameters.AddWithValue("$isActive", model.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        command.ExecuteNonQuery();

        // ChannelSkuTable 쪽은 신규 CSKU일 때만 자기참조(Msku=Csku)로 동시 등록한다(§3.1). 이미
        // 존재하면 손대지 않는다(CreateIfNew가 존재 시 아무것도 하지 않고 false 반환).
        _channelSkuRepository.CreateIfNew(FbaChannelCode, model.Csku, model.Csku, 0m, model.ItemName);
    }

    public List<FbaCskuModel> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Csku, ItemName, InvoiceDisplayName, Asin, CommodityDescription, HsCode, ItemPrice, Volume, UnitWeightG, QtyPerLayer, MocraListingNo, IsActive, UpdatedAt
            FROM FbaCskuMaster
            """;

        var result = new List<FbaCskuModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadModel(reader));
        }
        return result;
    }

    public FbaCskuModel? GetByCsku(string csku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Csku, ItemName, InvoiceDisplayName, Asin, CommodityDescription, HsCode, ItemPrice, Volume, UnitWeightG, QtyPerLayer, MocraListingNo, IsActive, UpdatedAt
            FROM FbaCskuMaster
            WHERE Csku = $csku
            """;
        command.Parameters.AddWithValue("$csku", csku);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadModel(reader) : null;
    }

    public void Delete(string csku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM FbaCskuMaster WHERE Csku = $csku";
        command.Parameters.AddWithValue("$csku", csku);
        command.ExecuteNonQuery();
    }

    private static FbaCskuModel ReadModel(SqliteDataReader reader) => new()
    {
        Csku = reader.GetString(0),
        ItemName = reader.GetString(1),
        InvoiceDisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
        Asin = reader.GetString(3),
        CommodityDescription = reader.GetString(4),
        HsCode = reader.GetString(5),
        ItemPrice = reader.GetDecimal(6),
        Volume = reader.GetString(7),
        UnitWeightG = reader.GetDouble(8),
        QtyPerLayer = reader.GetInt32(9),
        MocraListingNo = reader.GetString(10),
        IsActive = reader.GetInt32(11) != 0,
        UpdatedAt = reader.IsDBNull(12) || string.IsNullOrEmpty(reader.GetString(12)) ? null : DateTime.Parse(reader.GetString(12)),
    };
}
