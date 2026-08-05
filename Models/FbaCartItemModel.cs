namespace MiniERP2.Models;

/// <summary>FBA 발주 화면의 "미배정 품목" 임시저장 장바구니 1줄. CSKU 마스터 필드는 저장하지 않고
/// 불러올 때 FbaCskuRepository에서 다시 조회한다 — 저장 시점의 스냅샷이 아니라 최신 마스터를 쓴다.</summary>
public class FbaCartItemModel
{
    public required string Csku { get; set; }
    public int Qty { get; set; }
    public string? ExpiryDate { get; set; }
}
