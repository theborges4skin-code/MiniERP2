namespace MiniERP2.Models;

/// <summary>
/// CSKU별 통계에 로드한 마감/이익분석 결과 파일의 구분(CSKU별통계_개발기획서.md §2).
/// 채널코드로 자동판정하지 않고 사용자가 로드 시 체크박스로 지정한다.
/// </summary>
public enum CskuFileKind
{
    General,
    Amazon,
    RocketGross,

    /// <summary>거래처 마감보드 라인을 DB에서 바로 불러온 것(파일 아님). 온라인 채널과 합산해 보기 위함.</summary>
    Partner,
}

public static class CskuFileKindExtensions
{
    public static string ToDisplayName(this CskuFileKind kind) => kind switch
    {
        CskuFileKind.Amazon => "아마존",
        CskuFileKind.RocketGross => "로켓그로스",
        CskuFileKind.Partner => "거래처",
        _ => "일반",
    };
}
