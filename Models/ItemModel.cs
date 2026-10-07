namespace MiniERP2.Models;

public class ItemModel
{
    public string Sku { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal CostPrice { get; set; }
    public string? Reserve1 { get; set; }
    public string? Reserve2 { get; set; }
    public string? Reserve3 { get; set; }
    public string? ProductGroup { get; set; }

    /// <summary>
    /// 아마존 채널 전용 상품그룹(예: 보르, EGF, 이데). 마스터 상품그룹은 채널 단위(41.아마존미국 등)라
    /// 아마존 안에서 품목군별 실적을 보려면 별도 분류가 필요하다. 이익분석 결과 파일의 "아마존상품그룹" 열에 쓰인다.
    /// </summary>
    public string? AmazonGroup { get; set; }

    /// <summary>B2B 등 kg 단위 매입/납품에 쓰는 기본 단위입니다. 기본값 "kg".</summary>
    public string Unit { get; set; } = "kg";
}
