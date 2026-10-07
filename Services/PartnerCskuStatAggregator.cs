using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>거래처 마감보드 [CSKU별 통계]의 묶음 기준.</summary>
public enum PartnerCskuGroupBy
{
    Csku,
    MasterSku,
}

/// <summary>집계 입력 한 줄 — 어느 거래처의 어떤 마감 라인인지.</summary>
public sealed record PartnerCskuStatInput(string PartyName, PartnerClosingLine Line, bool IsUnshipped);

/// <summary>CSKU(또는 마스터SKU) 하나의 여러 거래처 합산 결과. 금액은 모두 VAT포함 원본 기준이다.</summary>
public class PartnerCskuStatRow
{
    public string Key { get; init; } = string.Empty;
    public string CskuCode { get; init; } = string.Empty;
    public string MasterSku { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public bool IsNonItem { get; init; }
    public int PartyCount { get; init; }
    public string Parties { get; init; } = string.Empty;
    public int LineCount { get; init; }
    public decimal Qty { get; init; }
    public decimal Supply { get; init; }
    public decimal Cost { get; init; }
    public decimal Profit { get; init; }

    public decimal? MarginRate => Supply == 0 ? null : Profit / Supply;
}

/// <summary>
/// 거래처 마감보드에서 고른 여러 거래처의 마감 라인을 CSKU별(또는 마스터SKU별)로 합산한다.
/// 이익은 라인 이익(운임 미반영) 합이다 — 거래처 단위로 배부된 운임은 품목에 나눌 근거가 없어
/// 호출 측에서 합계에만 따로 표시한다.
/// </summary>
public static class PartnerCskuStatAggregator
{
    /// <summary>품목이 아닌 라인(할인/에누리 조정, 택배비·배송비)인지.</summary>
    public static bool IsNonItemLine(PartnerClosingLine line) =>
        line.CskuCode == PartnerClosingRepository.AdjustmentLineCode
        || line.CskuCode == "택배비"
        || string.Equals(line.MasterSku, "shipping", StringComparison.OrdinalIgnoreCase);

    public static string KeyOf(PartnerClosingLine line, PartnerCskuGroupBy groupBy)
    {
        var csku = line.CskuCode.Trim();
        var msku = line.MasterSku.Trim();
        var key = groupBy == PartnerCskuGroupBy.MasterSku
            ? (msku.Length > 0 ? msku : csku)
            : (csku.Length > 0 ? csku : msku);
        return key.Length > 0 ? key : "(코드없음) " + line.ItemName.Trim();
    }

    public static List<PartnerCskuStatRow> Aggregate(IEnumerable<PartnerCskuStatInput> inputs, PartnerCskuGroupBy groupBy)
    {
        return inputs
            .GroupBy(i => KeyOf(i.Line, groupBy), StringComparer.Ordinal)
            .Select(g =>
            {
                var items = g.ToList();
                // 품목명/코드는 공급가가 가장 큰 라인의 값을 대표로 쓴다(CSKU별 통계 §4.2와 같은 규칙).
                var representative = items.OrderByDescending(i => i.Line.Qty * i.Line.UnitPrice).First().Line;
                var parties = items.Select(i => i.PartyName).Distinct(StringComparer.Ordinal).ToList();
                return new PartnerCskuStatRow
                {
                    Key = g.Key,
                    CskuCode = groupBy == PartnerCskuGroupBy.Csku ? g.Key : JoinDistinct(items.Select(i => i.Line.CskuCode)),
                    MasterSku = groupBy == PartnerCskuGroupBy.MasterSku ? g.Key : JoinDistinct(items.Select(i => i.Line.MasterSku)),
                    ItemName = representative.ItemName,
                    IsNonItem = items.All(i => IsNonItemLine(i.Line)),
                    PartyCount = parties.Count,
                    Parties = string.Join(", ", parties),
                    LineCount = items.Count,
                    Qty = items.Sum(i => i.Line.Qty),
                    Supply = items.Sum(i => i.Line.Qty * i.Line.UnitPrice),
                    Cost = items.Sum(i => i.Line.Qty * i.Line.CostPrice),
                    Profit = items.Sum(i => i.Line.Profit),
                };
            })
            .OrderBy(r => r.IsNonItem)
            .ThenByDescending(r => r.Profit)
            .ThenBy(r => r.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static string JoinDistinct(IEnumerable<string> values) =>
        string.Join(", ", values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal));
}

/// <summary>
/// 거래처 마감보드 요약을 [CSKU별 통계]의 소스 행으로 바꾼다 — 온라인 마감/이익분석 결과 파일과 같은
/// 표에서 합산하기 위함이다. 금액은 VAT포함 원본 그대로(온라인 분석결과와 같은 기준), 매출=정산=공급가,
/// 이익=라인 이익(운임 미반영). 택배비·할인 라인은 "제외(배송비 등)"로 분류해 집계에서 빠진다.
/// 채널 경유 거래처는 채널코드, 수동 거래처는 거래처명을 채널 자리에 쓴다.
/// </summary>
public static class PartnerCskuStatSource
{
    public const string PartnerStatus = "거래처마감";

    public static string ChannelCodeOf(PartnerClosingSummary summary) =>
        summary.PartyKey.StartsWith("CH:", StringComparison.Ordinal) ? summary.PartyKey["CH:".Length..] : summary.PartyName;

    public static List<CskuStatSourceRow> ToSourceRows(PartnerClosingSummary summary, string fileName)
    {
        var channelCode = ChannelCodeOf(summary);
        return summary.Lines.Select(l =>
        {
            var nonItem = PartnerCskuStatAggregator.IsNonItemLine(l);
            var supply = l.Qty * l.UnitPrice;
            return new CskuStatSourceRow
            {
                FileName = fileName,
                FileKind = CskuFileKind.Partner,
                ChannelCode = channelCode,
                ProductGroup = "거래처",
                ProductName = l.ItemName,
                OptionName = summary.PartyName,
                CskuCode = PartnerCskuStatAggregator.KeyOf(l, PartnerCskuGroupBy.Csku),
                Msku = l.MasterSku.Trim(),
                Qty = (int)Math.Round(l.Qty, MidpointRounding.AwayFromZero),
                Revenue = supply,
                Settlement = supply,
                Profit = l.Profit,
                Status = nonItem ? "제외(배송비 등)" : PartnerStatus,
                RowClass = nonItem ? CskuStatRowClass.Excluded : CskuStatRowClass.Normal,
            };
        }).ToList();
    }
}
