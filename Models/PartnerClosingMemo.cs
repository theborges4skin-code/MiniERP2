namespace MiniERP2.Models;

/// <summary>
/// 거래처 마감보드 메모 한 건. <see cref="OutboundDetailIds"/>가 비어있으면 거래처 전체에 대한
/// 메모(좌측 거래처 목록에서 추가)이고, 채워져 있으면 그 라인(들)을 참조하는 메모(우측 라인 상세에서
/// 단일/다중 선택 후 추가)다. <see cref="ShowOnStatement"/>/<see cref="ShowOnLedger"/>로 명세표·
/// 매출장 각각에 노출할지 정한다(PartnerClosingDocumentBuilder가 문서 작성 시 이 값을 읽는다).
/// </summary>
public class PartnerClosingMemo
{
    public long Id { get; set; }
    public string Period { get; set; } = "";
    public string PartyKey { get; set; } = "";
    public string MemoText { get; set; } = "";
    public bool ShowOnStatement { get; set; } = true;
    public bool ShowOnLedger { get; set; } = true;
    public List<long> OutboundDetailIds { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsPartyLevel => OutboundDetailIds.Count == 0;
}
