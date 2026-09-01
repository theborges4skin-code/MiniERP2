using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 거래처 마감보드 메모 추가/조회/삭제. 좌측 거래처 목록에서 열면 <paramref name="targetOutboundDetailIds"/>
/// 없이(거래처 전체 메모) 생성되고, 우측 라인 상세에서 단일/다중 선택 후 열면 그 라인들을 참조하는
/// 메모로 생성된다. 기존 메모(양쪽 종류 모두)도 같은 창에서 조회·삭제할 수 있다.
/// </summary>
public class PartnerClosingMemoDialog : Form
{
    private readonly PartnerClosingMemoRepository _repo = new();
    private readonly string _period;
    private readonly string _partyKey;
    private readonly List<long> _targetOutboundDetailIds;

    private readonly ExcelLikeDataGridView _grid = new();
    private readonly TextBox _newMemoBox = new();
    private readonly CheckBox _showOnStatementCheck = new();
    private readonly CheckBox _showOnLedgerCheck = new();

    public bool Changed { get; private set; }

    public PartnerClosingMemoDialog(string period, string partyKey, string partyLabel, List<long> targetOutboundDetailIds)
    {
        _period = period;
        _partyKey = partyKey;
        _targetOutboundDetailIds = targetOutboundDetailIds;
        InitializeComponent(partyLabel);
        Load += (s, e) => RunQuery();
    }

    private void InitializeComponent(string partyLabel)
    {
        Text = "거래처 마감보드 메모";
        Size = new Size(640, 560);
        StartPosition = FormStartPosition.CenterParent;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(8) };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var listGroup = new GroupBox { Text = $"{partyLabel} — 등록된 메모", Dock = DockStyle.Fill };
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "대상", Name = "TargetLabel", DataPropertyName = "TargetLabel", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "노출", Name = "VisibilityLabel", DataPropertyName = "VisibilityLabel", Width = 80 },
            new DataGridViewTextBoxColumn { HeaderText = "메모", Name = "MemoText", DataPropertyName = "MemoText", Width = 300, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { HeaderText = "등록일시", Name = "CreatedAtText", DataPropertyName = "CreatedAtText", Width = 120 }
        );
        var deleteMenu = new ContextMenuStrip();
        var deleteItem = new ToolStripMenuItem("이 메모 삭제");
        deleteItem.Click += OnDeleteClick;
        deleteMenu.Items.Add(deleteItem);
        _grid.ContextMenuStrip = deleteMenu;
        listGroup.Controls.Add(_grid);

        var addGroup = new GroupBox
        {
            Text = _targetOutboundDetailIds.Count == 0
                ? "새 메모 추가 — 대상: 거래처 전체"
                : $"새 메모 추가 — 대상: 선택 라인 {_targetOutboundDetailIds.Count}건",
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        var addLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, AutoSize = true };
        _newMemoBox.Multiline = true;
        _newMemoBox.Height = 60;
        _newMemoBox.Dock = DockStyle.Fill;
        addLayout.Controls.Add(_newMemoBox);

        var visibilityPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        _showOnStatementCheck.Text = "명세표에 표시";
        _showOnStatementCheck.Checked = true;
        _showOnStatementCheck.AutoSize = true;
        _showOnLedgerCheck.Text = "매출장에 표시";
        _showOnLedgerCheck.Checked = true;
        _showOnLedgerCheck.AutoSize = true;
        _showOnLedgerCheck.Margin = new Padding(16, 3, 3, 3);
        visibilityPanel.Controls.Add(_showOnStatementCheck);
        visibilityPanel.Controls.Add(_showOnLedgerCheck);
        addLayout.Controls.Add(visibilityPanel);

        var btnAdd = new Button { Text = "메모 추가", Size = new Size(90, 28), Anchor = AnchorStyles.Right };
        btnAdd.Click += OnAddClick;
        var addButtonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        addButtonPanel.Controls.Add(btnAdd);
        addLayout.Controls.Add(addButtonPanel);

        addGroup.Controls.Add(addLayout);

        var btnClose = new Button { Text = "닫기", Size = new Size(80, 30) };
        btnClose.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
        var closePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        closePanel.Controls.Add(btnClose);

        mainLayout.Controls.Add(listGroup, 0, 0);
        mainLayout.Controls.Add(addGroup, 0, 1);
        mainLayout.Controls.Add(closePanel, 0, 2);

        Controls.Add(mainLayout);
        AcceptButton = btnClose;
    }

    private void RunQuery()
    {
        var memos = _repo.GetForParty(_period, _partyKey);
        _grid.DataSource = memos.Select(m => new MemoRow(m)).ToList();
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        var text = _newMemoBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("메모 내용을 입력하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_showOnStatementCheck.Checked && !_showOnLedgerCheck.Checked)
        {
            MessageBox.Show("명세표/매출장 중 최소 하나에는 표시하도록 선택하세요(둘 다 끄면 어디에도 출력되지 않습니다).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _repo.Add(new PartnerClosingMemo
        {
            Period = _period,
            PartyKey = _partyKey,
            MemoText = text,
            ShowOnStatement = _showOnStatementCheck.Checked,
            ShowOnLedger = _showOnLedgerCheck.Checked,
            OutboundDetailIds = _targetOutboundDetailIds,
        });

        Changed = true;
        _newMemoBox.Text = "";
        RunQuery();
    }

    private void OnDeleteClick(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem is not MemoRow row) return;
        if (MessageBox.Show("이 메모를 삭제하시겠습니까?", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        _repo.Delete(row.Source.Id);
        Changed = true;
        RunQuery();
    }

    private sealed class MemoRow(PartnerClosingMemo source)
    {
        public PartnerClosingMemo Source { get; } = source;
        public string TargetLabel { get; } = source.IsPartyLevel ? "거래처 전체" : $"라인 {source.OutboundDetailIds.Count}건";
        public string VisibilityLabel { get; } = (source.ShowOnStatement, source.ShowOnLedger) switch
        {
            (true, true) => "명세표+매출장",
            (true, false) => "명세표만",
            (false, true) => "매출장만",
            _ => "미노출",
        };
        public string MemoText { get; } = source.MemoText;
        public string CreatedAtText { get; } = source.CreatedAt.ToString("yyyy-MM-dd HH:mm");
    }
}
