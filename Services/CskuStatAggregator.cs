using MiniERP2.Models;

namespace MiniERP2.Services;

/// <summary>
/// 정상 분류된 소스 행을 (파일구분, 채널, CSKU) 단위로 집계한다(CSKU별통계_개발기획서.md §4 — S2).
/// 예외·미매핑 행은 호출 측에서 이미 걸러졌다고 가정하고 여기서 다시 한 번 걸러낸다(방어적).
/// </summary>
public static class CskuStatAggregator
{
    /// <param name="rows">한 배치 내 모든 파일의 파싱 결과(정상/예외/미매핑 섞여 있어도 됨).</param>
    /// <param name="resolveChannelName">ChannelConfig 조회 등 채널명 해석. 미등록이면 ChannelCode를 그대로 반환하도록 호출 측에서 구성한다(§4.4).</param>
    public static List<CskuStatLine> Aggregate(IEnumerable<CskuStatSourceRow> rows, Func<string, string> resolveChannelName)
    {
        return rows
            .Where(r => r.RowClass == CskuStatRowClass.Normal)
            .GroupBy(r => (r.FileKind, r.ChannelCode, r.CskuCode))
            .Select(g =>
            {
                var group = g.ToList();
                // §4.2 — 상품그룹/상품명은 해당 CSKU 행 중 매출액이 가장 큰 행의 값.
                var representative = group.OrderByDescending(r => r.Revenue).First();
                return new CskuStatLine
                {
                    FileKind = g.Key.FileKind,
                    ChannelCode = g.Key.ChannelCode,
                    ChannelName = resolveChannelName(g.Key.ChannelCode),
                    CskuCode = g.Key.CskuCode,
                    ProductGroup = representative.ProductGroup,
                    ProductName = representative.ProductName,
                    RowCount = group.Count,
                    Qty = group.Sum(r => r.Qty),
                    Revenue = group.Sum(r => r.Revenue),
                    Settlement = group.Sum(r => r.Settlement),
                    Shipping = group.Sum(r => r.Shipping),
                    Fee = group.Sum(r => r.Fee),
                    Profit = group.Sum(r => r.Profit),
                };
            })
            .OrderBy(l => l.FileKind)
            .ThenBy(l => l.ChannelCode, StringComparer.Ordinal)
            .ThenBy(l => l.CskuCode, StringComparer.Ordinal)
            .ToList();
    }
}
