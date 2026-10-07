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

    [Category("기본 정보")]
    [DisplayName("실배송비 — 발송 1건당 금액(원)")]
    [Description("0보다 크면 이익분석의 배송비를 정산서 배송비 대신 '실제 발송 건수 × 이 금액'으로 다시 " +
        "계산합니다. 송장번호(정산서 매핑의 실제발송송장수 열)가 같은 행은 1건으로 보고 그 첫 행에만 " +
        "금액을 넣으며, 송장번호가 없는 행은 주문번호로 묶습니다(쿠팡처럼 정산서에 송장번호 열이 없는 " +
        "채널). 실배송비만 청구하는 온라인 거래처 채널에 씁니다. 0이면 사용하지 않습니다.")]
    public decimal ActualShippingFeePerShipment { get; set; }

    [Category("거래처 마감 배송비")]
    [DisplayName("OFS 배송비 청구 — 기본 체크")]
    [Description("체크하면 OFS 택배사 출력 미리보기에서 이 채널의 송장마다 '배송비 청구'가 기본으로 체크됩니다" +
        "(투유처럼 송장마다 청구하는 거래처). 체크하지 않으면 기본 꺼짐이고 필요한 송장만 직접 체크합니다(푸디처럼 " +
        "건마다 판단하는 거래처). 체크된 송장은 발주확정 시 배송비 라인 1줄이 함께 저장돼 거래처 마감보드 금액에 " +
        "자동으로 들어갑니다(원가 = 청구액, 이익 0).")]
    public bool OfsChargeShippingFeeByDefault { get; set; }

    [Category("거래처 마감 배송비")]
    [DisplayName("OFS 배송비 청구 — 기본 금액(원, VAT포함)")]
    [Description("OFS에서 배송비 청구 송장에 기본으로 들어가는 금액. 송장마다 미리보기에서 바꿀 수 있습니다.")]
    public decimal OfsShippingFeeAmount { get; set; } = 3000m;

    [Category("거래처 마감 배송비")]
    [DisplayName("OFS 배송비 청구 — 금액 선택지")]
    [Description("OFS 미리보기 오른쪽 클릭 메뉴에 나오는 청구액 선택지(쉼표로 구분). 예: 3000,4500(중량물)")]
    public string OfsShippingFeePresets { get; set; } = "3000,4500";

    [Category("기본 정보")]
    [DisplayName("연결행 기준 컬럼명")]
    [Description("정산서에서 매핑에 실패한 행이 이 컬럼 값이 같은 다른 행(이미 매핑된 상품 행)을 가지면, " +
        "그 행과 같은 CSKU로 수량 0 매핑합니다(금액은 그대로 이익에 반영). 오늘의집 '혜택정산' 쿠폰 행처럼 " +
        "상품명 대신 쿠폰명만 있고 주문옵션번호로 상품 행과 이어지는 행에 씁니다. (예: 주문옵션번호) 비워두면 사용하지 않습니다.")]
    public string LinkedRowKeyHeader { get; set; } = string.Empty;

    // 채널설정 창의 "발주서 매핑"/"정산서 매핑" 전용 탭에서 편집한다(PropertyGrid는 Dictionary를 지원하지 않음).
    [Browsable(false)]
    public Dictionary<StdField, FieldMapping> OrderFieldMappings { get; set; } = new();

    [Browsable(false)]
    public Dictionary<StdField, FieldMapping> SettlementFieldMappings { get; set; } = new();

    // 채널설정 창의 "광고비 헤더 설정" 탭에서 편집한다. 하나의 채널에 여러 파일 레이아웃을 등록해
    // 헤더 구성이 다른 여러 광고비 파일을 같은 채널로 처리할 수 있다.
    [Browsable(false)]
    public List<AdFileLayout> AdFileLayouts { get; set; } = new();

    // 광고매핑 창의 "채널 분리 규칙" 탭에서 편집하는 사용 여부/캠페인 소스 헤더는 여기(JSON)가
    // 아니라 AdChannelSplitRepository(DB)에 저장한다 — "채널 설정" 창이 이 JSON 전체를 로드해
    // 두었다가 무관한 항목을 저장할 때 값을 되돌려버리는 경쟁 문제를 피하기 위함.

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

    [Category("자동발주 연동")]
    [DisplayName("자동발주(표준) 파싱 프리셋으로 사용")]
    [Description("자동발주처리(Gmail 자동화)가 만든 표준 발주 xlsx를 이 채널의 발주서 매핑 설정으로 " +
        "파싱합니다. 전체 채널 중 정확히 1개만 체크해야 하며, 자동발주 알림에서 [발주 파일 로드로 " +
        "열기]를 누르면 채널 선택 없이 이 채널로 자동 로드됩니다.")]
    public bool IsAutoOrderStandardPreset { get; set; }

    [Category("자동발주 연동")]
    [DisplayName("자동발주 채널힌트")]
    [Description("자동발주처리 메일에서 읽은 채널 단서 문자열(channel_hint)이 이 목록과 일치하면, " +
        "표준 프리셋으로 불러온 주문의 채널을 이 채널로 자동 치환하고 이 채널에 쌓인 SKU 매핑 규칙을 " +
        "적용합니다. 쉼표(,)로 여러 별칭을 등록할 수 있습니다. (예: 쿠팡,COUPANG) 이 채널 자체가 " +
        "자동발주(표준) 프리셋이면 비워두세요.")]
    public string AutoOrderHints { get; set; } = string.Empty;
}
