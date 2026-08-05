namespace MiniERP2.Models;

public class CourierMaster
{
    public string CourierName { get; set; } = string.Empty;

    /// <summary>출력(발송) 양식 — 헤더별로 어떤 OfsOrderItem 속성을 내보낼지(엑셀 헤더 → 속성명).</summary>
    public string HeaderMappingJson { get; set; } = string.Empty;

    // 운송장 결과 가져오기(입수) 양식 — 택배사 프로그램에서 받은 엑셀의 헤더 시작행과, "수령인"/
    // "운송장번호" 값이 어느 헤더 열에 있는지를 지정한다. 출력 양식과 별개의 파일 형식이라 따로 둔다.
    public int TrackingImportHeaderRow { get; set; } = 1;
    public string TrackingImportRecipientHeader { get; set; } = string.Empty;
    public string TrackingImportTrackingNoHeader { get; set; } = string.Empty;

    /// <summary>
    /// 아래 3개는 선택 항목입니다. 운송장번호 선택 적용 창(TrackingAssignDialog)에서 같은 수령인에
    /// 대해 운송장번호가 여러 개 발견됐을 때, 어느 운송장번호가 어느 주문에 해당하는지 사용자가
    /// 구분할 수 있도록 참고 정보로만 보여줍니다. 결과 파일에 해당 열이 없거나 설정하지 않으면
    /// 비워두면 되고, 매칭 로직에는 전혀 영향을 주지 않습니다.
    /// </summary>
    public string TrackingImportOrderNoHeader { get; set; } = string.Empty;
    public string TrackingImportAddressHeader { get; set; } = string.Empty;
    public string TrackingImportProductNameHeader { get; set; } = string.Empty;

    /// <summary>
    /// 아래 2개도 선택 항목입니다. 운송장 파일 누락건 점검(TrackingBackfillViewer)에서 접수일자/
    /// 운임 통계와 뷰어 그리드 표시에 씁니다. 결과 파일에 해당 열이 없으면 비워두면 됩니다.
    /// </summary>
    public string TrackingImportReceivedDateHeader { get; set; } = string.Empty;
    public string TrackingImportFreightCostHeader { get; set; } = string.Empty;

    /// <summary>
    /// 송장표시명 뒤에 붙일 수량 표기 형식입니다. "##"이 실제 수량으로 치환됩니다(예: "   ▶[##개]" +
    /// 수량 2 → "   ▶**[2개]"). 비어있으면 기본형식(" ##개")을 씁니다. 수량이 2개 이상이면 대괄호
    /// 바로 앞에 수량만큼 "*"가 자동으로 붙고(예: 5개 → "▶*****[5개]"), 합포장(한 묶음에 품목이
    /// 2건 이상)이면 작업자가 알아보기 쉽도록 이 형식 전체 앞뒤에 "xx"가 추가로 붙습니다
    /// (Utils.ShipmentGrouping 참고).
    /// </summary>
    public string QuantityNotationFormat { get; set; } = string.Empty;
}
