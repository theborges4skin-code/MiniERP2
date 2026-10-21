namespace MiniERP2.MFG.Forms;

/// <summary>
/// 엑셀처럼 셀 범위 복사(Ctrl+C)·붙여넣기(Ctrl+V)를 하는 그리드(MiniERP2 ExcelLikeDataGridView의 핵심 동작을 옮긴 것).
/// 붙여넣기는 기존 행 범위 안에서만 채우고 읽기전용 칸은 건너뛴다. 편집 중일 때는 셀 안 텍스트 붙여넣기를 방해하지 않는다.
/// </summary>
public class ExcelLikeGrid : DataGridView
{
    public ExcelLikeGrid()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        AllowUserToOrderColumns = true;
        MultiSelect = true;
        SelectionMode = DataGridViewSelectionMode.CellSelect;
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        RowHeadersWidth = 28;
        BackgroundColor = SystemColors.Window;
        var menu = new ContextMenuStrip();
        menu.Items.Add("복사(&C)", null, (_, _) => CopySelection());
        menu.Items.Add("붙여넣기(&P)", null, (_, _) => Paste());
        menu.Items.Add("선택 행 삭제", null, (_, _) => DeleteSelectedRows());
        ContextMenuStrip = menu;
        DataError += (_, e) => e.ThrowException = false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!IsCurrentCellInEditMode)
        {
            if (keyData == (Keys.Control | Keys.V)) { Paste(); return true; }
            if (keyData == (Keys.Control | Keys.C)) { CopySelection(); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void CopySelection()
    {
        if (GetClipboardContent() is { } content) Clipboard.SetDataObject(content);
    }

    private void DeleteSelectedRows()
    {
        if (!AllowUserToDeleteRows) return;
        var rows = SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow).Concat(SelectedRows.Cast<DataGridViewRow>())
            .Where(r => r is not null && !r.IsNewRow).Distinct().ToList();
        if (rows.Count == 0 || !Ui.Confirm(this, $"선택한 {rows.Count}개 행을 삭제할까요? ('변경내역 저장'을 눌러야 실제로 삭제됩니다)")) return;
        foreach (var r in rows) Rows.Remove(r!);
    }

    private void Paste()
    {
        if (CurrentCell is null || !Clipboard.ContainsText()) return;
        var lines = Clipboard.GetText().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var startRow = CurrentCell.RowIndex;
        var startCol = CurrentCell.ColumnIndex;
        var lastRow = Rows.Count - (AllowUserToAddRows ? 2 : 1);
        for (var ro = 0; ro < lines.Length; ro++)
        {
            var rowIndex = startRow + ro;
            if (rowIndex > lastRow) break;
            var values = lines[ro].Split('\t');
            for (var co = 0; co < values.Length; co++)
            {
                var colIndex = startCol + co;
                if (colIndex >= Columns.Count) break;
                var cell = Rows[rowIndex].Cells[colIndex];
                if (cell.ReadOnly || cell.OwningColumn?.Visible != true) continue;
                try
                {
                    var type = Nullable.GetUnderlyingType(cell.ValueType ?? typeof(string)) ?? cell.ValueType ?? typeof(string);
                    var text = values[co];
                    cell.Value = string.IsNullOrEmpty(text) && type != typeof(string) ? DBNull.Value
                        : type == typeof(bool) ? text.Trim().ToUpperInvariant() is "Y" or "TRUE" or "1"
                        : Convert.ChangeType(text.Replace(",", ""), type, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    // 형식이 맞지 않는 값은 건너뛴다.
                }
            }
        }
    }
}
