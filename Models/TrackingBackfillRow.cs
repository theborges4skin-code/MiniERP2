namespace MiniERP2.Models;

/// <summary>
/// 운송장 결과 파일에서 읽은 한 줄. 운송장 파일 누락건 점검(TrackingBackfillViewer)의 그리드 행이자
/// 운임 통계 집계의 원천 데이터입니다. 등록(OFS 발주확정) 여부와 무관하게 파일에 있는 유효한
/// 운송장번호 행이면 전부 담습니다(운임 검증은 등록 여부와 무관하게 파일 단위로 산출해야 하므로).
/// </summary>
public class TrackingBackfillRow
{
    public DateTime? ReceivedAt { get; set; }
    public string TrackingNo { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;

    /// <summary>품목명 원문. 괄호·줄바꿈이 파일에서 이미 "_"로 치환돼 있을 수 있어 자동 파싱하지 않고 그대로 보여준다.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>주문번호메모 원문(고객주문번호 헤더 값). 라벨 분류의 주 판별 근거로도 쓰인다.</summary>
    public string OrderNoMemo { get; set; } = string.Empty;

    public decimal FreightCost { get; set; }

    /// <summary>분류 라벨(쿠팡로켓/네이버풀필/아마존FBA/거래처/기타/온라인주문). 판정용이 아니라 필터·정렬용.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>DB(OutboundDetailTable)에 이미 이 운송장번호가 존재하는지 — GetExistingTrackingNos 조회 결과.</summary>
    public bool IsRegistered { get; set; }

    /// <summary>[OFS로 보내기]로 이번 세션에 방금 전송했는지(로컬 힌트). 실제 등록 확정은 새로고침으로 재조회해야 확인된다.</summary>
    public bool JustSent { get; set; }

    public string SourceFileName { get; set; } = string.Empty;
    public int SourceRowNumber { get; set; }

    public string StatusText => IsRegistered ? "등록됨" : JustSent ? "전송됨(확인 필요)" : "미등록";
}
