namespace MiniERP2.Utils;

/// <summary>
/// CSKU 납품단가/원가(OutboundDetail.SupplyPrice, ChannelSkuModel 계열 원가)는 이 앱 전반에서
/// VAT포함 단가로 저장된다(사용자 확인, 2026-07-30). VAT별도 화면/문서를 만들 때는 이 값을 10/11로
/// 나눠 공급가 기준으로 역산해야 한다 — 그냥 곱해서 별도 세액을 얹으면 이중과세가 된다.
/// 거래처 마감보드 라이브 그리드와 <see cref="PartnerClosingDocumentBuilder"/>(매출장/명세표 발행)가
/// 이 상수를 공유해야 두 화면의 계산이 어긋나지 않는다.
/// </summary>
public static class VatCalculator
{
    public const decimal VatDivisor = 1.1m;

    /// <summary>VAT포함 원본 금액을 화면/문서 설정에 맞게 변환한다. vatExcluded=true면 10/11로 나눈
    /// 공급가 기준, false면 원본(VAT포함) 그대로.</summary>
    public static decimal ToDisplay(decimal vatIncludedAmount, bool vatExcluded) =>
        vatExcluded ? vatIncludedAmount / VatDivisor : vatIncludedAmount;
}
