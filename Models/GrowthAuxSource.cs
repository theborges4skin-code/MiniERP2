using System.ComponentModel;

namespace MiniERP2.Models;

public class GrowthAuxSource
{
    [Category("기본")]
    [DisplayName("활성화")]
    [Description("이 보조 소스 설정을 사용할지 여부를 결정합니다.")]
    public bool Enabled { get; set; }

    [Category("기본")]
    [DisplayName("대상 표준 필드")]
    [Description("이 보조 소스에서 가져온 값을 할당할 표준 필드입니다. (예: HandlingFee)")]
    public StdField TargetStdField { get; set; }

    [Category("위치")]
    [DisplayName("시트 이름")]
    [Description("보조 데이터가 위치한 시트의 이름입니다.")]
    public string? SheetName { get; set; }

    [Category("위치")]
    [DisplayName("헤더 행 번호")]
    [Description("필드 이름이 있는 행의 번호입니다 (1부터 시작).")]
    public int HeaderRow { get; set; } = 1;

    [Category("매칭 키")]
    [DisplayName("키 컬럼 헤더")]
    [Description("메인 시트와 보조 시트를 연결할 기준이 되는 열의 이름입니다. (예: 옵션ID)")]
    public string? KeyHeader { get; set; }

    [Category("매칭 키")]
    [DisplayName("값 컬럼 헤더")]
    [Description("보조 시트에서 가져올 값이 있는 열의 이름입니다. (예: 금액)")]
    public string? ValueHeader { get; set; }

    [Category("출력 (레거시)")]
    [DisplayName("출력 열 이름")]
    [Description("레거시 시스템 호환용 출력 열 이름입니다. 일반적으로 사용되지 않습니다.")]
    public string? OutCol { get; set; }
}
