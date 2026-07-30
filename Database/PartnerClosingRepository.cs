using System.Globalization;
using Microsoft.Data.Sqlite;
using MiniERP2.Models;

namespace MiniERP2.Database;

/// <summary>
/// 거래처 마감보드(거래처마감보드_개발기획서.md)의 기간별 집계·확정/취소·스냅샷 저장을 담당한다.
/// CH(채널 경유) 거래처는 OutboundDetailTable에서 라이브 집계 후 확정 시 스냅샷을 뜬다. MANUAL
/// (미경유) 거래처는 원천 라인이 없으므로 합계를 직접 입력받아 헤더에만 저장한다(§8).
/// </summary>
public class PartnerClosingRepository
{
    private readonly OutboundRepository _outboundRepo = new();
    private readonly OutboundShipmentRepository _shipmentRepo = new();
    private readonly PartnerMasterRepository _masterRepo = new();
    private readonly ChannelSkuRepository _channelSkuRepo = new();
    private readonly ItemRepository _itemRepo = new();

    // ── §7 좌측 목록 판정 ──────────────────────────────────────────────

    /// <summary>
    /// 보드 좌측 목록에 노출할 PartyKey 목록이다. includeAll=false면 즐겨찾기+활성 수동거래처+최근
    /// 3개월 활동 채널(§7), true면 필터 없이 1회라도 거래가 있었던 전체 거래처(전체 거래처 보기).
    /// 이미 이 기간에 헤더가 만들어진 거래처는 필터와 무관하게 항상 포함한다(작업 중 목록에서
    /// 사라지지 않도록).
    /// </summary>
    public List<string> GetVisiblePartyKeys(string period, bool includeAll)
    {
        var keys = new HashSet<string>();

        foreach (var p in _masterRepo.GetFavorites()) keys.Add(p.PartyKey);
        foreach (var p in _masterRepo.GetActiveManualPartners()) keys.Add(p.PartyKey);

        if (includeAll)
        {
            foreach (var ch in _outboundRepo.GetAllActiveChannelCodesEver()) keys.Add($"CH:{ch}");
            foreach (var key in GetAllManualPartyKeysEverClosed()) keys.Add(key);
            foreach (var p in _masterRepo.GetAll().Where(p => p.IsManual)) keys.Add(p.PartyKey);
        }
        else
        {
            var (from, to) = ThreeMonthWindow(period);
            foreach (var ch in _outboundRepo.GetActiveChannelCodesBetween(from, to)) keys.Add($"CH:{ch}");
        }

        foreach (var h in GetByPeriod(period)) keys.Add(h.PartyKey);

        return keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    private static (string from, string to) ThreeMonthWindow(string period)
    {
        var dt = DateTime.ParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture);
        return (dt.AddMonths(-2).ToString("yyyy-MM", CultureInfo.InvariantCulture), period);
    }

    private List<string> GetAllManualPartyKeysEverClosed()
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT PartyKey FROM PartnerClosingTable WHERE IsManual = 1";
        using var reader = cmd.ExecuteReader();
        var list = new List<string>();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    // ── 대조 화면 요약 ─────────────────────────────────────────────────

    /// <summary>
    /// 지정 거래처의 요약을 계산한다. 확정(ConfirmedAt 있음)된 헤더가 있으면 스냅샷 그대로,
    /// 없으면(CH는 항상, MANUAL은 이 기간에 아직 입력한 적 없으면) OutboundDetailTable에서 즉시
    /// 계산한 라이브 집계를 반환한다.
    /// </summary>
    public PartnerClosingSummary GetSummary(string period, string partyKey, string? partyNameHint = null)
    {
        var header = GetHeader(period, partyKey);
        var isManual = partyKey.StartsWith("MANUAL:", StringComparison.Ordinal);

        if (header != null)
        {
            if (header.ConfirmedAt != null) return BuildFromSnapshot(header);
            if (isManual) return BuildFromManualHeader(header);

            var live = BuildLiveSummary(period, partyKey, header.PartyName);
            live.ClosingId = header.Id;
            live.Status = header.Status;
            live.ReconcileNote = header.ReconcileNote;
            return live;
        }

        if (isManual)
        {
            var master = _masterRepo.GetByPartyKey(partyKey);
            return new PartnerClosingSummary
            {
                Period = period,
                PartyKey = partyKey,
                PartyName = master?.PartyName ?? partyNameHint ?? "",
                IsManual = true,
                Status = "미확인",
            };
        }

        return BuildLiveSummary(period, partyKey, partyNameHint ?? StripChannelPrefix(partyKey));
    }

