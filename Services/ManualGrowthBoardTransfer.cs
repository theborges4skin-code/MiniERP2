using System.Globalization;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>
/// 확정된 이공그로스수동마감을 거래처 마감보드로 보낸다. 채널명과 같은 이름의 수동(MANUAL) 거래처에
/// 명세표 라인(일자·품목별)을 그대로 확정 스냅샷으로 저장한다 — 마감보드 명세표가 원래 '제트'
/// 명세표와 같은 모양으로 나오도록 일자별 라인을 합치지 않는다. 금액은 VAT포함(매출 = 공급가액+세액).
/// 공급받는자 사업자 정보는 프로필명이 채널명과 같은 거래처 프로필에서 찾는다(DocPartyRepository.FindByCompanyName).
/// </summary>
public class ManualGrowthBoardTransfer(PartnerClosingRepository closingRepo, PartnerMasterRepository masterRepo)
{
    /// <summary>마감보드 헤더. 마감보드 쪽이 이미 확정돼 있으면 InvalidOperationException.</summary>
    public PartnerClosing Transfer(ManualGrowthClosing closing, string partyName)
    {
        if (closing.Status != ManualGrowthClosing.StatusConfirmed)
            throw new InvalidOperationException($"{closing.Period} 이공그로스수동마감이 확정 전입니다. 먼저 [마감 확정]을 하세요.");

        var partyKey = masterRepo.GetOrAddManualPartner(partyName);
        var note = $"이공그로스수동마감: {closing.SourceFileName} [{closing.SourceSheetName}]";
        return closingRepo.ReplaceManualLines(closing.Period, partyKey, partyName, BuildLines(closing), note, confirm: true);
    }

    public static List<PartnerClosingLine> BuildLines(ManualGrowthClosing closing)
    {
        var periodEnd = DateTime.ParseExact(closing.Period, "yyyy-MM", CultureInfo.InvariantCulture).AddMonths(1).AddDays(-1);
        return closing.Lines
            .Where(l => l.Qty != 0 || l.Revenue != 0)
            .Select(l => new PartnerClosingLine
            {
                LineDate = DateTime.TryParseExact(l.LineDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : periodEnd,
                CskuCode = l.CskuCode,
                MasterSku = l.MasterSku,
                ItemName = l.ItemName,
                // 마감보드 라인은 단가×수량 = 공급가합 구조라, 명세표 단가 원문 대신 VAT포함 매출을 수량으로
                // 나눈다. 수량 없이 금액만 있는 행(할인 등)은 수량 1로 둬야 금액이 빠지지 않는다.
                Qty = l.Qty == 0 ? 1 : l.Qty,
                UnitPrice = l.Qty == 0 ? l.Revenue : l.Revenue / l.Qty,
                CostPrice = l.CostPrice,
                Profit = l.Profit,
            })
            .ToList();
    }
}
