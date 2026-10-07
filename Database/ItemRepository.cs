using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

public class ItemRepository
{
    public void Upsert(ItemModel item)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            var existing = GetBySku(connection, item.Sku);
            if (existing is not null && existing.CostPrice != item.CostPrice)
            {
                using var historyCommand = connection.CreateCommand();
                historyCommand.Transaction = transaction;
                historyCommand.CommandText = """
                    INSERT INTO ItemCostHistory (Sku, OldCost, NewCost, ChangedAt)
                    VALUES ($sku, $oldCost, $newCost, $changedAt)
                    """;
                historyCommand.Parameters.AddWithValue("$sku", item.Sku);
                historyCommand.Parameters.AddWithValue("$oldCost", existing.CostPrice);
                historyCommand.Parameters.AddWithValue("$newCost", item.CostPrice);
                historyCommand.Parameters.AddWithValue("$changedAt", DateTime.Now);
                historyCommand.ExecuteNonQuery();
            }

            using var upsertCommand = connection.CreateCommand();
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText = """
                INSERT INTO ItemTable (Sku, ItemName, CostPrice, Reserve1, Reserve2, Reserve3, ProductGroup, Unit, AmazonGroup)
                VALUES ($sku, $itemName, $costPrice, $reserve1, $reserve2, $reserve3, $productGroup, $unit, $amazonGroup)
                ON CONFLICT(Sku) DO UPDATE SET
                    ItemName = excluded.ItemName,
                    CostPrice = excluded.CostPrice,
                    Reserve1 = excluded.Reserve1,
                    Reserve2 = excluded.Reserve2,
                    Reserve3 = excluded.Reserve3,
                    ProductGroup = excluded.ProductGroup,
                    Unit = excluded.Unit,
                    -- 아마존상품그룹을 모르는 호출부(신규 품목 다이얼로그·레거시 이관 등)가 null로 덮어써 지우지 않도록,
                    -- null이면 기존 값을 유지한다. 지우려면 빈 문자열을 넘긴다.
                    AmazonGroup = COALESCE(excluded.AmazonGroup, ItemTable.AmazonGroup)
                """;
            upsertCommand.Parameters.AddWithValue("$sku", item.Sku);
            upsertCommand.Parameters.AddWithValue("$itemName", item.ItemName);
            upsertCommand.Parameters.AddWithValue("$costPrice", item.CostPrice);
            upsertCommand.Parameters.AddWithValue("$reserve1", (object?)item.Reserve1 ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$reserve2", (object?)item.Reserve2 ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$reserve3", (object?)item.Reserve3 ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$productGroup", (object?)item.ProductGroup ?? DBNull.Value);
            upsertCommand.Parameters.AddWithValue("$unit", item.Unit);
            upsertCommand.Parameters.AddWithValue("$amazonGroup", (object?)item.AmazonGroup ?? DBNull.Value);
            upsertCommand.ExecuteNonQuery();

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 마스터SKU 코드를 바꾼다. ItemTable의 PK를 새 값으로 바꾸는 것과 같아서, 이 코드를
    /// 참조하는 다른 표(거래처별 CSKU 매핑, B2B 매입 단가, 견적 라인, Settlement 전용 매핑 규칙,
    /// 미확정 거래처 마감 라인)도 한 트랜잭션 안에서 함께 바꿔야 한다. 원가 변경 이력
    /// (ItemCostHistory)도 "이 품목의 과거 이력"을 찾는 조회 키일 뿐이라 함께 바꾼다.
    /// 반대로 ChannelSkuFieldHistory 등 "그 시점에 실제로 어떤 값이었는지"를 남기는 감사로그와,
    /// CSKU 코드를 저장하는 컬럼(RuleXxx.TargetSku, ChannelSkuPriceHistory.Msku — 이름과 달리
    /// 마스터SKU가 아니라 CSKU 코드를 담고 있음, OutboundDetailTable.MskuCode 등)은 대상이 아니므로
    /// 손대지 않는다. 확정된 거래처 마감의 라인은 발행 시점 스냅샷이라 일부러 갱신하지 않는다.
    /// </summary>
    public (bool Success, string Message) Rename(string oldSku, string newSku)
    {
        newSku = newSku.Trim();
        if (string.IsNullOrWhiteSpace(newSku)) return (false, "새 SKU 코드를 입력하세요.");
        if (newSku == oldSku) return (false, "기존 SKU 코드와 같습니다.");

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            if (GetBySku(connection, oldSku) is null)
                return (false, $"'{oldSku}' 품목을 찾을 수 없습니다.");
            if (GetBySku(connection, newSku) is not null)
                return (false, $"'{newSku}' 코드가 이미 사용 중입니다.");

            void Update(string sql)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$old", oldSku);
                command.Parameters.AddWithValue("$new", newSku);
                command.ExecuteNonQuery();
            }

            Update("UPDATE ItemTable SET Sku = $new WHERE Sku = $old");
            Update("UPDATE ItemCostHistory SET Sku = $new WHERE Sku = $old");
            Update("UPDATE ChannelSkuTable SET Msku = $new WHERE Msku = $old");
            Update("UPDATE PurchaseSkuTable SET Msku = $new WHERE Msku = $old");
            Update("UPDATE PurchaseSkuPriceHistory SET Msku = $new WHERE Msku = $old");
            Update("UPDATE PriceQuoteLineTable SET Msku = $new WHERE Msku = $old");
            Update("UPDATE RuleCondition SET TargetMsku = $new WHERE TargetMsku = $old");
            Update("""
                UPDATE PartnerClosingLineTable SET MasterSku = $new
                WHERE MasterSku = $old
                  AND ClosingId IN (SELECT Id FROM PartnerClosingTable WHERE ConfirmedAt IS NULL)
                """);

            transaction.Commit();
            return (true, $"'{oldSku}' → '{newSku}'(으)로 변경되었습니다.");
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void Delete(string sku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            // 1. 원가 변경 이력을 먼저 삭제합니다.
            using var historyCommand = connection.CreateCommand();
            historyCommand.Transaction = transaction;
            historyCommand.CommandText = "DELETE FROM ItemCostHistory WHERE Sku = $sku";
            historyCommand.Parameters.AddWithValue("$sku", sku);
            historyCommand.ExecuteNonQuery();

            // 2. 마스터 품목을 삭제합니다.
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM ItemTable WHERE Sku = $sku";
            deleteCommand.Parameters.AddWithValue("$sku", sku);
            deleteCommand.ExecuteNonQuery();

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public ItemModel? GetBySku(string sku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        return GetBySku(connection, sku);
    }

    public List<ItemModel> GetAll()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Sku, ItemName, CostPrice, Reserve1, Reserve2, Reserve3, ProductGroup, Unit, AmazonGroup FROM ItemTable";

        var items = new List<ItemModel>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(ReadItem(reader));
        }
        return items;
    }

