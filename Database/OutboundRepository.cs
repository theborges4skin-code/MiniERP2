using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 출고 확정된 주문 상세 내역(=발주/출고 이력)에 대한 데이터베이스 작업을 처리합니다.
/// </summary>
public class OutboundRepository
{
    /// <summary>
    /// 출고 상세 내역 목록을 데이터베이스에 저장합니다(발주확정 시점 = 발주이력의 시작점).
    /// 운송장번호가 이미 입력되어 있으면 "출고확정"으로, 없으면 "발주확정"으로 시작합니다.
    /// 이미 출고확정으로 확정된 건을 다시 저장해도(같은 OrderNo+MskuCode) 상태가 뒤로 되돌아가지
    /// 않도록, 새 운송장번호가 없으면 기존 Status/ConfirmedAt을 그대로 유지합니다.
    /// (ShipmentGroupKey, MskuCode) UNIQUE 충돌 시 ON CONFLICT가 기존 행을 조용히 덮어쓰므로,
    /// 저장 전에 같은 키의 기존 행이 있는지 미리 조회해 OrderNo가 다르면(=서로 다른 주문이 충돌)
    /// 반환값으로 알린다 — 호출 측이 사용자에게 경고할 수 있도록.
    /// </summary>
    public List<OutboundSaveConflict> SaveOutbound(IEnumerable<OutboundDetail> details)
    {
        var detailList = details.ToList();
        var conflicts = new List<OutboundSaveConflict>();

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.Transaction = transaction;
            checkCommand.CommandText = "SELECT OrderNo FROM OutboundDetailTable WHERE ShipmentGroupKey = $key AND MskuCode = $msku";
            var keyParam = checkCommand.Parameters.Add("$key", SqliteType.Text);
            var mskuParam = checkCommand.Parameters.Add("$msku", SqliteType.Text);

            foreach (var detail in detailList)
            {
                var effectiveKey = string.IsNullOrEmpty(detail.ShipmentGroupKey) ? detail.OrderNo : detail.ShipmentGroupKey;
                keyParam.Value = effectiveKey;
                mskuParam.Value = detail.MskuCode;

                if (checkCommand.ExecuteScalar() is string existingOrderNo && existingOrderNo != detail.OrderNo)
                {
                    conflicts.Add(new OutboundSaveConflict
                    {
                        ShipmentGroupKey = effectiveKey,
                        MskuCode = detail.MskuCode,
                        ExistingOrderNo = existingOrderNo,
                        NewOrderNo = detail.OrderNo,
                    });
                }
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO OutboundDetailTable (ChannelCode, OrderNo, ShipmentGroupKey, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName, Remark, PurchaseChannelCode, PurchasePrice, WeightKg)
            VALUES ($channelCode, $orderNo, $shipmentGroupKey, $trackingNo, $mskuCode, $qty, $supplyPrice, $createdAt, $status, $confirmedAt, $recipient, $address, $productName, $remark, $purchaseChannelCode, $purchasePrice, $weightKg)
            ON CONFLICT(ShipmentGroupKey, MskuCode) DO UPDATE SET
                ChannelCode = excluded.ChannelCode,
                OrderNo = excluded.OrderNo,
                TrackingNo = excluded.TrackingNo,
                Qty = excluded.Qty,
                -- 견적기록관리_개발기획서_확정본.md P1: 이미 출고확정된 건을 같은 키로 재저장해도
                -- (가격조정 등 무관한 이유로 흔히 발생) 단가가 조용히 최신값으로 덮이지 않게 한다.
                SupplyPrice = CASE WHEN OutboundDetailTable.Status = '출고확정' THEN OutboundDetailTable.SupplyPrice ELSE excluded.SupplyPrice END,
                Recipient = excluded.Recipient,
                Address = excluded.Address,
                ProductName = excluded.ProductName,
                Remark = excluded.Remark,
                PurchaseChannelCode = excluded.PurchaseChannelCode,
                PurchasePrice = excluded.PurchasePrice,
                WeightKg = excluded.WeightKg,
                Status = CASE WHEN excluded.TrackingNo <> '' THEN '출고확정' ELSE OutboundDetailTable.Status END,
                ConfirmedAt = CASE WHEN excluded.TrackingNo <> '' AND OutboundDetailTable.ConfirmedAt IS NULL THEN excluded.ConfirmedAt ELSE OutboundDetailTable.ConfirmedAt END
            """;

        foreach (var detail in detailList)
        {
            var hasTracking = !string.IsNullOrWhiteSpace(detail.TrackingNo);
            var now = DateTime.Now;

            command.Parameters.Clear();
            command.Parameters.AddWithValue("$channelCode", detail.ChannelCode);
            command.Parameters.AddWithValue("$orderNo", detail.OrderNo);
            command.Parameters.AddWithValue("$shipmentGroupKey", string.IsNullOrEmpty(detail.ShipmentGroupKey) ? detail.OrderNo : detail.ShipmentGroupKey);
            command.Parameters.AddWithValue("$trackingNo", (object?)detail.TrackingNo ?? DBNull.Value);
            command.Parameters.AddWithValue("$mskuCode", detail.MskuCode);
            command.Parameters.AddWithValue("$qty", detail.Qty);
            command.Parameters.AddWithValue("$supplyPrice", detail.SupplyPrice);
            command.Parameters.AddWithValue("$createdAt", now);
            command.Parameters.AddWithValue("$status", hasTracking ? "출고확정" : "발주확정");
            command.Parameters.AddWithValue("$confirmedAt", hasTracking ? now : (object)DBNull.Value);
            command.Parameters.AddWithValue("$recipient", detail.Recipient);
            command.Parameters.AddWithValue("$address", detail.Address);
            command.Parameters.AddWithValue("$productName", detail.ProductName);
            command.Parameters.AddWithValue("$remark", detail.Remark);
            command.Parameters.AddWithValue("$purchaseChannelCode", (object?)detail.PurchaseChannelCode ?? DBNull.Value);
            command.Parameters.AddWithValue("$purchasePrice", (object?)detail.PurchasePrice ?? DBNull.Value);
            command.Parameters.AddWithValue("$weightKg", (object?)detail.WeightKg ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return conflicts;
    }

    /// <summary>
    /// 선택된 발주이력을 "출고확정"으로 수동 확정합니다(운송장번호를 별도로 받지 않는 수기 발송확인용).
    /// </summary>
    public void MarkAsShipped(IEnumerable<long> ids)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE OutboundDetailTable SET Status = '출고확정', ConfirmedAt = $confirmedAt WHERE Id = $id";

        foreach (var id in ids)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$confirmedAt", DateTime.Now);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// 운송장 결과 가져오기로 특정 건의 운송장번호를 확정합니다(수령인 기준 매칭 후 사용자가 고른
    /// 1건에 적용). 운송장번호가 채워지면 항상 "출고확정"으로 바뀝니다.
    /// </summary>
    public void ApplyTrackingNo(long id, string trackingNo)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE OutboundDetailTable SET TrackingNo = $trackingNo, Status = '출고확정', ConfirmedAt = $confirmedAt WHERE Id = $id";
        command.Parameters.AddWithValue("$trackingNo", trackingNo);
        command.Parameters.AddWithValue("$confirmedAt", DateTime.Now);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 발주/출고 이력 관리창에서 수정한 내용(수량/납품가/운송장번호/상태 + B2B 매입처/원가/중량)을
    /// Id 기준으로 저장합니다. 이미 "출고확정" 상태인 행은 납품가(SupplyPrice)를 잠급니다(P3 —
    /// 견적기록관리_개발기획서_확정본.md §7.3) — 확정 이후 단가 변경은 §4.2 ② 소급 경로(미착수)로만
    /// 허용해야 마감 대조 데이터가 흔들리지 않습니다. 반환값은 그 잠김 때문에 요청한 납품가가 실제로는
    /// 반영되지 않았는지를 알려줘, 호출 측(OutboundHistoryForm)이 사용자에게 안내할 수 있게 합니다.
    /// </summary>
    public bool UpdateDetail(OutboundDetail detail)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();

        string? existingStatus = null;
        decimal existingSupplyPrice = 0m;
        using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "SELECT Status, SupplyPrice FROM OutboundDetailTable WHERE Id = $id";
            checkCommand.Parameters.AddWithValue("$id", detail.Id);
            using var reader = checkCommand.ExecuteReader();
            if (reader.Read())
            {
                existingStatus = reader.GetString(0);
                existingSupplyPrice = reader.GetDecimal(1);
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OutboundDetailTable
            SET Qty = $qty,
                SupplyPrice = CASE WHEN Status = '출고확정' THEN SupplyPrice ELSE $supplyPrice END,
                TrackingNo = $trackingNo, Status = $status,
                ConfirmedAt = $confirmedAt, PurchaseChannelCode = $purchaseChannelCode,
                PurchasePrice = $purchasePrice, WeightKg = $weightKg
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$qty", detail.Qty);
        command.Parameters.AddWithValue("$supplyPrice", detail.SupplyPrice);
        command.Parameters.AddWithValue("$purchaseChannelCode", (object?)detail.PurchaseChannelCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$purchasePrice", (object?)detail.PurchasePrice ?? DBNull.Value);
        command.Parameters.AddWithValue("$weightKg", (object?)detail.WeightKg ?? DBNull.Value);
        command.Parameters.AddWithValue("$trackingNo", detail.TrackingNo);
        command.Parameters.AddWithValue("$status", detail.Status);
        command.Parameters.AddWithValue("$confirmedAt", (object?)detail.ConfirmedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", detail.Id);
        command.ExecuteNonQuery();

        return existingStatus == "출고확정" && existingSupplyPrice != detail.SupplyPrice;
    }

    /// <summary>
    /// 선택한 발주/출고 이력을 삭제합니다(되돌릴 수 없으므로 호출 측에서 사용자 확인을 받아야 합니다).
    /// </summary>
    public void DeleteByIds(IEnumerable<long> ids)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM OutboundDetailTable WHERE Id = $id";

        foreach (var id in ids)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// 지정된 채널의, 지정된 기간(포함) 내 출고 상세 내역을 조회합니다(마감 대조용).
    /// </summary>
    public List<OutboundDetail> GetByChannel(string channelCode, DateTime from, DateTime to)
    {
        return GetHistory(channelCode, from, to);
    }

    private const string ClosingCols = "Id, ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName, Remark, PurchaseChannelCode, PurchasePrice, WeightKg, ShipmentGroupKey, CskuCode, ClosingPeriod";

    /// <summary>
    /// 거래처 마감보드(거래처마감보드_개발기획서.md §4)의 귀속월 판정 규칙에 따라 출고확정된 라인을
    /// 조회합니다: ClosingPeriod가 수동 지정되어 있으면 그 값, 아니면 ConfirmedAt의 연월이 period와
    /// 일치하는 건. channelCode가 null이면 전체 채널(수동 거래처는 대상 밖 — MANUAL 파티는
    /// PartnerClosingTable에서 직접 관리한다).
    /// </summary>
    public List<OutboundDetail> GetForClosingPeriod(string? channelCode, string period)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        var channelFilter = string.IsNullOrEmpty(channelCode) ? "" : "AND ChannelCode = $channelCode";
        command.CommandText = $"""
            SELECT {ClosingCols}
            FROM OutboundDetailTable
            WHERE ConfirmedAt IS NOT NULL
              AND (
                    (ClosingPeriod <> '' AND ClosingPeriod = $period)
                 OR (ClosingPeriod = '' AND substr(ConfirmedAt, 1, 7) = $period)
                  )
              {channelFilter}
            ORDER BY ConfirmedAt
            """;
        command.Parameters.AddWithValue("$period", period);
        if (!string.IsNullOrEmpty(channelCode)) command.Parameters.AddWithValue("$channelCode", channelCode);

        var results = new List<OutboundDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadOutboundDetail(reader));
        return results;
    }

    /// <summary>
    /// 발주확정만 되고 아직 출고확정(ConfirmedAt)되지 않은 건을 귀속월 기준으로 조회합니다
    /// (거래처마감보드 §4 "미출고 잔량" — 기본 집계에서는 제외하고 별도 표시하기 위한 용도).
    /// </summary>
    public List<OutboundDetail> GetUnshippedForPeriod(string? channelCode, string period)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        var channelFilter = string.IsNullOrEmpty(channelCode) ? "" : "AND ChannelCode = $channelCode";
        command.CommandText = $"""
            SELECT {ClosingCols}
            FROM OutboundDetailTable
            WHERE ConfirmedAt IS NULL
              AND (
                    (ClosingPeriod <> '' AND ClosingPeriod = $period)
                 OR (ClosingPeriod = '' AND substr(CreatedAt, 1, 7) = $period)
                  )
              {channelFilter}
            ORDER BY CreatedAt
            """;
        command.Parameters.AddWithValue("$period", period);
        if (!string.IsNullOrEmpty(channelCode)) command.Parameters.AddWithValue("$channelCode", channelCode);

        var results = new List<OutboundDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadOutboundDetail(reader));
        return results;
    }

    /// <summary>
    /// 지정된 기간 구간(YYYY-MM, 양끝 포함) 안에 출고확정 활동이 있었던 채널코드 목록입니다
    /// (거래처마감보드 §7 "최근 3개월 활동" 자동 판정용).
    /// </summary>
    public List<string> GetActiveChannelCodesBetween(string fromPeriodInclusive, string toPeriodInclusive)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT ChannelCode FROM OutboundDetailTable
            WHERE ConfirmedAt IS NOT NULL
              AND (
                    (ClosingPeriod <> '' AND ClosingPeriod BETWEEN $from AND $to)
                 OR (ClosingPeriod = '' AND substr(ConfirmedAt, 1, 7) BETWEEN $from AND $to)
                  )
            """;
        command.Parameters.AddWithValue("$from", fromPeriodInclusive);
        command.Parameters.AddWithValue("$to", toPeriodInclusive);

        var results = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(reader.GetString(0));
        return results;
    }

    /// <summary>1회라도 출고확정 이력이 있었던 전체 채널코드입니다(§7 "전체 거래처 보기").</summary>
    public List<string> GetAllActiveChannelCodesEver()
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT ChannelCode FROM OutboundDetailTable WHERE ConfirmedAt IS NOT NULL";

        var results = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(reader.GetString(0));
        return results;
    }

