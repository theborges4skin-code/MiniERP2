using System.Globalization;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Mapping;
using MiniERP2.UI;

namespace MiniERP2.Forms;

/// <summary>
/// 온라인 거래처 취합 파일 → 거래처 마감보드 전송 확인창. 취합 내보내기 직후(온라인 거래처 취합 창)와
/// 마감보드의 [온라인취합 불러오기]에서 같은 창을 쓴다. 거래처별 합계·미배정·기존 마감 상태를 보여주고
/// 마감월과 "마감확정 / 대조중으로만 보내기"를 고르게 한다. 파일만 저장해 두고 나중에 확정하려면 닫으면 된다.
/// </summary>
public class PartnerConsolidationClosingDialog : Form
{
    private readonly string _filePath;
    private readonly List<PartnerConsolidationClosingPackage> _packages;
    private readonly PartnerConsolidationClosingTransfer _transfer = new(new PartnerClosingRepository(), new PartnerMasterRepository());

    private readonly ComboBox _periodCombo = new();
    private readonly RadioButton _confirmRadio = new();
    private readonly RadioButton _draftRadio = new();
    private readonly ExcelLikeDataGridView _grid = new();
    private readonly Label _warningLabel = new();

    /// <summary>마감보드에 실제로 보낸 거래처가 1곳 이상이면 true(호출 측 새로고침용).</summary>
    public bool Transferred { get; private set; }

    public string SelectedPeriod => _periodCombo.Text.Trim();

    private PartnerConsolidationClosingDialog(string filePath, List<PartnerConsolidationClosingPackage> packages, string defaultPeriod)
    {
        _filePath = filePath;
        _packages = packages;
        InitializeComponent(defaultPeriod);
        RefreshGrid();
    }

    /// <summary>
    /// 파일을 읽어 창을 띄운다. 읽기 실패·거래처 없음이면 알림만 띄우고 false.
    /// defaultPeriod가 없으면 파일 생성월의 전월(마감은 보통 다음 달 초에 하므로)을 기본값으로 한다.
    /// </summary>
    public static bool ShowForFile(IWin32Window owner, string filePath, string? defaultPeriod = null)
    {
        var read = PartnerConsolidationClosingTransfer.ReadFile(filePath);
        if (read.Error != null)
        {
            MessageBox.Show(owner, read.Error, "온라인취합 불러오기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (read.Packages.Count == 0)
        {
            MessageBox.Show(owner, "상호명이 지정된 거래처가 없습니다. 채널설정에서 거래처(상호명) 연결을 확인하세요.",
                "온라인취합 불러오기", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var period = defaultPeriod ?? (read.FileCreatedAt ?? DateTime.Now).AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        using var dialog = new PartnerConsolidationClosingDialog(filePath, read.Packages, period);
        FormManager.ShowDialogSafe(dialog, owner);
        return dialog.Transferred;
    }

    private void InitializeComponent(string defaultPeriod)
    {
        Text = "거래처 마감보드로 보내기";
        Size = new Size(900, 440);
        StartPosition = FormStartPosition.CenterParent;
        TopMost = true;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Text = $"파일: {Path.GetFileName(_filePath)}\n상호명이 같은 수동 거래처로 품목별 합계 + 택배비 라인을 보냅니다(출고 상세는 파일로 보관).",
            Padding = new Padding(0, 0, 0, 6),
        });

        var optionPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        optionPanel.Controls.Add(new Label { Text = "마감월:", AutoSize = true, Padding = new Padding(0, 6, 2, 0) });
        _periodCombo.Width = 100;
        var month = DateTime.Now;
        for (int i = 0; i < 12; i++) _periodCombo.Items.Add(month.AddMonths(-i).ToString("yyyy-MM", CultureInfo.InvariantCulture));
        _periodCombo.Text = defaultPeriod;
        _periodCombo.TextChanged += (s, e) => RefreshGrid();
        optionPanel.Controls.Add(_periodCombo);
        _confirmRadio.Text = "마감확정까지";
        _confirmRadio.AutoSize = true;
        _confirmRadio.Checked = true;
        _confirmRadio.Padding = new Padding(16, 3, 0, 0);
        _draftRadio.Text = "대조중으로만 보내기(확정은 마감보드에서)";
        _draftRadio.AutoSize = true;
        _draftRadio.Padding = new Padding(8, 3, 0, 0);
        optionPanel.Controls.Add(_confirmRadio);
        optionPanel.Controls.Add(_draftRadio);
        layout.Controls.Add(optionPanel);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        DataGridViewTextBoxColumn Num(string header, string name, int width) => new()
        {
            HeaderText = header, Name = name, Width = width,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N0", Alignment = DataGridViewContentAlignment.MiddleRight },
        };
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "상호명", Name = "Company", Width = 190 },
            Num("품목수", "ItemCount", 55),
            Num("수량", "Qty", 60),
            Num("품목 공급가합", "ProductSupply", 110),
            Num("배송건수", "ShipmentCount", 65),
            Num("배송비", "Shipping", 85),
            Num("합계", "Total", 105),
            Num("단가미배정", "Unassigned", 75),
            new DataGridViewTextBoxColumn { HeaderText = "마감보드 현재 상태", Name = "Existing", Width = 130, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        layout.Controls.Add(_grid);

        _warningLabel.AutoSize = true;
        _warningLabel.ForeColor = Color.DarkOrange;
        _warningLabel.Padding = new Padding(0, 4, 0, 4);
        layout.Controls.Add(_warningLabel);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "나중에(파일만 보관)", Size = new Size(140, 30), DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "마감보드로 보내기", Size = new Size(140, 30) };
        ok.Click += (s, e) => DoTransfer();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons);

        CancelButton = cancel;
        Controls.Add(layout);
    }

