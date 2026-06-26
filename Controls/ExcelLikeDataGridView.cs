using MiniERP2.Config;
using MiniERP2.Models;
using System.ComponentModel;

namespace MiniERP2.Controls;

public class ExcelLikeDataGridView : DataGridView
{
    private readonly GridSettingsService _gridSettingsService = new();
    private string _persistenceKey = string.Empty;

    /// <summary>
    /// 레이아웃 설정을 저장하고 로드하는 데 사용할 고유 키입니다.
    /// 일반적으로 Form.Name + "." + DataGridView.Name으로 설정합니다.
    /// </summary>
    [Category("Behavior")]
    [Description("레이아웃 설정을 저장하고 로드하는 데 사용할 고유 키입니다.")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PersistenceKey
    {
        get => _persistenceKey;
        set
        {
            _persistenceKey = value;
            if (!string.IsNullOrEmpty(_persistenceKey) && !DesignMode)
            {
                LoadLayout();
            }
        }
    }

    public ExcelLikeDataGridView()
    {
        // 엑셀과 유사한 동작을 위한 기본 속성 설정
        AllowUserToOrderColumns = true;
        AllowUserToResizeRows = true;
        MultiSelect = true;
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;

        // 공통 컨텍스트 메뉴 설정
        var contextMenu = new ContextMenuStrip();
        var copyMenuItem = new ToolStripMenuItem("복사(&C)", null, OnCopyClick);
        var pasteMenuItem = new ToolStripMenuItem("붙여넣기(&P)", null, OnPasteClick);

        contextMenu.Items.AddRange(new ToolStripItem[] { copyMenuItem, pasteMenuItem });
        ContextMenuStrip = contextMenu;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.V))
        {
            OnPasteClick(this, EventArgs.Empty);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// 현재 열 레이아웃을 파일에 저장합니다.
    /// Form의 FormClosing 이벤트에서 호출하는 것을 권장합니다.
    /// </summary>
    public void SaveLayout()
    {
        if (string.IsNullOrEmpty(PersistenceKey) || DesignMode) return;

        var layouts = Columns.Cast<DataGridViewColumn>()
            .Select(c => new GridColumnLayout
            {
                Name = c.Name,
                DisplayIndex = c.DisplayIndex,
                Width = c.Width,
                Visible = c.Visible
            }).ToList();

        _gridSettingsService.SaveLayout(PersistenceKey, layouts);
    }

    /// <summary>
    /// 파일에서 열 레이아웃을 불러옵니다.
    /// PersistenceKey가 설정되면 자동으로 호출됩니다.
    /// </summary>
    public void LoadLayout()
    {
        if (string.IsNullOrEmpty(PersistenceKey) || DesignMode) return;

        var layouts = _gridSettingsService.LoadLayout(PersistenceKey);
        if (layouts is null) return;

        var layoutDict = layouts.ToDictionary(l => l.Name);
        foreach (var column in Columns.Cast<DataGridViewColumn>())
        {
            if (layoutDict.TryGetValue(column.Name, out var layout))
            {
                column.DisplayIndex = layout.DisplayIndex;
                column.Width = layout.Width;
                column.Visible = layout.Visible;
            }
        }
    }

    private void OnCopyClick(object? sender, EventArgs e)
    {
        if (SelectedCells.Count > 0)
        {
            Clipboard.SetDataObject(GetClipboardContent());
        }
    }

    /// <summary>
    /// 클립보드의 탭/개행 구분 텍스트(엑셀 복사 형식)를 현재 셀을 기준으로 붙여넣습니다.
    /// 기존 행의 범위 안에서만 채워지며(새 행은 추가하지 않음), 읽기전용 셀은 건너뜁니다.
    /// </summary>
    private void OnPasteClick(object? sender, EventArgs e)
    {
        if (CurrentCell == null || !Clipboard.ContainsText()) return;

        var text = Clipboard.GetText();
        if (string.IsNullOrEmpty(text)) return;

        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        var startRow = CurrentCell.RowIndex;
        var startCol = CurrentCell.ColumnIndex;
        var lastDataRowIndex = Rows.Count - (AllowUserToAddRows ? 2 : 1);

        for (int rowOffset = 0; rowOffset < lines.Length; rowOffset++)
        {
            var targetRowIndex = startRow + rowOffset;
            if (targetRowIndex > lastDataRowIndex) break; // 새 행은 만들지 않고, 기존 행 범위 안에서만 채운다

            var values = lines[rowOffset].Split('\t');
            for (int colOffset = 0; colOffset < values.Length; colOffset++)
            {
                var targetColIndex = startCol + colOffset;
                if (targetColIndex >= Columns.Count) break;

                var cell = Rows[targetRowIndex].Cells[targetColIndex];
                if (cell.ReadOnly || !cell.OwningColumn.Visible) continue;

                SetCellValue(cell, values[colOffset]);
            }
        }
    }

    private static void SetCellValue(DataGridViewCell cell, string text)
    {
        try
        {
            var targetType = cell.ValueType ?? typeof(string);
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (string.IsNullOrEmpty(text) && underlyingType != typeof(string))
            {
                cell.Value = null;
            }
            else
            {
                cell.Value = Convert.ChangeType(text, underlyingType);
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            // 형식이 맞지 않는 값은 무시하고 해당 셀은 건너뛴다.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // FormClosing 이벤트가 항상 발생한다고 보장할 수 없으므로,
            // Dispose 시점에도 레이아웃을 저장합니다.
            SaveLayout();
        }
        base.Dispose(disposing);
    }
}