    private static string StripChannelPrefix(string partyKey) =>
        partyKey.StartsWith("CH:", StringComparison.Ordinal) ? partyKey["CH:".Length..] : partyKey;

    private PartnerClosingSummary BuildLiveSummary(string period, string partyKey, string partyName)
    {
        var channelCode = StripChannelPrefix(partyKey);
        var periodLines = _outboundRepo.GetForClosingPeriod(channelCode, period);
        var unshippedCount = _outboundRepo.GetUnshippedForPeriod(channelCode, period).Count;

        var lines = periodLines.Select(BuildLine).ToList();
        var (freight, fallback) = AllocateFreight(periodLines);

        return new PartnerClosingSummary
        {
            Period = period,
            PartyKey = partyKey,
            PartyName = partyName,
            IsManual = false,
            Status = "미확인",
            TotalQty = lines.Sum(l => l.Qty),
            TotalSupply = lines.Sum(l => l.Qty * l.UnitPrice),
            TotalCost = lines.Sum(l => l.Qty * l.CostPrice),
            TotalProfit = lines.Sum(l => l.Profit) - freight,
            FreightAllocated = freight,
            UnshippedCount = unshippedCount,
            HasUnstableKeyLines = periodLines.Any(l => l.ShipmentGroupKey.StartsWith("__row_", StringComparison.Ordinal)),
            FreightFallbackByCount = fallback,
            Lines = lines,
        };
    }

    private PartnerClosingSummary BuildFromManualHeader(PartnerClosing header) => new()
    {
        Period = header.Period,
        PartyKey = header.PartyKey,
        PartyName = header.PartyName,
        IsManual = true,
        ClosingId = header.Id,
        Status = header.Status,
        TotalQty = header.TotalQty,
        TotalSupply = header.TotalSupply,
        TotalCost = header.TotalCost,
        TotalProfit = header.TotalProfit,
        FreightAllocated = header.FreightAllocated,
        ReconcileNote = header.ReconcileNote,
        ConfirmedAt = header.ConfirmedAt,
        DocHistoryId = header.DocHistoryId,
    };

    private PartnerClosingSummary BuildFromSnapshot(PartnerClosing header)
    {
        var lines = GetLinesByClosingId(header.Id);
        return new PartnerClosingSummary
        {
            Period = header.Period,
            PartyKey = header.PartyKey,
            PartyName = header.PartyName,
            IsManual = header.IsManual,
            ClosingId = header.Id,
            Status = header.Status,
            TotalQty = header.TotalQty,
            TotalSupply = header.TotalSupply,
            TotalCost = header.TotalCost,
            TotalProfit = header.TotalProfit,
            FreightAllocated = header.FreightAllocated,
            ReconcileNote = header.ReconcileNote,
            ConfirmedAt = header.ConfirmedAt,
            DocHistoryId = header.DocHistoryId,
            Lines = lines,
        };
    }

    private PartnerClosingLine BuildLine(OutboundDetail od)
    {
        var csku = !string.IsNullOrEmpty(od.CskuCode) ? od.CskuCode : od.MskuCode;
        var masterSku = _channelSkuRepo.ResolveMasterSku(od.ChannelCode, csku);
        var costPrice = od.PurchasePrice ?? _itemRepo.GetBySku(masterSku)?.CostPrice ?? 0m;

        return new PartnerClosingLine
        {
            OutboundDetailId = od.Id,
            LineDate = od.ConfirmedAt,
            CskuCode = csku,
            MasterSku = masterSku,
            ItemName = od.ProductName,
            Spec = "",
            Qty = od.Qty,
            UnitPrice = od.SupplyPrice,
            CostPrice = costPrice,
            Profit = (od.SupplyPrice - costPrice) * od.Qty,
        };
    }

