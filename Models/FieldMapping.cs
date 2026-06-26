namespace MiniERP2.Models;

public class FieldMapping
{
    public string? SheetName { get; set; }
    public int HeaderRow { get; set; } = 1;
    public string? Column { get; set; }
}
