namespace MiniERP2.Models;

/// <summary>메인 허브 메뉴/버튼 하나(레이블 기준)의 누적 사용 횟수와 마지막 사용 시각.</summary>
public class MenuUsageStat
{
    public int Count { get; set; }
    public DateTime LastUsedAt { get; set; }
}
