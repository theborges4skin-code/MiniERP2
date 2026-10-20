namespace MiniERP2.Controls;

/// <summary>
/// 일반 DataGridView 대신 쓰는 최소 대체 클래스 — 셀 단위 복사 버그만 고친다.
/// DataGridView는 SelectionMode 기본값 자체가 RowHeaderSelect라서, 셀 하나만
/// 클릭해도 그 행 전체가 "선택된 셀"로 잡히고 Ctrl+C 시 행 전체가 복사되어버린다
/// (사용자 신고, 2026-08-10 — 풀필먼트 발주 처리 화면에서 발견, 이후 전체 점검
/// 결과 SelectionMode를 CellSelect로 명시하지 않은 모든 그리드가 동일하게 영향
/// 받음을 확인). ExcelLikeDataGridView(Controls/ExcelLikeDataGridView.cs)가 같은
/// 문제를 이미 고쳤지만, 그 클래스는 컨텍스트 메뉴/열 레이아웃 저장/범용 붙여넣기 등
/// 부가 기능이 같이 딸려와서, 그런 기능이 필요 없거나(단순 조회/피커 그리드) 이미
/// 자체 Ctrl+V 처리를 갖고 있는 화면(풀필먼트 발주 처리 등)에 그대로 적용하면 기존
/// 동작이 바뀌거나 깨질 수 있다. 이 클래스는 오직 Ctrl+C 복사 동작만 고정한다.
/// 앱의 모든 그리드는 이 클래스나 ExcelLikeDataGridView를 써야 한다 — 순수 DataGridView를
/// 새로 만들면 행 전체 복사 버그가 다시 생긴다(2026-10-21 전체 재점검 때 18개가 남아 있었다).
/// </summary>
public class CellCopyDataGridView : DataGridView
{
    public CellCopyDataGridView()
    {
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // 기본 Ctrl+C는 DataGridView 내장 처리로 곧장 들어가 이 메서드를 거치지 않고
        // 처리되어버리므로, 커맨드 키 단계에서 가로채 항상 GridClipboard 규칙을 타게 한다.
        // 셀 편집 중에는 가로채지 않는다 — 편집 텍스트박스에서 일부 글자만 골라 복사하는
        // 일반 동작이 셀 전체 복사로 바뀌어버리기 때문(ExcelLikeDataGridView와 동일).
        if (!IsCurrentCellInEditMode && keyData == (Keys.Control | Keys.C))
        {
            GridClipboard.CopySelection(this);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