    /// <summary>
    /// 지정된 발주/출고 이력 Id들의 귀속월(ClosingPeriod)을 일괄 고정합니다(§4 수동 귀속월 변경).
    /// </summary>
    public void SetClosingPeriod(IEnumerable<long> ids, string period)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE OutboundDetailTable SET ClosingPeriod = $period WHERE Id = $id";
        var periodParam = command.Parameters.Add("$period", SqliteType.Text);
        var idParam = command.Parameters.Add("$id", SqliteType.Integer);
        periodParam.Value = period;
        foreach (var id in ids)
        {
            idParam.Value = id;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    /// <summary>여러 ShipmentGroupKey에 속한 모든 라인을 기간·채널 무관하게 조회합니다(§6 운임 가중배부 — 한 발송이 여러 귀속월에 걸쳐 있는 경우의 전체 중량 기준을 구하기 위함).</summary>
    public List<OutboundDetail> GetByShipmentGroupKeys(IEnumerable<string> shipmentGroupKeys)
    {
        var keys = shipmentGroupKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (keys.Count == 0) return [];

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        var paramNames = keys.Select((_, i) => $"$k{i}").ToList();
        command.CommandText = $"""
            SELECT {ClosingCols}
            FROM OutboundDetailTable
            WHERE ShipmentGroupKey IN ({string.Join(",", paramNames)})
            """;
        for (var i = 0; i < keys.Count; i++) command.Parameters.AddWithValue(paramNames[i], keys[i]);

        var results = new List<OutboundDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadOutboundDetail(reader));
        return results;
    }

