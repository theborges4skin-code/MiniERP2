namespace MiniERP2.Models;

/// <summary>
/// "지난 CSKU 불러오기"(FboRecentCskuPickerDialog)에 쓰는 한 항목 — 특정 CSKU가 특정 발주일에
/// 실제로 나갔던 박스/품목 구성 스냅샷입니다. Items의 BoxSeq는 원본 발주 안에서의 번호이며,
/// 새 발주에 넣을 때는 새로 채번해야 합니다(FboOrderForm.RecomputeBoxIdentifiers 참고).
/// </summary>
public sealed class FboRecentCskuGroup
{
    public required string Csku { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public List<FboBoxItem> Items { get; set; } = [];
    public Dictionary<int, string> BoxTypeBySeq { get; set; } = [];

    public int BoxCount => Items.Select(i => i.BoxSeq).Distinct().Count();
    public int TotalQty => Items.Sum(i => i.Qty);
}
