using System.ComponentModel;

namespace MiniERP2.Models;

public class FieldMapping
{
    [Category("위치")]
    [DisplayName("시트 이름")]
    [Description("데이터가 위치한 시트의 이름입니다. 비워두면 첫 번째 시트를 사용합니다.")]
    public string? SheetName { get; set; }

    [Category("위치")]
    [DisplayName("헤더 행 번호")]
    [Description("필드 이름이 있는 행의 번호입니다 (1부터 시작).")]
    public int HeaderRow { get; set; } = 1;

    [Category("위치")]
    [DisplayName("열 이름 또는 주소")]
    [Description("데이터가 있는 열의 이름(예: 상품명) 또는 주소(예: C)입니다.")]
    public string? Column { get; set; }
}
