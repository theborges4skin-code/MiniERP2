using Microsoft.Data.Sqlite;
using MiniERP2.Models;
using MiniERP2.Utils;

namespace MiniERP2.Database;

/// <summary>
/// 아마존 FBA 발주(FbaOrder/FbaBox/FbaBoxItem)에 대한 데이터베이스 작업을 처리한다. 저장은 항상
/// 발주 1건 전체(헤더+박스+품목)를 delete-then-reinsert로 교체한다(FboOrderRepository와 동일 패턴
/// — 그리드에서 자유롭게 박스/품목을 추가·삭제한 뒤 한 번에 저장하는 화면 흐름과 맞다).
/// </summary>
public class FbaOrderRepository
{
    /// <summary>해당 날짜의 다음 발주번호(FBA-yyyyMMdd-순번)를 계산한다.</summary>
    public string GenerateNextFbaNo(DateTime date)
    {
        var datePart = date.ToString("yyyyMMdd");
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM FbaOrder WHERE FbaNo LIKE $prefix";
        command.Parameters.AddWithValue("$prefix", $"FBA-{datePart}-%");
        var count = Convert.ToInt32(command.ExecuteScalar());
        return $"FBA-{datePart}-{count + 1:00}";
    }

    /// <summary>발주번호가 이미 DB에 존재하는지 확인한다. "복사하여 신규 발주"처럼 화면을 여러 개
    /// 동시에 열어둘 수 있는 흐름에서, 창을 연 시점에 미리 계산해 둔 번호가 저장 시점엔 다른 창이
    /// 먼저 저장해 선점했을 수 있다 — 그 경우를 저장 직전에 걸러내기 위함이다.</summary>
    public bool FbaNoExists(string fbaNo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM FbaOrder WHERE FbaNo = $fbaNo";
        command.Parameters.AddWithValue("$fbaNo", fbaNo);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// isNewOrder=true(최초 저장)면 순수 INSERT라 발주번호가 이미 존재하면 예외로 실패한다 — 동시에
    /// 열어둔 다른 발주 작성 창과 번호가 겹쳐도 서로 다른 발주를 조용히 덮어쓰지 않도록 하기 위함
    /// (§3.4). isNewOrder=false(같은 창에서 저장 후 이어서 수정 저장)면 같은 FbaNo를 의도적으로
    /// 다시 저장하는 것이므로 UPSERT한다.
    /// </summary>
    public void SaveOrder(FbaOrder order, List<FbaBox> boxes, List<FbaBoxItem> items, bool isNewOrder = false)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var orderCommand = connection.CreateCommand())
            {
                orderCommand.Transaction = transaction;
                orderCommand.CommandText = isNewOrder
                    ? """
                    INSERT INTO FbaOrder (FbaNo, OrderDate, ShipmentId, ReceiverName, Phone, Address, Status, Memo, CreatedAt, UpdatedAt)
                    VALUES ($fbaNo, $orderDate, $shipmentId, $receiverName, $phone, $address, $status, $memo, $createdAt, $updatedAt)
                    """
                    : """
                    INSERT INTO FbaOrder (FbaNo, OrderDate, ShipmentId, ReceiverName, Phone, Address, Status, Memo, CreatedAt, UpdatedAt)
                    VALUES ($fbaNo, $orderDate, $shipmentId, $receiverName, $phone, $address, $status, $memo, $createdAt, $updatedAt)
                    ON CONFLICT(FbaNo) DO UPDATE SET
                        OrderDate = excluded.OrderDate,
                        ShipmentId = excluded.ShipmentId,
                        ReceiverName = excluded.ReceiverName,
                        Phone = excluded.Phone,
                        Address = excluded.Address,
                        Status = excluded.Status,
                        Memo = excluded.Memo,
                        UpdatedAt = excluded.UpdatedAt
                    """;
                orderCommand.Parameters.AddWithValue("$fbaNo", order.FbaNo);
                orderCommand.Parameters.AddWithValue("$orderDate", order.OrderDate.ToString("yyyy-MM-dd"));
                orderCommand.Parameters.AddWithValue("$shipmentId", (object?)order.ShipmentId ?? DBNull.Value);
                orderCommand.Parameters.AddWithValue("$receiverName", order.ReceiverName);
                orderCommand.Parameters.AddWithValue("$phone", order.Phone);
                orderCommand.Parameters.AddWithValue("$address", order.Address);
                orderCommand.Parameters.AddWithValue("$status", order.Status);
                orderCommand.Parameters.AddWithValue("$memo", order.Memo);
                orderCommand.Parameters.AddWithValue("$createdAt", order.CreatedAt == default ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : order.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                orderCommand.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                orderCommand.ExecuteNonQuery();
            }

            using (var deleteItemsCommand = connection.CreateCommand())
            {
                deleteItemsCommand.Transaction = transaction;
                deleteItemsCommand.CommandText = "DELETE FROM FbaBoxItem WHERE FbaNo = $fbaNo";
                deleteItemsCommand.Parameters.AddWithValue("$fbaNo", order.FbaNo);
                deleteItemsCommand.ExecuteNonQuery();
            }

            using (var deleteBoxesCommand = connection.CreateCommand())
            {
                deleteBoxesCommand.Transaction = transaction;
                deleteBoxesCommand.CommandText = "DELETE FROM FbaBox WHERE FbaNo = $fbaNo";
                deleteBoxesCommand.Parameters.AddWithValue("$fbaNo", order.FbaNo);
                deleteBoxesCommand.ExecuteNonQuery();
            }

            foreach (var box in boxes)
            {
                using var boxCommand = connection.CreateCommand();
                boxCommand.Transaction = transaction;
                boxCommand.CommandText = """
                    INSERT INTO FbaBox (FbaNo, BoxSeq, BoxSpecName, WidthMm, DepthMm, HeightMm, IsCustomSize, WeightG, MatchKey, TrackingNo, TrackingLoadedAt, Status)
                    VALUES ($fbaNo, $boxSeq, $boxSpecName, $widthMm, $depthMm, $heightMm, $isCustomSize, $weightG, $matchKey, $trackingNo, $trackingLoadedAt, $status)
                    """;
                boxCommand.Parameters.AddWithValue("$fbaNo", order.FbaNo);
                boxCommand.Parameters.AddWithValue("$boxSeq", box.BoxSeq);
                boxCommand.Parameters.AddWithValue("$boxSpecName", box.BoxSpecName);
                boxCommand.Parameters.AddWithValue("$widthMm", box.WidthMm);
                boxCommand.Parameters.AddWithValue("$depthMm", box.DepthMm);
                boxCommand.Parameters.AddWithValue("$heightMm", box.HeightMm);
                boxCommand.Parameters.AddWithValue("$isCustomSize", box.IsCustomSize ? 1 : 0);
                boxCommand.Parameters.AddWithValue("$weightG", box.WeightG);
                boxCommand.Parameters.AddWithValue("$matchKey", box.MatchKey);
                boxCommand.Parameters.AddWithValue("$trackingNo", (object?)box.TrackingNo ?? DBNull.Value);
                boxCommand.Parameters.AddWithValue("$trackingLoadedAt", box.TrackingLoadedAt is { } loadedAt ? loadedAt.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                boxCommand.Parameters.AddWithValue("$status", box.Status);
                boxCommand.ExecuteNonQuery();
            }

            foreach (var item in items)
            {
                using var itemCommand = connection.CreateCommand();
                itemCommand.Transaction = transaction;
                itemCommand.CommandText = """
                    INSERT INTO FbaBoxItem (FbaNo, BoxSeq, ItemSeq, Csku, ItemName, InvoiceDisplayName, Asin, CommodityDescription, HsCode, ItemPrice, UnitWeightG, QtyPerLayer, Qty, ExpiryDate)
                    VALUES ($fbaNo, $boxSeq, $itemSeq, $csku, $itemName, $invoiceDisplayName, $asin, $commodityDescription, $hsCode, $itemPrice, $unitWeightG, $qtyPerLayer, $qty, $expiryDate)
                    """;
                itemCommand.Parameters.AddWithValue("$fbaNo", order.FbaNo);
                itemCommand.Parameters.AddWithValue("$boxSeq", item.BoxSeq);
                itemCommand.Parameters.AddWithValue("$itemSeq", item.ItemSeq);
                itemCommand.Parameters.AddWithValue("$csku", item.Csku);
                itemCommand.Parameters.AddWithValue("$itemName", item.ItemName);
                itemCommand.Parameters.AddWithValue("$invoiceDisplayName", (object?)item.InvoiceDisplayName ?? DBNull.Value);
                itemCommand.Parameters.AddWithValue("$asin", item.Asin);
                itemCommand.Parameters.AddWithValue("$commodityDescription", item.CommodityDescription);
                itemCommand.Parameters.AddWithValue("$hsCode", item.HsCode);
                itemCommand.Parameters.AddWithValue("$itemPrice", item.ItemPrice);
                itemCommand.Parameters.AddWithValue("$unitWeightG", item.UnitWeightG);
                itemCommand.Parameters.AddWithValue("$qtyPerLayer", item.QtyPerLayer);
                itemCommand.Parameters.AddWithValue("$qty", item.Qty);
                itemCommand.Parameters.AddWithValue("$expiryDate", (object?)item.ExpiryDate ?? DBNull.Value);
                itemCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>발주 1건(헤더+박스+품목)을 통째로 삭제한다. 출고완료(운송장번호 등록) 여부 확인/경고는
    /// 호출 측(FbaHistoryForm)의 책임이다 — 여기서는 무조건 삭제한다.</summary>
    public void DeleteOrder(string fbaNo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM FbaBoxItem WHERE FbaNo = $fbaNo";
                cmd.Parameters.AddWithValue("$fbaNo", fbaNo);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM FbaBox WHERE FbaNo = $fbaNo";
                cmd.Parameters.AddWithValue("$fbaNo", fbaNo);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM FbaOrder WHERE FbaNo = $fbaNo";
                cmd.Parameters.AddWithValue("$fbaNo", fbaNo);
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public (FbaOrder? Order, List<FbaBox> Boxes, List<FbaBoxItem> Items) GetOrder(string fbaNo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();

        FbaOrder? order = null;
        using (var orderCommand = connection.CreateCommand())
        {
            orderCommand.CommandText = """
                SELECT FbaNo, OrderDate, ShipmentId, ReceiverName, Phone, Address, Status, Memo, CreatedAt, UpdatedAt
                FROM FbaOrder WHERE FbaNo = $fbaNo
                """;
            orderCommand.Parameters.AddWithValue("$fbaNo", fbaNo);
            using var reader = orderCommand.ExecuteReader();
            if (reader.Read()) order = ReadOrder(reader);
        }

        var boxes = new List<FbaBox>();
        using (var boxCommand = connection.CreateCommand())
        {
            boxCommand.CommandText = """
                SELECT FbaNo, BoxSeq, BoxSpecName, WidthMm, DepthMm, HeightMm, IsCustomSize, WeightG, MatchKey, TrackingNo, TrackingLoadedAt, Status
                FROM FbaBox WHERE FbaNo = $fbaNo ORDER BY BoxSeq
                """;
            boxCommand.Parameters.AddWithValue("$fbaNo", fbaNo);
            using var reader = boxCommand.ExecuteReader();
            while (reader.Read()) boxes.Add(ReadBox(reader));
        }

        var items = new List<FbaBoxItem>();
        using (var itemCommand = connection.CreateCommand())
        {
            itemCommand.CommandText = """
                SELECT FbaNo, BoxSeq, ItemSeq, Csku, ItemName, InvoiceDisplayName, Asin, CommodityDescription, HsCode, ItemPrice, UnitWeightG, QtyPerLayer, Qty, ExpiryDate
                FROM FbaBoxItem WHERE FbaNo = $fbaNo ORDER BY BoxSeq, ItemSeq
                """;
            itemCommand.Parameters.AddWithValue("$fbaNo", fbaNo);
            using var reader = itemCommand.ExecuteReader();
            while (reader.Read()) items.Add(ReadItem(reader));
        }

        return (order, boxes, items);
    }

    /// <summary>
    /// 운송장 결과 매칭(§7.2)에서 쓴다 — 아직 운송장번호가 없는(=매칭 대상) 박스를 전부 가져온다.
    /// 결과 파일의 고객주문번호 표기가 DB의 MatchKey와 공백 등으로 미세하게 다를 수 있어, SQL로
    /// 걸러내지 않고 호출 측(FbaTrackingImporter)이 정규화 후 메모리에서 매칭한다.
    /// </summary>
    public List<FbaBox> GetPendingBoxes()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FbaNo, BoxSeq, BoxSpecName, WidthMm, DepthMm, HeightMm, IsCustomSize, WeightG, MatchKey, TrackingNo, TrackingLoadedAt, Status
            FROM FbaBox WHERE TrackingNo IS NULL OR TrackingNo = ''
            """;

        var boxes = new List<FbaBox>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) boxes.Add(ReadBox(reader));
        return boxes;
    }

    /// <summary>운송장번호를 박스 1건에 적용한다(§7.2).</summary>
    public void ApplyTracking(string fbaNo, int boxSeq, string trackingNo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE FbaBox SET TrackingNo = $trackingNo, TrackingLoadedAt = $loadedAt, Status = '운송장등록'
            WHERE FbaNo = $fbaNo AND BoxSeq = $boxSeq
            """;
        command.Parameters.AddWithValue("$trackingNo", trackingNo);
        command.Parameters.AddWithValue("$loadedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$fbaNo", fbaNo);
        command.Parameters.AddWithValue("$boxSeq", boxSeq);
        command.ExecuteNonQuery();
    }

    /// <summary>발주 이력 조회창(§8)에서 Shipment ID를 사후 일괄입력할 때 쓴다 — FbaOrderForm에서
    /// 발주 작성 시점에 입력을 놓쳤거나, 아마존 발급 Shipment ID가 발주 확정 이후에야 나오는 경우
    /// 발주 전체를 다시 열지 않고 이 값만 갱신한다. 각 박스의 MatchKey(고객주문번호, §7.1)도 새
    /// Shipment ID로 다시 채번한다 — 그래야 이후 하배출고이서를 (재)출력할 때 고객주문번호 칸에
    /// "[SEND] '{ShipmentId}' 총 ##박스중 ##번째"가 반영된다(원래 계획했던 동작, FbaOrderForm의
    /// RecalculateMatchKeys와 동일한 채번 규칙). 이미 운송장번호가 등록된 박스도 함께 재채번한다 —
    /// MatchKey는 재출력 표기용일 뿐 매칭 완료 후에는 더 쓰이지 않기 때문이다.</summary>
    public void UpdateShipmentId(string fbaNo, string? shipmentId)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var orderCommand = connection.CreateCommand())
            {
                orderCommand.Transaction = transaction;
                orderCommand.CommandText = "UPDATE FbaOrder SET ShipmentId = $shipmentId WHERE FbaNo = $fbaNo";
                orderCommand.Parameters.AddWithValue("$shipmentId", (object?)shipmentId ?? DBNull.Value);
                orderCommand.Parameters.AddWithValue("$fbaNo", fbaNo);
                orderCommand.ExecuteNonQuery();
            }

            var boxSeqs = new List<int>();
            using (var selectCommand = connection.CreateCommand())
            {
                selectCommand.Transaction = transaction;
                selectCommand.CommandText = "SELECT BoxSeq FROM FbaBox WHERE FbaNo = $fbaNo ORDER BY BoxSeq";
                selectCommand.Parameters.AddWithValue("$fbaNo", fbaNo);
                using var reader = selectCommand.ExecuteReader();
                while (reader.Read()) boxSeqs.Add(reader.GetInt32(0));
            }

            foreach (var boxSeq in boxSeqs)
            {
                using var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = "UPDATE FbaBox SET MatchKey = $matchKey WHERE FbaNo = $fbaNo AND BoxSeq = $boxSeq";
                updateCommand.Parameters.AddWithValue("$matchKey", FbaKeyGenerator.BuildMatchKey(shipmentId, boxSeqs.Count, boxSeq));
                updateCommand.Parameters.AddWithValue("$fbaNo", fbaNo);
                updateCommand.Parameters.AddWithValue("$boxSeq", boxSeq);
                updateCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>이력/조회 화면(§8)에서 쓴다 — 기간(발주일)으로 필터링한 박스-품목 단위 조인 결과.
    /// FBA는 채널 개념이 없어(수취지 1곳 고정) FBO의 채널 필터 인자가 없다.</summary>
    public List<FbaHistoryRow> GetHistory(DateTime from, DateTime to)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.FbaNo, o.OrderDate, o.ShipmentId, b.BoxSeq, i.Csku, i.ItemName, i.Qty, b.TrackingNo, b.Status, i.ItemSeq, i.ExpiryDate
            FROM FbaOrder o
            JOIN FbaBox b ON b.FbaNo = o.FbaNo
            JOIN FbaBoxItem i ON i.FbaNo = b.FbaNo AND i.BoxSeq = b.BoxSeq
            WHERE o.OrderDate >= $from AND o.OrderDate <= $to
            ORDER BY o.OrderDate DESC, o.FbaNo DESC, b.BoxSeq, i.ItemSeq
            """;
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));

        var result = new List<FbaHistoryRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new FbaHistoryRow
            {
                FbaNo = reader.GetString(0),
                OrderDate = DateTime.Parse(reader.GetString(1)),
                ShipmentId = reader.IsDBNull(2) ? null : reader.GetString(2),
                BoxSeq = reader.GetInt32(3),
                Csku = reader.GetString(4),
                ItemName = reader.GetString(5),
                Qty = reader.GetInt32(6),
                TrackingNo = reader.IsDBNull(7) ? null : reader.GetString(7),
                Status = reader.GetString(8),
                ItemSeq = reader.GetInt32(9),
                ExpiryDate = reader.IsDBNull(10) ? null : reader.GetString(10),
            });
        }
        return result;
    }

    private static FbaOrder ReadOrder(SqliteDataReader reader) => new()
    {
        FbaNo = reader.GetString(0),
        OrderDate = DateTime.Parse(reader.GetString(1)),
        ShipmentId = reader.IsDBNull(2) ? null : reader.GetString(2),
        ReceiverName = reader.GetString(3),
        Phone = reader.GetString(4),
        Address = reader.GetString(5),
        Status = reader.GetString(6),
        Memo = reader.GetString(7),
        CreatedAt = string.IsNullOrEmpty(reader.GetString(8)) ? default : DateTime.Parse(reader.GetString(8)),
        UpdatedAt = reader.IsDBNull(9) || string.IsNullOrEmpty(reader.GetString(9)) ? null : DateTime.Parse(reader.GetString(9)),
    };

    private static FbaBox ReadBox(SqliteDataReader reader) => new()
    {
        FbaNo = reader.GetString(0),
        BoxSeq = reader.GetInt32(1),
        BoxSpecName = reader.GetString(2),
        WidthMm = reader.GetDouble(3),
        DepthMm = reader.GetDouble(4),
        HeightMm = reader.GetDouble(5),
        IsCustomSize = reader.GetInt32(6) != 0,
        WeightG = reader.GetDouble(7),
        MatchKey = reader.GetString(8),
        TrackingNo = reader.IsDBNull(9) ? null : reader.GetString(9),
        TrackingLoadedAt = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10)),
        Status = reader.GetString(11),
    };

    private static FbaBoxItem ReadItem(SqliteDataReader reader) => new()
    {
        FbaNo = reader.GetString(0),
        BoxSeq = reader.GetInt32(1),
        ItemSeq = reader.GetInt32(2),
        Csku = reader.GetString(3),
        ItemName = reader.GetString(4),
        InvoiceDisplayName = reader.IsDBNull(5) ? null : reader.GetString(5),
        Asin = reader.GetString(6),
        CommodityDescription = reader.GetString(7),
        HsCode = reader.GetString(8),
        ItemPrice = reader.GetDecimal(9),
        UnitWeightG = reader.GetDouble(10),
        QtyPerLayer = reader.GetInt32(11),
        Qty = reader.GetInt32(12),
        ExpiryDate = reader.IsDBNull(13) ? null : reader.GetString(13),
    };
}
