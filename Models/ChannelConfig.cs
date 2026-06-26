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

    [Category("필드 매핑")]
    [DisplayName("표준 필드 매핑")]
    [Description("발주/정산 파일의 각 열을 표준 필드에 연결하는 설정입니다.")]
    public Dictionary<StdField, FieldMapping> FieldMappings { get; set; } = new();

    [Category("필드 매핑")]
    [DisplayName("보조 소스 설정 (쿠팡 그로스 등)")]
    [Description("메인 시트 외에 추가 비용(배송비, 입출고비 등)이 있는 보조 시트를 연결하는 설정입니다.")]
    public List<GrowthAuxSource> GrowthAuxSources { get; set; } = new();
}
