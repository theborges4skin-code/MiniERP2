namespace MiniERP2.Models;

/// <summary>
/// 정산파일에서 표준화된 한 행과, 그에 대한 이익분석 결과를 나타냅니다.
/// </summary>
public class SettlementData
{
    public long Id { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string? OptionName { get; set; }
    public string? Msku { get; set; }
    public int Qty { get; set; }
    public decimal Settlement { get; set; }
    public decimal Shipping { get; set; }
    public decimal Fee { get; set; }
    public decimal Profit { get; set; }
    public string? Status { get; set; }

    /// <summary>매출액 — 정산액(실제 지급액)과 별개로 화면에 보여주는 표준필드. 손익 계산에는 쓰지 않는다(기존과 동일하게 Settlement 기준).</summary>
    public decimal Revenue { get; set; }

    /// <summary>정산파일에 매핑된 원본 송장번호(미매핑 채널은 비어있음). "실제발송송장수" 채널 요약 집계에만 쓰이고 행 단위로 그리드에는 표시하지 않는다.</summary>
    public string? TrackingNo { get; set; }

    /// <summary>정산파일의 주문번호. 쿠팡일반 등 주문단위 배송비 분배가 필요한 채널에서만 채워진다.</summary>
    public string? OrderNo { get; set; }

    /// <summary>세금계산서번호(계산서번호). 쿠팡로켓 입고상세내역에서 StdField.TaxNo 매핑으로 채워진다.</summary>
    public string? TaxNo { get; set; }

    /// <summary>계산서발행일(작성일자). 계산서발행내역 파일 JOIN 후 SettlementForm이 채워준다.</summary>
    public string? TaxDate { get; set; }

    /// <summary>이벤트 유형. 아마존의 type 컬럼값(Order/Refund/Transfer 등). 비아마존 채널은 null.</summary>
    public string? EventType { get; set; }

    /// <summary>매핑된 마스터SKU의 상품그룹(채널의 CSKU 코드 해석 결과). 그리드에 바로 표시하기 위해 매핑 시점에 캐싱한다.</summary>
    public string? ProductGroup { get; set; }

    /// <summary>
    /// 정산 파일 원본 행의 (헤더명 -> 값) 전체. "원본데이터" 시트 내보내기 및 추후 디버깅용으로
    /// SettlementLoader가 채워준다. 표준 필드로 매핑되지 않은 열도 모두 포함된다.
    /// </summary>
    public Dictionary<string, string>? RawValues { get; set; }
}
