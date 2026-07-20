using System.ComponentModel;
using MiniERP2.Models;

namespace MiniERP2.Forms;

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
    private readonly List<OutboundDetail> _candidates;
    private readonly List<string> _trackingNos;

    private const string Unassigned = "(미지정)";

    public List<(OutboundDetail Detail, string TrackingNo)> Assignments { get; private set; } = [];

    public TrackingAssignDialog(string recipient, string address, List<OutboundDetail> candidates, List<string> trackingNos)
    {
        _candidates = candidates;
        _trackingNos = trackingNos;
        InitializeComponent(recipient, address);
    }

    private void InitializeComponent(string recipient, string address)
    {
        Text = "운송장번호 선택 적용";
        Size = new Size(780, 400);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

        var infoLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"수령인 \"{recipient}\"({address})에 대해 운송장 파일에서 서로 다른 운송장번호 {_trackingNos.Count}개가 발견됐습니다.\n" +
                   "아래 각 건에 적용할 운송장번호를 선택하세요. 같은 운송장번호를 여러 건에 고르면 합포장으로 함께 처리됩니다.\n" +
                   $"적용하지 않을 건은 \"{Unassigned}\"으로 두면 이번엔 건너뛰고 나중에 직접 입력할 수 있습니다.",
            AutoSize = false,
        };

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
        foreach (var tn in _trackingNos) trackingCol.Items.Add(tn);

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
        mainLayout.Controls.Add(_grid, 0, 1);
        mainLayout.Controls.Add(buttonPanel, 0, 2);
        Controls.Add(mainLayout);

        CancelButton = btnSkip;
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
