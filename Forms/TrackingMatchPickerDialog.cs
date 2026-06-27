using System.ComponentModel;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 운송장 결과 파일의 한 줄(수령인+운송장번호)에 동일한 수령인을 가진 발주확정 건이 여러 개일 때,
/// 그 운송장번호를 적용할 건을 사용자가 직접 골라야 한다(주소/품목이 운송장 파일엔 불분명하게
/// 나오므로 시스템이 자동으로 판단할 수 없음). 후보 목록에 수령인/주소/품목명/발주확정 시점을
/// 보여줘 구분할 수 있게 한다. 여러 건을 함께 선택하면(택배사에서 합포장돼 한 운송장으로 발송된
/// 경우) 모두 같은 운송장번호로 처리되고, 1건만 선택하면 그 건에만 개별로 적용된다.
/// </summary>
public class TrackingMatchPickerDialog : Form
{
    private readonly DataGridView _grid = new();
    public List<OutboundDetail> SelectedItems { get; private set; } = [];

    public TrackingMatchPickerDialog(string recipient, string trackingNo, List<OutboundDetail> candidates)
    {
        InitializeComponent(recipient, trackingNo, candidates);
    }

    private void InitializeComponent(string recipient, string trackingNo, List<OutboundDetail> candidates)
    {
        Text = "동일 수령인 발주건 선택";
        Size = new Size(700, 380);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));

        var infoLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = $"수령인 \"{recipient}\"(운송장번호: {trackingNo})와 일치하는 발주확정 건이 {candidates.Count}개입니다.\n" +
                   "여러 건을 함께 선택하면(Ctrl/Shift+클릭) 합포장으로 한 운송장에 묶인 것으로 보고 모두 같은 운송장번호로 처리합니다. 1건만 선택하면 그 건에만 적용됩니다.",
            AutoSize = false,
        };

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.ReadOnly = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "주문번호", DataPropertyName = "OrderNo", Width = 110 },
            new DataGridViewTextBoxColumn { HeaderText = "수령인", DataPropertyName = "Recipient", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "주소", DataPropertyName = "Address", Width = 220 },
            new DataGridViewTextBoxColumn { HeaderText = "품목명", DataPropertyName = "ProductName", Width = 130 },
            new DataGridViewTextBoxColumn { HeaderText = "발주확정 시점", DataPropertyName = "CreatedAt", Width = 110 }
        );
        _grid.DataSource = new BindingList<OutboundDetail>(candidates);
        _grid.CellDoubleClick += (s, e) => OnApplyClick(s, e);
        if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var btnSkip = new Button { Text = "건너뛰기", Width = 90 };
        var btnApply = new Button { Text = "선택 건에 적용", Width = 110 };
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
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem)
            .OfType<OutboundDetail>()
            .ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show("적용할 건을 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SelectedItems = selected;
        DialogResult = DialogResult.OK;
        Close();
    }
}
