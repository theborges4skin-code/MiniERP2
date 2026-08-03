namespace MiniERP2.Models;

/// <summary>
/// ⚠ 임시(실험용) — 문서관리 메인창(DocLineHistoryForm)의 "견적서 작성함"(장바구니)에 담긴 한 줄.
/// 이력 조회 화면에서 골라 담은 CSKU가 실제 QuoteLineItem으로 발행되기 전, 사용자가 마지막으로
/// 단가·수량을 조정할 수 있게 들고 있는 세션 스코프 데이터다(DB에 저장하지 않음).
/// </summary>
public class QuoteCartLine
{
    public string ChannelCode { get; set; } = "";
    public string ChannelName { get; set; } = "";

    /// <summary>비어있으면 CSKU에 연결되지 않은 자유품목.</summary>
    public string CskuCode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Packing { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal Qty { get; set; } = 1;
    public string Note { get; set; } = "";
}
