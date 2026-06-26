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

    [Category("고정값")]
    [DisplayName("고정값")]
    [Description("엑셀에서 읽지 않고 항상 이 값을 사용합니다. 비워두면 '열'에서 읽어옵니다. (예: 고정거래처의 수취인/주소)")]
    public string? FixedValue { get; set; }
}
