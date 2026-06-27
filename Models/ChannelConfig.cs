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

    [Category("기본 정보")]
    [DisplayName("누적발주서")]
    [Description("이 채널의 발주서 파일이 과거 이력까지 누적해서 담겨 있으면 체크하세요. 체크하면 발주 " +
        "파일을 불러올 때 발주일(발주서 매핑 탭에서 지정) 기준 최근 5일 이내 항목만 골라 선택창에서 " +
        "처리할 건을 고르게 됩니다. 발주일을 매핑하지 않으면 이 옵션은 동작하지 않습니다.")]
    public bool IsCumulativeOrderFile { get; set; }

    // 채널설정 창의 "발주서 매핑"/"정산서 매핑" 전용 탭에서 편집한다(PropertyGrid는 Dictionary를 지원하지 않음).
    [Browsable(false)]
    public Dictionary<StdField, FieldMapping> OrderFieldMappings { get; set; } = new();

    [Browsable(false)]
    public Dictionary<StdField, FieldMapping> SettlementFieldMappings { get; set; } = new();

    // 광고 매핑창("필드 매핑" 탭)에서 편집한다. 광고비 리포트는 발주서/정산서와 헤더 구성이 달라
    // 별도 키(AdStdField)로 둔다.
    [Browsable(false)]
    public Dictionary<AdStdField, FieldMapping> AdFieldMappings { get; set; } = new();

    [Category("필드 매핑")]
    [DisplayName("보조 소스 설정 (쿠팡 그로스 등)")]
    [Description("메인 시트 외에 추가 비용(배송비, 입출고비 등)이 있는 보조 시트를 연결하는 설정입니다.")]
    public List<GrowthAuxSource> GrowthAuxSources { get; set; } = new();

    // 채널설정 창의 "택배사 출력 고정값" 전용 탭에서 편집한다.
    [Browsable(false)]
    public List<CourierHeaderOverride> CourierHeaderOverrides { get; set; } = new();
}