    private bool IsValidPeriod => DateTime.TryParseExact(SelectedPeriod, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        foreach (var p in _packages)
        {
            var existing = IsValidPeriod ? _transfer.GetExistingHeader(SelectedPeriod, p.CompanyName) : null;
            var existingText = existing == null ? "없음(새로 추가)" : existing.ConfirmedAt != null ? $"{existing.Status} — 건너뜀" : $"{existing.Status} — 덮어씀";
            _grid.Rows.Add(p.CompanyName, p.ProductLines.Count, p.ProductQty, p.ProductSupply, p.ShipmentCount, p.ShippingFeeTotal, p.TotalSupply, p.UnassignedPriceCount, existingText);
        }

        var warnings = new List<string>();
        var unassigned = _packages.Where(p => p.UnassignedPriceCount > 0).ToList();
        if (unassigned.Count > 0)
            warnings.Add($"단가 미배정 품목이 있습니다({string.Join(", ", unassigned.Select(p => $"{p.CompanyName} {p.UnassignedPriceCount}건"))}) — 0원으로 들어갑니다. 확정 전에 단가를 입력하는 것을 권장합니다.");
        var missingCost = _packages.Where(p => p.MissingCostCount > 0).ToList();
        if (missingCost.Count > 0)
            warnings.Add($"제조원가가 없는 품목이 있어 이익이 크게 잡힙니다({string.Join(", ", missingCost.Select(p => $"{p.CompanyName} {p.MissingCostCount}건"))}).");
        _warningLabel.Text = string.Join("\n", warnings);
    }

    private void DoTransfer()
    {
        if (!IsValidPeriod)
        {
            MessageBox.Show(this, "마감월 형식이 올바르지 않습니다(YYYY-MM).", "알림", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = _confirmRadio.Checked;
        var fileName = Path.GetFileName(_filePath);
        var done = new List<string>();
        var skipped = new List<string>();
        foreach (var p in _packages)
        {
            try
            {
                _transfer.Transfer(SelectedPeriod, p, fileName, confirm);
                done.Add($"{p.CompanyName}  {p.TotalSupply:N0}원");
            }
            catch (InvalidOperationException ex)
            {
                skipped.Add(ex.Message);
            }
        }

        Transferred = done.Count > 0;
        var message = $"{SelectedPeriod} 마감보드 {(confirm ? "마감확정" : "대조중")} 전송 {done.Count}곳";
        if (done.Count > 0) message += "\n" + string.Join("\n", done);
        if (skipped.Count > 0) message += "\n\n건너뜀:\n" + string.Join("\n", skipped);
        MessageBox.Show(this, message, "거래처 마감보드로 보내기", MessageBoxButtons.OK, skipped.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

        DialogResult = DialogResult.OK;
        Close();
    }
}
