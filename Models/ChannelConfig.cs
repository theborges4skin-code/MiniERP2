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
        "파일을 불러올 때 발주일(발주서 매핑 탭에서 지정) 기준 최근 N일 이내 항목만 골라 선택창에서 " +
        "처리할 건을 고르게 됩니다. 발주일을 매핑하지 않으면 이 옵션은 동작하지 않습니다. " +
        "조회 기간은 '누적발주서 — 조회 기간(일)' 항목에서 변경할 수 있습니다.")]
    public bool IsCumulativeOrderFile { get; set; }

    [Category("기본 정보")]
    [DisplayName("누적발주서 — 조회 기간(일)")]
    [Description("누적발주서 채널에서 발주서 로드 시 발주일 기준 최근 N일 이내 항목만 표시합니다. 기본값 5일.")]
    public int CumulativeOrderWindowDays { get; set; } = 5;

    // 채널설정 창의 "발주서 매핑"/"정산서 매핑" 전용 탭에서 편집한다(PropertyGrid는 Dictionary를 지원하지 않음).
    [Browsable(false)]
    public Dictionary<StdField, FieldMapping> OrderFieldMappings { get; set; } = new();

    [Browsable(false)]
    public Dictionary<StdField, FieldMapping> SettlementFieldMappings { get; set; } = new();

    // 채널설정 창의 "광고비 헤더 설정" 탭에서 편집한다. 하나의 채널에 여러 파일 레이아웃을 등록해
    // 헤더 구성이 다른 여러 광고비 파일을 같은 채널로 처리할 수 있다.
    [Browsable(false)]
    public List<AdFileLayout> AdFileLayouts { get; set; } = new();

    // 채널설정 창의 "보조 소스" 전용 탭에서 편집한다(PropertyGrid의 CollectionEditor는 쓰기 불편함).
    [Browsable(false)]
    public List<GrowthAuxSource> GrowthAuxSources { get; set; } = new();

    // 쿠팡그로스 CFS(쿠팡풀필먼트서비스) 입출고비·배송비 파일 연동 설정.
    // null이면 CFS 비활성화(GrowthAuxSource 동작), non-null이면 CFS 활성화(GrowthAuxSource의
    // HandlingFee/ShippingFee 무시).
    [Browsable(false)]
    public GrowthCfsFeeConfig? GrowthCfsFee { get; set; }

    // 채널설정 창의 "택배사 출력 고정값" 전용 탭에서 편집한다.
    [Browsable(false)]
    public List<CourierHeaderOverride> CourierHeaderOverrides { get; set; } = new();

    [Category("아마존 전용")]
    [DisplayName("헤더 행 자동 탐지 — 기준 컬럼명")]
    [Description("헤더 행이 파일마다 달라질 때 이 컬럼명이 포함된 행을 헤더로 자동 탐지합니다. " +
        "비워두면 정산서 매핑 탭의 헤더 행 번호를 그대로 사용합니다. (예: date/time)")]
    public string HeaderRowDetectionColumn { get; set; } = string.Empty;

    [Category("아마존 전용")]
    [DisplayName("이익 제외 이벤트 유형값")]
    [Description("EventType(type 컬럼) 값이 이 문자열과 일치하는 행을 이익분석에서 제거합니다. " +
        "아마존 Transfer(입금) 행 제거에 사용합니다. 비워두면 아무 행도 제거하지 않습니다. (예: Transfer)")]
    public string AmazonTransferTypeValue { get; set; } = string.Empty;

    [Category("쿠팡로켓 전용")]
    [DisplayName("계산서발행내역 — 세금계산서번호 헤더")]
    [Description("계산서발행내역 파일에서 '세금계산서번호' 역할을 하는 열의 헤더명. " +
        "입고상세내역의 동일 헤더명을 JOIN 키로 사용합니다. 비워두면 발행일 매칭을 건너뜁니다.")]
    public string RocketInvoiceKeyHeader { get; set; } = string.Empty;

    [Category("쿠팡로켓 전용")]
    [DisplayName("계산서발행내역 — 계산서발행일 헤더")]
    [Description("계산서발행내역 파일에서 발행일 값이 있는 열의 헤더명.")]
    public string RocketInvoiceDateHeader { get; set; } = string.Empty;

    [Category("쿠팡로켓 전용")]
    [DisplayName("계산서발행내역 — 헤더 행 번호")]
    [Description("계산서발행내역 파일의 헤더가 있는 행 번호 (기본값 1).")]
    public int RocketInvoiceHeaderRow { get; set; } = 1;
}
