namespace MiniERP2.Utils;

/// <summary>
/// 채널 일괄등록(엑셀) 기능의 시트/열 이름과 스키마 버전. 내보내기(<c>ChannelTemplateExporter</c>)와
/// 가져오기(<c>ChannelBulkImportLoader</c>)가 같은 상수를 참조해야 왕복이 어긋나지 않는다.
/// </summary>
public static class ChannelBulkImportSchema
{
    /// <summary>양식 구조가 바뀌면 올려서, 옛 버전 파일을 업로드 시점에 거부할 수 있게 한다.</summary>
    public const int SchemaVersion = 1;

    public const string MetaSheet = "_META";
    public const string ChannelSheet = "채널";
    public const string OrderMappingSheet = "발주서매핑";
    public const string SettlementMappingSheet = "정산서매핑";
    public const string PartySheet = "거래처정보";
    public const string ReferenceSheet = "참고(유효값)";

    public static readonly string[] ChannelHeaders =
    [
        "채널코드", "채널명", "채널유형", "그룹", "표시순서", "즐겨찾기", "매입", "매출",
        "환율", "누적발주서", "누적조회일수", "자동발주채널힌트", "설정복사원본",
    ];

    public static readonly string[] MappingHeaders =
    [
        "채널명", "표준필드", "시트이름", "헤더행", "열이름", "고정값",
    ];

    public static readonly string[] PartyHeaders =
    [
        "채널명", "등록번호", "상호", "대표자", "주소", "업태", "종목", "전화", "이메일",
    ];

    public static string ToYn(bool value) => value ? "Y" : "N";

    /// <summary>공란이면 defaultValue, 그 외 "Y"(대소문자 무관)만 true로 취급한다.</summary>
    public static bool ParseYn(string? text, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(text)) return defaultValue;
        return string.Equals(text.Trim(), "Y", StringComparison.OrdinalIgnoreCase);
    }
}
