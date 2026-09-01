using System.ComponentModel;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 운송장 결과 파일에서 한 줄(=운송장번호 하나)에 대해 함께 읽어온 참고 정보. 매칭 로직은 여전히
/// 수령인 이름 기준이라 매칭 자체엔 쓰이지 않고, <see cref="TrackingAssignDialog"/>에서 사용자가
/// 여러 운송장번호 중 어느 걸 어느 주문에 적용할지 판단하는 참고용으로만 쓴다. 고객주문번호/
/// 상세주소/품목 헤더는 택배사 양식 설정에서 선택 항목이라 전부 비어있을 수 있다.
/// </summary>
public class TrackingFileRow(string trackingNo, string? orderNo, string? address, string? productName, string? courierName = null)
{
    public string TrackingNo { get; } = trackingNo;
    public string? OrderNo { get; } = orderNo;
    public string? Address { get; } = address;
    public string? ProductName { get; set; } = productName;

    /// <summary>택배사명("누적발주서 송장번호 입력" 전용 — 그 외 경로에서 만든 행은 항상 null).</summary>
    public string? CourierName { get; } = courierName;
}

/// <summary>
/// 같은 이름(+주소)의 발주확정 건이 여러 개인데, 운송장 결과 파일에서 그 이름에 해당하는 운송장번호가
/// 2개 이상 발견됐을 때 쓰는 선택창. 시스템이 어느 줄에 어느 운송장번호가 붙는지 판단할 수 없으므로
/// (같은 사람이 서로 다른 시점에 여러 번 주문했을 수도, 주소가 달라 동명이인일 수도 있음) 후보 줄마다
/// 운송장번호를 직접 골라 배정한다. 같은 운송장번호를 여러 줄에 배정하면 그 줄들은 합포장으로 함께
/// 처리된다. 배정하지 않은 줄은 그대로 남아 나중에 직접 입력해야 한다(호출부가 "확인" 열에 표시).
/// </summary>
public class TrackingAssignDialog : Form
{
    private readonly DataGridView _grid = new();
    private readonly DataGridView _fileRefGrid = new();
    private readonly List<OutboundDetail> _candidates;
    private readonly List<TrackingFileRow> _trackingRows;

    private const string Unassigned = "(미지정)";

    public List<(OutboundDetail Detail, string TrackingNo)> Assignments { get; private set; } = [];

    public TrackingAssignDialog(string recipient, string address, List<OutboundDetail> candidates, List<TrackingFileRow> trackingRows)
    {
        _candidates = candidates;
        _trackingRows = trackingRows;
        InitializeComponent(recipient, address);
    }

    private void InitializeComponent(string recipient, string address)
    {
        Text = "운송장번호 선택 적용";
        Size = new Size(900, 480);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(10) };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