    /// <summary>
    /// 발주/출고 이력을 조회합니다(발주/출고 이력 관리창용). channelCode가 null이면 전체 채널.
    /// </summary>
    public List<OutboundDetail> GetHistory(string? channelCode, DateTime from, DateTime to)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrEmpty(channelCode)
            ? """
                SELECT Id, ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName, Remark, PurchaseChannelCode, PurchasePrice, WeightKg, ShipmentGroupKey, CskuCode, ClosingPeriod
                FROM OutboundDetailTable
                WHERE CreatedAt >= $from AND CreatedAt <= $to
                ORDER BY CreatedAt
                """
            : """
                SELECT Id, ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName, Remark, PurchaseChannelCode, PurchasePrice, WeightKg, ShipmentGroupKey, CskuCode, ClosingPeriod
                FROM OutboundDetailTable
                WHERE ChannelCode = $channelCode AND CreatedAt >= $from AND CreatedAt <= $to
                ORDER BY CreatedAt
                """;
        if (!string.IsNullOrEmpty(channelCode))
        {
            command.Parameters.AddWithValue("$channelCode", channelCode);
        }
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$to", to);

        var results = new List<OutboundDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadOutboundDetail(reader));
        }
        return results;
    }

    /// <summary>
    /// 주어진 주문번호들 중 이미 발주확정/출고확정 이력이 있는 건을 찾는다(채널 무관 — 이력 저장 시의
    /// 충돌 판단 키(OrderNo, MskuCode)와 같은 기준으로 "동일 주문"을 판단). 발주서를 다시 불러왔을 때
    /// 같은 주문을 또 처리하는 건 아닌지 안내하는 데 사용한다(처리 자체를 막지는 않음).
    /// </summary>
    public List<OutboundDetail> FindByOrderNos(IEnumerable<string> orderNos)
    {
        var orderNoList = orderNos.Where(o => !string.IsNullOrWhiteSpace(o)).Distinct().ToList();
        if (orderNoList.Count == 0) return [];

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();

        var paramNames = orderNoList.Select((_, i) => $"$o{i}").ToList();
        command.CommandText = $"""
            SELECT Id, ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName, Remark, PurchaseChannelCode, PurchasePrice, WeightKg, ShipmentGroupKey, CskuCode, ClosingPeriod
            FROM OutboundDetailTable
            WHERE OrderNo IN ({string.Join(",", paramNames)})
            """;
        for (var i = 0; i < orderNoList.Count; i++)
        {
            command.Parameters.AddWithValue(paramNames[i], orderNoList[i]);
        }

        var results = new List<OutboundDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadOutboundDetail(reader));
        }
        return results;
    }

    /// <summary>
    /// 지정된 채널에서 출고 이력이 많은 상위 N개 CSKU를 반환합니다(수동 주문 빠른 추가용).
    /// </summary>
    public List<(string MskuCode, string ProductName, int OrderCount)> GetTopCskusByChannel(
        string channelCode, int topN = 5)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT MskuCode, ProductName, COUNT(*) AS Cnt
            FROM OutboundDetailTable
            WHERE ChannelCode = $channelCode
            GROUP BY MskuCode
            ORDER BY Cnt DESC
            LIMIT $topN
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$topN", topN);

        var results = new List<(string, string, int)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        return results;
    }

    private static OutboundDetail ReadOutboundDetail(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ChannelCode = reader.GetString(1),
        OrderNo = reader.GetString(2),
        TrackingNo = reader.GetString(3),
        MskuCode = reader.GetString(4),
        Qty = reader.GetInt32(5),
        SupplyPrice = reader.GetDecimal(6),
        CreatedAt = reader.GetDateTime(7),
        Status = reader.GetString(8),
        ConfirmedAt = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
        Recipient = reader.GetString(10),
        Address = reader.GetString(11),
        ProductName = reader.GetString(12),
        Remark = reader.GetString(13),
        PurchaseChannelCode = reader.IsDBNull(14) ? null : reader.GetString(14),
        PurchasePrice = reader.IsDBNull(15) ? null : reader.GetDecimal(15),
        WeightKg = reader.IsDBNull(16) ? null : reader.GetDecimal(16),
        ShipmentGroupKey = reader.IsDBNull(17) ? "" : reader.GetString(17),
        CskuCode = reader.IsDBNull(18) ? "" : reader.GetString(18),
        ClosingPeriod = reader.IsDBNull(19) ? "" : reader.GetString(19),
    };
}
