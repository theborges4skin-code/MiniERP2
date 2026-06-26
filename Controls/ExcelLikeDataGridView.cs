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

    private void OnPasteClick(object? sender, EventArgs e)
    {
        // 붙여넣기 로직은 복잡하며, 데이터 소스의 상태에 따라 달라집니다.
        // 여기서는 기본 아이디어만 제시하며, 실제 구현 시에는
        // 데이터 바인딩 여부, 트랜잭션 등을 고려해야 합니다.
        // TODO: 붙여넣기 로직 구현
        MessageBox.Show("붙여넣기 기능은 아직 구현되지 않았습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
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