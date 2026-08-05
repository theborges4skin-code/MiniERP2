using Microsoft.Data.Sqlite;
using MiniERP2.Models;
using MiniERP2.Utils;

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
            INSERT INTO OutboundDetailTable (ChannelCode, OrderNo, ShipmentGroupKey, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName, Remark, PurchaseChannelCode, PurchasePrice, WeightKg, LineKind)
            VALUES ($channelCode, $orderNo, $shipmentGroupKey, $trackingNo, $mskuCode, $qty, $supplyPrice, $createdAt, $status, $confirmedAt, $recipient, $address, $productName, $remark, $purchaseChannelCode, $purchasePrice, $weightKg, $lineKind)
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
                LineKind = excluded.LineKind,
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
            command.Parameters.AddWithValue("$lineKind", detail.LineKind);
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
    /// 주어진 운송장번호들 중 이미 DB에 존재하는(=이미 등록된) 것만 골라 반환합니다. 운송장 파일
    /// 누락건 점검(TrackingBackfillViewer)에서 파일의 운송장번호 전체를 이 결과와 비교해 미매칭
    /// 후보를 가려냅니다 — 현재 그리드에 로드된 부분집합이 아니라 DB 전체를 대상으로 조회해야
    /// 6월 발주/7월 출고처럼 조회 기간 밖에 있는 기존 등록 건까지 놓치지 않습니다.
    /// </summary>
    public HashSet<string> GetExistingTrackingNos(IEnumerable<string> trackingNos)
    {
        var numbers = trackingNos.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (numbers.Count == 0) return result;

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();

        var paramNames = numbers.Select((_, i) => $"$t{i}").ToList();
        command.CommandText = $"""
            SELECT DISTINCT TrackingNo FROM OutboundDetailTable
            WHERE TrackingNo IN ({string.Join(",", paramNames)})
            """;
        for (var i = 0; i < numbers.Count; i++)
        {
            command.Parameters.AddWithValue(paramNames[i], numbers[i]);
        }

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }
        return result;
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
                PurchasePrice = $purchasePrice, WeightKg = $weightKg,
                Remark = $remark, LineKind = $lineKind
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
        command.Parameters.AddWithValue("$remark", detail.Remark);
        command.Parameters.AddWithValue("$lineKind", detail.LineKind);
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
    public List<OutboundDetail> GetByChannel(string channelCode, DateTime from, DateTime to, LineKindScope scope = LineKindScope.All, string? lineKind = null)
    {
        return GetHistory(channelCode, from, to, scope, lineKind);
    }

    private const string ClosingCols = "Id, ChannelCode, OrderNo, TrackingNo, MskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName, Remark, PurchaseChannelCode, PurchasePrice, WeightKg, ShipmentGroupKey, CskuCode, ClosingPeriod, LineKind";

    /// <summary>
    /// LineKindScope에 따른 WHERE 절 조각을 만듭니다(샘플발송이력관리_개발기획서.md §4.4). 기존
    /// channelFilter와 같은 관례로, 조건이 없으면 빈 문자열을, 있으면 "AND ..."를 반환합니다.
    /// NonSaleOnly + lineKind 지정 시 호출부가 반드시 "$lineKind" 파라미터를 채워야 합니다.
    /// </summary>
    private static string BuildLineKindWhere(LineKindScope scope, string? lineKind) => scope switch
    {
        LineKindScope.SaleOnly => "AND LineKind = ''",
        LineKindScope.NonSaleOnly => string.IsNullOrEmpty(lineKind) ? "AND LineKind <> ''" : "AND LineKind = $lineKind",
        _ => "",
    };

    /// <summary>
    /// 거래처 마감보드(거래처마감보드_개발기획서.md §4)의 귀속월 판정 규칙에 따라 출고확정된 라인을
    /// 조회합니다: ClosingPeriod가 수동 지정되어 있으면 그 값, 아니면 ConfirmedAt의 연월이 period와
    /// 일치하는 건. channelCode가 null이면 전체 채널(수동 거래처는 대상 밖 — MANUAL 파티는
    /// PartnerClosingTable에서 직접 관리한다). 기본 스코프는 SaleOnly라 비매출 라인(샘플발송이력관리_
    /// 개발기획서.md §4.4)은 자동 제외되며, §6 비매출 집계는 NonSaleOnly로 명시 호출한다.
    /// </summary>
    public List<OutboundDetail> GetForClosingPeriod(string? channelCode, string period, LineKindScope scope = LineKindScope.SaleOnly, string? lineKind = null)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        var channelFilter = string.IsNullOrEmpty(channelCode) ? "" : "AND ChannelCode = $channelCode";
        var lineKindFilter = BuildLineKindWhere(scope, lineKind);
        command.CommandText = $"""
            SELECT {ClosingCols}
            FROM OutboundDetailTable
            WHERE ConfirmedAt IS NOT NULL
              AND (
                    (ClosingPeriod <> '' AND ClosingPeriod = $period)
                 OR (ClosingPeriod = '' AND substr(ConfirmedAt, 1, 7) = $period)
                  )
              {channelFilter}
              {lineKindFilter}
            ORDER BY ConfirmedAt
            """;
        command.Parameters.AddWithValue("$period", period);
        if (!string.IsNullOrEmpty(channelCode)) command.Parameters.AddWithValue("$channelCode", channelCode);
        if (scope == LineKindScope.NonSaleOnly && !string.IsNullOrEmpty(lineKind)) command.Parameters.AddWithValue("$lineKind", lineKind);

        var results = new List<OutboundDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadOutboundDetail(reader));
        return results;
    }

    /// <summary>
    /// 발주확정만 되고 아직 출고확정(ConfirmedAt)되지 않은 건을 귀속월 기준으로 조회합니다
    /// (거래처마감보드 §4 "미출고 잔량" — 기본 집계에서는 제외하고 별도 표시하기 위한 용도).
    /// </summary>
    public List<OutboundDetail> GetUnshippedForPeriod(string? channelCode, string period, LineKindScope scope = LineKindScope.SaleOnly, string? lineKind = null)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        var channelFilter = string.IsNullOrEmpty(channelCode) ? "" : "AND ChannelCode = $channelCode";
        var lineKindFilter = BuildLineKindWhere(scope, lineKind);
        command.CommandText = $"""
            SELECT {ClosingCols}
            FROM OutboundDetailTable
            WHERE ConfirmedAt IS NULL
              AND (
                    (ClosingPeriod <> '' AND ClosingPeriod = $period)
                 OR (ClosingPeriod = '' AND substr(CreatedAt, 1, 7) = $period)
                  )
              {channelFilter}
              {lineKindFilter}
            ORDER BY CreatedAt
            """;
        command.Parameters.AddWithValue("$period", period);
        if (!string.IsNullOrEmpty(channelCode)) command.Parameters.AddWithValue("$channelCode", channelCode);
        if (scope == LineKindScope.NonSaleOnly && !string.IsNullOrEmpty(lineKind)) command.Parameters.AddWithValue("$lineKind", lineKind);

        var results = new List<OutboundDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadOutboundDetail(reader));
        return results;
    }

    /// <summary>
    /// 지정된 기간 구간(YYYY-MM, 양끝 포함) 안에 출고 활동(확정 여부 무관)이 있었던 채널코드
    /// 목록입니다(거래처마감보드 §7 "최근 3개월 활동" 자동 판정용). 출고확정을 깜빡 누락한 건도
    /// 마감보드에서 재확인할 수 있어야 하므로, 미확정 건은 CreatedAt 기준으로 판정한다. 기본 스코프는
    /// SaleOnly라 샘플·CS 전용 채널의 활동은 여기서 잡히지 않는다(샘플발송이력관리_개발기획서.md §4.4).
    /// </summary>
    public List<string> GetActiveChannelCodesBetween(string fromPeriodInclusive, string toPeriodInclusive, LineKindScope scope = LineKindScope.SaleOnly, string? lineKind = null)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        var lineKindFilter = BuildLineKindWhere(scope, lineKind);
        command.CommandText = $"""
            SELECT DISTINCT ChannelCode FROM OutboundDetailTable
            WHERE (
                  (ClosingPeriod <> '' AND ClosingPeriod BETWEEN $from AND $to)
               OR (ClosingPeriod = '' AND ConfirmedAt IS NOT NULL AND substr(ConfirmedAt, 1, 7) BETWEEN $from AND $to)
               OR (ClosingPeriod = '' AND ConfirmedAt IS NULL AND substr(CreatedAt, 1, 7) BETWEEN $from AND $to)
                  )
              {lineKindFilter}
            """;
        command.Parameters.AddWithValue("$from", fromPeriodInclusive);
        command.Parameters.AddWithValue("$to", toPeriodInclusive);
        if (scope == LineKindScope.NonSaleOnly && !string.IsNullOrEmpty(lineKind)) command.Parameters.AddWithValue("$lineKind", lineKind);

        var results = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(reader.GetString(0));
        return results;
    }

    /// <summary>
    /// 출고 이력이 1건이라도 있는 전체 채널코드입니다(§7 "전체 거래처 보기"). 출고확정 여부는
    /// 따지지 않는다 — 마감보드는 확정을 깜빡한 건을 잡아내기 위한 화면이므로 미확정 채널도
    /// 노출되어야 한다. 기본 스코프는 SaleOnly(샘플발송이력관리_개발기획서.md §4.4).
    /// </summary>
    public List<string> GetAllActiveChannelCodesEver(LineKindScope scope = LineKindScope.SaleOnly, string? lineKind = null)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        var lineKindFilter = BuildLineKindWhere(scope, lineKind);
        command.CommandText = $"SELECT DISTINCT ChannelCode FROM OutboundDetailTable WHERE 1=1 {lineKindFilter}";
        if (scope == LineKindScope.NonSaleOnly && !string.IsNullOrEmpty(lineKind)) command.Parameters.AddWithValue("$lineKind", lineKind);

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

    /// <summary>
    /// 거래처 마감보드(거래처마감보드_개발기획서.md §2)에서, MiniERP2(OFS)를 경유하지 않은 주문을
    /// 수동으로 발주/출고 이력에 편입시킨다. 마감 대조 시점에 바로 집계 대상이 되어야 하므로 항상
    /// "출고확정" 상태+지정한 날짜로 즉시 확정해 넣는다(발주확정 단계 없음). 이렇게 넣은 건도
    /// OutboundDetailTable의 정상 행이라 발주/출고 이력 관리창(OutboundHistoryForm)에서 채널·기간
    /// 조건으로 조회하면 그대로 나온다 — 별도 저장소를 두지 않는다.
    /// </summary>
    public long AddManualEntry(OutboundDetail detail)
    {
        var orderNo = string.IsNullOrWhiteSpace(detail.OrderNo)
            ? $"수동-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4]}"
            : detail.OrderNo;
        var confirmedAt = detail.ConfirmedAt ?? DateTime.Now;

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO OutboundDetailTable (ChannelCode, OrderNo, ShipmentGroupKey, TrackingNo, MskuCode, CskuCode, Qty, SupplyPrice, CreatedAt, Status, ConfirmedAt, Recipient, Address, ProductName, Remark, PurchaseChannelCode, PurchasePrice, WeightKg)
            VALUES ($channelCode, $orderNo, $shipmentGroupKey, $trackingNo, $mskuCode, $cskuCode, $qty, $supplyPrice, $createdAt, '출고확정', $confirmedAt, $recipient, $address, $productName, $remark, $purchaseChannelCode, $purchasePrice, $weightKg);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$channelCode", detail.ChannelCode);
        command.Parameters.AddWithValue("$orderNo", orderNo);
        command.Parameters.AddWithValue("$shipmentGroupKey", orderNo);
        command.Parameters.AddWithValue("$trackingNo", "(수동입력)");
        command.Parameters.AddWithValue("$mskuCode", detail.MskuCode);
        command.Parameters.AddWithValue("$cskuCode", detail.CskuCode);
        command.Parameters.AddWithValue("$qty", detail.Qty);
        command.Parameters.AddWithValue("$supplyPrice", detail.SupplyPrice);
        command.Parameters.AddWithValue("$createdAt", DateTime.Now);
        command.Parameters.AddWithValue("$confirmedAt", confirmedAt);
        command.Parameters.AddWithValue("$recipient", detail.Recipient);
        command.Parameters.AddWithValue("$address", detail.Address);
        command.Parameters.AddWithValue("$productName", detail.ProductName);
        command.Parameters.AddWithValue("$remark", detail.Remark);
        command.Parameters.AddWithValue("$purchaseChannelCode", (object?)detail.PurchaseChannelCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$purchasePrice", (object?)detail.PurchasePrice ?? DBNull.Value);
        command.Parameters.AddWithValue("$weightKg", (object?)detail.WeightKg ?? DBNull.Value);
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>
    /// 이력채널 이관(샘플발송이력관리_개발기획서.md §5.5) 라인 1건의 소속을 바꾼다. supplyPrice/
    /// lineKind/closingPeriod가 null이면 기존 값을 그대로 유지한다(COALESCE). appendedRemark는
    /// 기존 Remark 말미에 그대로 이어붙는다(이관 흔적 추적 — 별도 이관 이력 테이블을 두지 않는다).
    /// UNIQUE(ShipmentGroupKey, MskuCode) 충돌 시 SqliteException을 그대로 던진다 — 호출 측
    /// (Services.ChannelTransferService)이 라인별로 잡아서 그 건만 스킵 처리한다(부분 성공 허용).
    /// </summary>
    public void TransferLineToChannel(long id, string channelCode, string cskuCode, decimal? supplyPrice, string? lineKind, string? closingPeriod, string appendedRemark)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OutboundDetailTable
            SET ChannelCode = $channelCode,
                MskuCode = $cskuCode,
                CskuCode = $cskuCode,
                SupplyPrice = COALESCE($supplyPrice, SupplyPrice),
                LineKind = COALESCE($lineKind, LineKind),
                ClosingPeriod = COALESCE($closingPeriod, ClosingPeriod),
                Remark = Remark || $appendedRemark
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$cskuCode", cskuCode);
        command.Parameters.AddWithValue("$supplyPrice", (object?)supplyPrice ?? DBNull.Value);
        command.Parameters.AddWithValue("$lineKind", (object?)lineKind ?? DBNull.Value);
        command.Parameters.AddWithValue("$closingPeriod", (object?)closingPeriod ?? DBNull.Value);
        command.Parameters.AddWithValue("$appendedRemark", appendedRemark);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Remark 말미에 텍스트를 이어붙인다(§5.5 — 마감확정으로 이관 제외된 라인에 상호참조 메모를
    /// 남길 때 사용. 별도 UPDATE 메서드로 두는 이유는 TransferLineToChannel과 달리 이 라인은
    /// 채널·CSKU 등을 전혀 바꾸지 않기 때문이다).
    /// </summary>
    public void AppendRemark(long id, string appendedText)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE OutboundDetailTable SET Remark = Remark || $text WHERE Id = $id";
        command.Parameters.AddWithValue("$text", appendedText);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>품목명만 단독으로 고친다(거래처마감보드 §1 — CSKU 미등록 등으로 품목명이 비어있는 경우 정정용).</summary>
    public void UpdateProductName(long id, string productName)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE OutboundDetailTable SET ProductName = $name WHERE Id = $id";
        command.Parameters.AddWithValue("$name", productName);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 출고확정 건의 납품가를 의도적으로 정정한다(거래처마감보드_개발기획서.md §4.2 ② 소급 경로).
    /// UpdateDetail의 확정 잠금(P3 — 재저장 시 조용히 덮이는 것을 막는 보호장치)과 달리, 이 메서드는
    /// 마감 대조 중 발견한 오류를 사용자가 명시적으로 바로잡는 조작이라 잠금을 거치지 않는다.
    /// </summary>
    public void CorrectSupplyPrice(long id, decimal newSupplyPrice)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE OutboundDetailTable SET SupplyPrice = $price WHERE Id = $id";
        command.Parameters.AddWithValue("$price", newSupplyPrice);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// 같은 채널의 같은 CSKU(구버전 데이터는 CskuCode가 비어있고 MskuCode에만 값이 있을 수 있어
    /// 그 경우도 함께 봄)를 가진 건 중, 지정한 날짜(포함) 이후 출고확정된 모든 라인의 납품가를
    /// 한꺼번에 정정한다(§4.2 ② 소급 경로 — 여러 건에 걸쳐 잘못 등록된 단가를 한 번에 바로잡을 때).
    /// </summary>
    public int CorrectSupplyPriceForCskuFromDate(string channelCode, string cskuCode, DateTime fromDateInclusive, decimal newSupplyPrice)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OutboundDetailTable
            SET SupplyPrice = $price
            WHERE ChannelCode = $channelCode
              AND (CskuCode = $csku OR (CskuCode = '' AND MskuCode = $csku))
              AND ConfirmedAt IS NOT NULL
              AND substr(ConfirmedAt, 1, 10) >= $fromDate
            """;
        command.Parameters.AddWithValue("$price", newSupplyPrice);
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$csku", cskuCode);
        command.Parameters.AddWithValue("$fromDate", fromDateInclusive.ToString("yyyy-MM-dd"));
        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// 여러 ShipmentGroupKey에 속한 모든 라인을 기간·채널 무관하게 조회합니다(§6 운임 가중배부 — 한
    /// 발송이 여러 귀속월에 걸쳐 있는 경우의 전체 중량 기준을 구하기 위함). 기본 스코프는 SaleOnly라
    /// 비매출 라인은 분모에서 제외된다 — 그래야 정상품+샘플이 동봉된 송장에서 운임 전액이 정상
    /// 라인에 배부된다(샘플발송이력관리_개발기획서.md §5.6).
    /// </summary>
    public List<OutboundDetail> GetByShipmentGroupKeys(IEnumerable<string> shipmentGroupKeys, LineKindScope scope = LineKindScope.SaleOnly, string? lineKind = null)
    {
        var keys = shipmentGroupKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (keys.Count == 0) return [];

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        var paramNames = keys.Select((_, i) => $"$k{i}").ToList();
        var lineKindFilter = BuildLineKindWhere(scope, lineKind);
        command.CommandText = $"""
            SELECT {ClosingCols}
            FROM OutboundDetailTable
            WHERE ShipmentGroupKey IN ({string.Join(",", paramNames)})
              {lineKindFilter}
            """;
        for (var i = 0; i < keys.Count; i++) command.Parameters.AddWithValue(paramNames[i], keys[i]);
        if (scope == LineKindScope.NonSaleOnly && !string.IsNullOrEmpty(lineKind)) command.Parameters.AddWithValue("$lineKind", lineKind);

        var results = new List<OutboundDetail>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) results.Add(ReadOutboundDetail(reader));
        return results;
    }

    /// <summary>
    /// 발주/출고 이력을 조회합니다(발주/출고 이력 관리창용). channelCode가 null이면 전체 채널. 기본
    /// 스코프는 All이라 이 창은 비매출 건도 그대로 보여준다(샘플발송이력관리_개발기획서.md §4.4) —
    /// 전수 확인·운송장 매칭·오등록 발견이 주 용도인 화면이라 여기서 숨기면 안 된다.
    /// </summary>
    public List<OutboundDetail> GetHistory(string? channelCode, DateTime from, DateTime to, LineKindScope scope = LineKindScope.All, string? lineKind = null)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        var channelFilter = string.IsNullOrEmpty(channelCode) ? "" : "AND ChannelCode = $channelCode";
        var lineKindFilter = BuildLineKindWhere(scope, lineKind);
        command.CommandText = $"""
            SELECT {ClosingCols}
            FROM OutboundDetailTable
            WHERE CreatedAt >= $from AND CreatedAt <= $to
              {channelFilter}
              {lineKindFilter}
            ORDER BY CreatedAt
            """;
        if (!string.IsNullOrEmpty(channelCode))
        {
            command.Parameters.AddWithValue("$channelCode", channelCode);
        }
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$to", to);
        if (scope == LineKindScope.NonSaleOnly && !string.IsNullOrEmpty(lineKind)) command.Parameters.AddWithValue("$lineKind", lineKind);

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
    /// 같은 주문을 또 처리하는 건 아닌지 안내하는 데 사용한다(처리 자체를 막지는 않음). 기본 스코프는
    /// All이다 — 중복 감지는 비매출 건도 예외 없이 걸러야 한다.
    /// </summary>
    public List<OutboundDetail> FindByOrderNos(IEnumerable<string> orderNos)
    {
        var orderNoList = orderNos.Where(o => !string.IsNullOrWhiteSpace(o)).Distinct().ToList();
        if (orderNoList.Count == 0) return [];

        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();

        var paramNames = orderNoList.Select((_, i) => $"$o{i}").ToList();
        command.CommandText = $"""
            SELECT {ClosingCols}
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
    /// 지정된 채널에서 출고 이력이 많은 상위 N개 CSKU를 반환합니다(수동 주문 빠른 추가용). 기본
    /// 스코프는 SaleOnly라 샘플·CS로 지정된 라인은 빈도 집계를 오염시키지 않는다
    /// (샘플발송이력관리_개발기획서.md §4.4).
    /// </summary>
    public List<(string MskuCode, string ProductName, int OrderCount)> GetTopCskusByChannel(
        string channelCode, int topN = 5, LineKindScope scope = LineKindScope.SaleOnly, string? lineKind = null)
    {
        using var connection = SqliteConnectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        var lineKindFilter = BuildLineKindWhere(scope, lineKind);
        command.CommandText = $"""
            SELECT MskuCode, ProductName, COUNT(*) AS Cnt
            FROM OutboundDetailTable
            WHERE ChannelCode = $channelCode
              {lineKindFilter}
            GROUP BY MskuCode
            ORDER BY Cnt DESC
            LIMIT $topN
            """;
        command.Parameters.AddWithValue("$channelCode", channelCode);
        command.Parameters.AddWithValue("$topN", topN);
        if (scope == LineKindScope.NonSaleOnly && !string.IsNullOrEmpty(lineKind)) command.Parameters.AddWithValue("$lineKind", lineKind);

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
        LineKind = reader.IsDBNull(20) ? "" : reader.GetString(20),
    };
}
