using System.Globalization;
using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 거래처 마감보드(거래처마감보드_개발기획서.md §9)의 발행 문서 빌더. 기존 `DocsForm`의 그리드 주입
/// 방식(`OutboundHistoryPickerDialog` 패턴)은 단일 거래처·다이얼로그 상호작용 전제라 다중 거래처
/// 일괄 발행에는 맞지 않아, 여기서는 `DocumentExporter`(§2 "출력 엔진 그대로 사용")를 직접 헤드리스로
/// 호출한다 — 단건이든 배치든 같은 경로로 처리된다.
/// </summary>
public static class PartnerClosingDocumentBuilder
{
    public static TradeStatementDoc BuildTradeStatement(PartnerClosingSummary summary, DocType docType, DocParty supplier, DocParty buyer, List<PartnerClosingMemo>? memos = null)
    {
        var vatExcluded = docType == DocType.TradeStatementVatExcl;
        // 명세표는 항상 "같은 날짜·같은 CSKU"만 합산한다(§3 요청) — 날짜가 다르면 같은 CSKU라도 별도 줄.
        var merged = MergeByCskuAndDate(summary.Lines, summary.Period, ignoreDate: false);
        var (lineNotes, footerLines) = ResolveMemos(merged, memos, forStatement: true);

        return new TradeStatementDoc
        {
            DocType = docType,
            Supplier = supplier,
            Buyer = buyer,
            IssueDate = DateTime.Today,
            StampImagePath = supplier.StampImagePath,
            FooterNote = string.Join("\n", footerLines),
            Lines = merged.Select((m, i) => new DocLineItem
            {
                Year = m.Year,
                Month = m.Month,
                Day = m.Day,
                ItemName = m.ItemName,
                Spec = m.Spec,
                Qty = m.Qty,
                UnitPrice = VatCalculator.ToDisplay(m.UnitPrice, vatExcluded),
                Note = lineNotes.TryGetValue(i, out var notes) ? string.Join("; ", notes) : "",
            }).ToList(),
        };
    }

    /// <summary>
    /// 매출장(내부 손익 검토용)의 단가/원가 기준을 <paramref name="vatExcluded"/>로 고른다 — CSKU
    /// 납품단가는 VAT포함 기준이므로(VatCalculator 주석), true면 10/11로 나눠 공급가 기준으로 보여주고
    /// (부가세는 실제 수익이 아니므로 기본값), false면 CSKU 값 그대로(VAT포함) 보여준다.
    /// <paramref name="ignoreDate"/>가 true면 날짜와 무관하게 CSKU 하나로 전체 기간을 합산하고
    /// (§3 옵션), false(기본)면 명세표와 같은 "날짜·CSKU" 단위로 합산한다.
    /// </summary>
    public static SalesLedgerDoc BuildSalesLedger(PartnerClosingSummary summary, DocParty supplier, DocParty buyer, bool ignoreDate = false, bool vatExcluded = true, List<PartnerClosingMemo>? memos = null)
    {
        var merged = MergeByCskuAndDate(summary.Lines, summary.Period, ignoreDate);
        var (lineNotes, footerLines) = ResolveMemos(merged, memos, forStatement: false);

        return new SalesLedgerDoc
        {
            Supplier = supplier,
            Buyer = buyer,
            IssueDate = DateTime.Today,
            StampImagePath = supplier.StampImagePath,
            IsVatExcluded = vatExcluded,
            FooterNote = string.Join("\n", footerLines),
            Lines = merged.Select((m, i) => new SalesLedgerLineItem
            {
                Year = m.Year,
                Month = m.Month,
                Day = m.Day,
                ItemName = m.ItemName,
                Spec = m.Spec,
                Qty = m.Qty,
                UnitPrice = VatCalculator.ToDisplay(m.UnitPrice, vatExcluded),
                CostPrice = VatCalculator.ToDisplay(m.CostPrice, vatExcluded),
                Note = lineNotes.TryGetValue(i, out var notes) ? string.Join("; ", notes) : "",
            }).ToList(),
        };
    }

