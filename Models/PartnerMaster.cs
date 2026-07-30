namespace MiniERP2.Models;

/// <summary>
/// 거래처 마감보드(거래처마감보드_개발기획서.md §5.1)의 거래처 자체(기간과 무관) 마스터.
/// 즐겨찾기(고정 노출)·수동 거래처 등록의 기준이 된다.
/// </summary>
public class PartnerMaster
{
    /// <summary>`CH:{채널코드}` 또는 `MANUAL:{순번}`.</summary>
    public string PartyKey { get; set; } = string.Empty;

    /// <summary>MANUAL 파티만 의미 있음(CH는 SalesChannelTable.ChannelName을 표시에 사용).</summary>
    public string PartyName { get; set; } = string.Empty;

    public bool IsManual { get; set; }

    /// <summary>SalesChannelTable.IsFavorite(OFS 채널 선택용)와는 별개의, 마감보드 전용 즐겨찾기.</summary>
    public bool IsFavorite { get; set; }

    /// <summary>false면 더 이상 목록에 노출되지 않는다(수동 거래처 소프트 비활성화, 이력은 보존).</summary>
    public bool IsActive { get; set; } = true;
}
