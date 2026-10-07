using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>
/// 온라인 매출 종합보고서에 필요한 DB 값을 모아 <see cref="OnlineReportBuilder"/>에 넘긴다.
/// 이익·광고비(ProfitFact/AdFact, 당월·전월), 거래처 마감보드(PartnerClosing), CSKU별 통계(당월·전월 최신 배치).
/// </summary>
public class OnlineReportDataSource
{
    private readonly ProfitFactRepository _factRepo = new();
    private readonly PartnerClosingRepository _partnerRepo = new();
    private readonly CskuStatRepository _cskuRepo = new();

    public OnlineReportBuilder.Result Build(string period, OnlineReportConfig config, OnlineReportMonthInput month)
    {
        var previousPeriod = OnlineReportBuilder.PreviousPeriod(period);
        var profit = _factRepo.GetProfitFacts([period]);
        var ads = _factRepo.GetAdFacts([period]);
        var previousProfit = _factRepo.GetProfitFacts([previousPeriod]);

        var closings = _partnerRepo.GetByPeriod(period);
        var partnerRows = closings.Select(c => new OnlineReportPartnerClosing(c.PartyName, c.TotalSupply, c.TotalProfit)).ToList();

        var result = OnlineReportBuilder.Build(period, config, month, profit, ads, previousProfit, partnerRows,
            LoadCsku(period), LoadCsku(previousPeriod));

        // 마감보드에서 아직 확정되지 않은 거래처가 보고서에 들어갔으면 알려준다.
        var draft = closings
            .Where(c => c.Status is not ("확정" or "출력완료"))
            .Where(c => config.Partners.Any(p => p.PartyNameKeywords.Any(k => !string.IsNullOrWhiteSpace(k) && c.PartyName.Contains(k, StringComparison.OrdinalIgnoreCase))))
            .Select(c => $"{c.PartyName}({c.Status})")
            .ToList();
        if (draft.Count > 0)
            result.Warnings.Add($"거래처 마감보드에서 아직 확정 전인 거래처가 포함됐습니다: {string.Join(", ", draft)}");
        return result;
    }

    /// <summary>해당 기간의 가장 최근 CSKU별 통계 배치(같은 달을 여러 번 만들었으면 마지막 것).</summary>
    private List<OnlineReportCskuRow> LoadCsku(string period)
    {
        var statPeriod = OnlineReportBuilder.ToCskuStatPeriod(period);
        var batch = _cskuRepo.GetBatches()
            .Where(b => b.Period == statPeriod || b.Period == period)
            .OrderByDescending(b => b.Id)
            .FirstOrDefault();
        if (batch is null) return [];
        return _cskuRepo.GetLines(batch.Id)
            .Select(l => new OnlineReportCskuRow(l.ChannelCode, l.CskuCode, l.ProductGroup, l.Revenue))
            .ToList();
    }
}