    /// <summary>
    /// §6 운임 배부: 같은 ShipmentGroupKey를 공유하는 모든 라인(기간 무관)의 WeightKg 가중치로
    /// OutboundShipmentTable.FreightCost를 나눈 뒤, 이번 기간분 라인들이 가져가는 몫만 합산한다.
    /// WeightKg이 전혀 없는 그룹은 라인 수 균등 배부로 폴백한다.
    /// </summary>
    private (decimal allocated, bool fallbackByCount) AllocateFreight(List<OutboundDetail> periodLines)
    {
        var groups = periodLines
            .Where(l => !string.IsNullOrEmpty(l.ShipmentGroupKey))
            .GroupBy(l => l.ShipmentGroupKey)
            .ToList();
        if (groups.Count == 0) return (0m, false);

        var shipments = _shipmentRepo.GetByKeys(groups.Select(g => g.Key)).ToDictionary(s => s.ShipmentGroupKey);
        var allLinesByKey = _outboundRepo.GetByShipmentGroupKeys(groups.Select(g => g.Key))
            .GroupBy(l => l.ShipmentGroupKey)
            .ToDictionary(g => g.Key, g => g.ToList());

        var total = 0m;
        var fallback = false;
        foreach (var g in groups)
        {
            if (!shipments.TryGetValue(g.Key, out var shipment) || shipment.FreightCost == 0) continue;

            var allInGroup = allLinesByKey.TryGetValue(g.Key, out var all) ? all : g.ToList();
            var totalWeight = allInGroup.Sum(l => l.WeightKg ?? 0m);

            decimal share;
            if (totalWeight > 0)
            {
                var thisWeight = g.Sum(l => l.WeightKg ?? 0m);
                share = shipment.FreightCost * (thisWeight / totalWeight);
            }
            else
            {
                fallback = true;
                share = allInGroup.Count > 0 ? shipment.FreightCost * g.Count() / allInGroup.Count : 0m;
            }
            total += share;
        }
        return (Math.Round(total, 0, MidpointRounding.AwayFromZero), fallback);
    }

    // ── 마감확정 / 확정취소 ────────────────────────────────────────────

    /// <summary>
    /// CH 거래처를 마감확정한다: 현재 라이브 집계로 라인 스냅샷을 뜨고, 원본 라인 중 ClosingPeriod가
    /// 비어있는 건에 이 기간을 고정 기입한 뒤(§4), 헤더를 확정 상태로 저장한다.
    /// </summary>
    public PartnerClosing Confirm(string period, string partyKey, string partyName)
    {
        var channelCode = StripChannelPrefix(partyKey);
        var periodLines = _outboundRepo.GetForClosingPeriod(channelCode, period);

        var toPin = periodLines.Where(l => string.IsNullOrEmpty(l.ClosingPeriod)).Select(l => l.Id).ToList();
        if (toPin.Count > 0) _outboundRepo.SetClosingPeriod(toPin, period);

        var lines = periodLines.Select(BuildLine).ToList();
        var (freight, _) = AllocateFreight(periodLines);

        var header = GetHeader(period, partyKey) ?? new PartnerClosing { Period = period, PartyKey = partyKey };
        header.PartyName = partyName;
        header.IsManual = false;
        header.Status = "확정";
        header.TotalQty = lines.Sum(l => l.Qty);
        header.TotalSupply = lines.Sum(l => l.Qty * l.UnitPrice);
        header.TotalCost = lines.Sum(l => l.Qty * l.CostPrice);
        header.TotalProfit = lines.Sum(l => l.Profit) - freight;
        header.FreightAllocated = freight;
        header.ConfirmedAt = DateTime.Now;

        SaveHeader(header);
        DeleteLinesByClosingId(header.Id);
        InsertLines(header.Id, lines);

        return header;
    }

