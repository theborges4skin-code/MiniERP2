using System.ComponentModel;
using System.Text.RegularExpressions;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// [CSKU별 통계] "거래처 마감 불러오기" — 마감월의 거래처 마감보드 목록을 보여주고 통계에 넣을 거래처를
/// 체크로 고른다. 온라인 채널 매출을 마감보드로 옮겨온 거래처(온라인취합·쿠팡그로스 등)는 온라인
/// 분석결과 파일과 이중으로 잡힐 수 있으니 사용자가 직접 빼도록 목록을 그대로 보여준다.
/// </summary>
public class PartnerClosingPickerDialog : Form
{
    private readonly PartnerClosingRepository _closingRepo = new();
    private readonly SalesChannelRepository _channelRepo = new();
    private readonly PartnerMasterRepository _masterRepo = new();

    private TextBox _periodBox = new();
    private CheckBox _includeAllCheck = new();
    private ExcelLikeDataGridView _grid = new();
    private Label _summaryLabel = new();
    private BindingList<PickRow> _rows = [];

    public string Period => _periodBox.Text.Trim();

    /// <summary>[확인] 시 체크된 거래처 요약.</summary>
    public List<PartnerClosingSummary> SelectedSummaries { get; private set; } = [];

    public PartnerClosingPickerDialog(string initialPeriod)
    {
        InitializeComponent(initialPeriod);
        Load += (s, e) => LoadRows();
    }

