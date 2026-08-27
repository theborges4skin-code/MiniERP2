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
    private int _permanentItemCount;

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
        _permanentItemCount = contextMenu.Items.Count;
    }

    /// <summary>
    /// 파생 폼이 항상 표시되어야 하는 우클릭 메뉴 항목(예: 분리배송/합포장 등)을 추가할 때 쓴다.
    /// 이 메서드 대신 ContextMenuStrip.Items.Add를 직접 쓰면, 메뉴가 열릴 때마다
    /// OnContextMenuOpening이 복사/붙여넣기 이후 항목을 전부 지우고 "이 창의 기능"만 다시
    /// 채우기 때문에 방금 추가한 항목이 곧바로 사라진다.
    /// </summary>
    public void AddPermanentContextMenuItems(params ToolStripItem[] items)
    {
        ContextMenuStrip!.Items.AddRange(items);
        _permanentItemCount += items.Length;
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
        // 셀을 편집 중일 때는(예: 메모행 추가 직후 자동으로 편집 모드에 들어간 "상품명" 칸)
        // Ctrl+C/V를 가로채면 안 된다 — 가로채면 편집 중인 텍스트박스 안에서 커서 위치에
        // 텍스트를 붙여넣는 일반적인 동작 대신 셀 범위 붙여넣기(OnPasteClick)가 실행돼, 편집
        // 중이던 내용이 커밋될 때 그 셀 값을 도로 덮어써버려 "붙여넣기가 안 된다"는 문제가
        // 있었다(사용자 신고). 편집 중이 아닐 때만(엑셀처럼 셀/행 범위를 복사·붙여넣기할 때) 이
        // 그리드의 자체 규칙을 적용한다.
        if (!IsCurrentCellInEditMode)
        {
            if (keyData == (Keys.Control | Keys.V))
            {
                OnPasteClick(this, EventArgs.Empty);
                return true;
            }
            // 기본 Ctrl+C는 DataGridView 내장 처리로 곧장 들어가 CopySelection()을 거치지 않는다
            // (아래 버그 설명 참고) — 이 앱의 복사 규칙을 항상 타도록 여기서도 가로챈다.
            if (keyData == (Keys.Control | Keys.C))
            {
                CopySelection();
                return true;
            }
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

        // 이 메뉴가 생성자에서 만든 그 인스턴스면(복사/붙여넣기 + AddPermanentContextMenuItems로
        // 추가된 항목) _permanentItemCount까지가 고정 항목이다. 파생 폼이 메뉴를 통째로 새로
        // 만들어 갈아끼운 경우(_pasteMenuItem이 없음)는 그 시점의 기존 항목을 전부 고정으로 본다.
        var fixedCount = menu.Items.Contains(_pasteMenuItem) ? _permanentItemCount : menu.Items.Count;

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

    private void OnCopyClick(object? sender, EventArgs e) => CopySelection();

    /// <summary>
    /// 이 앱 대부분의 그리드는 SelectionMode가 FullRowSelect/RowHeaderSelect다(우클릭 메뉴/버튼으로
    /// 행 단위 일괄 작업을 고르기 위함 — 마감확정, 선택 삭제 등). 그 모드에서는 셀 하나만 클릭해도
    /// DataGridView가 그 행 전체를 "선택된 셀"로 잡아버려서, 그대로 복사하면 셀 하나만 복사하려던
    /// 의도와 달리 행 전체가 복사돼 다른 셀에 붙여넣을 때 여러 칸이 한꺼번에 덮어써진다(사용자 신고,
    /// 2026-07-30). 클릭 한 번으로 걸린 단일 행 선택은 지금 클릭한 셀 하나만 복사하고, 여러 행을
    /// 일부러 골랐을 때(Ctrl/Shift+클릭)는 기존처럼 그 행들 전체를 복사한다(다른 곳에 붙여넣기용
    /// 배치 복사는 계속 지원). CellSelect 모드 그리드는 원래도 셀 단위라 영향 없음.
    /// </summary>
    private void CopySelection()
    {
        var isRowBasedMode = SelectionMode is DataGridViewSelectionMode.FullRowSelect or DataGridViewSelectionMode.RowHeaderSelect;

        if (isRowBasedMode && SelectedRows.Count <= 1 && CurrentCell != null)
        {
            Clipboard.SetText(CurrentCell.FormattedValue?.ToString() ?? string.Empty);
            return;
        }

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