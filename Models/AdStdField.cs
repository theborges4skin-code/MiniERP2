namespace MiniERP2.Models;

/// <summary>
/// 광고비 파일(채널별 광고 리포트)에서 다루는 표준 필드입니다. 발주서의 StdField와는 별개로
/// 둡니다 — 광고 리포트는 상품그룹 단위로만 분류하면 되고, 헤더 구성도 발주서와 다릅니다
/// (참고: SalesManagerV2의 AD_PRODUCT_NAME/AD_PRODUCT_ID/AD_OPTION/AD_COST/AD_EXTRA1/AD_EXTRA2).
/// </summary>
public enum AdStdField
{
    ProductName,
    ProductId,
    OptionName,
    Cost,
    Extra1,
    Extra2,
}
