using System.Text;

namespace MiniERP2.Controls;

/// <summary>
/// ExcelLikeDataGridView/CellCopyDataGridView가 공유하는 Ctrl+C 규칙. 이 앱 대부분의 그리드는
/// SelectionMode가 FullRowSelect/RowHeaderSelect라(행 단위 일괄 작업용) 셀 하나만 클릭해도
/// DataGridView가 그 행 전체를 "선택된 셀"로 잡아, 기본 복사는 행 전체를 복사해버린다 — 다른
/// 셀에 붙여넣으면 여러 칸이 한꺼번에 덮어써진다(사용자 신고 2026-07-30, 2026-08-10, 2026-10-21).
/// 행 기반 모드에서는 항상 "지금 클릭한 셀의 열"만 복사한다: 한 행이면 그 셀 값 하나, 여러 행을
/// 고르면(드래그/Shift/Ctrl+클릭) 선택한 행들의 같은 열 값을 위→아래 순서로 줄바꿈해 복사한다
/// (예전에는 여러 행 선택 시 행 전체를 복사해 신고가 이어졌다). 행 전체가 필요하면
/// ExcelLikeDataGridView의 우클릭 "행 전체 복사"를 쓴다. CellSelect 등 셀 단위 모드는 선택한
/// 셀 범위를 그대로 복사한다.
/// </summary>
internal static class GridClipboard
{
    public static void CopySelection(DataGridView grid)
    {
        var isRowBasedMode = grid.SelectionMode is DataGridViewSelectionMode.FullRowSelect or DataGridViewSelectionMode.RowHeaderSelect;

        if (isRowBasedMode && grid.CurrentCell != null && grid.SelectedRows.Count > 0)
        {
            var columnIndex = grid.CurrentCell.ColumnIndex;
            var rows = grid.SelectedRows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).OrderBy(r => r.Index).ToList();
            if (rows.Count <= 1)
            {
                SetText(FormattedText(grid.CurrentCell));
                return;
            }
            SetText(string.Join(Environment.NewLine, rows.Select(r => FormattedText(r.Cells[columnIndex]))));
            return;
        }

        if (isRowBasedMode && grid.CurrentCell != null && grid.SelectedCells.Count <= 1)
        {
            SetText(FormattedText(grid.CurrentCell));
            return;
        }

        if (grid.SelectedCells.Count > 0)
        {
            var content = grid.GetClipboardContent();
            if (content != null) Clipboard.SetDataObject(content);
        }
    }

    /// <summary>선택한 셀이 걸친 행들을 화면에 보이는 열 순서대로, 탭/줄바꿈 구분(엑셀 붙여넣기 형식)으로
    /// 복사한다. 셀 단위 복사가 기본이 된 뒤에도 행을 통째로 다른 곳에 옮길 때 쓴다.</summary>
    public static void CopyEntireRows(DataGridView grid)
    {
        var rows = grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow!)
            .Concat(grid.SelectedRows.Cast<DataGridViewRow>())
            .Where(r => r != null && !r.IsNewRow)
            .Distinct()
            .OrderBy(r => r.Index)
            .ToList();
        if (rows.Count == 0 && grid.CurrentRow is { IsNewRow: false } current) rows.Add(current);
        if (rows.Count == 0) return;

        var columns = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join("\t", columns.Select(c => FormattedText(row.Cells[c.Index]))));
        }
        SetText(sb.ToString().TrimEnd('\r', '\n'));
    }

    private static string FormattedText(DataGridViewCell cell) => cell.FormattedValue?.ToString() ?? string.Empty;

    private static void SetText(string text)
    {
        // Clipboard.SetText는 빈 문자열을 받으면 ArgumentNullException을 던진다.
        if (string.IsNullOrEmpty(text)) Clipboard.Clear();
        else Clipboard.SetText(text);
    }
}
