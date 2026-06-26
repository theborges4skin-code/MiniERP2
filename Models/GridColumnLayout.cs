namespace MiniERP2.Models;

public class GridColumnLayout
{
    public required string Name { get; set; }
    public int DisplayIndex { get; set; }
    public int Width { get; set; }
    public bool Visible { get; set; }
}