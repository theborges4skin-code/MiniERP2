using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// FBO(네이버 풀필먼트) 발주(FboOrder/FboBox/FboBoxItem)에 대한 데이터베이스 작업을 처리한다.
/// 저장은 항상 발주 1건 전체(헤더+박스+품목)를 delete-then-reinsert로 교체한다 — 그리드에서
/// 자유롭게 박스/품목을 추가·삭제한 뒤 한 번에 저장하는 화면 흐름과 맞다(부분 UPDATE보다 단순하고
/// 안전, ExportSummaryDraftRepository.SaveForMarket과 동일한 패턴).
/// </summary>
public class FboOrderRepository
{
    /// <summary>해당 날짜의 다음 발주번호(FBO-yyyyMMdd-순번)를 계산한다.</summary>
    public string GenerateNextFboNo(DateTime date)
    {
        var datePart = date.ToString("yyyyMMdd");
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM FboOrder WHERE FboNo LIKE $prefix";
        command.Parameters.AddWithValue("$prefix", $"FBO-{datePart}-%");
        var count = Convert.ToInt32(command.ExecuteScalar());
        return $"FBO-{datePart}-{count + 1:00}";
    }

    public void SaveOrder(FboOrder order, List<FboBox> boxes, List<FboBoxItem> items)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var orderCommand = connection.CreateCommand())
            {
                orderCommand.Transaction = transaction;
                orderCommand.CommandText = """
                    INSERT INTO FboOrder (FboNo, OrderDate, ChannelId, ReceiverName, Phone, Address, Status, Memo, CreatedAt, UpdatedAt)
                    VALUES ($fboNo, $orderDate, $channelId, $receiverName, $phone, $address, $status, $memo, $createdAt, $updatedAt)
                    ON CONFLICT(FboNo) DO UPDATE SET
                        OrderDate = excluded.OrderDate,
                        ChannelId = excluded.ChannelId,
                        ReceiverName = excluded.ReceiverName,
                        Phone = excluded.Phone,
                        Address = excluded.Address,
                        Status = excluded.Status,
                        Memo = excluded.Memo,
                        UpdatedAt = excluded.UpdatedAt
                    """;
                orderCommand.Parameters.AddWithValue("$fboNo", order.FboNo);
                orderCommand.Parameters.AddWithValue("$orderDate", order.OrderDate.ToString("yyyy-MM-dd"));
                orderCommand.Parameters.AddWithValue("$channelId", order.ChannelId);
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
                deleteItemsCommand.CommandText = "DELETE FROM FboBoxItem WHERE FboNo = $fboNo";
                deleteItemsCommand.Parameters.AddWithValue("$fboNo", order.FboNo);
                deleteItemsCommand.ExecuteNonQuery();
            }

            using (var deleteBoxesCommand = connection.CreateCommand())
            {
                deleteBoxesCommand.Transaction = transaction;
                deleteBoxesCommand.CommandText = "DELETE FROM FboBox WHERE FboNo = $fboNo";
                deleteBoxesCommand.Parameters.AddWithValue("$fboNo", order.FboNo);
                deleteBoxesCommand.ExecuteNonQuery();
            }

            foreach (var box in boxes)
            {
                using var boxCommand = connection.CreateCommand();
                boxCommand.Transaction = transaction;
                boxCommand.CommandText = """
                    INSERT INTO FboBox (FboNo, BoxSeq, ReceiverDisplayName, MatchKey, BoxType, TrackingNo, TrackingLoadedAt, Status)
                    VALUES ($fboNo, $boxSeq, $receiverDisplayName, $matchKey, $boxType, $trackingNo, $trackingLoadedAt, $status)
                    """;
                boxCommand.Parameters.AddWithValue("$fboNo", order.FboNo);
                boxCommand.Parameters.AddWithValue("$boxSeq", box.BoxSeq);
                boxCommand.Parameters.AddWithValue("$receiverDisplayName", box.ReceiverDisplayName);
                boxCommand.Parameters.AddWithValue("$matchKey", box.MatchKey);
                boxCommand.Parameters.AddWithValue("$boxType", box.BoxType);
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
                    INSERT INTO FboBoxItem (FboNo, BoxSeq, ItemSeq, Csku, FboItemCode, ItemName, InvoiceDisplayName, QtyPerBox, Qty, ExpiryDate)
                    VALUES ($fboNo, $boxSeq, $itemSeq, $csku, $fboItemCode, $itemName, $invoiceDisplayName, $qtyPerBox, $qty, $expiryDate)
                    """;
                itemCommand.Parameters.AddWithValue("$fboNo", order.FboNo);
                itemCommand.Parameters.AddWithValue("$boxSeq", item.BoxSeq);
                itemCommand.Parameters.AddWithValue("$itemSeq", item.ItemSeq);
                itemCommand.Parameters.AddWithValue("$csku", item.Csku);
                itemCommand.Parameters.AddWithValue("$fboItemCode", item.FboItemCode);
                itemCommand.Parameters.AddWithValue("$itemName", item.ItemName);
                itemCommand.Parameters.AddWithValue("$invoiceDisplayName", (object?)item.InvoiceDisplayName ?? DBNull.Value);
                itemCommand.Parameters.AddWithValue("$qtyPerBox", item.QtyPerBox);
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

    /// <summary>발주 1건(헤더+박스+품목)을 통째로 삭제한다. 출고확정(이송장번호 등록) 여부 확인/경고는
    /// 호출 측(FboHistoryForm)의 책임이다 — 여기서는 무조건 삭제한다.</summary>
    public void DeleteOrder(string fboNo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM FboBoxItem WHERE FboNo = $fboNo";
                cmd.Parameters.AddWithValue("$fboNo", fboNo);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM FboBox WHERE FboNo = $fboNo";
                cmd.Parameters.AddWithValue("$fboNo", fboNo);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM FboOrder WHERE FboNo = $fboNo";
                cmd.Parameters.AddWithValue("$fboNo", fboNo);
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

    public (FboOrder? Order, List<FboBox> Boxes, List<FboBoxItem> Items) GetOrder(string fboNo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();

        FboOrder? order = null;
        using (var orderCommand = connection.CreateCommand())
        {
            orderCommand.CommandText = """
                SELECT FboNo, OrderDate, ChannelId, ReceiverName, Phone, Address, Status, Memo, CreatedAt, UpdatedAt
                FROM FboOrder WHERE FboNo = $fboNo
                """;
            orderCommand.Parameters.AddWithValue("$fboNo", fboNo);
            using var reader = orderCommand.ExecuteReader();
            if (reader.Read()) order = ReadOrder(reader);
        }

        var boxes = new List<FboBox>();
        using (var boxCommand = connection.CreateCommand())
        {
            boxCommand.CommandText = """
                SELECT FboNo, BoxSeq, ReceiverDisplayName, MatchKey, BoxType, TrackingNo, TrackingLoadedAt, Status
                FROM FboBox WHERE FboNo = $fboNo ORDER BY BoxSeq
                """;
            boxCommand.Parameters.AddWithValue("$fboNo", fboNo);
            using var reader = boxCommand.ExecuteReader();
            while (reader.Read()) boxes.Add(ReadBox(reader));
        }

        var items = new List<FboBoxItem>();
        using (var itemCommand = connection.CreateCommand())
        {
            itemCommand.CommandText = """
                SELECT FboNo, BoxSeq, ItemSeq, Csku, FboItemCode, ItemName, InvoiceDisplayName, QtyPerBox, Qty, ExpiryDate
                FROM FboBoxItem WHERE FboNo = $fboNo ORDER BY BoxSeq, ItemSeq
                """;
            itemCommand.Parameters.AddWithValue("$fboNo", fboNo);
            using var reader = itemCommand.ExecuteReader();
            while (reader.Read()) items.Add(ReadItem(reader));
        }

        return (order, boxes, items);
    }

    /// <summary>
    /// 이송장 결과 매칭(Step5)에서 쓴다 — 아직 이송장번호가 없는(=매칭 대상) 박스를 전부 가져온다.
    /// 결과 파일의 고객주문번호 표기가 DB의 MatchKey와 '#' 유무 등으로 미세하게 다를 수 있어,
    /// SQL로 걸러내지 않고 호출 측(FboTrackingImporter)이 정규화 후 메모리에서 매칭한다.
    /// </summary>
    public List<FboBox> GetPendingBoxes()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT FboNo, BoxSeq, ReceiverDisplayName, MatchKey, BoxType, TrackingNo, TrackingLoadedAt, Status
            FROM FboBox WHERE TrackingNo IS NULL OR TrackingNo = ''
            """;

        var boxes = new List<FboBox>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) boxes.Add(ReadBox(reader));
        return boxes;
    }

    /// <summary>이송장번호를 박스 1건에 적용한다(Step5).</summary>
    public void ApplyTracking(string fboNo, int boxSeq, string trackingNo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE FboBox SET TrackingNo = $trackingNo, TrackingLoadedAt = $loadedAt, Status = '이송장등록'
            WHERE FboNo = $fboNo AND BoxSeq = $boxSeq
            """;
        command.Parameters.AddWithValue("$trackingNo", trackingNo);
        command.Parameters.AddWithValue("$loadedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("$fboNo", fboNo);
        command.Parameters.AddWithValue("$boxSeq", boxSeq);
        command.ExecuteNonQuery();
    }

    /// <summary>이력/조회 화면(Step7)에서 쓴다 — 기간(발주일)·채널로 필터링한 박스-품목 단위 조인 결과.</summary>
    public List<FboHistoryRow> GetHistory(string? channelId, DateTime from, DateTime to)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.FboNo, o.OrderDate, o.ChannelId, b.BoxSeq, b.ReceiverDisplayName, i.Csku, i.ItemName, i.Qty, b.TrackingNo, b.Status
            FROM FboOrder o
            JOIN FboBox b ON b.FboNo = o.FboNo
            JOIN FboBoxItem i ON i.FboNo = b.FboNo AND i.BoxSeq = b.BoxSeq
            WHERE o.OrderDate >= $from AND o.OrderDate <= $to
              AND ($channelId IS NULL OR o.ChannelId = $channelId)
            ORDER BY o.OrderDate DESC, o.FboNo DESC, b.BoxSeq, i.ItemSeq
            """;
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$channelId", (object?)channelId ?? DBNull.Value);

        var result = new List<FboHistoryRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new FboHistoryRow
            {
                FboNo = reader.GetString(0),
                OrderDate = DateTime.Parse(reader.GetString(1)),
                ChannelId = reader.GetString(2),
                BoxSeq = reader.GetInt32(3),
                ReceiverDisplayName = reader.GetString(4),
                Csku = reader.GetString(5),
                ItemName = reader.GetString(6),
                Qty = reader.GetInt32(7),
                TrackingNo = reader.IsDBNull(8) ? null : reader.GetString(8),
                Status = reader.GetString(9),
            });
        }
        return result;
    }

    /// <summary>
    /// "지난 CSKU 불러오기"에서 쓴다 — 최근에 실제로 나간 CSKU 최대 30종을 골라, 각 CSKU마다 가장
    /// 최근 발주일 최대 2건의 박스/품목 스냅샷을 돌려준다. 예: A품목이 7/15(2박스)·6/30(1박스)·
    /// 6/15(5박스)에 나갔다면 7/15·6/30 두 건만 반환(6/15는 세 번째라 제외). 대상 CSKU 30종은
    /// "가장 최근에 나간 날짜" 기준으로 고른다.
    /// </summary>
    public List<FboRecentCskuGroup> GetRecentCskuGroups(int maxCskus = 30, int maxDatesPerCsku = 2)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.OrderDate, b.BoxSeq, b.BoxType, i.ItemSeq, i.Csku, i.FboItemCode, i.ItemName,
                   i.InvoiceDisplayName, i.QtyPerBox, i.Qty, i.ExpiryDate
            FROM FboOrder o
            JOIN FboBox b ON b.FboNo = o.FboNo
            JOIN FboBoxItem i ON i.FboNo = b.FboNo AND i.BoxSeq = b.BoxSeq
            ORDER BY o.OrderDate DESC, i.Csku, b.BoxSeq, i.ItemSeq
            """;

        // (Csku, OrderDate) 단위로 모은다 — 같은 날 여러 박스/줄로 나갔어도 한 스냅샷으로 합친다.
        var groups = new Dictionary<(string Csku, DateTime OrderDate), FboRecentCskuGroup>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var orderDate = DateTime.Parse(reader.GetString(0));
                var boxSeq = reader.GetInt32(1);
                var boxType = reader.GetString(2);
                var csku = reader.GetString(4);

                var key = (csku, orderDate);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new FboRecentCskuGroup { Csku = csku, OrderDate = orderDate };
                    groups[key] = group;
                }

                group.BoxTypeBySeq.TryAdd(boxSeq, boxType);
                group.Items.Add(new FboBoxItem
                {
                    FboNo = string.Empty,
                    BoxSeq = boxSeq,
                    ItemSeq = reader.GetInt32(3),
                    Csku = csku,
                    FboItemCode = reader.GetString(5),
                    ItemName = reader.GetString(6),
                    InvoiceDisplayName = reader.IsDBNull(7) ? null : reader.GetString(7),
                    QtyPerBox = reader.GetInt32(8),
                    Qty = reader.GetInt32(9),
                    ExpiryDate = reader.IsDBNull(10) ? null : reader.GetString(10),
                });
                if (string.IsNullOrEmpty(group.ItemName)) group.ItemName = group.Items[^1].ItemName;
            }
        }

        return groups.Values
            .GroupBy(g => g.Csku)
            .Select(cskuGroup => new
            {
                Csku = cskuGroup.Key,
                LatestDate = cskuGroup.Max(g => g.OrderDate),
                Entries = cskuGroup.OrderByDescending(g => g.OrderDate).Take(maxDatesPerCsku).ToList(),
            })
            .OrderByDescending(x => x.LatestDate)
            .Take(maxCskus)
            .SelectMany(x => x.Entries)
            .OrderByDescending(g => g.OrderDate).ThenBy(g => g.Csku)
            .ToList();
    }

    private static FboOrder ReadOrder(SqliteDataReader reader) => new()
    {
        FboNo = reader.GetString(0),
        OrderDate = DateTime.Parse(reader.GetString(1)),
        ChannelId = reader.GetString(2),
        ReceiverName = reader.GetString(3),
        Phone = reader.GetString(4),
        Address = reader.GetString(5),
        Status = reader.GetString(6),
        Memo = reader.GetString(7),
        CreatedAt = string.IsNullOrEmpty(reader.GetString(8)) ? default : DateTime.Parse(reader.GetString(8)),
        UpdatedAt = reader.IsDBNull(9) || string.IsNullOrEmpty(reader.GetString(9)) ? null : DateTime.Parse(reader.GetString(9)),
    };

    private static FboBox ReadBox(SqliteDataReader reader) => new()
    {
        FboNo = reader.GetString(0),
        BoxSeq = reader.GetInt32(1),
        ReceiverDisplayName = reader.GetString(2),
        MatchKey = reader.GetString(3),
        BoxType = reader.GetString(4),
        TrackingNo = reader.IsDBNull(5) ? null : reader.GetString(5),
        TrackingLoadedAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
        Status = reader.GetString(7),
    };

    private static FboBoxItem ReadItem(SqliteDataReader reader) => new()
    {
        FboNo = reader.GetString(0),
        BoxSeq = reader.GetInt32(1),
        ItemSeq = reader.GetInt32(2),
        Csku = reader.GetString(3),
        FboItemCode = reader.GetString(4),
        ItemName = reader.GetString(5),
        InvoiceDisplayName = reader.IsDBNull(6) ? null : reader.GetString(6),
        QtyPerBox = reader.GetInt32(7),
        Qty = reader.GetInt32(8),
        ExpiryDate = reader.IsDBNull(9) ? null : reader.GetString(9),
    };
}