        var infoLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"수령인 \"{recipient}\"({address})에 대해 운송장 파일에서 서로 다른 운송장번호 {_trackingRows.Count}개가 발견됐습니다.\n" +
                   "아래 각 건에 적용할 운송장번호를 선택하세요. 같은 운송장번호를 여러 건에 고르면 합포장으로 함께 처리됩니다.\n" +
                   $"적용하지 않을 건은 \"{Unassigned}\"으로 두면 이번엔 건너뛰고 나중에 직접 입력할 수 있습니다.",
            AutoSize = false,
        };

        // 운송장 파일 쪽 정보(읽기 전용 참고용) — 파일에 있던 운송장번호별 고객주문번호/수령인/
        // 상세주소/품목을 보여줘서, 아래 후보 그리드에서 어느 운송장번호를 골라야 할지 판단하는 데
        // 도움을 준다. 해당 헤더가 택배사 양식에 설정 안 돼 있으면 빈 칸으로 나온다.
        var fileRefLabel = new Label { Dock = DockStyle.Top, Text = "운송장 파일 내용(참고용):", AutoSize = true, Padding = new Padding(0, 0, 0, 2) };
        _fileRefGrid.Dock = DockStyle.Fill;
        _fileRefGrid.AutoGenerateColumns = false;
        _fileRefGrid.AllowUserToAddRows = false;
        _fileRefGrid.ReadOnly = true;
        _fileRefGrid.RowHeadersVisible = false;
        _fileRefGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "운송장번호", DataPropertyName = "TrackingNo", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "고객주문번호", DataPropertyName = "OrderNo", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "수령인", DataPropertyName = "Recipient", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "상세주소", DataPropertyName = "Address", Width = 200 },
            new DataGridViewTextBoxColumn { HeaderText = "품목", DataPropertyName = "ProductName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill }
        );
        _fileRefGrid.DataSource = new BindingList<TrackingFileRefRow>(
            _trackingRows.Select(r => new TrackingFileRefRow(r.TrackingNo, r.OrderNo, recipient, r.Address, r.ProductName)).ToList());

        var fileRefPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        fileRefPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fileRefPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fileRefPanel.Controls.Add(fileRefLabel, 0, 0);
        fileRefPanel.Controls.Add(_fileRefGrid, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;

        var trackingCol = new DataGridViewComboBoxColumn
        {
            Name = "AssignedTrackingNo",
            HeaderText = "적용 운송장번호",
            Width = 170,
            FlatStyle = FlatStyle.Flat,
        };
        trackingCol.Items.Add(Unassigned);
        foreach (var r in _trackingRows) trackingCol.Items.Add(r.TrackingNo);

        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "주문번호", DataPropertyName = "OrderNo", Width = 110, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "품목명", DataPropertyName = "ProductName", Width = 150, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "수량", DataPropertyName = "Qty", Width = 55, ReadOnly = true },
            new DataGridViewTextBoxColumn { HeaderText = "발주확정 시점", DataPropertyName = "CreatedAt", Width = 130, ReadOnly = true },
            trackingCol
        );
        _grid.DataSource = new BindingList<OutboundDetail>(_candidates);
        foreach (DataGridViewRow row in _grid.Rows) row.Cells["AssignedTrackingNo"].Value = Unassigned;

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnSkip = new Button { Text = "모두 건너뛰기", Width = 100 };
        var btnApply = new Button { Text = "적용", Width = 90 };
        btnSkip.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        btnApply.Click += OnApplyClick;
        buttonPanel.Controls.Add(btnSkip);
        buttonPanel.Controls.Add(btnApply);

        mainLayout.Controls.Add(infoLabel, 0, 0);
        mainLayout.Controls.Add(fileRefPanel, 0, 1);
        mainLayout.Controls.Add(_grid, 0, 2);
        mainLayout.Controls.Add(buttonPanel, 0, 3);
        Controls.Add(mainLayout);

        CancelButton = btnSkip;
    }

    /// <summary>참고 그리드 표시 전용 — 수령인은 다이얼로그를 연 쪽에서 이미 알고 있는 값을 그대로 채운다.</summary>
    private class TrackingFileRefRow(string trackingNo, string? orderNo, string recipient, string? address, string? productName)
    {
        public string TrackingNo { get; } = trackingNo;
        public string? OrderNo { get; } = orderNo;
        public string Recipient { get; } = recipient;
        public string? Address { get; } = address;
        public string? ProductName { get; } = productName;
    }

    private void OnApplyClick(object? sender, EventArgs e)
    {
        _grid.EndEdit();
        var assignments = new List<(OutboundDetail, string)>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not OutboundDetail detail) continue;
            var chosen = row.Cells["AssignedTrackingNo"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(chosen) || chosen == Unassigned) continue;
            assignments.Add((detail, chosen));
        }

        if (assignments.Count == 0)
        {
            MessageBox.Show($"적용할 건을 하나 이상 선택하세요(모두 건너뛰려면 \"모두 건너뛰기\"를 누르세요).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Assignments = assignments;
        DialogResult = DialogResult.OK;
        Close();
    }
}
