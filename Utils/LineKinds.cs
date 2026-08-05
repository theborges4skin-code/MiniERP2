namespace MiniERP2.Utils;

/// <summary>
/// OutboundDetail.LineKind에 들어갈 수 있는 값의 단일 출처입니다(샘플발송이력관리_개발기획서.md §2 D1).
/// OFS 콤보·이력창 필터·마감보드 필터가 전부 이 배열을 참조해, 목록이 여러 곳에서 따로 관리되는
/// 상황을 막습니다. v1에서는 사용자 편집이 불가능한 코드 상수입니다.
/// </summary>
public static class LineKinds
{
    public const string Sample = "샘플";
    public const string Cs = "CS";
    public const string Other = "기타";

    public static readonly string[] All = [Sample, Cs, Other];
}
