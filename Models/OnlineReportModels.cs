namespace MiniERP2.Models;

// 온라인 매출 종합보고서 — 수동 엑셀 "온라인 매출(YY년MM월).xlsx"를 대체한다.
// 설정(OnlineReportConfig)은 JSON 파일, 월별 입력(OnlineReportMonthInput)은 DB(OnlineReportMonthTable)에 둔다.

/// <summary>보고서 설정 — 채널 블록·품목 열·거래처·비용 항목·택배비 단가 등. online_report_config.json.</summary>
public class OnlineReportConfig
{
    /// <summary>채널×상품그룹 페이지의 블록(위→아래 순서).</summary>
    public List<OnlineReportBlock> Blocks { get; set; } = [];

    /// <summary>고정 품목 열(왼→오 순서). 어느 열에도 속하지 않는 그룹은 FallbackColumnNumber 열로 간다.</summary>
    public List<OnlineReportGroupColumn> GroupColumns { get; set; } = [];

    /// <summary>분류가 명확하지 않은 그룹이 들어갈 열의 번호(기본 22 = 기타).</summary>
    public int FallbackColumnNumber { get; set; } = 22;

    /// <summary>요약 페이지 거래처 표(왼→오 순서).</summary>
    public List<OnlineReportPartner> Partners { get; set; } = [];

    public List<string> CostItems { get; set; } = [];
    public List<string> ExportMarkets { get; set; } = [];

    /// <summary>보고서에 넣지 않아도 경고하지 않을 채널(거래처 하단 집계로 가는 채널 등).</summary>
    public List<string> IgnoredChannelCodes { get; set; } = [];

    /// <summary>채널명이 이것으로 시작하면 경고하지 않는다(거래처 위탁 온라인 채널 "온_한결_스마트" 등).</summary>
    public List<string> IgnoredChannelNamePrefixes { get; set; } = ["온_"];

    public OnlineReportFreightSettings Freight { get; set; } = new();

    public string FontName { get; set; } = "맑은 고딕";

    /// <summary>채널×상품그룹 페이지의 기준 인쇄 배율(%). 채널이 늘어 세로 한 장을 넘으면 이보다 작게 자동 축소된다.</summary>
    /// <remarks>65%는 기존 수동 엑셀(페이지 맞춤 인쇄) PDF와 같은 글자 크기(2026-10-02 실측).</remarks>
    public int BaseScalePercent { get; set; } = 65;
}

public class OnlineReportBlock
{
    public string Name { get; set; } = string.Empty;
    public List<string> ChannelCodes { get; set; } = [];

    /// <summary>택배비 열(정산 배송비 ÷ 기준단가)에 넣을지. 쿠팡로켓·그로스처럼 고객부담 배송비가 아닌 채널은 끈다.</summary>
    public bool CountShipping { get; set; } = true;

    /// <summary>요약 페이지에서 "로켓+그로스"로 묶을지(아니면 "온라인(로켓 제외)").</summary>
    public bool IsRocketGrowth { get; set; }
}

public class OnlineReportGroupColumn
{
    /// <summary>열 위에 표시하는 번호(예: 14). 상품그룹 "14.면도"의 앞 번호와 맞춘다.</summary>
    public int Number { get; set; }
    public string Header { get; set; } = string.Empty;

    /// <summary>이 열에 합칠 상품그룹 번호들. 비어 있으면 Number 하나만.</summary>
    public List<int> MemberNumbers { get; set; } = [];

    public IEnumerable<int> EffectiveMembers => MemberNumbers.Count > 0 ? MemberNumbers : [Number];
}

public class OnlineReportPartner
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>거래처 마감보드의 거래처명에 이 중 하나라도 들어 있으면 합산한다.</summary>
    public List<string> PartyNameKeywords { get; set; } = [];
}

public class OnlineReportFreightSettings
{
    /// <summary>고객부담 배송비 기준단가. 채널 택배 건수 = 정산 배송비 ÷ 이 값, 실택배비 조정의 건당 기준 수입.</summary>
    public decimal BaseFee { get; set; } = 3000m;
    /// <summary>이 운임 이하면 소형 부자재비, 초과면 대형 부자재비.</summary>
    public decimal PackingThreshold { get; set; } = 3200m;
    public decimal PackingSmall { get; set; } = 400m;
    public decimal PackingLarge { get; set; } = 1100m;
    /// <summary>풀필먼트 건당 비용(보관·입출고·포장 포함 평균). 부자재비를 따로 붙이지 않는다.</summary>
    public decimal FulfillmentUnitCost { get; set; } = 4000m;
}

/// <summary>월별 수동 입력값. OnlineReportMonthTable에 JSON으로 저장.</summary>
public class OnlineReportMonthInput
{
    public decimal ExchangeRate { get; set; }
    public string ExchangeRateNote { get; set; } = string.Empty;

    public Dictionary<string, decimal> Costs { get; set; } = [];
    public List<OnlineReportExportEntry> Exports { get; set; } = [];

    public List<OnlineReportFreightTier> FreightTiers { get; set; } = [];
    public string FreightSourceNote { get; set; } = string.Empty;
    public int FulfillmentCount { get; set; }

    /// <summary>값이 있으면 운임표 계산 대신 이 금액을 실택배비 조정액으로 쓴다(음수 = 비용).</summary>
    public decimal? FreightAdjustmentOverride { get; set; }

    /// <summary>거래처 마감보드 값 대신 쓸 수동값(표시명 기준).</summary>
    public List<OnlineReportPartnerOverride> PartnerOverrides { get; set; } = [];

    /// <summary>밀크런·CJ입고비처럼 광고비 행에 더할 추가 비용(양수 입력 = 비용).</summary>
    public List<OnlineReportExtraAdCost> ExtraAdCosts { get; set; } = [];

    /// <summary>요약 페이지 하단 비고란에 그대로 찍을 이 달의 메모(예: 단가 인상, 이벤트, 특이사항).</summary>
    public string Memo { get; set; } = string.Empty;
}

public class OnlineReportExportEntry
{
    public string Market { get; set; } = string.Empty;
    public decimal RevenueUsd { get; set; }
    public decimal ProfitUsd { get; set; }
}

public class OnlineReportFreightTier
{
    public decimal Rate { get; set; }
    public int Count { get; set; }
}

public class OnlineReportPartnerOverride
{
    public string DisplayName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public decimal Profit { get; set; }
}

public class OnlineReportExtraAdCost
{
    public string BlockName { get; set; } = string.Empty;
    /// <summary>품목 열 번호. 0이면 기타(Fallback) 열.</summary>
    public int ColumnNumber { get; set; }
    public decimal Amount { get; set; }
    public string Memo { get; set; } = string.Empty;
}

/// <summary>거래처 마감보드 1건(보고서 입력용 축약).</summary>
public record OnlineReportPartnerClosing(string PartyName, decimal Revenue, decimal Profit);

/// <summary>CSKU별 통계 1행(보고서 입력용 축약).</summary>
public record OnlineReportCskuRow(string ChannelCode, string CskuCode, string ProductGroup, decimal Revenue);
