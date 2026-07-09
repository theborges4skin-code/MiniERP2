namespace MiniERP2.Models;

public class DocHistoryRecord
{
    public int Id { get; set; }
    public string DocType { get; set; } = "";
    public DateTime IssueDate { get; set; }
    public string BuyerName { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public string FilePath { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
