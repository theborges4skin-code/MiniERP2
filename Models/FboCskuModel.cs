namespace MiniERP2.Models;

/// <summary>
/// 네이버 풀필먼트(FBO) 전용 품목 마스터 1건입니다. Csku는 <see cref="ChannelSkuModel"/>의
/// (ChannelCode="NAVER_FBO", CskuCode)와 연동되며(FboCskuRepository.Upsert 참고), 재고 관리는
/// 하지 않고 순수히 발주서/입고재고 엑셀 생성에 필요한 값만 담는다.
/// </summary>
public class FboCskuModel
{
    public required string Csku { get; set; }
    public string FboItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    /// <summary>택배사 출력양식(하배출고이서)의 "품목명" 칸에 쓸 표시명입니다. OFS의
    /// ChannelSkuModel.InvoiceDisplayName과 같은 목적 — 내부 관리용 ItemName과 실제 택배시스템에
    /// 업로드할 표기가 다를 수 있어 분리합니다. 비어있으면 내보내기 시 ItemName으로 대체됩니다.</summary>
    public string? InvoiceDisplayName { get; set; }
    public int QtyPerBox { get; set; }
    public string BoxType { get; set; } = "소";
    public string? FreightType { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? UpdatedAt { get; set; }
}