    /// <summary>MANUAL 거래처를 마감확정한다(§8 — 원천 라인이 없으므로 직접 입력한 합계를 그대로 고정).</summary>
    public PartnerClosing ConfirmManual(string period, string partyKey, string partyName, decimal totalQty, decimal totalSupply, decimal totalProfit, string reconcileNote)
    {
        var header = GetHeader(period, partyKey) ?? new PartnerClosing { Period = period, PartyKey = partyKey };
        header.PartyName = partyName;
        header.IsManual = true;
        header.Status = "확정";
        header.TotalQty = totalQty;
        header.TotalSupply = totalSupply;
        header.TotalCost = 0;
        header.TotalProfit = totalProfit;
        header.FreightAllocated = 0;
        header.ReconcileNote = reconcileNote;
        header.ConfirmedAt = DateTime.Now;

        SaveHeader(header);
        return header;
    }

    /// <summary>대조 진행 중(미확인/대조중) 상태의 MANUAL 헤더를 저장한다. 확정은 ConfirmManual로만 한다.</summary>
    public PartnerClosing SaveManualDraft(string period, string partyKey, string partyName, decimal totalQty, decimal totalSupply, decimal totalProfit, string status, string reconcileNote)
    {
        var header = GetHeader(period, partyKey) ?? new PartnerClosing { Period = period, PartyKey = partyKey };
        header.PartyName = partyName;
        header.IsManual = true;
        header.Status = status;
        header.TotalQty = totalQty;
        header.TotalSupply = totalSupply;
        header.TotalProfit = totalProfit;
        header.ReconcileNote = reconcileNote;
        SaveHeader(header);
        return header;
    }

    /// <summary>
    /// 확정을 취소한다: 라인 스냅샷을 삭제하고 상태를 대조중으로 되돌린다(§7). 이미 발행완료였어도
    /// 진행하며(발행 문서 자체는 DocHistoryTable에 남는다), 그 경고는 호출 측(UI)의 책임이다.
    /// </summary>
    public void Cancel(long closingId)
    {
        DeleteLinesByClosingId(closingId);

        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE PartnerClosingTable SET Status = '대조중', ConfirmedAt = NULL WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", closingId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>귀속월을 수동으로 재지정한다(§4 우클릭 [귀속월 변경]).</summary>
    public void ReassignPeriod(IEnumerable<long> outboundDetailIds, string newPeriod) =>
        _outboundRepo.SetClosingPeriod(outboundDetailIds, newPeriod);

    public void SetReconcileNote(long closingId, string note)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE PartnerClosingTable SET ReconcileNote = $note WHERE Id = $id";
        cmd.Parameters.AddWithValue("$note", note);
        cmd.Parameters.AddWithValue("$id", closingId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// CH 거래처의 비고를 저장한다. 아직 이 기간의 헤더가 없으면(대조만 하고 확정 전) 대조중
    /// 상태로 빈 헤더를 먼저 만든다.
    /// </summary>
    public void SetReconcileNoteForParty(string period, string partyKey, string partyName, string note)
    {
        var header = GetHeader(period, partyKey);
        if (header == null)
        {
            header = new PartnerClosing { Period = period, PartyKey = partyKey, PartyName = partyName, Status = "대조중" };
            SaveHeader(header);
        }
        SetReconcileNote(header.Id, note);
    }

    /// <summary>발행 완료 처리: DocHistoryId를 연결하고 상태를 발행완료로 바꾼다(§9).</summary>
    public void MarkPublished(long closingId, long docHistoryId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE PartnerClosingTable SET Status = '발행완료', DocHistoryId = $docId WHERE Id = $id";
        cmd.Parameters.AddWithValue("$docId", docHistoryId);
        cmd.Parameters.AddWithValue("$id", closingId);
        cmd.ExecuteNonQuery();
    }

    // ── 헤더/라인 CRUD ─────────────────────────────────────────────────

    private const string HeaderCols = "Id, Period, PartyKey, PartyName, IsManual, Status, TotalQty, TotalSupply, TotalCost, TotalProfit, FreightAllocated, ReconcileNote, ConfirmedAt, DocHistoryId";

    public List<PartnerClosing> GetByPeriod(string period)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {HeaderCols} FROM PartnerClosingTable WHERE Period = $period";
        cmd.Parameters.AddWithValue("$period", period);
        using var reader = cmd.ExecuteReader();
        var list = new List<PartnerClosing>();
        while (reader.Read()) list.Add(MapHeader(reader));
        return list;
    }

    public PartnerClosing? GetHeader(string period, string partyKey)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {HeaderCols} FROM PartnerClosingTable WHERE Period = $period AND PartyKey = $key";
        cmd.Parameters.AddWithValue("$period", period);
        cmd.Parameters.AddWithValue("$key", partyKey);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapHeader(reader) : null;
    }

