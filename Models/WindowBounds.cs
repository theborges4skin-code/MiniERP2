namespace MiniERP2.Models;

/// <summary>
/// 창 하나의 마지막 크기/위치/상태를 저장하기 위한 모델입니다(기획서 2.6절).
/// </summary>
public class WindowBounds
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string WindowState { get; set; } = "Normal";
}
