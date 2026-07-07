namespace MiniERP2.Models;

/// <summary>
/// 채널의 광고비 파일 레이아웃 하나를 나타냅니다. 하나의 채널에 여러 파일 형식(헤더/시트 구성이
/// 다른 파일들)이 있을 때 각각 별도 레이아웃으로 등록합니다.
/// <para>MatchColumns의 모든 문자열이 하나의 헤더 후보 행에서 발견되면 이 레이아웃으로 자동 선택됩니다
/// (AND 조건). 비어 있으면 자동탐지 대상에서 제외하고 수동 선택만 가능합니다.</para>
/// </summary>
public class AdFileLayout
{
    public string LayoutId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string LayoutName { get; set; } = string.Empty;

    /// <summary>자동탐지 AND 조건 목록. 파일 헤더 후보 행에 이 문자열이 모두 포함되어야 매칭됩니다.</summary>
    public List<string> MatchColumns { get; set; } = new();

    public Dictionary<AdStdField, FieldMapping> FieldMappings { get; set; } = new();
}