    public List<PartnerClosingLine> GetLinesByClosingId(long closingId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, ClosingId, OutboundDetailId, LineDate, CskuCode, MasterSku, ItemName, Spec, Qty, UnitPrice, CostPrice, Profit
            FROM PartnerClosingLineTable WHERE ClosingId = $id ORDER BY LineDate, Id
            """;
        cmd.Parameters.AddWithValue("$id", closingId);
        using var reader = cmd.ExecuteReader();
        var list = new List<PartnerClosingLine>();
        while (reader.Read()) list.Add(MapLine(reader));
        return list;
    }

    private void SaveHeader(PartnerClosing h)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        if (h.Id == 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO PartnerClosingTable (Period, PartyKey, PartyName, IsManual, Status, TotalQty, TotalSupply, TotalCost, TotalProfit, FreightAllocated, ReconcileNote, ConfirmedAt, DocHistoryId)
                VALUES ($period, $key, $name, $manual, $status, $qty, $supply, $cost, $profit, $freight, $note, $confirmedAt, $docId);
                SELECT last_insert_rowid();
                """;
            BindHeader(cmd, h);
            h.Id = (long)cmd.ExecuteScalar()!;
        }
        else
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE PartnerClosingTable SET
                    PartyName = $name, IsManual = $manual, Status = $status, TotalQty = $qty,
                    TotalSupply = $supply, TotalCost = $cost, TotalProfit = $profit,
                    FreightAllocated = $freight, ReconcileNote = $note, ConfirmedAt = $confirmedAt,
                    DocHistoryId = $docId
                WHERE Id = $id
                """;
            BindHeader(cmd, h);
            cmd.Parameters.AddWithValue("$id", h.Id);
            cmd.ExecuteNonQuery();
        }
    }

    private static void BindHeader(SqliteCommand cmd, PartnerClosing h)
    {
        cmd.Parameters.AddWithValue("$period", h.Period);
        cmd.Parameters.AddWithValue("$key", h.PartyKey);
        cmd.Parameters.AddWithValue("$name", h.PartyName);
        cmd.Parameters.AddWithValue("$manual", h.IsManual ? 1 : 0);
        cmd.Parameters.AddWithValue("$status", h.Status);
        cmd.Parameters.AddWithValue("$qty", h.TotalQty);
        cmd.Parameters.AddWithValue("$supply", h.TotalSupply);
        cmd.Parameters.AddWithValue("$cost", h.TotalCost);
        cmd.Parameters.AddWithValue("$profit", h.TotalProfit);
        cmd.Parameters.AddWithValue("$freight", h.FreightAllocated);
        cmd.Parameters.AddWithValue("$note", h.ReconcileNote);
        cmd.Parameters.AddWithValue("$confirmedAt", (object?)h.ConfirmedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$docId", (object?)h.DocHistoryId ?? DBNull.Value);
    }

    private void DeleteLinesByClosingId(long closingId)
    {
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM PartnerClosingLineTable WHERE ClosingId = $id";
        cmd.Parameters.AddWithValue("$id", closingId);
        cmd.ExecuteNonQuery();
    }

    private void InsertLines(long closingId, List<PartnerClosingLine> lines)
    {
        if (lines.Count == 0) return;
        using var conn = SqliteConnectionFactory.OpenConnection();
        using var transaction = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO PartnerClosingLineTable (ClosingId, OutboundDetailId, LineDate, CskuCode, MasterSku, ItemName, Spec, Qty, UnitPrice, CostPrice, Profit)
            VALUES ($closingId, $outboundId, $lineDate, $csku, $masterSku, $itemName, $spec, $qty, $unitPrice, $costPrice, $profit)
            """;
        var closingIdParam = cmd.Parameters.Add("$closingId", SqliteType.Integer);
        var outboundIdParam = cmd.Parameters.Add("$outboundId", SqliteType.Integer);
        var lineDateParam = cmd.Parameters.Add("$lineDate", SqliteType.Text);
        var cskuParam = cmd.Parameters.Add("$csku", SqliteType.Text);
        var masterSkuParam = cmd.Parameters.Add("$masterSku", SqliteType.Text);
        var itemNameParam = cmd.Parameters.Add("$itemName", SqliteType.Text);
        var specParam = cmd.Parameters.Add("$spec", SqliteType.Text);
        var qtyParam = cmd.Parameters.Add("$qty", SqliteType.Real);
        var unitPriceParam = cmd.Parameters.Add("$unitPrice", SqliteType.Real);
        var costPriceParam = cmd.Parameters.Add("$costPrice", SqliteType.Real);
        var profitParam = cmd.Parameters.Add("$profit", SqliteType.Real);