    private void InitializeComponent(string initialPeriod)
    {
        Text = "거래처 마감 불러오기";
        Size = new Size(820, 600);
        StartPosition = FormStartPosition.CenterParent;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5), WrapContents = false };
        _periodBox = new TextBox { Width = 80, Text = initialPeriod };
        var btnLoad = new Button { Text = "조회", Size = new Size(60, 28) };
        btnLoad.Click += (s, e) => LoadRows();
        _includeAllCheck = new CheckBox { Text = "전체 거래처 보기", AutoSize = true, Padding = new Padding(8, 5, 0, 0) };
        _includeAllCheck.CheckedChanged += (s, e) => LoadRows();
        var btnCheckAll = new Button { Text = "전체 선택", Size = new Size(75, 28), Margin = new Padding(16, 3, 3, 3) };
        btnCheckAll.Click += (s, e) => SetAllChecked(true);
        var btnUncheckAll = new Button { Text = "전체 해제", Size = new Size(75, 28) };
        btnUncheckAll.Click += (s, e) => SetAllChecked(false);

        toolbar.Controls.Add(new Label { Text = "마감월:", AutoSize = true, Padding = new Padding(0, 5, 2, 0) });
        toolbar.Controls.Add(_periodBox);
        toolbar.Controls.Add(btnLoad);
        toolbar.Controls.Add(_includeAllCheck);
        toolbar.Controls.Add(btnCheckAll);
        toolbar.Controls.Add(btnUncheckAll);

        _grid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            PersistenceKey = "PartnerClosingPickerDialog.Grid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
        };
        var money = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight };
        _grid.Columns.AddRange(
            new DataGridViewCheckBoxColumn { HeaderText = "선택", Name = "Checked", DataPropertyName = "Checked", Width = 45 },
            new DataGridViewTextBoxColumn { HeaderText = "거래처", Name = "PartyName", DataPropertyName = "PartyName", Width = 200, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "상태", Name = "Status", DataPropertyName = "Status", Width = 70, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "라인수", Name = "LineCount", DataPropertyName = "LineCount", Width = 60, ReadOnly = true, DefaultCellStyle = money },
            new DataGridViewTextBoxColumn { HeaderText = "공급가합", Name = "TotalSupply", DataPropertyName = "TotalSupply", Width = 110, ReadOnly = true, DefaultCellStyle = money },
            new DataGridViewTextBoxColumn { HeaderText = "이익", Name = "TotalProfit", DataPropertyName = "TotalProfit", Width = 100, ReadOnly = true, DefaultCellStyle = money },
            new DataGridViewTextBoxColumn { HeaderText = "비고", Name = "Note", DataPropertyName = "Note", ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        // 체크 즉시 합계를 갱신하려면 체크박스 셀 편집을 바로 커밋해야 한다.
        _grid.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (s, e) => UpdateSummary();
        // 여러 줄을 선택한 뒤 스페이스로 한꺼번에 체크 토글.
        _grid.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Space) return;
            var selected = _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<PickRow>().ToList();
            if (selected.Count <= 1) return;
            var target = !selected.All(r => r.Checked);
            foreach (var r in selected) r.Checked = target;
            _grid.Refresh();
            UpdateSummary();
            e.Handled = true;
        };

        _summaryLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
        var btnCancel = new Button { Text = "취소", Size = new Size(80, 30), DialogResult = DialogResult.Cancel };
        var btnOk = new Button { Text = "불러오기", Size = new Size(90, 30) };
        btnOk.Click += OnOkClick;
        buttons.Controls.Add(btnCancel);
        buttons.Controls.Add(btnOk);
        CancelButton = btnCancel;

        mainLayout.Controls.Add(toolbar, 0, 0);
        mainLayout.Controls.Add(_grid, 0, 1);
        mainLayout.Controls.Add(_summaryLabel, 0, 2);
        mainLayout.Controls.Add(buttons, 0, 3);
        Controls.Add(mainLayout);
    }

    private void LoadRows()
    {
        if (!Regex.IsMatch(Period, @"^\d{4}-\d{2}$"))
        {
            _summaryLabel.Text = "마감월 형식이 올바르지 않습니다(YYYY-MM).";
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            var channelNames = _channelRepo.GetAll().ToDictionary(c => c.ChannelCode, c => c.ChannelName);
            var manualMaster = _masterRepo.GetAll().Where(p => p.IsManual).ToDictionary(p => p.PartyKey);
            var rows = new List<PickRow>();
            foreach (var key in _closingRepo.GetVisiblePartyKeys(Period, _includeAllCheck.Checked))
            {
                var nameHint = key.StartsWith("CH:", StringComparison.Ordinal)
                    ? channelNames.GetValueOrDefault(key["CH:".Length..], key["CH:".Length..])
                    : manualMaster.GetValueOrDefault(key)?.PartyName ?? key;
                var summary = _closingRepo.GetSummary(Period, key, nameHint);
                // 이번 달 실적이 없는 거래처는 목록만 길게 만들 뿐이라 뺀다.
                if (summary.Lines.Count == 0 && summary.TotalSupply == 0) continue;
                rows.Add(new PickRow(summary));
            }
            _rows = new BindingList<PickRow>(rows.OrderBy(r => r.PartyName, StringComparer.CurrentCulture).ToList());
            _grid.DataSource = _rows;
            UpdateSummary();
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void SetAllChecked(bool value)
    {
        foreach (var r in _rows) r.Checked = value && r.LineCount > 0;
        _grid.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var checkedRows = _rows.Where(r => r.Checked).ToList();
        _summaryLabel.Text = $"선택 {checkedRows.Count}/{_rows.Count}곳 — 공급가 {checkedRows.Sum(r => r.TotalSupply):N0} / 이익 {checkedRows.Sum(r => r.TotalProfit):N0} (VAT포함)   ※ 온라인 분석결과 파일과 겹치는 거래처는 체크 해제하세요. Space = 선택 줄 일괄 토글";
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        _grid.EndEdit();
        var picked = _rows.Where(r => r.Checked).ToList();
        if (picked.Count == 0)
        {
            MessageBox.Show(this, "불러올 거래처를 체크하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SelectedSummaries = picked.Select(r => r.Source).ToList();
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed class PickRow(PartnerClosingSummary source)
    {
        public PartnerClosingSummary Source { get; } = source;

        // 라인 없이 금액만 입력된 수동 거래처는 품목을 알 수 없어 통계에 넣을 수 없다 — 기본 해제.
        public bool Checked { get; set; } = source.Lines.Count > 0;
        public string PartyName { get; } = source.PartyName;
        public string Status { get; } = source.Status;
        public int LineCount { get; } = source.Lines.Count;
        public decimal TotalSupply { get; } = source.TotalSupply;
        public decimal TotalProfit { get; } = source.TotalProfit;
        public string Note { get; } = source.Lines.Count == 0 ? "라인 없음(금액만 입력) — 품목 통계 불가" : source.ReconcileNote;
    }
}
