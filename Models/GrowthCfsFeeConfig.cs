using System.ComponentModel;

namespace MiniERP2.Models;

/// <summary>
/// 쿠팡그로스 CFS(쿠팡풀필먼트서비스) 입출고비·배송비 파일 파싱 설정.
/// ChannelConfig.GrowthCfsFee != null 이면 CFS 모드 활성화.
/// </summary>
public class GrowthCfsFeeConfig
{
    [Category("정산 파일")]
    [DisplayName("정산 옵션ID 헤더")]
    [Description("정산 파일에서 옵션ID 역할을 하는 열의 헤더명. RawValues에서 조회 키로 사용됩니다.")]
    public string SettlementOptionIdHeader { get; set; } = "옵션ID";

    [Category("입출고비 시트")]
    [DisplayName("시트 이름")]
    [Description("CFS 파일에서 입출고비 데이터가 있는 시트 이름.")]
    public string HandlingSheetName { get; set; } = "입출고비";

    [Category("입출고비 시트")]
    [DisplayName("헤더 행 번호")]
    [Description("CFS 입출고비 시트의 헤더 행 번호 (2단 헤더면 아래 행 번호, 기본 8).")]
    public int HandlingHeaderRow { get; set; } = 8;

    [Category("입출고비 시트")]
    [DisplayName("옵션ID 헤더")]
    [Description("CFS 입출고비 시트에서 옵션ID 역할을 하는 열의 헤더명.")]
    public string CfsOptionIdHeader { get; set; } = "옵션ID";

    [Category("입출고비 시트")]
    [DisplayName("입출고비 금액 헤더")]
    [Description("CFS 입출고비 시트에서 입출고비로 사용할 열의 헤더명.")]
    public string HandlingFeeHeader { get; set; } = "할인적용가(A-B)";

    [Category("배송비 시트")]
    [DisplayName("시트 이름")]
    [Description("CFS 파일에서 배송비 데이터가 있는 시트 이름.")]
    public string ShippingSheetName { get; set; } = "배송비";

    [Category("배송비 시트")]
    [DisplayName("헤더 행 번호")]
    [Description("CFS 배송비 시트의 헤더 행 번호 (기본 8).")]
    public int ShippingHeaderRow { get; set; } = 8;

    [Category("배송비 시트")]
    [DisplayName("배송비 금액 헤더")]
    [Description("CFS 배송비 시트에서 배송비로 사용할 열의 헤더명.")]
    public string ShippingFeeHeader { get; set; } = "최종비용";
}
