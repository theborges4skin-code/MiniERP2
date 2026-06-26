namespace MiniERP2.Models;

public class GrowthAuxSource
{
    public bool Enabled { get; set; }
    public StdField TargetStdField { get; set; }
    public string? SheetName { get; set; }
    public int HeaderRow { get; set; } = 1;
    public string? KeyHeader { get; set; }
    public string? ValueHeader { get; set; }
    public string? OutCol { get; set; }
}
