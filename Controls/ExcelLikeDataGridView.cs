using MiniERP2.Config;
using MiniERP2.Models;
using MiniERP2.Utils;
using System.Collections;
using System.ComponentModel;

namespace MiniERP2.Controls;

public class ExcelLikeDataGridView : DataGridView
{
    private readonly GridSettingsService _gridSettingsService = new();
    private string _persistenceKey = string.Empty;
    private readonly ToolStripMenuItem _copyMenuItem;
    private readonly ToolStripMenuItem _pasteMenuItem;

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
        DoubleBuffered = true;
        AllowUserToOrderColumns = true;
        AllowUserToResizeRows = true;
        MultiSelect = true;
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        ColumnHeaderMouseClick += OnColumnHeaderMouseClick;

        // 공통 컨텍스트 메뉴 설정 — 복사/붙여넣기는 고정 항목이고, 이 창에 있는 모든 버튼의
        // 기능은 메뉴가 열릴 때마다 동적으로 추가된다(OnContextMenuOpening).
        var contextMenu = new ContextMenuStrip();
        _copyMenuItem = new ToolStripMenuItem("복사(&C)", null, OnCopyClick);
        _pasteMenuItem = new ToolStripMenuItem("붙여넣기(&P)", null, OnPasteClick);

        contextMenu.Items.AddRange(new ToolStripItem[] { _copyMenuItem, _pasteMenuItem });
        ContextMenuStrip = contextMenu;
    }

    /// <summary>
    /// Control.ContextMenuStrip은 virtual이라, 파생 폼이 나중에 자체 메뉴로 교체해도
    /// "이 창의 버튼" 동적 메뉴(OnContextMenuOpening)가 새 메뉴에도 항상 따라붙도록 가로챈다.
    /// </summary>
    public override ContextMenuStrip? ContextMenuStrip
    {
        get => base.ContextMenuStrip;
        set
        {
            if (base.ContextMenuStrip != null) base.ContextMenuStrip.Opening -= OnContextMenuOpening;
            base.ContextMenuStrip = value;
            if (value != null) value.Opening += OnContextMenuOpening;
        }
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
    /// 99.2 공통 UX: 헤더를 클릭하면 데이터소스 종류(DataTable/BindingList/일반 IList)에 관계없이
    /// 항상 정렬되도록 한다. DataGridView의 기본 Sort()는 IBindingList.SupportsSorting이 false인
    /// BindingList&lt;T&gt; 등에서는 예외를 던지기 때문에, 데이터소스를 직접 들여다보고 정렬한다.
    /// </summary>
    private void OnColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.RowIndex != -1) return;
        if (e.ColumnIndex < 0 || e.ColumnIndex >= Columns.Count) return;

        var column = Columns[e.ColumnIndex];
        var propertyName = string.IsNullOrEmpty(column.DataPropertyName) ? column.Name : column.DataPropertyName;
        if (string.IsNullOrEmpty(propertyName)) return;

        var ascending = column.HeaderCell.SortGlyphDirection != SortOrder.Ascending;
        if (!GridSorter.TrySort(DataSource, propertyName, ascending)) return;

        // BindingList<T>는 Clear+Add가 ListChanged를 일으켜 자동 갱신되지만, 변경통지가 없는 일반
        // List<T> 등은 DataSource를 다시 설정해야 그리드가 갱신된다.
        if (DataSource is IList and not IBindingList)
        {
            var dataSource = DataSource;
            DataSource = null;
            DataSource = dataSource;
        }

        foreach (DataGridViewColumn col in Columns) col.HeaderCell.SortGlyphDirection = SortOrder.None;

        // AutoGenerateColumns가 bool 컬럼을 DataGridViewCheckBoxColumn으로 만들 때 SortMode를
        // 자동으로 NotSortable로 고정해버린다(다른 타입 컬럼은 Automatic). 정렬 자체는 위
        // GridSorter.TrySort로 이미 끝났으니, 화살표 표시만 가능하도록 Programmatic으로 승격한다.
        // Button/Image 열은 SortMode 전환 자체가 막혀있으므로 그 경우만 화살표 표시를 생략한다.
        if (column.SortMode == DataGridViewColumnSortMode.NotSortable)
        {
            try { column.SortMode = DataGridViewColumnSortMode.Programmatic; }
            catch (InvalidOperationException) { Refresh(); return; }
        }
        column.HeaderCell.SortGlyphDirection = ascending ? SortOrder.Ascending : SortOrder.Descending;
        Refresh();
    }

    /// <summary>
    /// 99.2 공통 UX: 우클릭 메뉴를 열 때마다, 이 그리드가 속한 창(Form)에 있는 모든 버튼의 기능을
    /// 동적으로 추가한다(복사/붙여넣기 등 고정 항목은 그대로 유지). 비활성/숨김 버튼은 제외한다.
    /// </summary>
    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenuStrip menu) return;

        var fixedCount = menu.Items.Cast<ToolStripItem>().TakeWhile(i => i != _pasteMenuItem).Count() + 1;
        // _pasteMenuItem이 이 메뉴에 없는 폼 전용 메뉴라면(파생 폼이 메뉴를 통째로 새로 만든 경우)
        // 모든 기존 항목을 고정 항목으로 보고 그 뒤에만 추가한다.
        if (!menu.Items.Contains(_pasteMenuItem)) fixedCount = menu.Items.Count;

        while (menu.Items.Count > fixedCount) menu.Items.RemoveAt(fixedCount);

        var form = FindForm();
        if (form == null) return;

        var buttons = new List<Button>();
        CollectButtons(form, buttons);
        var actionable = buttons.Where(b => b.Visible && b.Enabled && !string.IsNullOrWhiteSpace(b.Text)).ToList();
        if (actionable.Count == 0) return;

        menu.Items.Add(new ToolStripSeparator());
        var header = new ToolStripMenuItem("이 창의 기능") { Enabled = false };
        menu.Items.Add(header);
        foreach (var button in actionable)
        {
            var label = button.Text.Replace("&", "");
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => button.PerformClick();
            menu.Items.Add(item);
        }
    }

    private static void CollectButtons(Control parent, List<Button> result)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Button button) result.Add(button);
            if (child.HasChildren) CollectButtons(child, result);
        }
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