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
    /// 송장표시명 뒤에 붙일 수량 표기 형식입니다. "##"이 실제 수량으로 치환됩니다(예: "   ▶[##개]" +
    /// 수량 2 → "   ▶[2개]"). 비어있으면 기본형식(" ##개")을 씁니다. 합포장(한 묶음에 품목이 2건
    /// 이상)이면 작업자가 알아보기 쉽도록 이 형식 앞뒤에 "xx"가 자동으로 붙습니다
    /// (Utils.ShipmentGrouping 참고).
    /// </summary>
    public string QuantityNotationFormat { get; set; } = string.Empty;
}
