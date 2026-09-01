namespace MiniERP2.Models;

/// <summary>배치 내 (구분, 채널, CSKU) 집계 결과 1행(§4, §5).</summary>
public class CskuStatLine
{
    public long Id { get; set; }
    public long BatchId { get; set; }
    public CskuFileKind FileKind { get; set; }
    public string ChannelCode { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string CskuCode { get; set; } = string.Empty;
    public string ProductGroup { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public int Qty { get; set; }
    public decimal Revenue { get; set; }
    public decimal Settlement { get; set; }
    public decimal Shipping { get; set; }
    public decimal Fee { get; set; }
    public decimal Profit { get; set; }

    /// <summary>이익액/매출액. 매출액 0이면 공백(§4.2) — DB에 저장하지 않고 표시 시점에 계산한다.</summary>
    public decimal? MarginRate => Revenue == 0 ? null : Profit / Revenue;

    /// <summary>그리드/엑셀 표시용(§6 "구분" 열). DB에는 FileKind(enum 이름)로 저장한다.</summary>
    public string FileKindDisplay => FileKind.ToDisplayName();
}
