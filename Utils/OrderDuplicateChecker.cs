using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 발주 파일을 불러왔을 때 "이미 발주/출고 이력이 있는 건"을 골라 OFS 그리드의 "중복" 열에 표시할
/// 문구를 채운다. 같은 곳으로 두 번 출고하는 정상 거래도 있으므로 판정 결과로 처리를 막지는 않고
/// (사용자 요청: "눈에 띄게만 표시 — 눈으로 보고 판단"), 어느 정도로 같은 건인지만 구분해 보여준다.
///
/// 판정 기준은 이력의 충돌 판단 키와 같은 주문번호(채널 무관)이며, 같은 주문번호 안에서 SKU와
/// 수량까지 맞는지에 따라 세 단계로 나눈다.
/// </summary>
public static class OrderDuplicateChecker
{
    /// <summary>주문번호 + 매핑된 SKU + 수량까지 같은 이력이 있음 — 같은 파일을 두 번 불러왔을 가능성이 가장 큼.</summary>
    public const string ExactLabel = "중복(동일)";

    /// <summary>주문번호 + SKU는 같지만 수량이 다름 — 부분 출고/수량 변경분일 수 있음.</summary>
    public const string QtyDifferentLabel = "중복(수량다름)";

    /// <summary>주문번호만 같음 — 같은 주문의 다른 품목을 이미 처리했을 수 있음.</summary>
    public const string OrderNoOnlyLabel = "중복(주문번호)";

    /// <summary>
    /// 기존 이력을 주문번호로 묶어둔 조회용 인덱스. 한 번 만들어 두면 그리드에서 SKU를 고쳐도
    /// DB를 다시 읽지 않고 그 줄만 다시 판정할 수 있다.
    /// </summary>
    public sealed class HistoryIndex
    {
        private readonly Dictionary<string, List<OutboundDetail>> _byOrderNo;

        private HistoryIndex(Dictionary<string, List<OutboundDetail>> byOrderNo) => _byOrderNo = byOrderNo;

        public static HistoryIndex Build(IEnumerable<OutboundDetail> history)
        {
            var byOrderNo = history
                .Where(d => !string.IsNullOrWhiteSpace(d.OrderNo))
                .GroupBy(d => d.OrderNo.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            return new HistoryIndex(byOrderNo);
        }

        /// <summary>이력이 하나도 없으면 true — 호출부에서 굳이 훑지 않아도 되게 한다.</summary>
        public bool IsEmpty => _byOrderNo.Count == 0;

        /// <summary>한 줄을 판정해 표시 문구를 채우거나 지운다. 중복으로 판정되면 true.</summary>
        public bool Annotate(OfsOrderItem item)
        {
            item.DuplicateNote = null;
            item.DuplicateDetail = null;

            var orderNo = item.OrderNo?.Trim();
            if (string.IsNullOrEmpty(orderNo)) return false;
            if (!_byOrderNo.TryGetValue(orderNo, out var matches) || matches.Count == 0) return false;

            var sku = item.MappedSku?.Trim() ?? string.Empty;
            var sameSku = sku.Length == 0
                ? new List<OutboundDetail>()
                : matches.Where(d => string.Equals(d.MskuCode?.Trim(), sku, StringComparison.OrdinalIgnoreCase)).ToList();

            item.DuplicateNote = sameSku.Count == 0
                ? OrderNoOnlyLabel
                : sameSku.Any(d => d.Qty == item.Quantity) ? ExactLabel : QtyDifferentLabel;
            item.DuplicateDetail = BuildDetail(orderNo, matches);
            return true;
        }

        /// <summary>여러 줄을 한꺼번에 판정하고 중복으로 표시된 줄 수를 돌려준다.</summary>
        public int Annotate(IEnumerable<OfsOrderItem> items)
        {
            var flagged = 0;
            foreach (var item in items)
            {
                if (Annotate(item)) flagged++;
            }
            return flagged;
        }
    }

    /// <summary>"중복" 열 셀의 강조 정도 — 문구별로 색을 다르게 주기 위한 구분.</summary>
    public static bool IsStrongDuplicate(string? note) => note == ExactLabel || note == QtyDifferentLabel;

    private const int MaxDetailLines = 5;

    private static string BuildDetail(string orderNo, List<OutboundDetail> matches)
    {
        var lines = matches
            .OrderByDescending(d => d.CreatedAt)
            .Take(MaxDetailLines)
            .Select(d =>
            {
                var sku = string.IsNullOrWhiteSpace(d.MskuCode) ? "(SKU 없음)" : d.MskuCode;
                var tracking = string.IsNullOrWhiteSpace(d.TrackingNo) ? string.Empty : $" / 송장 {d.TrackingNo}";
                var recipient = string.IsNullOrWhiteSpace(d.Recipient) ? string.Empty : $" / {d.Recipient}";
                return $"· {d.CreatedAt:M월 d일 H시} {d.Status} / {sku} / {d.Qty}개{recipient}{tracking}";
            })
            .ToList();

        if (matches.Count > MaxDetailLines) lines.Add($"· ... 외 {matches.Count - MaxDetailLines}건");

        return $"주문번호 {orderNo} — 기존 발주/출고 이력 {matches.Count}건\n{string.Join("\n", lines)}\n"
             + "(같은 곳으로 두 번 출고하는 경우일 수도 있으니 직접 확인하세요.)";
    }
}
