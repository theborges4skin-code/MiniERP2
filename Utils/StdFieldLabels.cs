using MiniERP2.Models;

namespace MiniERP2.Utils;

/// <summary>
/// 발주서/정산서 매핑 표준필드(<see cref="StdField"/>)의 한글 라벨 변환.
/// 채널설정 화면의 매핑 그리드와 채널 일괄등록(엑셀) 기능이 같은 라벨 문자열을 공유해야
/// 왕복(내보내기→가져오기)이 어긋나지 않으므로 한 곳에 모아둔다.
/// </summary>
public static class StdFieldLabels
{
    public static string GetLabel(StdField field) => field switch
    {
        StdField.ProductName => "상품명",
        StdField.OptionName => "옵션명",
        StdField.ProductNo => "주문번호",
        StdField.Quantity => "수량",
        StdField.SettlementAmount => "정산액",
        StdField.ShippingFee => "배송비",
        StdField.HandlingFee => "입출고비",
        StdField.Recipient => "수취인",
        StdField.Phone => "연락처",
        StdField.Address => "주소",
        StdField.DeliveryMessage => "배송메세지",
        StdField.OrderDate => "발주일(누적발주서용)",
        StdField.Remark => "비고(내부관리용)",
        StdField.ChannelHint => "채널힌트(자동발주 전용)",
        StdField.Revenue => "매출액",
        StdField.TrackingNo => "실제발송송장수(원본 송장번호 열)",
        _ => field.ToString(),
    };

    /// <summary>라벨 문자열로 <see cref="StdField"/>를 역변환한다. 일치하는 필드가 없으면 null.</summary>
    public static StdField? TryParseLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var trimmed = label.Trim();
        foreach (var field in Enum.GetValues<StdField>())
        {
            if (string.Equals(GetLabel(field), trimmed, StringComparison.Ordinal)) return field;
        }
        return null;
    }
}