    public static string DefaultFileName(PartnerClosingSummary summary, string docLabel)
    {
        var safeName = summary.PartyName;
        foreach (var c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');
        return $"{safeName}_{summary.Period}_{docLabel}.xlsx";
    }

    private sealed record MergedLine(int Year, int Month, int Day, string CskuCode, string ItemName, string Spec, decimal Qty, decimal UnitPrice, decimal CostPrice, List<long> OutboundDetailIds);

    /// <summary>
    /// 같은 (귀속일, CSKU) 라인을 하나로 합산한다(§3). ignoreDate=true면 CSKU만으로 합산하고
    /// 날짜는 귀속월(Period) 1일로 표기하되 "일" 칸은 비워(Day=0) 특정일이 아님을 나타낸다
    /// (DocumentExporter.WriteDataRow가 Day&lt;=0이면 그 칸을 그냥 안 쓴다). 단가/원가는 라인별로
    /// 다를 수 있어(가격 변경 등) 단순 평균이 아니라 "합계 ÷ 합계수량"으로 역산해 총액이 정확히
    /// 보존되게 한다.
    /// </summary>
    private static List<MergedLine> MergeByCskuAndDate(List<PartnerClosingLine> lines, string period, bool ignoreDate)
    {
        var groups = ignoreDate
            ? lines.GroupBy(l => (Date: (DateTime?)null, l.CskuCode))
            : lines.GroupBy(l => (Date: l.LineDate?.Date, l.CskuCode));

        var periodFallback = DateTime.TryParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : DateTime.Today;

        return groups.Select(g =>
            {
                var qty = g.Sum(l => l.Qty);
                var supply = g.Sum(l => l.Qty * l.UnitPrice);
                var cost = g.Sum(l => l.Qty * l.CostPrice);
                var first = g.First();
                var date = g.Key.Date;

                return new MergedLine(
                    date?.Year ?? periodFallback.Year,
                    date?.Month ?? periodFallback.Month,
                    date?.Day ?? 0, // 0이면 "일" 칸을 비워 특정일이 아님을 나타냄
                    g.Key.CskuCode,
                    first.ItemName,
                    first.Spec,
                    qty,
                    qty == 0 ? 0 : supply / qty,
                    qty == 0 ? 0 : cost / qty,
                    g.Where(l => l.OutboundDetailId != null).Select(l => l.OutboundDetailId!.Value).ToList());
            })
            .OrderBy(m => m.Year).ThenBy(m => m.Month).ThenBy(m => m.Day)
            .ThenBy(m => m.CskuCode, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>인라인 비고 칸에 싣기엔 너무 길다고 보는 기준 — 넘으면 하단 메모 섹션으로 뺀다.</summary>
    private const int InlineNoteMaxLength = 40;

    /// <summary>
    /// 메모를 병합된 라인(<paramref name="merged"/>)에 매칭한다. 거래처 전체 메모(OutboundDetailIds
    /// 비어있음)는 항상 하단 메모 섹션(FooterNote)으로 간다. 라인 참조 메모는 참조한 Id들이 정확히
    /// 병합 그룹 하나에만 속하고 텍스트가 짧으면(<see cref="InlineNoteMaxLength"/> 이하) 그 라인의
    /// 비고 칸에 싣고, 여러 병합 그룹에 걸치거나(다중 선택) 너무 길면 하단 메모 섹션에 적용 대상
    /// 라인을 나열해 싣는다(사용자 요청 — "일일히 출력이 어려운 경우 하단 별도 칸 활용").
    /// </summary>
    private static (Dictionary<int, List<string>> LineNotes, List<string> FooterLines) ResolveMemos(
        List<MergedLine> merged, List<PartnerClosingMemo>? memos, bool forStatement)
    {
        var lineNotes = new Dictionary<int, List<string>>();
        var footerLines = new List<string>();
        if (memos == null) return (lineNotes, footerLines);

        foreach (var memo in memos)
        {
            if (forStatement && !memo.ShowOnStatement) continue;
            if (!forStatement && !memo.ShowOnLedger) continue;

            if (memo.IsPartyLevel)
            {
                footerLines.Add(memo.MemoText);
                continue;
            }

            var matchedIndexes = merged
                .Select((m, i) => (m, i))
                .Where(x => memo.OutboundDetailIds.Any(id => x.m.OutboundDetailIds.Contains(id)))
                .Select(x => x.i)
                .Distinct()
                .ToList();

            if (matchedIndexes.Count == 1 && memo.MemoText.Length <= InlineNoteMaxLength)
            {
                var idx = matchedIndexes[0];
                if (!lineNotes.TryGetValue(idx, out var notes)) lineNotes[idx] = notes = [];
                notes.Add(memo.MemoText);
            }
            else
            {
                var refLabel = matchedIndexes.Count == 0
                    ? "대상 라인 확인 불가"
                    : string.Join(", ", matchedIndexes.Select(i => $"{merged[i].Month}/{merged[i].Day} {merged[i].ItemName}"));
                footerLines.Add($"[{refLabel}] {memo.MemoText}");
            }
        }

        return (lineNotes, footerLines);
    }
}
