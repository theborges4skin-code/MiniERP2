using System.ComponentModel;

namespace MiniERP2.Models;

public class ChannelConfig
{
    [Category("기본 정보")]
    [DisplayName("채널 코드")]
    [Description("시스템에서 채널을 식별하는 고유 코드입니다. (예: COUPANG)")]
    [ReadOnly(true)]
    public string ChannelCode { get; set; } = string.Empty;

    [Category("기본 정보")]
    [DisplayName("채널 이름")]
    [Description("UI에 표시될 채널의 이름입니다. (예: 쿠팡)")]
    public string ChannelName { get; set; } = string.Empty;

    [Category("기본 정보")]
    [DisplayName("채널 유형")]
    [Description("정산 및 데이터 처리 방식을 결정하는 유형입니다.")]
    public ChannelType ChannelType { get; set; } = ChannelType.General;

    [Category("기본 정보")]
    [DisplayName("환율 (아마존 채널용)")]
    [Description("아마존 등 외화 정산 채널의 이익분석에 사용할 환율입니다. 원화 채널은 1로 둡니다.")]
    public decimal ExchangeRate { get; set; } = 1m;

    // 채널설정 창의 "발주서 매핑"/"정산서 매핑" 전용 탭에서 편집한다(PropertyGrid는 Dictionary를 지원하지 않음).
    [Browsable(false)]
    public Dictionary<StdField, FieldMapping> OrderFieldMappings { get; set; } = new();

    [Browsable(false)]
    public Dictionary<StdField, FieldMapping> SettlementFieldMappings { get; set; } = new();

    [Category("필드 매핑")]
    [DisplayName("보조 소스 설정 (쿠팡 그로스 등)")]
    [Description("메인 시트 외에 추가 비용(배송비, 입출고비 등)이 있는 보조 시트를 연결하는 설정입니다.")]
    public List<GrowthAuxSource> GrowthAuxSources { get; set; } = new();
}