        foreach (var line in lines)
        {
            closingIdParam.Value = closingId;
            outboundIdParam.Value = (object?)line.OutboundDetailId ?? DBNull.Value;
            lineDateParam.Value = line.LineDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value;
            cskuParam.Value = line.CskuCode;
            masterSkuParam.Value = line.MasterSku;
            itemNameParam.Value = line.ItemName;
            specParam.Value = line.Spec;
            qtyParam.Value = line.Qty;
            unitPriceParam.Value = line.UnitPrice;
            costPriceParam.Value = line.CostPrice;
            profitParam.Value = line.Profit;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static PartnerClosing MapHeader(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Period = r.GetString(1),
        PartyKey = r.GetString(2),
        PartyName = r.IsDBNull(3) ? "" : r.GetString(3),
        IsManual = r.GetInt32(4) == 1,
        Status = r.GetString(5),
        TotalQty = r.GetDecimal(6),
        TotalSupply = r.GetDecimal(7),
        TotalCost = r.GetDecimal(8),
        TotalProfit = r.GetDecimal(9),
        FreightAllocated = r.GetDecimal(10),
        ReconcileNote = r.IsDBNull(11) ? "" : r.GetString(11),
        ConfirmedAt = r.IsDBNull(12) ? null : DateTime.Parse(r.GetString(12), CultureInfo.InvariantCulture),
        DocHistoryId = r.IsDBNull(13) ? null : r.GetInt64(13),
    };

    private static PartnerClosingLine MapLine(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        ClosingId = r.GetInt64(1),
        OutboundDetailId = r.IsDBNull(2) ? null : r.GetInt64(2),
        LineDate = r.IsDBNull(3) ? null : DateTime.Parse(r.GetString(3), CultureInfo.InvariantCulture),
        CskuCode = r.GetString(4),
        MasterSku = r.GetString(5),
        ItemName = r.GetString(6),
        Spec = r.GetString(7),
        Qty = r.GetDecimal(8),
        UnitPrice = r.GetDecimal(9),
        CostPrice = r.GetDecimal(10),
        Profit = r.GetDecimal(11),
    };
}