    public List<ItemCostHistory> GetCostHistory(string sku)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Sku, OldCost, NewCost, ChangedAt
            FROM ItemCostHistory
            WHERE Sku = $sku
            ORDER BY Id
            """;
        command.Parameters.AddWithValue("$sku", sku);

        var history = new List<ItemCostHistory>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            history.Add(new ItemCostHistory
            {
                Id = reader.GetInt64(0),
                Sku = reader.GetString(1),
                OldCost = reader.GetDecimal(2),
                NewCost = reader.GetDecimal(3),
                ChangedAt = reader.GetDateTime(4),
            });
        }
        return history;
    }

    private static ItemModel? GetBySku(SqliteConnection connection, string sku)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Sku, ItemName, CostPrice, Reserve1, Reserve2, Reserve3, ProductGroup, Unit, AmazonGroup FROM ItemTable WHERE Sku = $sku";
        command.Parameters.AddWithValue("$sku", sku);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadItem(reader) : null;
    }

    private static ItemModel ReadItem(SqliteDataReader reader) => new()
    {
        Sku = reader.GetString(0),
        ItemName = reader.GetString(1),
        CostPrice = reader.GetDecimal(2),
        Reserve1 = reader.IsDBNull(3) ? null : reader.GetString(3),
        Reserve2 = reader.IsDBNull(4) ? null : reader.GetString(4),
        Reserve3 = reader.IsDBNull(5) ? null : reader.GetString(5),
        ProductGroup = reader.IsDBNull(6) ? null : reader.GetString(6),
        Unit = reader.IsDBNull(7) ? "kg" : reader.GetString(7),
        AmazonGroup = reader.IsDBNull(8) ? null : reader.GetString(8),
    };
}
