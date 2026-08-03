using System.ComponentModel;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 마스터SKU가 등록되어 있지 않은(=원가를 조회할 수 없는) CSKU를 채널 불문하고 한 번에 찾아 보여준다.
/// 이런 CSKU는 매핑 규칙은 정상 동작하지만 정산/이익분석에서 "원가 정보 없음"으로 남는다
/// (거래처별 CSKU 관리의 그리드 직접입력 저장 경로가 과거 마스터SKU 존재 여부를 검증하지 않아 생길 수 있었다 —
/// ChannelCskuForm.OnSaveClick 참고). 목록에서 항목을 선택하면 그 채널/CSKU로 바로 이동해 고칠 수 있다.
/// </summary>
public class OrphanCskuFinderDialog : Form
{
    private readonly DataGridView _grid = new();

    public string? SelectedChannelCode { get; private set; }
    public string? SelectedCskuCode { get; private set; }

    public OrphanCskuFinderDialog(List<ChannelSkuModel> orphans, List<SalesChannel> channels)
    {
        InitializeComponent();
        LoadRows(orphans, channels);
    }

    private void InitializeComponent()
    {
        Text = "마스터SKU 미등록 CSKU 찾기";
        Size = new Size(720, 420);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(480, 260);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ConfirmSelection(); };

        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "ChannelName", HeaderText = "채널", DataPropertyName = "ChannelName", FillWeight = 22 },
            new DataGridViewTextBoxColumn { Name = "CskuCode", HeaderText = "CSKU 코드", DataPropertyName = "CskuCode", FillWeight = 24 },
            new DataGridViewTextBoxColumn { Name = "Msku", HeaderText = "연결된(없는) 마스터SKU", DataPropertyName = "Msku", FillWeight = 22 },
            new DataGridViewTextBoxColumn { Name = "InvoiceDisplayName", HeaderText = "송장표시명", DataPropertyName = "InvoiceDisplayName", FillWeight = 32 }
        );

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8, 8, 8, 0),
            Text = "이 CSKU들은 매핑 규칙은 정상 연결되어 있지만, 연결된 마스터SKU가 실제로 등록되어 있지 않아 정산/이익분석에서 \"원가 정보 없음\"으로 표시됩니다. 더블클릭하면 해당 채널/CSKU로 이동합니다.",
            ForeColor = Color.DimGray,
        };

        var buttonPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(6) };
        var btnGoTo = new Button { Text = "이 채널로 이동", Width = 110 };
        var btnClose = new Button { Text = "닫기", Width = 80 };
        btnGoTo.Click += (_, _) => ConfirmSelection();
        btnClose.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttonPanel.Controls.Add(btnClose);
        buttonPanel.Controls.Add(btnGoTo);

        Controls.Add(_grid);
        Controls.Add(hint);
        Controls.Add(buttonPanel);
        CancelButton = btnClose;
    }

    private void LoadRows(List<ChannelSkuModel> orphans, List<SalesChannel> channels)
    {
        var nameByCode = channels.ToDictionary(c => c.ChannelCode, c => c.ChannelName);
        var rows = orphans
            .Select(c => new OrphanRow(
                nameByCode.GetValueOrDefault(c.ChannelCode, c.ChannelCode),
                c.ChannelCode,
                c.CskuCode,
                c.Msku,
                c.InvoiceDisplayName ?? ""))
            .OrderBy(r => r.ChannelName)
            .ThenBy(r => r.CskuCode)
            .ToList();

        _grid.DataSource = new BindingList<OrphanRow>(rows);
        if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
    }

    private void ConfirmSelection()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].DataBoundItem is not OrphanRow row) return;
        SelectedChannelCode = row.ChannelCode;
        SelectedCskuCode = row.CskuCode;
        DialogResult = DialogResult.OK;
        Close();
    }

    private record OrphanRow(string ChannelName, string ChannelCode, string CskuCode, string Msku, string InvoiceDisplayName);
}
